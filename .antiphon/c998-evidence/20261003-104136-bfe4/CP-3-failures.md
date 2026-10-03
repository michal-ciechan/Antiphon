## Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration
outcome: Failed
durationSeconds: 0.264849
baseline: n/a
message:
ShouldAssertException: methodBoth
    should be empty but had
1
    item and was
["tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs::CheckpointTempUsageTests (Integration class has a Unit method)"]

Additional Info:
    methods that inherit one lane and declare the other: tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs::CheckpointTempUsageTests (Integration class has a Unit method)
stack:
   at Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration() in /work/worktrees/task-3eceba1b/tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs:line 54
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
dotnet run --project tools/Antiphon.Checkpoints -- row --name CP-3 --project tests/Antiphon.Tests --output-path bin-c998-unit/ --filter "/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration" --no-build
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c998-unit/ -- --treenode-filter "/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration"

