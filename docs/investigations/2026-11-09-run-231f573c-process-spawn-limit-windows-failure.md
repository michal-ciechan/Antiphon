# Run 231f573c (rc/2026-11-13) - antiphon-all-group-2 failure: ProcessSpawnLimitTests

Read-only extraction from the desktop qualification logs, 2026-11-09. No build, no test, no code change.

- Run: 231f573c834141c2acab4fe0f0ef8847, rc/2026-11-13 = `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` (same SHA as origin/master at the time of reading)
- Logs: `C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b\logs\2026-11-09-0440-231f573c\`
  - `antiphon-all-group-2-tests.log` (3385 B), `antiphon-all-group-2.trx` (382906 B), `antiphon-all-group-2.timing.json`,
    `antiphon-all-group-2-execution\log_261109054145043.diag` (747535 B)
- Checkout under test: `C:\Antiphon\releases\checkout-0b36ae35`

## Diagnosis (short)

**This is not a process-spawn, timing, load, pwsh, git or XDG_CACHE_HOME failure.** The failing test is a
static reflection roster guard that took 102 ms. It reports that one class is on the wrong limiter:

- `Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests` carries `[ParallelLimiter<ProcessSpawnLimit>]`
  (`tests/Antiphon.Tests/Checkpoints/PhaseJournalKillDiagnosticTests.cs:14`). The guard requires every class in
  the Checkpoints namespace that is not in `SharedCheckoutCheckpointClasses` to carry
  `[ParallelLimiter<CheckpointToolSpawnLimit>]` and not the strict one
  (`tests/Antiphon.Tests/ProcessSpawnLimitTests.cs:245-251`, assert at `:274`).

This is a **semantic merge collision** between two lands that were each green on their own base:

| Commit | Author date (UTC) | Commit/land date (UTC) | What |
|---|---|---|---|
| `292f49dfa` | 2026-11-08 00:08 | 2026-11-08 04:01 | Adds the guard `Checkpoint_tool_classes_carry_their_own_limiter` (red first) |
| `e51d4375a` | 2026-11-08 01:12 | 2026-11-08 04:01 | Moves the existing checkpoint-tool classes to `CheckpointToolSpawnLimit` (green) |
| `be4c317a3` | 2026-11-08 05:26 | 2026-11-08 09:37 | CARD-1748 adds the new class `PhaseJournalKillDiagnosticTests` with the old `[ParallelLimiter<ProcessSpawnLimit>]` |
| `8b7092b62`, `ccc472160`, `d0a7add59` | 05:42-07:04 | 09:37 | CARD-1748 fix/witness commits; the limiter attribute is never changed |

CARD-1748 copied the pre-roster limiter convention into a new Checkpoints class. Its commits were landed on top of
the roster commits (`292f49dfa` is an ancestor of `be4c317a3`), but nothing in the CARD-1748 Code / Review / land
path ran `ProcessSpawnLimitTests` against the final tree (inference: minimal-testing briefs run only the card's own
filters; the land does one compile, not the guard suite).

**The failure is deterministic and platform-independent** (pure reflection over attributes). It is red on
origin/master `653f1145d` and on every rc that contains both `e51d4375a` and `be4c317a3`: rc/2026-11-12
(`5fef2c23b`) and rc/2026-11-13. rc/2026-11-11 (`3364197bf`) has the roster but not CARD-1748, so it is green there.

### Why the rc run was the first to catch it

- The previous live run a6410818 (rc/2026-11-12, which already contained the collision) produced 58
  `antiphon-all-group-*.trx` files but **no `antiphon-all-group-2.trx`** - group 2 never ran there before that
  run was superseded, so the defect sat unobserved for a whole rc generation.
- The CARD-1748 pipeline never ran the cross-cutting roster guards (`ProcessSpawnLimitTests`) on its rebased
  tree. A new test class in `tests/Antiphon.Tests/Checkpoints/` is exactly the input that guard exists to check.
- Linux fixers and Reviews on server2 run narrow `--treenode-filter` selections; `ProcessSpawnLimitTests` is in
  no card's narrow filter unless the card touches limiters.

### Repair (for the Debug stage; not done here)

One-line attribute change in `tests/Antiphon.Tests/Checkpoints/PhaseJournalKillDiagnosticTests.cs:14`:
`[ParallelLimiter<ProcessSpawnLimit>]` -> `[ParallelLimiter<CheckpointToolSpawnLimit>]` (unless the class shares
the checkout, in which case add it to `SharedCheckoutCheckpointClasses` and the strict roster instead; it uses
`FakeDriver`, so the tool lane is the correct one). Verify with
`--treenode-filter "/*/Antiphon.Tests/ProcessSpawnLimitTests/*"` plus `/*/Antiphon.Tests.Checkpoints/PhaseJournalKillDiagnosticTests/*`.
Goes to master and to rc (both contain it). Prevention worth a card: run `ProcessSpawnLimitTests` (all static
roster guards; ~3 s) in every land's final-tree check, or whenever a diff adds a `[ParallelLimiter<...>]` attribute.

## Extracted evidence

### Failing test

- Name: `Antiphon.Tests.ProcessSpawnLimitTests.Checkpoint_tool_classes_carry_their_own_limiter`
- Outcome: Failed; duration `00:00:00.1028331` (log: `102ms`)
- Start/end: `2026-11-09T05:41:55.4112202Z` / `05:41:55.5140533Z`; computer `DESKTOP-KTLKPIF`
- Phase id: `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.ProcessSpawnLimitTests.1.1.Checkpoint_tool_classes_carry_their_own_limiter.1.1.0`

### Error message and stack (verbatim from antiphon-all-group-2-tests.log; full stack is 17 lines, TUnit internals omitted by the runner)

```
failed Checkpoint_tool_classes_carry_their_own_limiter (102ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: misses
      should be empty but had
  2
      items and was
  ["Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must carry [ParallelLimiter<CheckpointToolSpawnLimit>]", "Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must not keep [ParallelLimiter<ProcessSpawnLimit>]"]

  Additional Info:
      Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must carry [ParallelLimiter<CheckpointToolSpawnLimit>]
  Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must not keep [ParallelLimiter<ProcessSpawnLimit>]
    at Antiphon.Tests.ProcessSpawnLimitTests.Checkpoint_tool_classes_carry_their_own_limiter() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\ProcessSpawnLimitTests.cs:274
    at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
    --- End of stack trace from previous location ---
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

### Source at the failing line (rc 653f1145d)

`tests/Antiphon.Tests/ProcessSpawnLimitTests.cs:245-251,273-274`:

```csharp
else
{
    if (!tool)
        misses.Add(type.FullName + " must carry [ParallelLimiter<CheckpointToolSpawnLimit>]");
    if (strict)
        misses.Add(type.FullName + " must not keep [ParallelLimiter<ProcessSpawnLimit>]");
}
...
limited.ShouldBeGreaterThan(0, "the Checkpoints namespace has process-limited classes");
misses.ShouldBeEmpty(string.Join(Environment.NewLine, misses));
```

`tests/Antiphon.Tests/Checkpoints/PhaseJournalKillDiagnosticTests.cs:9-15`:

```csharp
/// CARD-1748: a Unit row killed at its cap (no TRX) must print journal-derived
/// completed/pending counts. Those counts are a diagnostic, never a receipt.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PhaseJournalKillDiagnosticTests : CheckpointTestBase
```

Other Checkpoints classes still on the strict limiter at rc (all legitimate shared-checkout members, none reported
by the guard): `CheckpointRepeatBuildIsolationTests`, `CheckpointRepeatHostTests`, `CheckpointSourceExecutionTests`,
`CheckpointStatePublicationTests`, `CheckpointTerminalReceiptTests`, `RerunFilterHostTests`.

### Process-spawn / limiter / pwsh / git / XDG_CACHE_HOME / timeout text

- No `XDG_CACHE_HOME`, pwsh, git, spawn or timeout text accompanies the failure; the only limiter text is the
  assertion message above.
- diag: `ProcessorCount: 8`; `TESTINGPLATFORM_DEFAULT_HANG_TIMEOUT: ''` (no hang timeout set). No other matches.

### Neighbouring limiter tests in the chunk (all in ProcessSpawnLimitTests, start time / duration / outcome)

```
05:41:55.409  0.0918964  Passed  No_class_carries_both_limiters
05:41:55.411  0.1028331  Failed  Checkpoint_tool_classes_carry_their_own_limiter
05:41:55.411  2.4766381  Passed  Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers
05:41:55.412  0.0003772  Passed  Live_runner_send_input_method_carries_the_limiter
05:41:55.415  0.0004833  Passed  C1514_Checkpoint_workers_are_separate_from_the_git_lane
05:41:55.474  0.0007620  Passed  Process_spawning_classes_carry_the_limiter
05:41:55.476  0.0003341  Passed  Git_only_classes_carry_the_git_limiter
05:41:55.4xx  <0.0005    Passed  Git_lane_limit_* / Checkpoint_tool_lane_limit_honours_the_override(*) (all rows)
```

### Chunk summary

- 133 total, 1 failed, 132 passed, 0 skipped; TUnit duration 1m 07.989s; runWallSeconds 68.725; buildSeconds 468.58;
  fixtureSetupSeconds 3.846; p50 0.008 s, p95 1.319 s, max 34.713 s.
- Classes: ProcessSpawnLimitTests 37, AgentTuiProfileServiceTests 22, DispatchCrashWarningTests 21,
  CachingChatClientTests 13, AgentTuiPersistenceTests 12, SmokeTests 10, ProgressAwareWaitTests 8,
  AgentTuiProfileConcurrencyTests 4, LandDeliveryWaitRoutingTests 4, AgentTuiProfileImporterBackfillTests 1,
  ProgramStartupConcurrencyTests 1.
- Slowest: `Two_entry_point_invocations_racing_on_startup_both_serve` 34.713 s,
  `Migration_downgrades_the_five_tables_and_four_columns_cleanly` 12.634 s,
  `OrchestratorStateApi_snapshot_includes_running_and_retry` 6.256 s,
  `Model_has_owned_profile_revisions_and_effective_session_contracts` 3.139 s,
  `Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers` 2.477 s. All passed.

### Host and tools (sampled at read time, 2026-11-09, not at failure time)

- TUnit v1.44.0.0, .NET 9.0.22, Microsoft Testing Platform v2.2.2, Windows 10.0.19045 win-x64
- CPU: Intel i7-6700K, 8 logical processors, LoadPercentage 43 (live run still in progress); memory 6.4 GB free of 31.9 GB
- `git` = `C:\Program Files\Git\cmd\git.exe`, git version 2.50.1.windows.1
- `pwsh` = WindowsApps `Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`, 7.6.9
- dotnet SDK 10.0.300

Load is irrelevant to this failure: the assertion is about attribute metadata and fails identically at any load.
