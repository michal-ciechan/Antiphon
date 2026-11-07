# Review CARD-1761 (CARD-1758 slice 1), ordinary round 1, Final/Full

Verdict: CLEAN. No blocker. Five advisories (A-1..A-5) for cards, none holds the land.

Subject: origin/feat/card-task-3d0b1390 tip `825296b0ed6e275a52951128beab1dff7f953e78`
(Code task 3d0b1390-4a31-4d59-bc4f-e87580b7f98d, Grok). Red `aeb5e66a3f2df63ace011b78fb3073d2b5fd7ea7`.
Base `0e7aef0379a073c206e8398dcaed539bc33eaf9b` (on master). Reviewed in the read-only mirror
/work/worktrees/task-08a29a5c at that tip; no source changed; the only commit on
feat/card-task-08a29a5c is this report.

Landable: `git merge-tree --write-tree origin/master HEAD` (master c2965dc7b1a377ce57aabfede628d5179bcdfaad)
is clean, tree `b3b1eef7c2e47f21ab8782718d55a749e7ca6e27`. `check-evidence-diff.ps1` base..head: commits=2
entries=0 violations=0 exit 0. No migration. `tests/test-execution-policy.json` unchanged. No OS-selected
production branch changed, so the OS-branch witness obligation is empty.

## Brief items

1. Call sites. Runner facts (src/Antiphon.SessionRunner/SessionRunnerRuntime.cs): one `_lastSequence` per
   session, monotonic (`Math.Max` everywhere, lines 2747, 3005, 4157, 4322, 4692, 4731), read under `_gate` by
   `ToDto()` (session GET), `GetBuffer()` and the snapshot. `HandleOutput` (4685) advances it in the same lock
   that appends `_liveBuffer` and feeds `_screen`.
   - `GetLastSequenceAsync` and `GetRunnerSequenceOrDefaultAsync` now read the session record: same field,
     same value; the `catch` fallback is unchanged. Consumers (prompt-start sequence, first-output wait,
     quiet waits, manual-turn quiet, VerifiedPromptSubmitter) see identical numbers.
   - Codex tick: one `GetSnapshotAsync` frame feeds `CodexTurnScreenTracker.Observe(renderedScreen, rawOutput,
     outputMark)` in the declared order (src/Antiphon.Agents.Pty/CodexDetectors.cs:134). Before, text, screen
     and sequence came from three reads; one frame is the rule delivery already uses (CARD-0574 D-1).
   - `WaitForOutputAsync` pty branch: a frame is held only while the session GET's `LastSequence` equals the
     frame's. Missed-change audit: sequence never resets inside a session object (restart and adopt restore
     `Max(_lastSequence, manifest lastSeq)`), no wraparound (int64), resize re-renders through `HandleOutput`,
     re-bind of the accepted generation does not touch the sequence, the screen is fed only by `HandleOutput`
     and the adopt replay. The raw text can change without the sequence in exactly two places:
     `ClearLiveBufferAsync` (4441) and `RebuildInterpretationFromAnsiLog` (4147). The clear is called only in
     the adapters' `SendPromptAsync`, before the wait starts and on the same sequential adapter; the replay
     re-feeds the same bytes. Neither can flip the done/idle/visible predicates, and the next chunk refreshes
     the frame. Non-pty (`herdr`) and a null `Backend` (old runner) keep a snapshot per poll, witnessed.
   - `TryGetLiveSnapshotAsync` default omits the tail. The only reader of `AgentSessionLiveSnapshot.Buffer` is
     the watchdog (WatchdogService.cs:72, :131), which now passes `WhenScreenEmpty`; the runtime reads the tail
     under the same `string.IsNullOrWhiteSpace(RenderedScreen)` predicate the watchdog applies. The UI backlog
     (`AgentSessionService.GetBufferAsync` -> `GetBufferSnapshotAsync` -> `GetBufferAsync`), the local-command
     poll (SessionMessageQueueService.cs:6071), reconciliation re-adopt and the diagnostics bundle still read
     the ANSI tail directly. The sync `TryGetLiveSnapshot` caller (BootReplyWatchdogService:444) never reads
     `.Buffer`.
2. Omitted ANSI tail: dialog detection is the runner-side `Dialog` on the snapshot (ProviderDialogObservationService
   unchanged, PC-9 witness); Codex and Grok parsers read `RawOutput` from the snapshot, never the tail; the
   watchdog keeps the tail for the empty-screen fallback (PC-10 witness plus the runtime empty-screen witness).
3. Old runner / new server: `LastSequence` has always been on `RunnerSessionDto`; `Backend` is CARD-0186, and a
   runner without it yields null, which keeps today's per-poll snapshot (witnessed). No wire change.
4. Transcript pull: untouched by the diff (`TryGetTranscriptAsync`, `CatchUpTranscriptAsync`, confirm loop).
5. Witnesses and PCs: see below.
6. Regressions elsewhere: none found. `EnsureStarted()` at the top of `WaitForOutputAsync` only changes a
   not-started session with a non-positive timeout from `false` to a throw; no caller does that.

## Witnesses

Red commit aeb5e66a3 carries 9 tests. Red at base was not re-executed here (budget); it holds by construction:
at base `GetLastSequenceAsync` and the manual-turn read use the buffer route (BufferReads 0 fails), the Codex
tick reads the buffer, the pty wait snapshots every poll (SnapshotReads 1 and 2 fail), and the screen-only
snapshot reads the tail; herdr, unknown-backend and dialog pass at base. That is 6 red, 3 green, matching Code.
Four tests (empty-screen, occupied-screen, Include, watchdog pin) were first committed in the fix commit; they
use the new `LiveAnsiTail` enum and cannot compile at base (A-4).

