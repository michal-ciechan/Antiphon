# CARD-0190 — a Codex session that has never been typed at is not "stuck", and must stop being reported as if it were — plan

**Date:** 2026-08-25 · **Card:** CARD-0190 (`34249b62-6d92-4795-be73-91901e91adcc`) · **Status:** plan
(investigate + design; no implementation in this pass) · **Verified against:** `master` @ `b504cfa`,
worktree `card-task-d3bd6bad`. Every file:line below was re-read out of the code on that commit;
every count and timestamp below was measured on this machine against the live deployment on
2026-08-25 (times UTC unless marked local/BST).

---

## Verdict up front

1. **The card's mechanism is confirmed, with one correction that changes the design.** The Codex
   tailer refuses every rollout that predates the child (C3) and, in `C:\src\Antiphon`, there were 16
   such files plus 4 newer ones belonging to other sessions (refused on C4). The correction: the
   session's own rollout is **not created lazily under a new name on the day it is first written** —
   it is created lazily at the first submit but **named for the session's start time and filed under
   the start date's directory** (`sessions/2026/08/22/rollout-2026-08-22T13-58-33-<uuid>.jsonl`,
   `CreationTimeUtc 2026-08-25 18:49:13Z`, 3.26 days after the child started). Its `session_meta`
   timestamp is the *write* time, so C3 passes trivially. Nothing about C3 needs to change (§1, §2).
2. **f04cd114 was never stuck. Nothing had ever been sent to it** — zero `SessionQueuedMessages` rows
   in its lifetime, an empty composer on screen, and Codex gets no launch-time input at all (no
   `/remote-control`, no launch note: both are Claude-only — §1.4). It was correctly waiting for its
   first prompt for 3 d 6 h while the runner logged **934** `AdoptionRefused` warnings (one every
   5 min from 60 s after launch) and the server wrote **504** `TranscriptBindFailed` + **42**
   `TranscriptBindStuck` (Critical) incidents against it (§2).
3. **Sending it one prompt bound it in 2.5 s, end to end** (§2.3): rollout created 1 s after the
   submit, `adopted Codex rollout` 2.5 s later, `UserPrompt → AssistantText("PONG") → TurnEnd`
   ingested, Mode:Now receipt `Delivered / confirmedBy: transcript` in 3 s, refusal stream stopped
   at report #407. Three peer Codex sessions in the same cwd today bound the same way 3–8 s after
   their first prompt with the same 16 stale candidates present. **The session is bound now.**
4. **The defect is an asymmetry in the fault reporting, present in BOTH tailers.** The "no candidates
   at all" report (`IsEmptyCensus`) is already gated on *input having been delivered* — so an untyped
   session in a **fresh** cwd is correctly silent — but the "candidates existed and were all refused"
   report (`MaybeReportRefusal`) has no such gate. So the only thing that turns a quiet, correct wait
   into a three-day Critical storm is **whether any rollout has ever been written under that cwd**
   (§1.3). That is why the CARD-0195/0204 "pre-existing rollouts in a shared cwd" note is not a
   separate compounding factor to weigh — it is the *trigger* for the storm, and the input gate
   removes it without touching C3 (§4.6).
5. **Home shows a healthy green "Idle" for two reasons, neither of them a missing UI mechanism.**
   CARD-0180 S4's tailer-agnostic `TranscriptBound` DTO field, orange rail dot and `Unbound` badge
   already cover Codex — but (a) the **deployed runner predates S4** (built `13d43f65`, 08-24 08:21
   BST; S4 is `92a4bf0`, 08-24 23:54 BST), so `transcriptBinding` is `null` on every live session
   and the client draws nothing; and (b) once S4 *is* deployed, its condition (`unbound && Running &&
   0 entries`) would paint this waiting session **orange** — the wrong severity — because S4 has no
   third state (§3). The design adds that state and rides S4's existing plumbing (§4.4).

---

## 1. Mechanism, confirmed in code

### 1.1 The rule set and where the refusal comes from

`src/Antiphon.SessionRunner/CodexTranscriptTailer.cs`:

- `LocateAsync` (`:350`) polls every 250 ms for the session's lifetime: sidecar path first
  (`:362`), then `Evaluate()` (`:476`) over **every** `rollout-*.jsonl` under `CODEX_HOME/sessions`
  (`SearchOption.AllDirectories`, `:490`). Discovery is unavoidable for Codex — no `--session-id`
  (`:12-19`).
