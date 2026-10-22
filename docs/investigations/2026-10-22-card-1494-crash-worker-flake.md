# CARD-1494: CheckNote Legacy crash-worker flake and build-output writes

Debug task 50c720df, 2026-10-22, server2 Linux runner mirror at `e70a567107f56ffa35a9baa861f208d3ca5eea77`.
Read-only: no source changed.

## Verdict

This is a test-harness flake. No product defect has been shown. The fixed 25 s poll window starts
when the crash-worker child is spawned, so it also has to cover the child's start. That child is a
second full `Antiphon.Tests` TUnit host. Under server2 load (loadavg 22-41 on 24 cores), the child
took **11-25.6 s** just to reach its first marker. On a passing case, delivery then took one or two
more 5 s notification-scan passes. Whichever case runs into the slowest stretch of host load fails,
which is why the failing case changes from run to run.

The receipt refusal has a separate cause in the same fixture. The child root is a path relative to
the test host's working directory, and `dotnet run` for this Microsoft.Testing.Platform project
starts in the output directory. Every run therefore leaves
`tests/Antiphon.Tests/bin-<x>/.antiphon/acceptance/card-0079/crash-<guid>/`, and nothing deletes it.
When a case fails, the scanner child is never killed. It is orphaned, keeps writing into that root
after the test host has exited, and is still alive 81 s later.

## 1. How the test spawns and synchronises with the child

- `CheckCompactionFixture.CreateRoot()` (`tests/Antiphon.Tests/TestHelpers/CheckCompactionFixture.cs:13`)
  builds `Path.GetFullPath(".antiphon/acceptance/card-0079/crash-<guid>")`. That path is relative to
  `Environment.CurrentDirectory`. The method also writes an `owner` file (the parent pid).
- `CheckCompactionFixture.Start` runs `dotnet <Antiphon.Tests.dll> --treenode-filter
  /*/*/CheckCompactionCrashTests/Resume_reservation_survives_crash_without_a_second_launch` with
  `WorkingDirectory = Environment.CurrentDirectory`. It passes the JSON request in the
  `ANTIPHON_C79_CRASH_WORKER` environment variable. The child boots the whole TUnit host.
  `TestDbFixture.InitializeAsync` then dispatches it through `TestWorkerModes` to
  `CheckCompactionCrashWorker.RunAsync`. That method asserts the root lies under its own
  `cwd/.antiphon/acceptance/card-0079/` and runs under a 90 s budget.
- The child writes into the root:
  - `produced.json`, when the `legacy-note-produced` boundary is reached. Both produce-hold and the
    scanner's captured recovery reach it.
  - `held-<pid>`, at the requested hold.
  - `scans.txt`, appended at the end of each `AgentTaskLandNotificationHostedService` pass, whose
    loop delay is 5 s.
  - `resume-calls.txt` and `<pid>.episodes.txt`, from the sweep scenarios.
- The parent writes into the root: `stop`, plus `<pid>.stdout.log` and `<pid>.stderr.log` from
  `DrainAsync`.
- What the test waits for (`CheckNoteDeliveryHandoffTests.H5.cs:69,223,302`): a `UserPrompt`
  transcript row that matches the note body. The child's attached `FakeAgentProtocolAdapter.OnSubmitted`
  inserts that row (`BridgeQueueHarness.cs:275`). The poll runs every 100 ms until
  `DateTime.UtcNow + 25 s`, measured from just after `Start`, so the child's start time is inside
  the window.
- `WaitForHeldAsync(root, 1, 40 s)` returns silently when its budget runs out. The next read of
  `produced.json` then fails with a misleading `FileNotFoundException`.
- The four spawning cases are `Legacy_produced_note_survives_pre_enqueue_worker_death(False/True)`,
  which runs a producer child and then a scanner child, `Legacy_captured_note_survives_worker_death`
  and `Legacy_note_recovery_runs_with_checks_disabled`. The five `AssertLegacyReceiptAsync` cases are
  in-process and not affected. `CheckCompactionCrashTests` (7 users of `CreateRoot`/`Start`) shares the
  same fixture, so it has the same output-directory writes.

## 2. Reproduction on server2 (build-slot, `bin-c1494/`, filter `/*/*/CheckNoteDeliveryHandoffTests/(Legacy_produced*)|(Legacy_captured*)|(Legacy_note_recovery*)`)

Build: 3 m 24 s, slot granted with 0 s wait. Three repetitions; I stopped at three because of the
35-minute budget. The pattern was already clear.

