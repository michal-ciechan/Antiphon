using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

public sealed class TimingProbeTests
{
    [Before(Test)]
    public void Before()
    {
        Mark("setup");
        using (TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "before"))
        {
        }
    }

    [After(Test)]
    public void After()
    {
        using (TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "after"))
        {
        }

        Mark("teardown");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public void Parameterized(int value)
    {
        Mark("body");
        _ = value;
    }

    [Test]
    [Repeat(1)]
    public void Repeated() => Mark("body");

    [Test]
    public async Task Ordinary()
    {
        Mark("body");
        TestTiming.RememberCurrentTest();
        await Task.Run(() =>
        {
            using (TestTiming.BeginPhase(TestPhaseKind.OurCode, "child"))
            {
            }
        });
    }

    [Test]
    public void Fails()
    {
        Mark("body");
        throw new InvalidOperationException("CANARY-secret-sk-probe-should-not-leak");
    }

    [Test]
    [Timeout(200)]
    public async Task Cancels(CancellationToken cancellationToken)
    {
        Mark("body");
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static void Mark(string phase)
    {
        var id = TestContext.Current!.Metadata.TestDetails.TestId;
        var line = "C1514_NATIVE " + phase + " " + id;
        Console.WriteLine(line);
        TestContext.Current.Output.WriteLine(line);
    }
}

// These cases deliberately have no probe-specific identity output. The shared
// receiver must make ordinary tests joinable too.
public sealed class TimingPlainProbeTests
{
    [Test]
    public void Plain() { }
}

public sealed class TimingAsyncProbeTests
{
    [Test]
    public async Task Async() => await Task.Yield();
}
