# CARD-0299 — Codex Plan brief typed, Enter never submitted, Failed at 10 minutes with 0 tokens

**Date:** 2026-09-01. **Card:** CARD-0299. **Incident task:** `4ea068fa` (CARD-0288 Plan, Codex Frontier).
**Session:** `41959e81-9fdf-4c00-a31b-fc1facc8c4ea`. **Status:** mechanism pinned from live rows + logs;
not re-dispatched (four later Codex Plan dispatches the same day succeeded).

This is CARD-0133's production signature returning after both shipped backstops
(`-c disable_paste_burst=true`, `09a6a8ba` S1 positive-submit evidence). It is not a `cx.ps1` keystroke
bug and it is not a one-off.

## Verdict

| Question | Answer |
|---|---|
| Did `cx.ps1` fail to send Enter? | **No. `cx.ps1` is not on this path.** The registry launches `codex.cmd` (`server/appsettings.json` definition `codex`). The brief is typed by `SessionMessageQueueService.DeliverAsync`, not `RunnerCodexAdapter.SendPromptAsync` / `CodexSubmitConfirmation`. |
| Did Antiphon skip the submit step? | **No.** One `\r` was written. Server log: *"confirmed by degraded screen-only verdict after 30s with no transcript row … **1 Enter(s) sent**"* at 11:30:40 +01. |
| Was it a timing race against composer readiness? | **Partly.** Ready is still quiet-after-visible only (`RunnerCodexAdapter.WaitForReadyAsync`). The last ANSI frame still shows `Starting MCP servers (1/2): node_repl (1s  esc to interrupt)` **and** the full brief in the composer. MCP boot never cleared; the TUI then emitted zero further bytes from 10:30:17Z until kill at 10:40:08Z. |
| Why was the brief marked `Sent`? | CARD-0133 S1 (`SubmitEvidence.IsPositive`) latched a **false emptied-composer** on a transient snapshot, suppressed every re-Enter, and at 30 s took the unobservable-baseline degraded `Delivered`. The durable last frame still holds the body. Tests that pin "body still visible → 3 Enters → `NoSubmitOutput`" (`Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters`) did not see this latch. |
| One-off vs recurring? | **Recurring, rarer.** Since 2026-08-27 (post `disable_paste_burst`): **3 of 55** Codex sessions (5.5 %) match 0 `TranscriptEntries` + brief `Sent` / attempts 1 / null baseline. Pre-fix CARD-0133 was 9/78 (11.5 %). Same-day controls CARD-0298 and CARD-0304 succeeded cleanly. |
| `gpt-5.6-terra` vs expected `sol`? | **Red herring for the unsubmitted Enter.** Every Frontier Codex session inspected today — including the successful CARD-0298 / CARD-0304 / CARD-0030 Plans — paints `gpt-5.6-terra` on the banner. `~/.codex/config.toml` has `model = "gpt-5.6-terra"`. Reasoning effort **did** override (`xhigh` on screen vs config `high`), so `-c` works. Whether `--model gpt-5.6-sol` is ignored by codex-cli 0.151.0 is a separate launch-arg question; it did not distinguish fail from success. |

## Timeline (Europe/London = UTC+1)

| UTC | Local | What |
|---|---|---|
| 09:34:03 | 10:34 | Task `4ea068fa` created (Worker/Plan, Frontier, Codex). Held behind CARD-0292. |
| 10:30:03.487 | 11:30:03 | Dispatched. Session `41959e81` Starting. Brief spilled (4,120 UTF-8 bytes → pointer 638 chars to `.antiphon/task-4ea068fa-brief.md`). Event: "Dispatched … (gpt-5.6-sol)". |
| 10:30:03.650 | 11:30:03 | Pty-host pid 43052; child `codex.cmd` pid 54336; ModernConPty 1.24.260710001. |
| 10:30:06.745 | 11:30:06 | Queue stamps brief `Sent`, attempts 1, baseline null. `firstInputAtUtc` 10:30:06.753. Sidecar `transcriptPath: null`. |
| 10:30:17 | 11:30:17 | Last ANSI byte. Frame closed (`ESC[?2026l`). Composer still holds the full pointer. MCP boot line still on screen, elapsed frozen at `1s`. No `Working (`. |
| 10:30:40 | 11:30:40 | Queue: degraded screen-only `Sent`, 1 Enter. |
| 10:31:06 / 10:36:06 | | Runner: no cwd-matching Codex rollout (C3 refused 4 older files). Running unbound. |
| 10:40:08 | 11:40:08 | Delivery watchdog fails the task (10 min). `KilledByRequest`. Tokens 0/0. Ephemeral agent deleted (any `DeliveryUnverified` row would cascade with it). |
| 10:40:11 | 11:40:11 | `TranscriptMissing` standalone alert: child exited without a rollout "although input had been delivered to it." |

