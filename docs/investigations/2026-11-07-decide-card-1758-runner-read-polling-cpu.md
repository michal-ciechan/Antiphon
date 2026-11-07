# Decide CARD-1758: conditional runner reads

Date: 2026-11-07. Card: CARD-1758 (High/Normal). Base: `5322f3d2012e60a04afce2e0db9dd5263e47b491`.
Source measurement: `docs/investigations/2026-11-07-desktop-server-runner-cpu-trace.md` on `origin/feat/card-task-7ca19c43` (5-minute sampled-thread-time trace, desktop server PID 330800 and session runner PID 153560, 2026-11-07 12:16:35Z–12:21:35Z, both Debug). This pass did not re-trace. Nothing ran on the desktop. No product code changed.

## Verdict

The cost is real and the mechanism is confirmed from the code. The server deserializes full runner read bodies with reflection `System.Text.Json` on every call (`JsonSerializerDefaults.Web` at `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:81`). The runner rebuilds those bodies on every call and writes them back as JSON. The large bodies are the ANSI tail (`GET /sessions/{id}/buffer`, cap 256 KiB, file read every time) and the snapshot (`GET /sessions/{id}/snapshot`: in-memory live buffer up to twice that cap, plus the rendered screen, plus dialog). Several hot callers need only `LastSequence`, or one unchanged screen, and still download the whole body. Transcript catch-up and the boot-turn wait download the entire transcript on every tick.

