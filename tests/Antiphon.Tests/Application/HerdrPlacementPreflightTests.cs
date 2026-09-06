using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentControl")]
public class HerdrPlacementPreflightTests
{
    [Test]
    public async Task Public_start_refused_by_preflight_is_409_before_any_row_or_queue_mutation()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            harness.Runner.PlacementCheck = _ =>
                throw new ConflictException("pane occupied", HerdrProblemTypes.PaneOccupied);

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Preflight Refuse",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            var beforeId = agent.PersistentSessionId;
            var beforeCount = await CountSessions(agent.Id);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);

            var after = await harness.AgentService.GetByIdAsync(agent.Id, CancellationToken.None);
            after.Status.ShouldNotBe(AgentStatus.Running);
            after.PersistentSessionId.ShouldBe(beforeId);
            after.HerdrTabLabel.ShouldBe("Orch");
            (await CountSessions(agent.Id)).ShouldBe(beforeCount);
            adapter.StartedHerdr.ShouldBeNull();
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Preflight_passes_the_chosen_session_id_and_named_options()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Preflight Id",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var check = harness.Runner.CheckCalls.ShouldHaveSingleItem();
            check.SessionId.ToString("D").ShouldBe(detail.PersistentSessionId);
            check.Herdr.TabLabel.ShouldBe("Orch");
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unpinned_start_never_calls_preflight()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Unpinned", workspace, SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            harness.Runner.CheckCalls.ShouldBeEmpty();
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Already_live_start_stays_idempotent_and_skips_preflight()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Live Skip",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            var checks = harness.Runner.CheckCalls.Count;
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(RemoteControl: false), CancellationToken.None);
            harness.Runner.CheckCalls.Count.ShouldBe(checks);
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Runner_409_after_allowed_preflight_fails_the_row_asynchronously_and_never_falls_back()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            adapter.ThrowOnStartFactory = () =>
                new ConflictException("pane occupied by foreign", HerdrProblemTypes.PaneOccupied);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "Race 409",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            var started = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            started.PersistentSessionId.ShouldNotBeNull();
            try
            {
                await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch
            {
                // queue rethrows launch failures after recording Failed
            }

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var row = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == started.PersistentSessionId);
            row.Status.ShouldBe(SessionStatus.Failed);
            row.FailureReason.ShouldContain("pane occupied by foreign");
            var after = await verify.Agents.SingleAsync(a => a.Id == agent.Id);
            after.Status.ShouldBe(AgentStatus.Failed);
            after.PersistentSessionId.ShouldBe(started.PersistentSessionId);
            adapter.Started.ShouldBeFalse();
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Named_launch_refused_when_runner_lacks_named_tab_placement_capability()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            harness.Runner.AdvertiseHerdrNamedTabPlacement = false;
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    "No Cap",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.Refused);
            ex.Message.ShouldContain(RunnerCapabilityFeatures.HerdrNamedTabPlacement);
            harness.Runner.CheckCalls.ShouldBeEmpty();
            adapter.StartedHerdr.ShouldBeNull();
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unpinned_launch_on_an_old_runner_is_unaffected()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "ws");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            harness.Runner.AdvertiseHerdrNamedTabPlacement = false;
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Old runner unpinned", workspace, SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            adapter.StartedHerdr.ShouldNotBeNull();
            adapter.StartedHerdr!.TabLabel.ShouldBeNull();
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task<int> CountSessions(Guid agentId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var key = agentId; // sessions are cardless; count by cwd via agent
        var agent = await db.Agents.SingleAsync(a => a.Id == key);
        return await db.AgentSessions.CountAsync(s => s.Cwd == agent.WorkingDirectory);
    }
}