## What the last frame actually is

Stripped tail of `C:\logs\antiphon\session-runner\41959e819fdf4c00a31bfc1facc8c4ea.ansi.log` (91,172 bytes):

- Banner: `OpenAI Codex (v0.151.0)`, `model: gpt-5.6-terra xhigh`, YOLO, `C:\src\Antiphon`.
- `Starting MCP servers (1/2): node_repl (1s  esc to interrupt)` still visible (offset 88,984 / 91,172).
- Composer: the entire 638-char pointer, twice (wrapped body + bottom `>` row). Unique to this fail vs today's successes: `tab to queue message`.
- Zero `Working (` in the whole log. Frame ends `ESC[?2026l`.

Successful same-day Codex Plan ANSI logs (CARD-0304 `f83bb10b…`, CARD-0298 `c52e24f3…`, CARD-0030 `eb702add…`) are 600 KB–1 MB, also show `gpt-5.6-terra`, sometimes show `Starting MCP` **early**, never freeze with it still on the last frame, and never show `tab to queue message`.

## Code path (delegate brief, not boot prompt)

1. `AgentTaskDispatcher.DispatchOneAsync` launches via `_launchQueue.EnqueueInteractiveSession` and enqueues the brief `WhenIdle` / `QueuedMessageOrigin.Delegation`. Comment in that method is load-bearing: the brief goes through the queue, never `adapter.SendPromptAsync`.
2. `CodexSubmitConfirmation` (20 s, 3 extra Enters, throws if the transcript is live) therefore **never ran**.
3. `DeliverAsync` typed the pointer, saw composer evidence, settled ≤3 s, sent one `\r`, then `WaitForTranscriptConfirmAsync` on the unobservable branch (zero `TranscriptEntries` — Codex creates the rollout on first submit).
4. For Codex, `sawPositiveSubmit = SubmitEvidence.IsPositive(...)`: Working indicator **or** head fragment visible before Enter and gone after.
5. Unobservable deadline: positive → degraded `Delivered` + `RecordDeliveryUnverifiedAsync`; none → `NoSubmitOutput` and re-Enter every 7 s up to 3. Production took the first arm after **one** Enter.

`SubmitEvidence` emptied-composer is a single-poll latch (`if (!sawPositiveSubmit)`). A mid-redraw snapshot whose rendered screen does not contain the 40-char head (empty, ghost hint, MCP spinner frame) latches true forever. Later polls that still show the body cannot un-latch, and `mayReEnter` is `observable || !sawPositiveSubmit`.

That is how a session whose **last** frame still holds the brief is certified `Sent` with 1 Enter.

## What was ruled out

- **`cx.ps1` / ACP wrapper.** Definition `Exe` is `codex.cmd`. Pty-host log: `Launched C:\Users\lndco\AppData\Roaming\npm\codex.cmd`.
- **Antiphon never sending `\r`.** The 1-Enter log line is the confirm loop's own counter (`entersSent = 1` at the submitting write).
- **PasteBurst as the remaining primary mechanism.** `-c disable_paste_burst=true` is still composed before `developer_instructions` (so it is not the arg that a huge `-c` value could swallow). Rate dropped 11.5 % → 5.5 %; this incident still matches the post-S1 "1 Enter, degraded Sent" log line CARD-0133 named as the thing S1 should have stopped.
- **Model mismatch as the Enter cause.** Fail and successes all banner `gpt-5.6-terra`. Config default is terra. `xhigh` on the fail proves the Frontier `-c model_reasoning_effort` landed.
- **Transcript bind refusing a real rollout.** Runner: no rollout written under `C:\Users\lndco\.codex\sessions` for this child at all. Sidecar `transcriptPath: null` for the whole life.