Phone-home does the same work a second time: `PhoneHomeFraming.ReadFrameAsync` parses the frame (`src/Antiphon.SessionRunner.Contracts/PhoneHomeFraming.cs:28-52`), then `PhoneHomeRunnerClient.Read<T>` deserializes the payload again (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:649-654`). Event delivery parses it a third time via `GetRawText()` (`:642`).

The trace cannot name the caller. Async continuations hide it, and `LiveTimingHttpHandler` measures HTTP without reading the URI or the resilience operation name (`src/Antiphon.Observability/LiveTimingHttpHandler.cs:4-8`). The operation names already exist (`runner.get-buffer`, `runner.get-snapshot`, `runner.get-transcript`, `runner.list`, `runner.get` in `src/Antiphon.Resilience/ResilienceNames.cs:73-79`) and are not counted. The rates below are the call sites and their intervals. That is enough to choose the slices. A new counter is not a prerequisite.

## Plain English

- The runner keeps handing the server a full copy of the terminal and the transcript, and the server rebuilds those objects from scratch every time it asks.
- The asks that hurt are the ones that only wanted "did the sequence change?" or "did the screen change?" and still received hundreds of kilobytes.
- The fastest of those asks runs only while a session is starting its first turn: Claude re-reads the whole snapshot every 50 ms, and Codex re-reads the transcript, two snapshots, and the ANSI tail every 250 ms, for up to five minutes.
- After that, the steady asks are slower: a screen-and-buffer pair every 10 seconds per live session, and the same pair every 500 ms while a Codex or Grok delivery is being confirmed (at most about 30 seconds).
- The transcript row that proves a prompt was delivered already lives in the database. These body downloads are not that proof. They must keep seeing a new row when one exists, and they must not invent a "nothing arrived" answer from a shortcut.
- Four slices, in order, cut the copies without a lockstep upgrade: ask for less from the existing routes, serialize the JSON that still has to move, let a quiet pty answer "unchanged", then let a transcript pull ask for rows after a cursor. The browser terminal is already a one-shot backlog plus live deltas, so it is not part of this.

## What the trace measured

Shares are of each process's active samples. In the window the server used 1.30 cores and the runner 0.23 cores.

| Share | Where |
|---|---|
| Server 12.10% (about 0.16 cores) | `SessionRunnerHttpClient.SendReadAsync` 6.47% and `ReadJsonAsync` 5.25%, plus phone-home frame parse, in one union. Leaf frames include `Utf8JsonReader.ConsumeStringAndValidate` and the reflection constructor converter. |
| Runner about 14% (about 0.03 cores) | Response JSON writing 7.7%, and the session read routes 6.5% (`GetBuffer` → `ReadAnsiLog`, `GetSnapshotAsync`). |

`ReadJsonAsync` is inside `SendReadAsync`, so those two server figures overlap. Snapshot deserialization uses `ReadFromJsonAsync` directly (`SessionRunnerHttpClient.cs:587-592`) and is in the send figure, not the `ReadJsonAsync` figure. Output on `GET /events` is already one chunk per event (`RunnerOutputEvent` at `SessionRunnerRuntime.cs:4698-4700`) and is parsed in `ParseEvent` (`SessionRunnerHttpClient.cs:964-975`), not in `ReadJsonAsync`. That stream is necessary work. The waste is the repeated full-body reads.

Both processes were Debug builds. Release JIT would shrink the reflection tax. It would not stop the file read or the byte copy. CARD-1624 (Review) is the unbound transcript locate scan, a different runner cost, and is not part of this card. CARD-1756 is the tailer's own 300 ms stat poll. CARD-1757 is the host-stats process snapshot. Leave those alone.

## Route inventory

Body sizes are the caps in code, not bytes captured in the trace. The runner ANSI tail and the live buffer are capped by `ReplayBufferMaxChars` = 256 KiB (`src/Antiphon.SessionRunner/SessionRunnerSettings.cs:14`). The live buffer is trimmed only after it exceeds twice that (`SessionRunnerRuntime.cs:4169-4173`), so one snapshot's `RawOutput` can be about 512 KiB of text before trim. The rendered screen is the terminal grid, much smaller. `GET /sessions/{id}` and `GET /sessions` are one small record per session (`RunnerSessionDto`, `SessionRunnerContracts.cs:232-274`).

`LastSequence` advances in the same lock as the live buffer and the screen feed (`HandleOutput`, `SessionRunnerRuntime.cs:4685-4696`). A pty snapshot's raw text and screen are that buffer and that screen (`:4355-4364`). The buffer route does not return the memory buffer. It re-reads the ANSI log tail from disk on every call (`GetBuffer` `:4288-4293`, `ReadAnsiLog` `:5016-5055`).

| Route | Response | Who asks | Interval | Shape of the ask |
|---|---|---|---|---|
| `GET /sessions/{id}/buffer` and phone-home `Buffer` | `RunnerBufferDto`: session id, ANSI tail, `LastSequence` (`SessionRunnerContracts.cs:276-279`). Runner: `Program.cs:330-331`. | `GetLastSequenceAsync` reads the whole tail and keeps the number (`RunnerTerminalSession.cs:222-225`). `GetRunnerSequenceOrDefaultAsync` does the same (`AgentSessionRuntime.cs:2454-2466`). `TryGetLiveSnapshotAsync` always pairs it with a snapshot (`:2166-2167`). Watchdog uses the tail only when the rendered screen is empty (`WatchdogService.cs:68-72` and `:130-132`). UI backlog and re-adopt are rare (`SessionTerminal.tsx:81-140`; `SessionReconciliationService.cs:660`). | Quiet-wait and visible-wait: every 50 ms (`RunnerTerminalSession.cs:246-260` and `:304-315`). Manual turn: every 100 ms (`AgentSessionRuntime.cs:28`, `:2656`, `:2671`). Codex/Grok boot turn: every 250 ms. Delivery and watchdog: see snapshot row. | Sequence-only callers throw the text away. |
| `GET /sessions/{id}/snapshot` and phone-home `Snapshot` | `RunnerSnapshotDto`: raw output, rendered screen, `LastSequence`, start, accepted generation, dialog (`SessionRunnerContracts.cs:281-288`). Pty path is in memory. Herdr path is an on-demand `pane.read` (`SessionRunnerRuntime.cs:4309-4333`). | `TryGetLiveSnapshotAsync` (delivery, watchdog, boot-reply, expectations). Claude boot turn: `WaitForOutputAsync` (`RunnerClaudeAdapter.cs:221-227` → `RunnerTerminalSession.cs:228-239`). Codex boot turn asks twice, text then screen, plus the buffer (`RunnerCodexAdapter.cs:250-273`). Grok boot turn asks for the text (`RunnerGrokAdapter.cs:230-244`). Dialog reads throw away everything except `Dialog` (`ProviderDialogObservationService.cs:10-11`). | Claude boot turn: every 50 ms, for up to `ClaudeDoneMaxWaitMs` = 300000 (`AgentRegistrySettings.cs:61`). Codex and Grok boot turns: every 250 ms, same 300000 ms ceiling (`:81`, `:124`). Delivery confirm, composer evidence, and settle: every `PollIntervalMs` = 500 (`SupervisionSettings.cs:323`), confirm window `TranscriptConfirmTimeoutSeconds` = 30 (`:375`). Watchdog: every `ScanIntervalMs` = 10000, once per live session (`WatchdogSettings.cs:6`, `WatchdogService.cs:65-68`). | The only production caller of `WaitForTurnCompleteAsync` is session start (`AgentSessionService.cs:279`). Later delegated turns do not sit in that loop. A single local Claude start can still request about 20 full snapshots a second for up to five minutes. A Codex start can request about 16 heavy reads a second (transcript + two snapshots + buffer). |
| `GET /sessions/{id}/transcript` and phone-home `Transcript` | Full `RunnerTranscriptDto` entry list, `LastSequence`, readiness, generation (`SessionRunnerContracts.cs:383-391`). No `since` parameter (`SessionReadLaunchRoutes.cs:18-19`). | Boot-turn verdict every tick (`RunnerCodexAdapter.cs:292`, `RunnerGrokAdapter.cs:287`). `CatchUpTranscriptAsync` / `PullAndPersistAsync` before a "the row is absent" decision (`AgentSessionRuntime.cs:958-959`, `:1019-1025`). Ceiling and working-transcript probes. | 4/s during a Codex or Grok boot turn. Confirm-loop catch-up at most once a second (`SessionMessageQueueService.cs:4307`, `:4461-4464`). On event reconnect, once (`SessionRunnerEventPump.cs:50`). | Already-pushed rows are fetched again in full. |
| `GET /sessions/{id}` and phone-home `Get` | Small `RunnerSessionDto`, including `LastSequence`. | `TryGetLiveMetadataAsync` (`AgentSessionRuntime.cs:2220`). Delivery sequence-advance and settle (`SessionMessageQueueService.cs:4347`, `:4798`, `:4829`). | Every 500 ms during confirm and settle. | Already the cheap sequence read. The buffer route is the expensive duplicate of this number. |
| `GET /sessions` and phone-home `List` | One small record per session. | Reconciliation every `IntervalMs` = 15000 (`SessionReconciliationSettings.cs:21`, `SessionReconciliationService.cs:158`). Watchdog list every 10 s. Phone-home inventory every `InventoryRefreshSeconds` = 30 (`PhoneHomeRunnerSettings.cs:78`). | 2–6/min per runner, not per session. | Too small, and too rare, to explain the trace. |
| `GET /host-stats` | Small sample. | `HostStatsPollService` every `PollIntervalMs` = 5000 (`HostStatsSettings.cs:9`, `HostStatsPollService.cs:43`, `:109`). | 12/min per runner. | CARD-1757 owns the sampler. Leave the read. |
| `GET /capabilities` | Small, TTL-cached (`SessionRunnerHttpClient.cs:485-512`). | Launch and feature gates. | Not a poll. | Leave it. |
| `GET /events` and phone-home events | One output chunk or one transcript row. | `SessionRunnerEventPump` holds one stream. Phone-home pushes the same events. | Per chunk and per row. | Already a delta. Keep it. |

The comment on `ReadAnsiLog` (`SessionRunnerRuntime.cs:5010-5014`) says the server polls `/buffer` every 50 ms. The browser does not. `SessionTerminal.tsx` loads the backlog once and then applies SignalR `AgentTextDelta`. The 50 ms cadence that still exists is the runner client's own `WaitForOutputAsync` (snapshot) and quiet-wait (buffer).

Per busy local session, order-of-magnitude request rates while that loop is actually running:

| Situation | Buffer | Snapshot | Transcript | Session GET |
|---|---:|---:|---:|---:|
| Claude boot turn | 0 | 20/s | 0 | 0 |
| Codex boot turn | 4/s | 8/s | 4/s | 0 |
| Grok boot turn | up to 4/s | 4/s | 4/s | 0 |
| Codex/Grok delivery confirm (≤30 s) | 2/s | 2/s | ≤1/s | 2/s |
| Watchdog, steady | 0.1/s | 0.1/s | 0 | 0 |
| Open terminal in the UI | one | 0 | 0 | 0 |

`TryGetLiveSnapshotAsync` is one snapshot plus one buffer, so the watchdog and the delivery confirm each pay the file read as well as the snapshot. The trace's 12% and 14% are the sum of whichever of these were in that window. This pass cannot split that sum further. The fast loops are the only ones dense enough to matter on a quiet runner; the 10-second watchdog matters when many local sessions are live.

## What can get cheaper

UserPrompt evidence stays the delivery verdict (`docs/session-runtime-invariants.md` states that complete native `UserPrompt` confirmation remains required). The confirm loop already matches stored transcript rows (`SessionMessageQueueService.cs:4741-4757`). Screen advance is a redraw signal, not delivery (`SupervisionSettings.cs:360-365`). The pull exists because the live stream has been late: on session `e809ce65` the confirming row was stored 45 seconds late (`AgentSessionRuntime.cs:950-956`). Any shortcut that can answer "no row" without the runner's current rows is out of scope. `ProviderDialogObservationService` states that no cached Clear grants input authority (`:6`). A runner round trip that returns "unchanged" is still an answer from the runner. A server-side timer cache of Clear, of the screen, or of the transcript is not.

### 1. Sequence and one-frame polls, on the routes that already exist

Saving: the ReadAnsiLog path for every caller that keeps only `LastSequence`, and one of the two Codex snapshots, and the buffer half of a snapshot the caller did not need. On a Claude boot turn, replace ~20 full snapshots/s with ~20 small session GETs/s plus a snapshot only when `LastSequence` changes. On a Codex boot turn, one snapshot per 250 ms replaces two snapshots and one buffer read; the transcript pull remains until slice 4. Staleness: zero for a pty, because raw text and screen change in `HandleOutput` under the same lock as `LastSequence`. Risk: low for sequence and for the screen predicate. The Codex tracker today samples text, screen, and sequence from three different reads; one snapshot is one frame, which is the rule delivery already uses (`RunnerTerminalSession.cs:212-214`). Do not use the session GET's `LastSequence` as a substitute for a herdr `pane.read`: herdr's screen is not fed by `HandleOutput`. Herdr snapshots stay full reads. Do not skip a dialog read that will authorize input. Smallest change: server only. `GetLastSequenceAsync` and `GetRunnerSequenceOrDefaultAsync` call `GetAsync` (the session DTO already carries `LastSequence`). Codex `WaitForTurnCompleteAsync` uses one `GetSnapshotAsync` per tick. Claude `WaitForOutputAsync` polls `GetAsync` and fetches the snapshot when the sequence moves. `TryGetLiveSnapshotAsync` grows a caller flag so a screen-only caller does not also read the ANSI tail; the watchdog keeps the tail for the empty-screen fallback.

### 2. Source-generated JSON, and one phone-home parse

Saving: the reflection tax on every body that is still sent, including snapshot, buffer, transcript, SSE events, and phone-home frames. This is the part of the server 12% and the runner 7.7% that is `System.Text.Json` reflection. It does not skip `ReadAnsiLog`. Staleness: none. The JSON shape stays the Web defaults already on the wire (camelCase). Risk: low, if a round-trip test pins one buffer, one snapshot, one transcript event, and one phone-home frame against today's bytes. Smallest change: one `JsonSerializerContext` shared by the HTTP client, the runner's `Results.Ok` / event hub, and `PhoneHomeFraming`. On the server, deserialize `frame.Payload` once; stop `GetRawText()` plus a second parse for events. Old and new peers already tolerate additive JSON fields (`SessionRunnerContracts.cs:365-366` states that pattern). No new field is required here.

### 3. Pty 304 for buffer and snapshot

Saving: when a poll's observation tuple is unchanged, the runner skips building the strings and both sides skip the large parse. This is the rest of the quiet 50 ms / 250 ms / 500 ms / 10 s polls after slice 1 has removed the pointless ones. During active output the sequence changes every chunk, so the body is still sent. That remaining cost is real work. Staleness: zero relative to the runner's current tuple. The runner compares and either returns 304 or the new body. Risk: medium. A 304 is "same as this tuple", and the caller must already hold the body for that tuple. A caller with no body omits `If-None-Match` and receives today's 200. The tuple for a pty snapshot is accepted generation, `LastSequence`, `inputRevision` (`SessionRunnerRuntime.cs:2764`, incremented on input at `:4426`), and the dialog state fields that do not include input text. Input can change the dialog without output, so `LastSequence` alone is the wrong key. The buffer's text changes only with output, so its key is accepted generation plus `LastSequence`. Herdr stays a full `pane.read` with no 304, because its screen is not a function of `HandleOutput`. Do not 304 a dialog read into a stored Clear without the runner having just compared the tuple. Do not add a time-to-live. Smallest change: optional `If-None-Match` on the two GETs and the same key on phone-home `Buffer` and `Snapshot` as an optional request field. Unknown headers and unknown fields are ignored. 200 with a body remains the answer.

### 4. Transcript cursor

Saving: the 4/s full-transcript download during a Codex or Grok boot turn, and repeat catch-up of an already stored prefix. A long session's transcript is the body that grows without the 256 KiB cap. Staleness: zero when the cursor is contiguous. Risk: medium, and this is the slice that can weaken UserPrompt evidence if it is careless. The server may apply a delta only when the first returned sequence is the cursor plus one and the last entry's sequence equals `LastSequence`. If the first sequence is lower, the body is a full snapshot (that is what today's runner returns). If the first sequence jumps ahead of the cursor plus one, the server discards the delta and reads the full snapshot. A cursor of zero, or no cursor, is the full snapshot. A generation mismatch is a full snapshot, not a delta from another launch. `CatchUp` before an irreversible "no UserPrompt" decision keeps that rule, so a gap cannot become a negative verdict. Smallest change: optional `?since=` on the existing GET, and an optional `since` field on phone-home `Transcript`. The response shape stays `RunnerTranscriptDto`. No new route.

### Left as they are

`GET /sessions`, `GET /sessions/{id}`, host-stats, capabilities, and the event stream. List and session GET are already small. Pushing full buffers over phone-home would copy more, and it would retire the pull that exists because the stream was measured late. Slowing `PollIntervalMs`, the 50 ms screen wait, or the 250 ms turn wait would change delivery timing and turn-complete detection in order to avoid a copy. Slices 1–3 keep the cadence and drop the copy.

## Compatibility

Server2 and the desktop runner upgrade at different times. Every slice has to behave as today when the peer is old.

| Peer mix | Required behaviour |
|---|---|
| New server, old runner | Slice 1 uses only `GET /sessions/{id}`, `GET .../snapshot`, and `GET .../buffer`, which old runners already serve. Slice 2 is the same JSON. Slice 3: an old runner ignores `If-None-Match` and returns 200 with the body; the new server treats 200 as the body. Slice 4: an old minimal-API runner ignores an unknown query and returns the full list; the new server sees sequences at or below the cursor and treats that as the full snapshot. A phone-home runner that rejects an unknown field is avoided by sending `since` and the match key only when `GET /capabilities` lists the new feature, using the existing feature-list pattern (`hostStatsV1` and the others). Unsupported phone-home operations already come back as unsupported; these slices do not add a new operation, so an old runner never has to recognise one. |
| Old server, new runner | The old server sends no match header and no `since`. The new runner returns the full 200 body it returns today. |
| Both new | Match header and `since` are used. 304 carries no body. A delta is applied only when it is contiguous. |

No lockstep deploy. No fallback that treats a missing body as an empty screen, an empty session list, or an absent UserPrompt.

## Recommendation

Do the four slices as four reviewable changes, in this order. Do not land them as one card-sized change. Duplicate search on 2026-11-07 (`card.ps1 search` for `conditional read`, `since-sequence`, `source-generated JsonSerializer`, `ReadAnsiLog`, `runner read responses`, `ETag`, `runner poll`, board Antiphon, `-All`) found CARD-1758 itself and, on `ReadAnsiLog`-adjacent CPU text, CARD-1624. CARD-1624 stays the tailer locate scan. No open card already covers these slices.

| Slice | Change | Importance | Urgency | Why this order |
|---|---|---|---|---|
| 1 | Server-only. Sequence polls use `GET /sessions/{id}`. Codex uses one snapshot per tick. Claude fetches a snapshot only after the sequence moves. Screen-only live snapshots skip the ANSI tail. Herdr snapshots stay full. | High | High | Removes the file read and the duplicate snapshot with no wire change, on the only loops fast enough to dominate the trace. |
| 2 | Source-generated JSON for the large runner DTOs, the event records, and `PhoneHomeFrame`, plus one-pass payload decode. | High | High | Cuts the reflection the trace actually sampled, on bytes slice 1 still sends. Same JSON, so it can ship beside slice 1. |
| 3 | Pty `If-None-Match` / 304 on buffer and snapshot, keyed by generation + sequence + input revision + dialog state. Herdr excluded. | High | Normal | Finishes the quiet polls. Depends on slice 1 so the remaining callers are the ones that truly need the text. |
| 4 | Optional transcript `since`, applied only when contiguous, otherwise the full snapshot. | High | Normal | Largest byte win on long sessions, and the only slice that touches UserPrompt catch-up. Keep it last and pin the gap tests. |

Expected saving against the trace: slice 1 takes the runner's `ReadAnsiLog` / extra snapshot share on the fast loops (the 6.5% route time, and the JSON write of those bodies). Slice 2 takes the reflection share of whatever bodies remain (part of the server 12% and the runner 7.7%). Slice 3 takes the quiet remainder of those bodies. Slice 4 takes repeated full transcripts, which the trace folded into the same JSON time and did not separate. Active output chunks and new transcript rows still serialize. That is the work the product has to do.

Acceptance for each slice: the delivery confirm still matches a stored UserPrompt and still pulls before a negative verdict; a dialog Clear still comes from a runner round trip on that call; a herdr snapshot still performs `pane.read`; an old peer still receives and returns today's full body.

## Not done, noted

No product change in this pass. The fix is the four slices above, and it was not started.

## Sources

- Trace verdict and the CARD-1758 row: `docs/investigations/2026-11-07-desktop-server-runner-cpu-trace.md` (task 7ca19c43), findings S4 and recommendation 5.
- CARD-1758 problem statement, read from the board on 2026-11-07.
- Call sites and caps cited above. Intervals are the defaults in code. `server/appsettings.json` does not override `DeliveryVerification:PollIntervalMs` (the `PollIntervalMs` of 5000 at line 81 is host stats).