PC-1..PC-10 each map to a witness that fails under that mutant: PC-1 sequence (BufferReads 0), PC-2 manual turn,
PC-3 Codex (SnapshotReads == TranscriptReads+1, BufferReads 0), PC-4 stable sequence (SnapshotReads 1), PC-5
refresh (matched, SnapshotReads 2), PC-6 herdr/unknown (SnapshotReads > 2), PC-7 omit/occupied, PC-8
empty-screen/Include, PC-9 dialog (SnapshotReads 1, SessionGets 0), PC-10 watchdog text pin. All stay pending
for SourceLanding Mutation.

## Checkpoint rerun (one run, checkpoint tool, leased)

Plan: /work/worktrees/task-08a29a5c/.antiphon/c1761-review-plan.md (ignored). Run 20261107-173414-803e.
Build bin-c1761-rv (tests/Antiphon.Tests) cold, slot granted, 5m58s. Rows:

CHECKPOINT CP-1 commit=825296b0ed6e275a52951128beab1dff7f953e78 build=ok filter=/*/*/(Card1761RunnerReadPollTests*)|(RunnerCodexAdapterReadyTests*)|(RunnerStartupStaleObservationTests*)|(RunnerTerminalSessionGenerationTests*)|(RunnerTerminalSessionInputEncodingTests*)|(AgentSessionRuntimeBoundedReadTests*)/* executed=65 passed=65 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=825296b0ed6e275a52951128beab1dff7f953e78 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=45.466s wall=45.514s
CHECKPOINT CP-2 commit=825296b0ed6e275a52951128beab1dff7f953e78 build=reused filter=/*/*/(WatchdogServiceTests*)|(BootReplyWatchdogTests*)|(BootStallDetectionTests*)|(ClaudeDialogDeliveryTests*)|(CodexStartupDeliveryTests*)|(ExpectationSchedulingTests*)|(ExpectationSnapshotTests*)|(PhoneHomePendingInventoryTests*)|(RemoteControlRecoveryTests*)|(ExpectationWatchdogJobTests*)/* executed=191 passed=191 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=825296b0ed6e275a52951128beab1dff7f953e78 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=222.387s wall=222.433s
CHECKPOINT CP-3 commit=825296b0ed6e275a52951128beab1dff7f953e78 build=reused filter=/*/*/(RemoteControlMaintenanceQueueTests*)|(RemoteControlRecoveryPtyIntegrationTests*)/* executed=40 passed=40 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=825296b0ed6e275a52951128beab1dff7f953e78 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=55.652s wall=55.688s
CHECKPOINT CP-4 commit=825296b0ed6e275a52951128beab1dff7f953e78 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=825296b0ed6e275a52951128beab1dff7f953e78 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=107.323s wall=107.373s
BUDGET wall=830.111s execution=830.107s admissionOnly=0.004s
unlisted: none (the tool ran no other build or test command)
wall: 13m50s  sequential-equivalent: 7m11s  builds: 1  rows: 4 green 0 red 0 skipped
verdict: GREEN exit=0

`validate --evidence report.json --expected-source-sha 825296b0ed...`: `CHECKPOINT SOURCE VALID source=825296b0ed6e275a52951128beab1dff7f953e78 rows=4`, exit 0.
CP-1 TRX (created 2026-11-07T17:40:31Z) lists all 13 `Card1761_*` methods Passed.
Code's one skip (C1011, Windows-only) was not in this selection; every row here has skipped=0.
Unlisted runs: one leased `dotnet build tools/Antiphon.Checkpoints` (lease afff4427, 7 s) to run the tool
with `--no-build`; nothing else. The whole Unit lane was not run: the brief's REVIEW line commissions the
touched classes plus the guard batch, which is what ran (Code ran the same selection).
Performance: the longest row is CP-2 at 3m42s (CodexStartupDeliveryTests 100 s, BootStallDetectionTests 100 s);
nothing near 30 minutes.

## Advisories (cards, not blockers)

- A-1 Herdr session GET cost. Where: RunnerTerminalSession.GetLastSequenceAsync and
  AgentSessionRuntime.GetRunnerSequenceOrDefaultAsync. Failure: on a herdr session the runner's
  single-session GET (SessionRunnerRuntime.GetAsync:1114) runs TryStampHerdrVerifiedAsync (pane.get and
  pane.process-info under a pane lease) and RefreshHerdrSurfaceAsync (a status read) on every call, where the
  buffer route was a near-free ANSI-log read. The 50 ms quiet waits, the 100 ms manual turn and the 250 ms Grok
  turn now cost herdr RPCs at those rates. Why not a blocker: the sequence value is the same field, the quiet
  verdict only samples it more often, and herdr is an opt-in spike. Fix: keep the buffer route (or add a
  herdr-free session read) for non-pty backends in those two readers, or accept and note it in slice 3.
- A-2 Claude boot turn under active output now issues two calls per 50 ms tick (session GET, then snapshot)
  instead of one; quiet ticks drop to one small GET. Expected from the decision; record for slice 3.
- A-3 PC-10's witness is a source-text pin on WatchdogService.cs; a behavioural witness (WatchdogService over
  AgentSessionRuntime with the CountingRunner, empty screen -> BufferReads 1) would be stronger. The herdr and
  unknown-backend witnesses assert count ratios from a 250 ms wall-clock window (SnapshotReads > 2); mild
  flake risk under load.
- A-4 Four witnesses first appear in the fix commit (new enum, cannot compile at base). State this in the
  plan or split the enum into the red commit next time.
- A-5 EnsureStarted moved ahead of the deadline check in WaitForOutputAsync (behaviour change only for a
  not-started session with a non-positive timeout; no caller).

Residue: tools/Antiphon.Checkpoints/bin and obj (ignored) remain in the mirror; the tool removed bin-c1761-rv/.