| Run | Slot wait | Load start/end | Result | Duration |
|---|---|---|---|---|
| 1 | 150 s | 33.8 / 22.4 | 4/4 pass | 2 m 38 s |
| 2 | 150 s | 28.9 / 30.8 | 3/4 (captured red) | 2 m 31 s |
| 3 | 60 s | 30.8 / 41.2 | 1/4 (busy=True, captured, checks_disabled red) | 3 m 20 s |

That is 4 failures in 12 executions, each `prompts.Count should be 1 but was 0`. The timings below
come from a 100 ms file poller on the root and are measured from the child's spawn:

| Run | Case | Child start to first marker | Spawn to prompt (25 s limit) |
|---|---|---|---|
| 1 | produce producer | 11.7 s (held) | - |
| 1 | produce scanner | 8.3 s (scan 1) | 12.4 s pass |
| 1 | produce producer/scanner | 11.7 s / 10.4 s | 14.9 s pass |
| 1 | captured | 11.2 s (produced) | 14.1 s pass |
| 1 | checks_disabled | 15.7 s (scan 1) | 21.5 s pass |
| 2 | produce producer/scanner | 11.0 s / 11.6 s | 20.8 s pass |
| 2 | produce producer/scanner | 21.2 s / 17.9 s | 22.3 s pass |
| 2 | captured | 23.4 s (produced) | **red**: no scan pass before 25 s |
| 2 | checks_disabled | 15.9 s | 20.3 s pass |
| 3 | produce producer/scanner | 22.0 s / 14.5 s | 23.5 s pass |
| 3 | produce(True) producer/scanner | **25.6 s** / 21.6 s | **red** (53 s test) |
| 3 | captured | **25.2 s** (produced) | **red**: past the window before any scan |
| 3 | checks_disabled | 15.0 s | **red**: 3 scan passes by 25 s, no prompt |

Mechanism:

- Child host start is the dominant term. It ranged from 8.3 to 25.6 s and grew with load.
- After that, the passing cases delivered 1.6 to 8.9 s after the first scan marker, which is the
  first or second 5 s pass.
- Three of the four reds never reached a scan pass in time, so they are pure start latency.
- The checks_disabled red in run 3 (load 41) is the only one with scan passes inside the window: 3
  passes over about 10 s and no prompt. That is slower than any passing case (about 1 pass), but it
  happened at the highest load. The child logs to `NullLogger`, and its stderr is never drained on
  failure, so this run cannot separate a slow pass from a stuck delivery. The Code task's on-timeout
  diagnostic below is what settles it.
- Rejected causes:
  - Build-slot contention affects only the slot wait (60-150 s), which is outside the test.
  - EF/schema reconcile is not involved: the in-process `AssertLegacyReceiptAsync` cases never
    failed.
- Product path: a passing run delivers exactly one prompt and keeps the Check-run and CheckCount
  invariants. Nothing observed contradicts the Legacy delivery after a pre-enqueue worker death.

## 3. Build-output writes and `cleanup_refused`

- **Root location.** The parent's `Environment.CurrentDirectory` under
  `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-<x>/` is the output
  directory. Observed: every root was created at
  `tests/Antiphon.Tests/bin-c1494/.antiphon/acceptance/card-0079/crash-<guid>/`, and the child's guard
  inherits the same cwd. Code 5b7d64fd saw the same thing at
  `bin-c1464/.antiphon/acceptance/card-0079/crash-*`.
- **Files left behind.** Nothing deletes the root. After three runs it held 12 `crash-*` directories
  containing `owner`, `produced.json`, `held-<pid>`, `scans.txt`, `stop`, `<pid>.stdout.log` and
  `<pid>.stderr.log`.
- **Orphaned child.** When the `prompts.Count` assertion throws, the scanner never gets `stop` and
  is never killed: there is no `try/finally DrainAsync`. Child pid 1720, the run-3 checks_disabled
  scanner whose `Root` was in its environment, was still alive 81 s after spawn, after the test host
  had exited, and it exited only at its 90 s budget. Its `scans.txt` had 3 lines. The schema drop
  ends its scans, so no further passes reach the boundary.
- **What the validator forbids.** The checkpoint tool owns `<project>/bin-<x>/` as a sealed isolated
  output. It deletes only entries recorded at creation or seal, or adopted at the observed test-host
  exit under the custody rule (CARD-1368: regular single-link file, tool uid, ctime/mtime not after
  the observed exit; see `docs/testing-and-build.md` checkpoint runner, `OutputCleanup.CleanOwnedOutputs`).
  An orphan writing `scans.txt` (and `<pid>.*.log`) under `bin-<x>/.antiphon/acceptance/card-0079/`
  after the host exit leaves an unrecorded or changed entry. Cleanup then refuses, the receipt gets
  `CleanupRefusals`, and `ReportValidator.Validate` returns `cleanup_refused`
  (`tools/Antiphon.Checkpoints/Report/ReportValidator.cs:26`).
