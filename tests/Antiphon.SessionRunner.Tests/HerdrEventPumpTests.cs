using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0162 B2: event pump pins — disabled ⇒ no work; subscription vocabulary; events are
/// triggers. Full lifecycle against FakeHerdrServer is covered where the nested RunnerSession
/// construction is feasible without a full host.
/// </summary>
public class HerdrEventPumpTests
{
    [Test]
    public async Task Disabled_pump_exits_without_touching_herdr()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = Path.GetTempPath() }),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }));

        var pump = new HerdrEventPumpService(
            runtime,
            new HerdrClient(new HerdrSettings { Enabled = false, Session = fake.Session }),
            Options.Create(new HerdrSettings { Enabled = false }),
            NullLogger<HerdrEventPumpService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pump.StartAsync(cts.Token);
        await Task.Delay(200);
        await pump.StopAsync(CancellationToken.None);

        fake.Requests.Any(r => r.GetProperty("method").GetString() == "events.subscribe")
            .ShouldBeFalse("disabled pump must not open a subscription");
    }

    [Test]
    public void LiveHerdrPanes_empty_when_no_herdr_sessions()
    {
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = Path.GetTempPath() }),
            NullLogger<SessionRunnerRuntime>.Instance);
        runtime.LiveHerdrPanes().ShouldBeEmpty();
    }

    [Test]
    public void Event_type_constants_pair_dotted_subscribe_with_underscored_wire()
    {
        HerdrEventTypes.PaneClosedSubscribe.ShouldBe("pane.closed");
        HerdrEventTypes.PaneClosedWire.ShouldBe("pane_closed");
        HerdrEventTypes.PaneExitedSubscribe.ShouldBe("pane.exited");
        HerdrEventTypes.PaneExitedWire.ShouldBe("pane_exited");
        HerdrEventTypes.PaneAgentStatusChangedSubscribe.ShouldBe("pane.agent_status_changed");
        HerdrEventTypes.PaneAgentStatusChangedWire.ShouldBe("pane_agent_status_changed");
    }

    [Test]
    public void SessionAgentStatus_event_name_is_additive()
    {
        SessionRunnerEventNames.SessionAgentStatus.ShouldBe("SessionAgentStatus");
    }
}
