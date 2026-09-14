using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("RemoteControlRecovery")]
public class RemoteControlModalWatchTests
{
    [Test]
    public async Task C514_Modal_watch_is_independent_of_connection_watch()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(
            configureSupervision: s =>
            {
                s.RcWatch.Enabled = false;
                s.RcModalWatch.Enabled = true;
            });
        h.Adapter.RemoteControlMenuOpen = true;
        (await h.TickWatchAsync()).ShouldBeGreaterThan(0);
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.CountAsync(e =>
            e.SessionId == h.SessionId && e.ResolvedAt == null)).ShouldBe(1);
    }

    [Test]
    public async Task C514_Erased_menu_in_raw_history_is_not_current()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Runner.RawOutputOverride = RemoteControlRecoveryHarness.MenuScreen;
        h.Runner.RenderedScreenOverride = "> ";
        h.Adapter.RemoteControlMenuOpen = false;
        await h.TickWatchAsync();
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.CountAsync(e => e.SessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task C514_Shutdown_stops_modal_background_work()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(
            configureSupervision: s => s.Enabled = false);
        h.Adapter.RemoteControlMenuOpen = true;
        (await h.TickWatchAsync()).ShouldBe(0);
        h.Runner.ListCalls.ShouldBe(0);
        h.Runner.SnapshotCalls.ShouldBe(0);

        using var cts = new CancellationTokenSource();
        await using var h2 = await RemoteControlRecoveryHarness.CreateAsync();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => h2.TickWatchAsync(cts.Token));
    }

    [Test]
    public async Task C514_Working_fork_with_spinner_is_observed()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.MarkWorkingAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        await using var db = h.CreateDb();
        (await db.RemoteControlModalEpisodes.CountAsync(e =>
            e.SessionId == h.SessionId && e.ResolvedAt == null)).ShouldBe(1);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Card_nonAlwaysOn_and_rcDisabled_sessions_are_observed()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await using (var db = h.CreateDb())
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            agent.AlwaysOn = false;
            agent.RemoteControlEnabled = false;
            await db.SaveChangesAsync();
        }

        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        await using var verify = h.CreateDb();
        (await verify.RemoteControlModalEpisodes.AnyAsync(e =>
            e.SessionId == h.SessionId && e.ResolvedAt == null)).ShouldBeTrue();
    }

    [Test]
    public async Task C514_Hung_candidate_does_not_hide_next_menu()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(
            configureSupervision: s => s.RcModalWatch.SnapshotTimeoutSeconds = 1);
        var secondId = Guid.NewGuid();
        var secondAdapter = new FakeAgentProtocolAdapter
        {
            RemoteControlMenuOpen = true,
            AcceptedStartedAt = h.Generation,
            Pid = 99,
        };
        await using (var db = h.CreateDb())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = secondId,
                DefinitionName = "c514-second",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                CreatedAt = DateTime.UtcNow,
                StartedAt = h.Generation,
                LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        h.Runner.ExtraAdapters[secondId] = secondAdapter;
        h.Runner.ExtraSessions =
        [
            new SessionRunnerSessionDto(
                secondId, 99, h.Generation, "Running", null, AgentExitReason.Unknown, 0,
                AcceptedStartedAt: h.Generation),
        ];
        h.Runner.HangSessions.Add(h.SessionId);
        h.Runner.HangDelay = TimeSpan.FromSeconds(30);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await h.TickWatchAsync(cts.Token);
        await using var verify = h.CreateDb();
        (await verify.RemoteControlModalEpisodes.AnyAsync(e =>
            e.SessionId == secondId && e.ResolvedAt == null)).ShouldBeTrue();
    }

    [Test]
    public async Task C514_Late_menu_without_request_is_still_observed()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        await using var db = h.CreateDb();
        var episode = await db.RemoteControlModalEpisodes.SingleAsync(e => e.SessionId == h.SessionId);
        episode.RelatedMaintenanceQueueId.ShouldBeNull();
    }

    [Test]
    public async Task C514_Old_runner_snapshot_is_generation_unproven()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        h.Runner.SnapshotAcceptedStartedAt = null;
        var observation = await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None);
        observation.ObservedGeneration.ShouldBeNull();
        observation.Reason.ShouldBe("ObservationGenerationUnproven");
        observation.GenerationProven.ShouldBeFalse();
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation, observation, null, CancellationToken.None);
        episode.ShouldBeNull();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }
}
