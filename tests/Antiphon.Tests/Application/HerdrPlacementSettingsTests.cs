using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Migrations;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class HerdrPlacementSettingsTests
{
    [Test]
    public async Task Create_persists_trimmed_labels_and_detail_returns_them()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Labels"),
                "D:/src/app",
                SessionBackend: SessionBackend.Herdr,
                HerdrWorkspaceLabel: " PredictionMarkets ",
                HerdrTabLabel: " Orch "),
            CancellationToken.None);

        created.HerdrTabLabel.ShouldBe("Orch");
        created.HerdrWorkspaceLabel.ShouldBe("PredictionMarkets");
        var listed = await service.GetAllAsync(CancellationToken.None);
        listed.Single(a => a.Id == created.Id).HerdrTabLabel.ShouldBe("Orch");
        listed.Single(a => a.Id == created.Id).HerdrWorkspaceLabel.ShouldBe("PredictionMarkets");
    }

    [Test]
    public async Task Update_null_keeps_and_empty_clears_each_label_independently()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Clear"),
                "D:/src/app",
                SessionBackend: SessionBackend.Herdr,
                HerdrWorkspaceLabel: "PredictionMarkets",
                HerdrTabLabel: "Orch"),
            CancellationToken.None);

        var afterTab = await service.UpdateAsync(
            created.Id,
            Patch(created, herdrTabLabel: ""),
            CancellationToken.None);
        afterTab.HerdrTabLabel.ShouldBeNull();
        afterTab.HerdrWorkspaceLabel.ShouldBe("PredictionMarkets");

        var afterEmpty = await service.UpdateAsync(
            created.Id,
            Patch(afterTab),
            CancellationToken.None);
        afterEmpty.HerdrTabLabel.ShouldBeNull();
        afterEmpty.HerdrWorkspaceLabel.ShouldBe("PredictionMarkets");

        var afterWs = await service.UpdateAsync(
            created.Id,
            Patch(afterEmpty, herdrWorkspaceLabel: "   "),
            CancellationToken.None);
        afterWs.HerdrWorkspaceLabel.ShouldBeNull();
        afterWs.HerdrTabLabel.ShouldBeNull();
    }

    [Test]
    public async Task Labels_over_256_or_with_control_characters_are_422()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var tooLong = new string('x', 257);
        await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateAgentRequest(Unique("Long"), "D:/src/app", HerdrTabLabel: tooLong),
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            service.CreateAsync(
                new CreateAgentRequest(Unique("Ctrl"), "D:/src/app", HerdrWorkspaceLabel: "Or\nch"),
                CancellationToken.None));
    }

    [Test]
    public async Task Agent_model_has_two_label_columns_and_no_herdr_id_columns()
    {
        await using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Agent))!;
        entity.FindProperty(nameof(Agent.HerdrWorkspaceLabel))!.GetMaxLength().ShouldBe(256);
        entity.FindProperty(nameof(Agent.HerdrTabLabel))!.GetMaxLength().ShouldBe(256);
        entity.FindProperty(nameof(Agent.HerdrWorkspaceLabel))!.IsNullable.ShouldBeTrue();
        entity.FindProperty(nameof(Agent.HerdrTabLabel))!.IsNullable.ShouldBeTrue();
        entity.GetProperties().Select(p => p.Name)
            .ShouldNotContain(n => n.EndsWith("PaneId") || n.EndsWith("TabId") || n.EndsWith("WorkspaceId"));

        var source = Directory.GetFiles(
                Path.Combine(FindRepoRoot(), "server", "Migrations"),
                "*AddAgentHerdrPlacementLabels.cs")
            .ShouldHaveSingleItem();
        var text = await File.ReadAllTextAsync(source);
        text.ShouldContain("AddColumn<string>");
        text.ShouldContain("HerdrTabLabel");
        text.ShouldContain("HerdrWorkspaceLabel");
        System.Text.RegularExpressions.Regex.Matches(text, "AddColumn").Count.ShouldBe(2);
        _ = typeof(AddAgentHerdrPlacementLabels);
    }

    [Test]
    public async Task Labels_persist_across_fresh_session_ids_and_backend_switch()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "ws"));
            var first = new FakeAgentProtocolAdapter();
            var second = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [first, second], defaultKind: "ClaudeCode");
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    Unique("Persist"),
                    Path.Combine(tempRoot, "ws"),
                    SessionBackend: SessionBackend.Herdr,
                    HerdrWorkspaceLabel: "PredictionMarkets",
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);

            var one = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await MarkEnded(one.PersistentSessionId!);
            var two = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            two.PersistentSessionId.ShouldNotBe(one.PersistentSessionId);

            var patched = await harness.AgentService.UpdateAsync(
                agent.Id, Patch(two, sessionBackend: SessionBackend.PtyHost), CancellationToken.None);
            patched = await harness.AgentService.UpdateAsync(
                agent.Id, Patch(patched, sessionBackend: SessionBackend.Herdr), CancellationToken.None);
            patched.HerdrTabLabel.ShouldBe("Orch");
            patched.HerdrWorkspaceLabel.ShouldBe("PredictionMarkets");
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Updating_labels_on_a_live_agent_launches_nothing()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "ws"));
            var adapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [adapter], defaultKind: "ClaudeCode");
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(
                    Unique("Live"),
                    Path.Combine(tempRoot, "ws"),
                    SessionBackend: SessionBackend.Herdr,
                    HerdrTabLabel: "Orch"),
                CancellationToken.None);
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            adapter.Started.ShouldBeTrue();
            var starts = harness.Runner.CheckCalls.Count;
            var before = await harness.AgentService.GetByIdAsync(agent.Id, CancellationToken.None);
            await harness.AgentService.UpdateAsync(
                agent.Id, Patch(before, herdrTabLabel: "Orch2"), CancellationToken.None);
            adapter.Started.ShouldBeTrue();
            harness.Runner.CheckCalls.Count.ShouldBe(starts);
            var session = await CreateContext().AgentSessions.SingleAsync(s => s.Id.ToString() == before.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static UpdateAgentRequest Patch(
        AgentDetailDto agent,
        string? herdrWorkspaceLabel = null,
        string? herdrTabLabel = null,
        SessionBackend? sessionBackend = null) =>
        new(
            agent.Name,
            agent.WorkingDirectory,
            agent.Details,
            agent.DefaultWorkflowTemplateId,
            agent.AssignmentPolicy,
            agent.BoardId,
            agent.AlwaysOn,
            agent.RemoteControlEnabled,
            agent.SystemPromptAppend,
            agent.ModelLevel,
            agent.TuiProfileId,
            agent.ModelId,
            agent.ReplyStyle,
            sessionBackend ?? agent.SessionBackend,
            HerdrWorkspaceLabel: herdrWorkspaceLabel,
            HerdrTabLabel: herdrTabLabel);

    private static async Task MarkEnded(string sessionId)
    {
        await using var db = CreateContext();
        var id = Guid.Parse(sessionId);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == id);
        session.Status = SessionStatus.Failed;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static AgentService CreateService(AppDbContext db) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string Unique(string prefix)
    {
        var value = $"{prefix}-{Guid.NewGuid():N}";
        return value.Length <= 40 ? value : value[..40];
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
