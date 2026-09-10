using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class MutationPipelineTests
{
    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Working)]
    public async Task C470_mutation_completion_consumes_ready(AgentTaskStatus openStatus)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.Review, "CARD-0470");
        var code = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            "code", cardId: card.Id, createdAt: DateTime.UtcNow.AddMinutes(-30), completedAt: DateTime.UtcNow.AddMinutes(-20),
            deliverablePath: "docs/superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md", nextStage: PipelineHandoffKind.Mutation, nextHandoff: "owner and SHA");
        var first = await CreateService(db).GetAsync(default);
        first.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready.ShouldHaveSingleItem().SourcePlanTaskId.ShouldBe(code.Id);
        var mutation = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Mutation, openStatus,
            "mutation", cardId: card.Id, createdAt: DateTime.UtcNow.AddMinutes(-10));
        (await CreateService(db).GetAsync(default)).Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready.ShouldBeEmpty();
        mutation.Status = AgentTaskStatus.Succeeded;
        mutation.CompletedAt = DateTime.UtcNow.AddMinutes(-1);
        mutation.NextStage = PipelineHandoffKind.Review;
        mutation.NextHandoff = "original Code owner";
        mutation.DeliverablePath = code.DeliverablePath;
        await db.SaveChangesAsync();
        await using var verify = CreateContext(schema);
        var after = await CreateService(verify).GetAsync(default);
        after.Stages.Single(s => s.Role == AgentTaskRole.Review).Ready.ShouldHaveSingleItem().SourcePlanTaskId.ShouldBe(mutation.Id);
        after.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready.ShouldBeEmpty();
        mutation.NextStage = PipelineHandoffKind.Land;
        await db.SaveChangesAsync();
        var landed = await CreateService(db).GetAsync(default);
        landed.Stages.Single(s => s.Role == AgentTaskRole.Review).Ready.ShouldBeEmpty();
        landed.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready.ShouldBeEmpty();
    }

    private static AgentTaskPipelineStatusService CreateService(
        AppDbContext db, DelegationSettings? settings = null)
    {
        var resolved = settings ?? new DelegationSettings();
        var options = Options.Create(resolved);
        return new AgentTaskPipelineStatusService(
            db,
            options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance),
            TimeProvider.System);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static DateTime? Truncate(DateTime? value) =>
        value is DateTime utc
            ? DateTime.SpecifyKind(new DateTime(utc.Ticks / 10 * 10, DateTimeKind.Utc), DateTimeKind.Utc)
            : null;

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string directory,
        AgentTaskRole role,
        AgentTaskStatus status,
        string title,
        DateTime? createdAt = null,
        DateTime? dispatchedAt = null,
        DateTime? completedAt = null,
        Guid? cardId = null,
        Guid? sessionId = null,
        WorkspaceMode workspace = WorkspaceMode.Shared,
        string? repoPath = null,
        string? deliverablePath = null,
        string? scope = null,
        AgentKind agentKind = AgentKind.ClaudeCode,
        AgentModelLevel modelLevel = AgentModelLevel.High,
        string? worktreeBranch = null,
        DateTime? landRequestedAt = null,
        PipelineHandoffKind? nextStage = null,
        string? nextHandoff = null)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = role,
            Status = status,
            Workspace = workspace,
            WorkingDirectory = directory,
            RepoPath = repoPath,
            Scope = scope,
            CardId = cardId,
            AgentSessionId = sessionId,
            AgentKind = agentKind,
            ModelLevel = modelLevel,
            WorktreeBranch = worktreeBranch,
            LandRequestedAt = Truncate(landRequestedAt),
            AgentName = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? "delegate" : null,
            DispatchedAt = Truncate(dispatchedAt),
            CompletedAt = Truncate(completedAt),
            DeliverablePath = deliverablePath,
            NextStage = nextStage,
            NextHandoff = nextHandoff,
            CreatedAt = Truncate(createdAt ?? now)!.Value,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedSessionAsync(AppDbContext db, Guid sessionId, string cwd)
    {
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Card> SeedCardAsync(
        AppDbContext db, CardStatus status, string identifier, bool archived = false)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"pipe-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/pipe.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Pipe {identifier}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = status.ToString().ToLowerInvariant(),
            Name = status.ToString(),
            ColumnOrder = 0,
            CardStatus = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} title",
            Description = "Pipeline test.",
            Status = status,
            ArchivedAt = archived ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pipe").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
