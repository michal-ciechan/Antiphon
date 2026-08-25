using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Text.Json;
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
    public void Event_type_constants_preserve_the_schema_wire_spelling_and_expose_the_measured_dotted_one()
    {
        HerdrEventTypes.PaneClosedSubscribe.ShouldBe("pane.closed");
        HerdrEventTypes.PaneClosedWire.ShouldBe("pane_closed");
        HerdrEventTypes.PaneExitedSubscribe.ShouldBe("pane.exited");
        HerdrEventTypes.PaneExitedWire.ShouldBe("pane_exited");
        HerdrEventTypes.PaneAgentStatusChangedSubscribe.ShouldBe("pane.agent_status_changed");
        HerdrEventTypes.PaneAgentStatusChangedWire.ShouldBe("pane_agent_status_changed");
        HerdrEventTypes.PaneAgentStatusChangedWireDotted.ShouldBe("pane.agent_status_changed");
    }

    [Test]
    public async Task Dotted_status_event_updates_the_session_and_publishes_SessionAgentStatus()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = new SessionRunnerSettings
        {
            SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-pump-{Guid.NewGuid():N}"),
            PtyHostLingerHours = 0.02,
        };
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

        var sessionId = Guid.NewGuid();
        var started = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId, "claude", ["--dangerously-skip-permissions"], new Dictionary<string, string>(),
                settings.SessionLogPath, 120, 30, TranscriptEnabled: false, Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions($"test-{sessionId:N}"[..32], "card0163-pump",
                    settings.SessionLogPath, "card0163-pump")),
            CancellationToken.None);
        started.Status.ShouldBe("Running");

        var herdrSettings = new HerdrSettings { Enabled = true, Session = fake.Session };
        var pump = new HerdrEventPumpService(runtime, new HerdrClient(herdrSettings), Options.Create(herdrSettings),
            NullLogger<HerdrEventPumpService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var events = runtime.Subscribe(cts.Token);
        await pump.StartAsync(cts.Token);

        var subscribedUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < subscribedUntil && fake.SubscriptionRecords.Count == 0)
            await Task.Delay(50, cts.Token);
        fake.SubscriptionRecords.Count.ShouldBeGreaterThan(0);

        var paneId = fake.RequireAgentPaneId();
        fake.EnqueuePaneAgentStatusChanged(paneId, fake.Workspaces[0].WorkspaceId, "working");

        RunnerAgentStatusEvent? status = null;
        while (!cts.IsCancellationRequested)
        {
            var published = await events.ReadAsync(cts.Token);
            if (published.EventName != SessionRunnerEventNames.SessionAgentStatus)
                continue;

            status = JsonSerializer.Deserialize<RunnerAgentStatusEvent>(published.Json);
            if (status?.AgentStatus == "working")
                break;
        }

        status.ShouldNotBeNull();
        status!.SessionId.ShouldBe(sessionId);
        status.AgentStatus.ShouldBe("working");
        runtime.Get(sessionId).AgentStatus.ShouldBe("working");

        await pump.StopAsync(CancellationToken.None);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        try { Directory.Delete(settings.SessionLogPath, recursive: true); } catch { }
    }

    [Test]
    public void SessionAgentStatus_event_name_is_additive()
    {
        SessionRunnerEventNames.SessionAgentStatus.ShouldBe("SessionAgentStatus");
    }
}
