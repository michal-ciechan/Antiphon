## Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets
outcome: Failed
durationSeconds: 24.2974448
baseline: n/a
message:
ShouldAssertException: run.Output
    should not contain (case insensitive comparison)
"SENTINEL_C1008_HTTP_CREDENTIAL"
    but was actually
"SENTINEL_C1008_HTTP_CREDENTIAL
Rolling deploy stopped: RecycleTaskCensusUnknown
"
stack:
   at Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets() in /work/worktrees/task-5fcba512/tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:line 327
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
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-2 --project tests/Antiphon.Tests --output-path bin-c1008-scripts/ --filter "/*/*/RollingVolumeRecycleScriptTests/C1008_Refusal_receipts_do_not_leak_secrets" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1008-scripts/ -- --treenode-filter "/*/*/RollingVolumeRecycleScriptTests/C1008_Refusal_receipts_do_not_leak_secrets"

