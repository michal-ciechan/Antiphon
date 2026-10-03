## Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census
outcome: Failed
durationSeconds: 5.02577
baseline: n/a
message:
ShouldAssertException: UsageLibrary.Errors(result.RootElement, "native")
    should be empty but had
1
    item and was
["full roster checkpoint cases=349 expected=290"]

Additional Info:
    the current compiled checkpoint roster clears the Full census and floor
stack:
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census() in /work/worktrees/task-3eceba1b/tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs:line 173
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
stdout:
[CARD-0204] SessionRunner__BaseUrl=http://127.0.0.1:1, Delegation__CheckInterpreterEnabled=false, Delegation__DiagnoseEnabled=false and Delegation__OutputDistillerEnabled=false for every Program boot in this assembly (inherited SessionRunner__BaseUrl='<unset>') - test hosts never launch on the production session-runner.
[CARD-0298] Hangfire__ServerEnabled=false for every Program boot in this assembly - test hosts never start a Hangfire worker (no WMI census, no production-runner list).
[CARD-0045] cleared inherited ANTIPHON_PTY_BACKEND='inbox' — pty tests declare their backend; the suite means the same thing whoever launched it.

rerun:
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-1 --project tests/Antiphon.Tests --output-path bin-c998-tests-red/ --filter "/*/*/CheckpointTempUsageTests/full_suite_checkpoint_floor_uses_the_current_census" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c998-tests-red/ -- --treenode-filter "/*/*/CheckpointTempUsageTests/full_suite_checkpoint_floor_uses_the_current_census"

## Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster
outcome: Failed
durationSeconds: 6.0672184
baseline: n/a
message:
ShouldAssertException: UsageLibrary.Errors(result.RootElement, "native")
    should be empty but had
1
    item and was
["namespace census selected=349 expected=290 executed=331 skipped=18"]
stack:
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster() in /work/worktrees/task-3eceba1b/tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs:line 113
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
stdout:

rerun:
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-1 --project tests/Antiphon.Tests --output-path bin-c998-tests-red/ --filter "/*/*/CheckpointTempUsageTests/namespace_census_uses_the_native_execution_roster" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c998-tests-red/ -- --treenode-filter "/*/*/CheckpointTempUsageTests/namespace_census_uses_the_native_execution_roster"

## Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_matches_compiled_checkpoint_cases
outcome: Failed
durationSeconds: 2.190653
baseline: n/a
message:
ShouldAssertException: result.RootElement.GetProperty("selected").GetInt32()
    should be
349
    but was
290

Additional Info:
    scripts/lib/checkpoint-usage.ps1 Get-NamespaceCensus is stale against the compiled checkpoint cases
stack:
   at Antiphon.Tests.Checkpoints.CheckpointTempUsageTests.namespace_census_matches_compiled_checkpoint_cases() in /work/worktrees/task-3eceba1b/tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs:line 138
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
stdout:

rerun:
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-1 --project tests/Antiphon.Tests --output-path bin-c998-tests-red/ --filter "/*/*/CheckpointTempUsageTests/namespace_census_matches_compiled_checkpoint_cases" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c998-tests-red/ -- --treenode-filter "/*/*/CheckpointTempUsageTests/namespace_census_matches_compiled_checkpoint_cases"

