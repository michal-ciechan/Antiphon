using TUnit.Core;
using TUnit.Core.Enums;
using TUnit.Core.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Early start runs before [Before(Test)]. Late end runs after [After(Test)], so fixture
/// teardown stays inside the case envelope.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestTimingAttribute : Attribute, ITestStartEventReceiver, ITestEndEventReceiver, ILastTestInAssemblyEventReceiver
{
    EventReceiverStage ITestStartEventReceiver.Stage => EventReceiverStage.Early;
    EventReceiverStage ITestEndEventReceiver.Stage => EventReceiverStage.Late;

    public ValueTask OnTestStart(TestContext context)
    {
        var session = TestTiming.Resolve();
        if (session is { Enabled: true })
            session.OnTestStart(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTestEnd(TestContext context)
    {
        var session = TestTiming.Resolve();
        if (session is { Enabled: true })
            session.OnTestEnd(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnLastTestInAssembly(AssemblyHookContext context, TestContext testContext)
    {
        var session = TestTiming.Resolve();
        if (session is { Enabled: true })
            session.FlushHealth();
        return ValueTask.CompletedTask;
    }
}
