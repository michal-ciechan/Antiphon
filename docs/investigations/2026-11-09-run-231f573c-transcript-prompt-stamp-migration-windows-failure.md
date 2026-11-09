# Run 231f573c, chunk all-group-96: Card1773TranscriptPromptStampMigrationTests on Windows

Run 231f573c on rc/2026-11-13 (candidate `rc-2026-11-13-653f1145de5b`, checkout
`C:\Antiphon\releases\checkout-0b36ae35` at `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`), desktop,
Windows 10.0.19045, .NET 9.0.22, TUnit 1.44.0.0, Microsoft Testing Platform 2.2.2.
I read the following files and changed none of them:

- `logs/2026-11-09-0440-231f573c/antiphon-all-group-96-tests.log`
- `logs/2026-11-09-0440-231f573c/antiphon-all-group-96.trx`
- `logs/2026-11-09-0440-231f573c/antiphon-all-group-96.timing.json`
- `logs/2026-11-09-0440-231f573c/antiphon-all-group-96-execution/log_261109082433846.diag`

## Verdict

The failure is **deterministic and platform-dependent**. It is not caused by load or timing. The test
asserts an exact string that ends in a hard-coded `"\n"`. EF Core's migration SQL generator ends each
`SqlOperation` command with `AppendLine`, which writes `Environment.NewLine`. That is `"\r\n"` on
Windows. So the assertion fails on every Windows run and passes on every Linux run.

## Failed test

- Test: `Antiphon.Tests.Application.Card1773TranscriptPromptStampMigrationTests.Up_renames_an_invalid_index_drops_it_and_creates_if_missing`
- Categories: `Integration`, `Slow`; `[NotInParallel("Card1773PromptStampMigration")]`
- Outcome: Failed. Duration `00:00:03.8986707`, from `2026-11-09T08:24:43.9112353Z` to `08:24:47.8099060Z`.
- Phase id: `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.Card1773TranscriptPromptStampMigrationTests.1.1.Up_renames_an_invalid_index_drops_it_and_creates_if_missing.1.1.0`

### Error message (tests.log, verbatim)

```text
failed Up_renames_an_invalid_index_drops_it_and_creates_if_missing (3s 898ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: commands[1].CommandText
      should be
  "DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence_invalid";
  "
      but was
  "DROP INDEX CONCURRENTLY IF EXISTS "IX_TranscriptEntries_AgentSessionId_Kind_Sequence_invalid";
  "
      difference
  Difference     |                                                                                                      |    |
                 |                                                                                                     \|/  \|/
  Index          | ...  75   76   77   78   79   80   81   82   83   84   85   86   87   88   89   90   91   92   93   94   95
  Expected Value | ...  _    S    e    q    u    e    n    c    e    _    i    n    v    a    l    i    d    "    ;    \n
  Actual Value   | ...  _    S    e    q    u    e    n    c    e    _    i    n    v    a    l    i    d    "    ;    \r   \n
  Expected Code  | ...  95   83   101  113  117  101  110  99   101  95   105  110  118  97   108  105  100  34   59   10
  Actual Code    | ...  95   83   101  113  117  101  110  99   101  95   105  110  118  97   108  105  100  34   59   13   10
```

### Stack (complete as reported; TUnit omits its internals)