- `Evaluate()` applies C1 (`:494`), C2 cwd from `session_meta` (`:505`), **C3** (`:511-516`,
  `EpochOk` `:565`: first timestamped record ≥ `childStartUtc − 2 s`, waived on resume), C4 (`:519`,
  a recorded prompt matching `SessionInputLog`). Each near-miss is appended to `Refusals` with its
  reason; the fault detail is `Refusals.Take(5)` (`:606`).
- `MaybeReportRefusal` (`:591`) fires when `Refusals.Count > 0` continuously for
  `_refusalFaultDelay` (60 s), then every `_refusalFaultRepeat` (5 min), publishing
  `RunnerTranscriptFaultEvent(Kind: AdoptionRefused, UnboundSeconds, Repeat)`. **It does not consult
  `_inputLog`.**
- `MaybeReportNoCandidates` (`:632`) is gated by `IsEmptyCensus` (`:673`), which requires
  `_inputLog is { IsEmpty: false }` — the comment at `:670` states the intent exactly: *"an untouched
  composer with no file is the normal first-prompt wait, not a fault."* The same intent is written
  at `:44-47`: *"'Missing' is the normal state for as long as nobody types."*
- `ReportMissingAfterChildExit` (`:686`) is also input-gated (`:688`).

So the code already *knows* that "nothing typed yet" is not a fault — it just forgets it on the one
branch that happens to run whenever the cwd has history.

### 1.2 The server side turns the repeat into an escalation

`server/Application/Services/TranscriptBindingIncidentService.cs`:

- `OnTranscriptFaultAsync` (`:48`) writes `TranscriptBindFailed` (15) per event — Warning, or
  Critical when channel-bound (`:107-118`).
- `MaybeEscalateStuckAsync` (`:212`) adds `TranscriptBindStuck` (27, **Critical regardless of
  channel binding**) once `UnboundSeconds ≥ StuckAfterMinutes` (30, `SupervisionSettings.cs:341`),
  re-firing every `StuckRepeatMinutes` (60). This is CARD-0101's deliberate answer to a real cascade
  and is correct *for a session that has input and no transcript*. It has no way to know this
  session has no input — the event carries no such fact.

`UnboundSeconds` counts from the **episode** start (`refusingSince`, reset on every runner restart),
not from the child start: the latest Stuck row says "33.6h" for a session unbound 3.3 days, because
the runner was restarted 08-24 08:56 UTC. Cosmetic today; it becomes the honest number under §4.2.

### 1.3 Why a fresh cwd is quiet and a shared cwd storms — the asymmetry

| cwd history | `Refusals` | `CwdMatched` | before first input | after first input, no bind |
|---|---|---|---|---|
| no rollout ever written under this cwd | 0 | 0 | **silent** (`IsEmptyCensus` needs input) | `TranscriptMissing` after 60 s ✔ |
| any prior rollout under this cwd (Codex Desktop, another delegate, the operator) | ≥ 1 (all C3) | ≥ 1 | **`AdoptionRefused` every 5 min, `Stuck` hourly** ✘ | `AdoptionRefused` every 5 min ✔ |

Same session, same child, same (correct) bind decision — opposite noise, decided by whether anyone
has ever run Codex in that directory. `C:\src\Antiphon` has 20 rollouts (3 `Codex Desktop`/vscode,
17 `codex-tui`), 16 of them older than f04cd114's child; `TranscriptTailer.cs` (Claude) has the
identical shape (`MaybeReportRefusal :760` ungated, `IsEmptyCensus :846-852` gated).

### 1.4 Codex receives no launch-time input, so "never typed" is its normal post-launch state

- `AgentSessionService.SendRemoteControlCommandsAsync` (`:1280`) types `/remote-control` + `/rename`
  — Claude adapters only; a Codex launch types nothing.
- `AgentControlService` (`:311-319`) builds `LaunchNotes` only when `isClaudeCode && … &&
  SystemPromptAppend` is set — a Codex AlwaysOn or channel-bound agent gets **no** launch note either.
- Therefore every Codex session — standing, AlwaysOn, channel-bound, delegate — starts in the
  "awaiting first input" state and stays there until the queue types something. For a delegate that
  is milliseconds (the brief); for a channel agent it is until the first message; for an
  operator-started standing agent like f04cd114 it is **indefinite**, and legitimately so.

### 1.5 Rollout file naming — measured, and it matters for anyone who reads the sessions root

Measured on the probe (§2.3), codex-cli 0.147.0:

- File **created at first submit** (`CreationTimeUtc = LastWriteTimeUtc = 2026-08-25 18:49:13Z`;
  child start 2026-08-22 12:58:32Z). The "created lazily" claim at `CodexTranscriptTailer.cs:44`
  holds.
