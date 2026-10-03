## Antiphon.Tests.Checkpoints.PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery
outcome: Failed
durationSeconds: 0.3236131
baseline: n/a
message:
ShouldAssertException: Run(conditionalSibling).ExitCode
    should be
0
    but was
1

Additional Info:
    c1005-conditional-sibling
stack:
   at Antiphon.Tests.Checkpoints.PlanCoverageCensusTests.census_reports_unmapped_selection_without_dynamic_discovery() in /work/worktrees/task-b288ec96/tests/Antiphon.Tests/Checkpoints/PlanCoverageCensusTests.cs:line 286
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
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-1 --project tests/Antiphon.Tests --output-path bin-c1005-final/ --filter "/*/*/PlanCoverageCensusTests/census_reports_unmapped_selection_without_dynamic_discovery" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1005-final/ -- --treenode-filter "/*/*/PlanCoverageCensusTests/census_reports_unmapped_selection_without_dynamic_discovery"

