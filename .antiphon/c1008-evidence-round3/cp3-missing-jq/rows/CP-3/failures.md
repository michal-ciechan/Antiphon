## Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance
outcome: Failed
durationSeconds: 0.2527764
baseline: n/a
message:
ShouldAssertException: run.Output
    should contain (case insensitive comparison)
"RunnerBusy"
    but was actually
"DIAGNOSIS=RecycleToolsMissing
"
stack:
   at Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance() in /work/worktrees/task-3aebd909/tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:line 2743
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
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-3 --project tests/Antiphon.Tests --output-path bin-c1008-cache/ --filter "/*/*/RemoteScriptContractTests/C849_Deploy_prepares_and_verifies_before_acceptance" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1008-cache/ -- --treenode-filter "/*/*/RemoteScriptContractTests/C849_Deploy_prepares_and_verifies_before_acceptance"

