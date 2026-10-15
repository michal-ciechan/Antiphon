# Intentional D-13 regression input: one native TUnit 1.44/MTP 2.2.2 failure and
# its matching TRX record, selected from the 2026-10-15 RC run (task 1c14bf28).
# This fixture is consumed by NightlyExecutionDiagnosticTests, not qualification evidence.
$D13FailureRecord = @'
TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0 }, DisplayName = C488_ApprovalPollingCannotConfirm, Properties = [ TimingProperty { GlobalTiming = TimingInfo { StartTime = 10/15/2026 22:44:18 +00:00, EndTime = 10/15/2026 22:44:18 +00:00, Duration = 00:00:00.0115161 }, StepTimings = [] }, TrxExceptionProperty { Message = ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
FalseStackTrace =    at Antiphon.E2E.Fixtures.LandDeliveryFixture.InitializeAsync(Boolean busy, String cut, Boolean dispatch, String completionForm) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:line 59
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 238
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 271
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalPollingCannotConfirm() in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 447
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) --- }, TrxCategoriesProperty { Categories = [OptIn] }, TrxFullyQualifiedTypeNameProperty { FullyQualifiedTypeName = Antiphon.E2E.AgentTaskLandDeliveryE2ETests }, TestMetadataProperty { Key = OptIn, Value =  }, TestMethodIdentifierProperty { AssemblyFullName = Antiphon.E2E, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Antiphon.E2E, TypeName = AgentTaskLandDeliveryE2ETests, MethodName = C488_ApprovalPollingCannotConfirm, MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Threading.Tasks.Task }, TestFileLocationProperty { FilePath = /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs, LineSpan = LinePositionSpan { Start = LinePosition { Line = 446, Column = 0 }, End = LinePosition { Line = 446, Column = 0 } } }, ErrorTestNodeStateProperty { Explanation = [Test Failure] ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
False, Exception = TUnit.Engine.Exceptions.TestFailedException: ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
False
   at Antiphon.E2E.Fixtures.LandDeliveryFixture.InitializeAsync(Boolean busy, String cut, Boolean dispatch, String completionForm) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:line 59
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 238
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 271
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalPollingCannotConfirm() in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 447
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) --- } ] } }
'@
$D13FailureTrx = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions><UnitTest name="C488_ApprovalPollingCannotConfirm" storage="/work/worktrees/task-1c14bf28/tests/antiphon.e2e/bin-rce2e-1c14bf28/antiphon.e2e.dll" id="1ecad690-e92d-deac-60f3-68172f7c0114" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestCategory><TestCategoryItem TestCategory="OptIn" /></TestCategory><Execution id="d838b049-257f-4d21-ba61-dedc57fc6606" /><Properties><Property><Key>OptIn</Key><Value></Value></Property></Properties><TestMethod codeBase="/work/worktrees/task-1c14bf28/tests/Antiphon.E2E/bin-rce2e-1c14bf28/Antiphon.E2E.dll" adapterTypeName="executor://TUnitExtension/1.44.0.0" className="Antiphon.E2E.AgentTaskLandDeliveryE2ETests" name="C488_ApprovalPollingCannotConfirm" /></UnitTest></TestDefinitions><Results><UnitTestResult executionId="d838b049-257f-4d21-ba61-dedc57fc6606" testId="1ecad690-e92d-deac-60f3-68172f7c0114" testName="C488_ApprovalPollingCannotConfirm" computerName="fc2983ea4991" duration="00:00:00.0115161" startTime="2026-10-15T22:44:18.9095532+00:00" endTime="2026-10-15T22:44:18.9210693+00:00" testType="13CDC9D9-DDB5-4fa4-A97D-D965CCFC6D4B" outcome="Failed" testListId="8C84FA94-04C1-424b-9868-57A2D4851A1D" relativeResultsDirectory="d838b049-257f-4d21-ba61-dedc57fc6606" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Output><ErrorInfo><Message>ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
False</Message><StackTrace>   at Antiphon.E2E.Fixtures.LandDeliveryFixture.InitializeAsync(Boolean busy, String cut, Boolean dispatch, String completionForm) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:line 59
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 238
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(String state) in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 271
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalPollingCannotConfirm() in /work/worktrees/task-1c14bf28/tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:line 447
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---</StackTrace></ErrorInfo></Output></UnitTestResult></Results></TestRun>
'@