```text
    at Antiphon.Tests.Application.Card1773TranscriptPromptStampMigrationTests.Up_renames_an_invalid_index_drops_it_and_creates_if_missing() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\Card1773TranscriptPromptStampMigrationTests.cs:41
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

The diag file (lines 630-689) contains the same message and stack. It gives the frame as
`...Card1773TranscriptPromptStampMigrationTests.cs:line 41`.

### Timeout, process, git, pwsh and path text

- No timeout text for this test. The diag file contains only
  `TESTINGPLATFORM_DEFAULT_HANG_TIMEOUT: ''` at line 18, which is run setup.
- This is a pure metadata test: it uses `Metadata()` and `IMigrationsSqlGenerator.Generate`. It starts no
  process and runs no git or pwsh command. No such text appears for it.
- The only path is the source frame
  `C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Application\Card1773TranscriptPromptStampMigrationTests.cs:41`.
- TRX RunInfo: `Error 2026-11-09T08:28:05.048487 Exit code indicates failure: '2'`. This is the
  normal "a test failed" exit, not a crash.

## Source at line 41 (rc 653f1145 = master, unchanged)

```csharp
// Generate(SqlOperation) calls AppendLine, so each command ends with a newline.
commands[1].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}_invalid\";\n");   // line 41
...
down[0].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";\n");              // line 48
down[1].CommandText.ShouldBe($"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}_invalid\";\n");      // line 49
```

The run never reached lines 48 and 49. They contain the same hard-coded `\n` and will fail next on
Windows if only line 41 is fixed.

Introducing commit: `57bbfc99d1810c99f2bf6e1a928427918c05d1fd`, *test(card-1773): expect the
generator newline on retry drops*. It was authored by `antiphon-server2-runner` (Linux) on
2026-11-08 14:46Z and cherry-picked from `938bd7c13c0b0a71d6e83a5ea66b2a2560510c1b`. It changed the three
exact-match assertions from no trailing newline to a literal `\n`. That is correct on Linux, where
`Environment.NewLine == "\n"`, and wrong on Windows. The commit is an ancestor of both `origin/master`
and the rc tip `653f1145`.

## Neighbouring tests in the chunk

Chunk totals: 203 tests, 201 passed, 1 failed, 1 skipped. The skipped test was
`C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation`, which is Linux-only by
design. Run wall time 3m31s; TRX ran 08:24:34Z to 08:28:05Z.

| Test (class) | Outcome | Duration |
|---|---|---|
| Card1773...`Up_renames_an_invalid_index_drops_it_and_creates_if_missing` | **Failed** | 3.899 s |
| Card1773...`Retry_replaces_an_interrupted_invalid_index` (real Postgres) | Passed | 31.727 s |
| Card1773...`Retry_finishes_cleanup_after_invalid_index_rename` (real Postgres) | Passed | 8.560 s |
| ClaudeEffortByRoleDispatchTests `a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override` (same start tick) | Passed | 5.453 s |
| ContextCompactionAgentTests `CreateAsync_persists_null_overrides...` / `UpdateAsync_persists_overrides...` (same start tick) | Passed | 2.089 s / 2.355 s |
| C0824RefOnlyBoundaryTests `Incomplete_preparation_refuses_initial_and_resumed_land` (same start tick) | Passed | 16.630 s |
| GrokRulesCompositionTests `Standing_channel_composition...(Herdr)` / `(PtyHost)` | Passed | 5.651 s / 5.640 s |

The two Retry tests in the same class ran against a real migration database and passed. On Windows
they exercise the actual recovery SQL. That is evidence that the migration itself works on Windows
and only the string witness is wrong.

## Host load hints (timing.json)

`buildSeconds 468.58`, `buildSlot not-used`, `queueWaitSeconds 0`, `runWallSeconds 212.338`,
`fixtureSetupSeconds 2.784`, `processSpawnSeconds 1.865`, `processWaitSeconds 46.619`,
`p50 0.092 s`, `p95 7.359 s`, `max 35.676 s` (`All_desktop_consumers_skip_ref_only_without_deleting_refs`).
The 3.9 s for a metadata-only test is mostly first-use EF model build in a host where about 20 tests
started in the same tick. It is load-inflated, but load has no bearing on the outcome: the assertion
compares bytes and fails identically on any Windows host.

## Diagnosis

1. tests.log shows only the `\r\n` versus `\n` byte difference (codes `13 10` versus `10`) at index
   94-95. Every other character of the SQL is identical.
2. Line 41 hard-codes `\n`. EF's `MigrationCommandListBuilder.AppendLine` writes `Environment.NewLine`.
3. The assertion was authored and verified only on server2 (Linux) in `57bbfc99d`. Before that commit
   the expectation had no newline, which failed on Linux. The repair made the test Linux-only.

The failure is deterministic. It will fail on every Windows run of this chunk and on every rerun.
Do not retry it.

## Suggested fix (for the Debug stage; not applied here)

Make the three exact-match assertions newline-agnostic. Either compare against
`$"...;{Environment.NewLine}"`, or normalise with `.ReplaceLineEndings("\n")` before `ShouldBe`.
Change lines 41, 48 and 49. Native proof: run the single method on Windows with
`--treenode-filter "/*/*/Card1773TranscriptPromptStampMigrationTests/Up_renames_an_invalid_index_drops_it_and_creates_if_missing"`.

## Why missed (facts for the Debug why-missed note)

- Introducing commit: `57bbfc99d1810c99f2bf6e1a928427918c05d1fd`, cherry-picked from
  `938bd7c13c0b0a71d6e83a5ea66b2a2560510c1b` (CARD-1773).
- Outside the selection: the repair was verified on server2 (Linux) only. No Windows run of
  `Card1773TranscriptPromptStampMigrationTests` happened before the rc cut. The class is
  `Integration`/`Slow`, which the minimal Linux green covered.
- Cheapest pre-land check: a change under `tests/Antiphon.Tests/**/*Migration*Tests.cs` that asserts
  generated SQL text needs one Windows run of that class:
  `/*/*/Card1773TranscriptPromptStampMigrationTests/*`.
  A cheaper static rule is to flag `ShouldBe(...\n")` against `CommandText`.