## Recurrence

`scripts/codex-boot-census.ps1` shape, queried 2026-09-01: Codex sessions since 2026-08-27 with 0 transcript rows and brief `Sent`:

| Started (UTC) | Session | Task | Title |
|---|---|---|---|
| 2026-08-27 20:24 | `5ab31a20` | `a35b7242` | CARD-0216 S2-S5 |
| 2026-08-30 04:36 | `a3631539` | `5fa603cb` | CARD-0230 investigation |
| 2026-09-01 10:30 | `41959e81` | `4ea068fa` | CARD-0288 Plan (this card) |

55 Codex sessions, 48 with a transcript, 3 wedges. Same-day Codex Plan after this one: `4c03891e` (CARD-0030), `45f3db13` (CARD-0304), `a0de6293` (CARD-0298), `a75fda51` (CARD-0032) — all `Succeeded` with 8–10 transcript rows and 166k–210k input tokens.

## Recommended fix shape (do not widen timeouts)

Priority is the reverse of "add a readiness probe":

1. **S1 hole (small, unblocks the 30 s path).** At the unobservable deadline, re-evaluate `SubmitEvidence` against the *current* screen. If `HeadFragmentIsVisible(screenNow, body)`, it is not a submit — `NoSubmitOutput`, keep re-Entering until `SubmitAttempts`. Do not latch emptied-composer on a single poll; require the head gone for `PostEvidenceSettleMs` (500 ms) of consecutive snapshots. Pin: a fake that echoes the body, then emits one empty/ghost frame, then the body again, must send 3 Enters and return `NoSubmitOutput` (today would `Sent` / 1 Enter). Existing `Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters` stays; it never saw the transient-empty frame.

2. **CARD-0133 S3.** Both Codex adapters: after quiet + trust, wait until `Starting MCP servers` / `Booting MCP server` have been absent 500 ms, bound `CodexBootStatusMaxWaitMs` (10 s). This session froze with that line still up; CARD-0195 already measured typing during it as MCP-interrupt / queued-input. Ready must not fire on a 1 Hz boot spinner going quiet because it hung.

3. **CARD-0133 S2.** `NoSubmitOutput` on a cold Codex delegate's first delivery (null baseline, origin Delegation, attempts 1) → `BootWedged` + kill + one relaunch. Today's follow-through is revert → 60 s sweep → re-type into a frozen TUI → park → 10-minute watchdog. Against a child that stopped painting at +14 s every one of those is theatre. Four later dispatches the same day succeeded on a fresh process.

Do not widen `TranscriptConfirmTimeoutSeconds` (30), `DeliveryFailTimeoutMinutes` (10), or `CodexReadyQuietPeriodMs`. A frozen TUI is silent forever.

`disable_paste_burst` stays. It is not sufficient.

## Artifacts

- ANSI: `C:\logs\antiphon\session-runner\41959e819fdf4c00a31bfc1facc8c4ea.ansi.log`
- Pty-host: `C:\logs\antiphon\session-runner\pty-hosts\logs\41959e819fdf4c00a31bfc1facc8c4ea.log`
- Sidecar: `C:\logs\antiphon\session-runner\transcripts\41959e819fdf4c00a31bfc1facc8c4ea.json`
- Server: `C:\src\Antiphon\server\logs\antiphon-20260901.log` lines 4582–4596, 4691, 4696
- Runner: `C:\src\Antiphon\logs\session-runner.log` 11:31:06 / 11:36:06 / 11:40:11 WRN
- Precedent: `docs/superpowers/plans/2026-08-27-card-0133-codex-readiness-and-boot-wedge-plan.md`, CARD-0133 (Backlog; S1 shipped `09a6a8ba`, S2–S4 not)