- **Inference, not measured.** I did not run the checkpoint tool here. The refusal in 5b7d64fd's run
  coincided with two crash-worker reds in CP-2, which matches the orphan path. A green run's writes
  happen inside the host window and should be adopted. Either way, the fix removes the writes from
  the build output.

## 4. Recommended Code task (about 45 min, closed checkpoints)

Scope: `tests/Antiphon.Tests/TestHelpers/CheckCompactionFixture.cs`,
`tests/Antiphon.Tests/TestHelpers/CheckCompactionCrashWorker.cs`,
`tests/Antiphon.Tests/Application/CheckNoteDeliveryHandoffTests.H5.cs`. Tests only; no product
change.

### Slice S1: red witnesses, committed first

1. `CheckCompactionFixture_root_is_outside_the_build_output` (Unit): `CreateRoot()` must not be under
   `AppContext.BaseDirectory` or `Environment.CurrentDirectory`. Red today.
2. `Crash_worker_is_killed_when_the_wait_fails` (Integration, ProcessSpawnLimit): start a `scan`
   child against an isolated schema. Throw inside the new `RunAsync`/`await using` handle before
   `stop`. Assert `Process.HasExited` within 10 s of disposal. Red today: the child lives until its
   90 s budget.

### Slice S2: the fix

- Give `CreateRoot()` a per-test temp root:
  `Directory.CreateTempSubdirectory("antiphon-c79-crash-")`. Pass the owned prefix to the child
  explicitly, for example in a `CrashWorkerRequest.OwnedRoot` field or a marker file, and keep the
  ownership guard. Do not fall back to a cwd-relative path. The parent deletes the root in `finally`;
  on red it keeps the root only when an env flag is set, and logs the path.
- Wrap each child in an `IAsyncDisposable` handle whose `DisposeAsync` writes `stop`, waits up to
  10 s, then runs `Kill(entireProcessTree)` and drains. Every test uses `await using`, so a failed
  assertion can no longer orphan a child.
- Replace each fixed 25 s window with a condition wait:
  1. Wait for child readiness, meaning the first `scans.txt` line, `produced.json` or `held-*`.
     Bound it at 90 s, and fail fast with the drained stderr if the child exits first.
  2. Then wait for the prompt. Bound it at 60 s, and fail fast if the child exits.
  3. On timeout, include in the failure message: the notification state, `NextAttemptAt`, the
     `SessionQueuedMessages` status and attempts, the `scans.txt` line count, and the child's
     stderr. This tells start latency apart from a stuck delivery, which is the open question from
     the run-3 checks_disabled red.
- Raise the child budget (90 s) above the parent's total bound (150 s), or make the parent's bounds
  sum to less than the child's budget.
- Make `WaitForHeldAsync` throw on timeout instead of returning silently, and give its budget the
  same 90 s.
- Lane: these classes are already `Integration` and `ProcessSpawnLimit`. Add `[Category("Slow")]`
  plus a `slow-tests-allowlist.txt` row only if the TRX shows a test over 5 s that is not already
  allowlisted. The guard is `TestClassificationGuardTests`. No new lane is needed.

### Checkpoints

| CP | Build | Filter | Expect |
|---|---|---|---|
| CP-1 | `bin-c1494/` | `/*/*/CheckCompactionFixtureTests/*` (new, Unit) | red at S1, green at S2 |
| CP-2 | `bin-c1494/` | `/*/*/CheckNoteDeliveryHandoffTests/(Legacy_produced*)\|(Legacy_captured*)\|(Legacy_note_recovery*)` with `repeat: 5` | 20/20, receipt valid (no `cleanup_refused`) |
| CP-3 | `bin-c1494/` | `/*/*/CheckCompactionCrashTests/*` | all green, receipt valid |

The memory notes the `(A*)|(B*)` method-segment form as the working OR grammar on TUnit 1.44.

### rc applicability

`CheckCompactionFixture.cs`, `CheckCompactionCrashWorker.cs`, `CheckNoteDeliveryHandoffTests.H5.cs`
and `CheckCompactionCrashTests.cs` are byte-identical on `origin/rc/2026-10-20`, so the fix
cherry-picks cleanly. It is a test-only port that unblocks rc receipts as well.