- File **named for the session start** — `rollout-2026-08-22T13-58-33-01a0298d-…` (local time of
  launch; the UUIDv7's time component agrees) — and **filed under the start date's directory**
  `sessions/2026/08/22/`, not today's. Today's directory count did not change when the file appeared.
- `session_meta.timestamp` on line 0 is the **write** time (`18:49:14`), so C3 compares the write
  time to child start and passes. C3 is therefore not fragile here.
- Consequence for tooling: **never narrow discovery to "today's" directory** — a session launched
  on day D and first prompted on day D+3 writes to D. The tailer's `AllDirectories` walk is correct
  and must stay. (The rollout census script in §A.3 groups by `session_meta.cwd` for the same reason.)

---

## 2. Live evidence — session `f04cd114-18d9-4cf0-b71e-3ef581f9261a`

### 2.1 State at the start of this pass (2026-08-25 ~18:30Z)

| fact | value | source |
|---|---|---|
| `AgentSessions.Status` | `2` (Running), `Cwd C:\src\Antiphon`, `AgentKind 2` (Codex), `StartedAt 2026-08-22 12:58:32Z` | DB |
| owning agent | `06a847ea` "Codex" (slug `codex`), `AlwaysOn = false`, no `ChatChannels` row, no `CardId`, not a pool delegate | DB |
| runner | `status Running, pid 9476, hostPid 41828, adopted: true, startedAt 2026-08-22T12:58:32.58Z, lastSequence 1226` | `GET :17204/sessions/{id}` |
| sidecar | `transcriptPath: null, how: null, format: codex, childStartUtc 2026-08-22T12:58:32.58Z, resumeLaunch false` (written 12:58:32Z, never updated) | `C:\logs\antiphon\session-runner\transcripts\f04cd114….json` |
| `TranscriptEntries` | **0** | DB |
| `SessionQueuedMessages` | **0 rows, ever** | DB |
| screen | Codex 0.147.0 "Update available → 0.149.0" box, `>_ OpenAI Codex` banner, **empty composer** | `GET …/snapshot` |
| runner refusal lines | 121 (08-22) + 287 (08-23) + 289 (08-24) + 237 (08-25 to 18:44Z) = **934**, first at 12:59:32Z (60 s after launch), every 5 min since | `%TEMP%\antiphon-logs\session-runner-2026082{2,3,4,5}.log` |
| `AgentIncidents` Kind 15 | 282 (08-24) + 222 (08-25) = **504**, first 08-24 00:10Z | DB |
| `AgentIncidents` Kind 27 (Critical) | 23 (08-24) + 19 (08-25) = **42**, latest 18:33Z: *"STILL unbound after 33.6h … (404 report(s))"* | DB |
| Home / `GET /api/agents/06a847ea` | `liveSession.transcriptBinding: null`, `status Running` → green icon, `Idle` badge | server API |

The refusal detail every time: the same five oldest cwd-matched files, all C3
(`…2026/08/17/rollout-…01a01193… first timestamped record 2026-08-17T21:14:05 predates the child
start 2026-08-22T12:58:32`, then 08/18, 08/20, …). The `Take(5)` window never reaches the four
post-start files refused on C4 (§4.6).

Incident rows start 08-24 00:10Z although refusals started 08-22 12:59Z — the server side only began
recording this session's faults then (server restart / pump reconnect); the runner never stopped.

### 2.2 The same cwd, the same day, three sessions that DID bind

With the identical 16 stale candidates present, `session-runner-20260825.log`:

| session | rollout created (filename, local) | `adopted Codex rollout` | Δ |
|---|---|---|---|
| `3d064bf4` | `03-57-53` | 03:58:01 BST | 8 s |
| `44b5ae59` | `04-19-01` | 04:19:24 BST | 23 s (includes the boot prompt's own delay) |
| `9eca9bc6` | `13-53-07` | 13:53:12 BST | 5 s |

All three have `TranscriptEntries` (10 / 5 / 30) and no Kind 15/27 incidents. The stale files never
blocked a bind; they only make the pre-input wait noisy.

The fourth peer, `8be1afc5` (02:40–02:50Z, killed), is **not** this fault: input was delivered, Enter
produced no further pty output, no rollout was ever created, and `ReportMissingAfterChildExit` fired
correctly on exit. CARD-0195 §4 documents it and recommends tracking it here; §5 below declines that
— it is a Codex *delivery* fault (the composer did not submit), and every rule in this plan leaves
its reporting intact or makes it clearer (§4.5).

### 2.3 The probe — one prompt, sent at 18:49:12Z

Safety check first: empty composer, no queued messages, no task, no channel, `AlwaysOn = false` (so
no kill/restart arm on a failed delivery). Cost: one Codex turn.

```
POST /api/sessions/f04cd114-…/messages
{"body":"Antiphon transcript-bind probe for CARD-0190 (task d3bd6bad): reply with the single word PONG and nothing else.","mode":"Now"}
→ 200 {"working":true,"lastDelivery":{"verdict":"Delivered","confirmedBy":"transcript","degraded":false,"at":"2026-08-25T18:49:15.94Z"}}
```

| t | event |
|---|---|
| 18:49:12Z | POST |
| 18:49:13Z | `sessions/2026/08/22/rollout-2026-08-22T13-58-33-01a0298d-2dbe-7c33-9d26-78c8d7246b97.jsonl` **created** (107 KB, 14 lines after the turn) |
| 18:49:15.70Z | runner: `adopted Codex rollout … cwd C:\src\Antiphon matched and a recorded prompt is text this session was sent (C1-C4)` / `Tailing Codex rollout …` |
| 18:49:15.50Z | `TranscriptEntries` seq 1 `UserPrompt` (the probe text) |
| 18:49:18.02Z | seq 2 `AssistantText` = `PONG` |
| 18:49:18.30Z | seq 3 `TurnEnd` |
| 18:48:55 BST | last refusal line, **report #407** — none since |

So the answer to the brief's question is **yes, measured**: sending a prompt binds it immediately.
The card's live example is therefore consumed; §A.2 gives the two-minute recipe to recreate the
state for the build pass's verification.

---

## 3. Why Home lies — and why CARD-0180 S4 is the right mechanism but not yet the right shape

CARD-0180 S4 (`92a4bf0`, Done) already made binding state tailer-agnostic:

- runner `RunnerSessionDto.TranscriptBound: bool?` / `TranscriptBindHow`
  (`SessionRunnerContracts.cs:97-102`), filled from `ITranscriptTailer.BoundTranscriptPath`
  (`SessionRunnerRuntime.cs:1675-1676`) — `CodexTranscriptTailer` implements it (`:145-148`);
- server `AgentService.AttachTranscriptBindingAsync` (`:240-274`) → `TranscriptBinding =
  "bound" | "unbound" | null` on the live-session summary (`BoardDtos.cs:101`); *"a runner that does
  not answer leaves it null — never guessed as unbound"*;
- client: `AgentRail.tsx:52-53,77` orange dot + tooltip *"Terminal live — no transcript bound"*;
  `SessionTranscriptPanel.tsx:309-316` orange `Unbound` badge + alert when
  `entries.length === 0 && transcriptBinding === 'unbound' && liveStatus === 'Running'`.

Two gaps, measured:

1. **Deployment.** `GET :17204/capabilities` → `commitSha 13d43f65…`, `assemblyWriteTimeUtc
   2026-08-24T08:56:36Z`, `processStartUtc 2026-08-24T08:56:49Z`. S4 landed 15 h later. The live
   `/sessions/{id}` JSON has no `transcriptBound` key; the server therefore reports `null`, which the
   client renders exactly like `bound`. No incident says the runner is stale — `RunnerBuildStale`
   (29) fires only from a launch-time mismatch check (`AgentSessionService.cs:253`,
   `AgentTaskDispatcher.cs:669`) and there have been zero rows of it. **`pwsh -File
   scripts/restart-session-runner.ps1` is a required deploy step for this card and for S4**; sessions
   survive it (pty-host split).
2. **Shape.** S4 is binary. After the restart, f04cd114-before-the-probe would have shown the orange
   dot and `Unbound` badge — an alarm colour on a session that is healthy and waiting. The card's
   §3 asks for exactly the third state S4 lacks: *"Idle, no transcript yet"*, neutral, honest.

---

## 4. Design

Principle, stated once: **before the first byte of input has reached this child, no bind outcome is
a fault, and the UI says "no transcript yet" in a neutral voice. From the first input onward,
everything CARD-0006 / 0073 / 0101 / 0180 built stays exactly as loud as it is.** Nothing below
weakens a rule (C0–C4 untouched), and nothing makes an actually-failed bind quieter *after input*.

### 4.1 D1 — gate refusal reporting on delivered input, in both tailers, in lockstep

`CodexTranscriptTailer.LocateAsync` (`:385-388`) and `TranscriptTailer.LocateAsync` (`:407-417`):

```csharp
var inputDelivered = InputDelivered;            // §4.3
refusingSince = inputDelivered && verdict.Refusals.Count > 0 ? refusingSince ?? now : null;
MaybeReportRefusal(...);                        // unchanged body — the gate is on the clock
emptySince   = inputDelivered && IsEmptyCensus(verdict) ? emptySince ?? now : null;
```

`IsEmptyCensus` drops its own `_inputLog` clause in favour of the same `InputDelivered` so the two
reports share one gate (the Claude tailer's `ExactFileHeldBy` arm — CARD-0180 S2's "exact file held
by another claim" — is a **real** conflict regardless of input and stays ungated: a file named for
*this* session held by someone else is never a first-prompt wait).

Effect on the table in §1.3: both "before first input" cells become **silent**. Every "after first
input" cell is unchanged.

### 4.2 D2 — the refusal clock starts at first input, not at child start or runner start

Because `refusingSince` is now null until input arrives, `UnboundSeconds` measures *"seconds since
this session was first typed at without a bind"* — the number an operator actually wants, and the
one `TranscriptBindStuck`'s 30-minute threshold was written for. The 60 s `_refusalFaultDelay` then
gives a slow first turn the same grace it gets today, counted from the prompt.

### 4.3 D3 — "input delivered" must survive a runner restart: stamp it in the sidecar

`SessionInputLog` is deliberately not persisted (`SessionInputLog.cs:22-24`) and that stays — its
job is C4 evidence. But the *fact* that input was ever delivered is now load-bearing for D1, and an
adopted session (`RestoreTailerFromSidecar`, `SessionRunnerRuntime.cs:1567`) starts with an empty
log. Without persistence, the 8be1afc5 shape (typed at, no rollout, then a runner restart) would go
quiet — a regression on CARD-0073.

- `TranscriptSidecar` (`TranscriptSidecar.cs:19`) gains `DateTime? FirstInputAtUtc { get; init; }`.
- `RunnerSession.WriteAsync` (`SessionRunnerRuntime.cs:1744-1748`) stamps it **once**, on the first
  `Append` that leaves the log non-empty (`SaveSidecar(current with { FirstInputAtUtc = now })`),
  same pattern as `RecordTranscriptBinding` (`:1298`).
- Both tailer constructors take `DateTime? firstInputUtc` (from the sidecar on adopt, null on a fresh
  launch) and define `InputDelivered => _firstInputUtc is not null || _inputLog is { IsEmpty: false }`.
  Launch paths (`:1076`, `:1102`) pass null; adopt paths (`:1586`, `:1606`) pass
  `sidecar.FirstInputAtUtc`.
- `ReportMissingAfterChildExit` (both tailers) switches from `_inputLog.IsEmpty` to `InputDelivered`
  for the same reason.

Sidecar schema is additive (`SchemaVersion` stays 1; a missing property deserialises as null).

### 4.4 D4 — a third binding state, carried on the DTO S4 already ships, rendered neutrally

Runner (`SessionRunnerContracts.cs`, additive):

```csharp
// CARD-0190: why TranscriptBound is false. Null when bound, on older runners, or with no tailer.
//   "awaiting-input" — no input has reached the child since it started; nothing can exist to bind.
//   "locating"       — input delivered less than the refusal delay ago; discovery in progress.
//   "refused"        — input delivered; candidates exist and every one was refused (AdoptionRefused live).
//   "missing"        — input delivered; no candidate has appeared (TranscriptMissing live).
string? TranscriptUnboundReason = null,
```

`ITranscriptTailer` gains `string? UnboundReason { get; }`; each tailer derives it from the same
variables `LocateAsync` already holds (`InputDelivered`, `refusingSince`/`emptySince`, the last
verdict). Grok's tailer is deterministic and reports `null`/bound.

Server: `AttachTranscriptBindingAsync` maps `TranscriptBound == false && reason == "awaiting-input"`
→ `TranscriptBinding = "awaiting-input"`; every other `false` stays `"unbound"`. The DTO type widens
to `'bound' | 'unbound' | 'awaiting-input' | null` (`BoardDtos.cs:101`, `client/src/api/boards.ts:175`).
Optionally carry `TranscriptUnboundReason` through verbatim for the Report-bug bundle (CARD-0179's
`session.json` already includes `transcriptBinding`).

Client:

- `SessionTranscriptPanel.tsx:309-316`: `unboundLive` keeps its orange path for `'unbound'`. A new
  `awaitingInput` (`entries.length === 0 && transcriptBinding === 'awaiting-input' && Running`)
  renders a **grey/dimmed** badge `Idle · no transcript yet` and, in place of the orange alert, a
  neutral empty-state line: *"Nothing has been sent to this session since it started ({age}). Codex
  creates its transcript at the first prompt — send one and it will bind."* with the existing composer
  as the call to action. No link to the incidents tab (there are none to see).
- `AgentRail.tsx:52-53,77`: the orange dot stays `'unbound'`-only. For `'awaiting-input'` change only
  the **tooltip** (*"Terminal live — no transcript yet (nothing sent since start)"*); no dot. The rail
  is a glance surface and this state is not attention-worthy.
- Age comes from `liveSession.startedAt`, already on the summary.

The attention feed (`/api/attention`) is not widened — same argument as CARD-0180 §5.

### 4.5 D5 — after input, name the two shapes honestly: "nothing appeared" vs "a stranger's file"

Today, with a shared cwd, a post-input session whose rollout never appears (the 8be1afc5 shape) is
reported as `AdoptionRefused: refusing every Codex rollout candidate …` naming five files from
2026-08-17 — true, and misleading: the operator reads "it keeps rejecting files" when the story is
"Codex never wrote one". A C3 refusal proves only that a file is older than this child; it says
nothing about whether *this* child has written anything. So:

- `CandidateVerdict` gains `int PostStartCandidates` — cwd-matched candidates that **passed C3** (so
  were refused on C1 or C4, or won).
- `MaybeReportRefusal` publishes `Kind = TranscriptMissing` when `PostStartCandidates == 0` with the
  detail *"No Codex rollout has been written for this session in the {N}s since input was delivered;
  {k} cwd-matched rollout(s) older than the child were refused (C3)"* — and `AdoptionRefused`, with
  today's detail, only when at least one post-start candidate was refused. Same change in the Claude
  tailer (its `CandidateVerdict` has the same fields).
- Detail ordering: list C4/C1 refusals **before** C3 ones, newest first, so `Take(5)` shows the
  near-misses that carry information (the four post-start files) rather than the five oldest
  strangers. Diagnostic-only; CARD-0073's "never silently pre-filter" argument at `:466-474` is
  untouched — every file is still evaluated and counted in the census.

Server side needs no new incident kind: `TranscriptBindingIncidentService` already keys severity on
`fault.Kind` only for `ClaimRevoked`; both kinds land as `TranscriptBindFailed` with the kind in
`failureReason`, and `MaybeEscalateStuckAsync` is kind-agnostic. The *text* is what changes.

### 4.6 D6 — pre-existing rollouts in a shared cwd: no C3 change, no pre-filter

Decision: **noted, and closed by D1 rather than by touching C3.** The evidence (§2.2, §2.3) is that
stale same-cwd rollouts — Codex Desktop's or another delegate's — never prevented a bind; the
session's own file wins the moment it exists because its `session_meta` timestamp is the write time.
Their only cost was converting an untyped session's silence into a storm, which D1 removes, and a
misleading incident text, which D5 fixes. A timestamp pre-filter would silently hide the census
CARD-0073 preserved on purpose. Perf is a non-issue: `CodexRolloutProbe.Refresh` is offset-based and
incremental (`CodexRolloutProbe.cs:81-105`); the once-per-file deep scan of a stale file is paid
once. The 2026-08-17 `codex.exe` that CARD-0195 §4 found still alive with its rollout open is a
process-leak observation, not a binding one — leave it to the pty-host census work (CARD-0204).

### 4.7 D7 — deployment and the existing backlog

- `pwsh -File scripts/restart-session-runner.ps1` after merge (and note in the closing comment that
  S4 was not live until this restart). Sessions survive; every unbound Codex session is re-adopted
  with `FirstInputAtUtc = null` from its old sidecar — correct for f04cd114-like sessions (never typed)
  and conservative for any session typed at before the restart (it re-stamps on its next input).
- The 504 + 42 existing incident rows are history and stay; nothing rewrites them.
- No settings change. `StuckAfterMinutes`/`StuckRepeatMinutes` keep their meaning, now measured from
  first input.

### 4.8 Considered and rejected

- **A genuine timeout on "awaiting input" (e.g. Warning after 24 h).** Rejected: a standing agent
  nobody has typed at is not a transcript fault at any age; "idle for days" is a kind-agnostic
  operator concern (and AlwaysOn agents are typed at by their channel). Inventing a timeout would
  recreate a quieter version of the same false alarm. The neutral UI state (D4) is the honest signal.
- **Typing a Codex boot prompt at launch to force the rollout early.** Rejected: spends a model turn
  per launch, creates a turn the delivery-verification baseline must then skip, and CARD-0195 shows
  Codex's first Enter is exactly where its unexplained no-submit fault lives — adding a boot prompt
  would move that fault to launch time for every Codex session.
- **A new `AgentIncidentKind`.** Not needed: the pre-input state raises nothing, and the post-input
  split rides `failureReason`.

---

## 5. Out of scope, stated

- **Codex Enter-without-submit** (session `8be1afc5`, CARD-0195 §4): input rendered in the composer,
  Enter produced zero bytes, no rollout, child killed. A delivery fault under
  `SessionMessageQueueService`'s Codex path, not a binding one. Under this plan it reports as
  `TranscriptMissing` with the "nothing written since input was delivered" text (D5), which is the
  correct pointer. Needs its own card with a headed probe.
- **Leaked `codex.exe` processes** with open rollouts (CARD-0195 §4 last paragraph) — CARD-0204's
  census territory.
- **Runner-build-stale detection outside launch** (§3.1): a runner 15 h behind master with no
  incident. Worth a card; not this one.
- **`UnboundSeconds` before D2** (episode-relative) — becomes correct as a side effect; no separate fix.

---

## 6. Verification / test design

**Runner — `tests/Antiphon.SessionRunner.Tests/CodexTranscriptTailerTests.cs`** (fixture already has
a fake sessions root, a `SessionInputLog`, `refusalFaultDelay`/`refusalFaultRepeat` knobs and an event
hub capture):

1. `Stale_same_cwd_rollouts_with_no_input_delivered_stay_silent_indefinitely` — seed two rollouts
   older than `childStartUtc` in the cwd, no input, run past 3× the fault delay: **zero**
   `SessionTranscriptFault` events; `UnboundReason == "awaiting-input"`; `TranscriptBound == false`.
   (Red today: `AdoptionRefused` at 60 s.)
2. `First_input_starts_the_refusal_clock_from_the_input_not_the_child_start` — same seed; deliver
   input at t = 3× delay; fault fires at t + delay with `UnboundSeconds ≈ delay`, not `4× delay`.
3. `Input_delivered_before_a_restart_is_remembered_from_the_sidecar` — construct with
   `firstInputUtc` set and an empty log; stale candidates → `AdoptionRefused`/`TranscriptMissing`
   fires as if the log were non-empty; child exit → `ReportMissingAfterChildExit` fires.
4. `Only_C3_refusals_after_input_report_TranscriptMissing_not_AdoptionRefused` — stale-only
   candidates + input → `Kind == TranscriptMissing`, detail names the count of C3 refusals; add one
   post-start file with a non-matching prompt → `Kind == AdoptionRefused`, and that file is first in
   the detail.
5. `A_prompt_after_days_of_waiting_binds_and_reports_nothing` — the f04cd114 shape end to end:
   stale seed, no input for a long virtual wait, then input + a rollout whose filename carries the
   child-start date and whose `session_meta.timestamp` is now → bound within the poll interval,
   no fault ever published, `UnboundReason == null`.
6. Existing tests that must stay green unchanged: `A_rollout_that_predates_the_child_is_refused_even_when_the_prompt_matches`,
   `A_strangers_rollout_in_the_same_cwd_is_never_adopted`, `Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent`,
   `A_lazily_created_rollout_is_picked_up_when_it_finally_appears`.

**Runner — `TranscriptAdoptionSafetyTests.cs`** (Claude tailer, same fixture family): mirror of 1–4
(`Zero_candidates_without_delivered_input_stays_silent` already pins the fresh-cwd half; add the
stale-cwd half), plus `Exact_file_held_by_another_claim_is_reported_even_before_input` pinning the
CARD-0180 S2 carve-out in D1.

**Runner — sidecar round-trip** (there is no `TranscriptSidecarTests`; `TranscriptAdoptionSafetyTests` is where sidecar restore is pinned — add it there): `FirstInputAtUtc`
round-trips and a pre-CARD-0190 sidecar without it loads as null.

**Server — `tests/Antiphon.Tests/Application/TranscriptBindingIncidentTests.cs`**: unchanged
behaviour; add one case that a `TranscriptMissing` fault with `UnboundSeconds ≥ StuckAfterMinutes`
still escalates (kind-agnostic escalation stays).

**Server — `AgentService` binding attach** (beside the S4 test): a runner DTO with
`TranscriptBound=false, TranscriptUnboundReason="awaiting-input"` → `TranscriptBinding ==
"awaiting-input"`; `"refused"` → `"unbound"`; null bound → null.

**Client — `SessionTranscriptPanel.test.tsx`, `AgentRail` test**: `'awaiting-input'` renders the grey
badge and neutral copy, **no** `unbound-badge`, **no** `rail-unbound-dot-*`; `'unbound'` renders both
exactly as S4 pinned.

**Live re-verification recipe** — §A.2.

---

## 7. Build order (each slice commits + pushes on its own)

1. **S1 runner — D1 + D2** in both tailers (`InputDelivered` from the in-memory log only), tests
   6.1, 6.2, 6.6 and the Claude mirror. Smallest change; ends the storm on its own.
2. **S2 runner — D3** sidecar `FirstInputAtUtc` + `WriteAsync` stamp + adopt-path plumbing; test 6.3
   + sidecar round-trip.
3. **S3 runner — D5** `PostStartCandidates`, kind split, detail ordering; test 6.4, 6.5.
4. **S4 runner → server → client — D4** `TranscriptUnboundReason` on the DTO, the tri-state mapping,
   the neutral UI; server + client tests.
5. **Deploy**: `pwsh -File scripts/restart-session-runner.ps1`; confirm `GET :17204/capabilities`
   reports the new `commitSha`; confirm `GET /api/agents/06a847ea` shows `transcriptBinding: "bound"`
   for f04cd114 (it is bound since 18:49Z) and that a freshly-launched untyped Codex session (§A.2)
   shows `"awaiting-input"` with no incidents after 10 minutes.
6. Close with the incident counts from §2.1 in the reason.

Test runs: `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-0190/
--treenode-filter "/*/*/CodexTranscriptTailerTests/*"` (then `TranscriptAdoptionSafetyTests`), server
`Antiphon.Tests` chunked by namespace, client `pwsh -File scripts/test-client.ps1`. Delete every
`bin-0190` directory afterwards.

---

## Appendix

### A.1 Queries used (re-runnable)

```powershell
# session / incidents / queue
docker exec antiphon-postgres psql -U antiphon -d antiphon -At -c "select ""Status"",""Cwd"",""StartedAt"" from ""AgentSessions"" where ""Id""='f04cd114-18d9-4cf0-b71e-3ef581f9261a';"
docker exec antiphon-postgres psql -U antiphon -d antiphon -At -c "select date_trunc('day',""CreatedAt""),""Kind"",count(*) from ""AgentIncidents"" where ""SessionId""='f04cd114-18d9-4cf0-b71e-3ef581f9261a' group by 1,2 order by 1,2;"
docker exec antiphon-postgres psql -U antiphon -d antiphon -At -c "select count(*) from ""SessionQueuedMessages"" where ""AgentSessionId""='f04cd114-18d9-4cf0-b71e-3ef581f9261a';"
# runner
curl -s http://localhost:17204/sessions/f04cd114-18d9-4cf0-b71e-3ef581f9261a
curl -s http://localhost:17204/capabilities
Get-Content C:\logs\antiphon\session-runner\transcripts\f04cd11418d94cf0b71e3ef581f9261a.json
Select-String -Path "$env:TEMP\antiphon-logs\session-runner-2026082*.log" -Pattern 'f04cd114.*refusing every Codex rollout' | Measure-Object
```

### A.2 Recreating the live state for verification (two minutes, no model turns)

1. Launch a Codex session for a standing (non-AlwaysOn, non-channel) agent whose
   `WorkingDirectory` is `C:\src\Antiphon` (any cwd with ≥ 1 prior rollout works). Send nothing.
2. Before this card: from +60 s the runner logs `refusing every Codex rollout candidate` every 5 min
   and Kind 15 rows appear; at +30 min Kind 27. After this card: nothing; the DTO reports
   `transcriptBinding: "awaiting-input"`; Home shows the grey `Idle · no transcript yet`.
3. `POST /api/sessions/{id}/messages {"body":"<≥12 chars>","mode":"Now"}` → rollout under
   `~/.codex/sessions/<launch date>/rollout-<launch local time>-<uuid>.jsonl` within ~1 s, bound
   within ~3 s, `transcriptBinding: "bound"`.

### A.3 Rollout census (as run 2026-08-25, before the probe)

65 rollouts under `~/.codex/sessions`; 20 with `session_meta.cwd = C:\src\Antiphon` (3 originator
`Codex Desktop`/source `vscode`, 17 `codex-tui`/`cli`); 16 older than f04cd114's child (refused C3),
4 newer (08-24 07:07, 08-25 02:57 / 03:19 / 12:53 — the peers in §2.2, refused C4). Script: group
`Get-ChildItem -Recurse rollout-*.jsonl` by the `payload.cwd` of line 0 — not by directory date
(§1.5).
