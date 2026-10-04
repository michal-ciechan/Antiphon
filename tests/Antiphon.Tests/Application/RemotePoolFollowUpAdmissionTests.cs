using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1037: real Create and fresh PostgreSQL reads prove admission before persistence.
/// The seeded predecessor attachment substitutes for prior dispatch, not provider delivery.
/// </summary>
[Category("Integration")]
public sealed class RemotePoolFollowUpAdmissionTests
{
    [Test]
    public async Task Remote_pool_follow_up_refuses_before_insert()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode, AgentKind.Codex })
        foreach (var workspace in new WorkspaceMode?[] { null, WorkspaceMode.Shared, WorkspaceMode.ReadOnly })
        {
            await using var seed = await PoolPredecessor.CreateAsync(
                schema.ConnectionString, kind, "server2", "server2", taskDirectory: true);
            await AssertRemoteRefusalAsync(seed, workspace, requestedRunner: null, "server2",
                $"{kind}/{workspace?.ToString() ?? "omitted"}");
        }

        foreach (var (name, agentRunner, priorRunner, requestedRunner, expectedRunner, disabled) in
                 new (string, string?, string?, string?, string, bool)[]
                 {
                     ("blank agent inherits prior", "   ", "server2", null, "server2", false),
                     ("agent binding wins", " remote-other ", "server2", null, "remote-other", false),
                     ("unknown binding while disabled", "unknown-remote", "server2", null, "unknown-remote", true),
                     ("explicit local cannot move process", "server2", "server2", "local", "server2", false),
                 })
        {
            await using var seed = await PoolPredecessor.CreateAsync(
                schema.ConnectionString, AgentKind.ClaudeCode, agentRunner, priorRunner, taskDirectory: false);
            if (disabled)
                seed.Kit.PhoneHome.Enabled = false;
            await AssertRemoteRefusalAsync(seed, workspace: null, requestedRunner, expectedRunner, name);
        }
    }

    [Test]
    public async Task Local_pool_follow_up_keeps_admission()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var (agentRunner, priorRunner) in new (string?, string?)[]
                 {
                     (null, null), ("   ", "   "), ("local", "local"),
                     ("DESKTOP", "DESKTOP"), ("local", "server2"),
                 })
        {
            await using var seed = await PoolPredecessor.CreateAsync(
                schema.ConnectionString, AgentKind.ClaudeCode, agentRunner, priorRunner, taskDirectory: false);
            await using var db = seed.Kit.Context();
            AgentTaskCreatedDto? created = null;
            Exception? failure = null;
            try
            {
                created = await seed.Kit.Service(db).CreateAsync(
                    seed.Follow(), seed.Kit.Caller, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            failure.ShouldBeNull("local-pool-follow-up-admitted");
            created.ShouldNotBeNull();
            var saved = await seed.Kit.ReadAsync(created.Id);
            saved.Task.AgentId.ShouldBe(seed.AgentId);
            saved.Task.FollowUpOfTaskId.ShouldBe(seed.PriorId);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Shared);
            saved.Task.WorkingDirectory.ShouldBe(seed.DirectoryPath);
        }
    }

    [Test]
    public async Task Explicit_worktree_follow_up_keeps_existing_conflict()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var seed = await PoolPredecessor.CreateAsync(
            schema.ConnectionString, AgentKind.ClaudeCode, "server2", "server2", taskDirectory: true);
        await using var db = seed.Kit.Context();
        var events = new MockEventBus();
        var before = await seed.CountsAsync();
        var refused = await Should.ThrowAsync<ValidationException>(() => seed.Kit.Service(db, eventBus: events)
            .CreateAsync(seed.Follow() with { Workspace = WorkspaceMode.Worktree }, seed.Kit.Caller,
                CancellationToken.None));
        refused.StatusCode.ShouldBe(422);
        refused.Code.ShouldBe("workspace_existing_agent_conflict");
        refused.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.Workspace));
        (await seed.CountsAsync()).ShouldBe(before);
        db.ChangeTracker.Entries<AgentTask>().ShouldNotContain(e => e.State == EntityState.Added);
        events.PublishedEvents.ShouldBeEmpty();
    }

    private static async Task AssertRemoteRefusalAsync(
        PoolPredecessor seed, WorkspaceMode? workspace, string? requestedRunner, string expectedRunner, string row)
    {
        await using var db = seed.Kit.Context();
        var events = new MockEventBus();
        var before = await seed.CountsAsync();
        var refused = await Should.ThrowAsync<ValidationException>(() => seed.Kit.Service(db, eventBus: events)
                .CreateAsync(seed.Follow() with { Workspace = workspace, RunnerId = requestedRunner },
                    seed.Kit.Caller, CancellationToken.None),
            "remote-pool-refused-before-insert: " + row);
        refused.StatusCode.ShouldBe(422, row);
        refused.Code.ShouldBe("follow_up_remote_pool_unsupported", row);
        refused.Errors.Keys.ShouldHaveSingleItem().ShouldBe(nameof(CreateAgentTaskRequest.FollowUpOnTask), row);
        var fieldError = string.Join(" ", refused.Errors[nameof(CreateAgentTaskRequest.FollowUpOnTask)]);
        foreach (var text in new[] { refused.Message, fieldError })
        foreach (var token in new[]
                 {
                     DelegationReportFormatter.Short(seed.PriorId), expectedRunner,
                     "-Worktree", "-StartRef", "without -OnAgent",
                 })
            text.ShouldContain(token, Case.Sensitive, row);
        (await seed.CountsAsync()).ShouldBe(before, row + ": fresh stored task/event/session counts");
        db.ChangeTracker.Entries<AgentTask>().ShouldNotContain(e => e.State == EntityState.Added, row);
        events.PublishedEvents.ShouldBeEmpty(row);
    }

    private sealed class PoolPredecessor(DefaultRunnerKit kit, Guid priorId, Guid agentId, string scratchRoot,
        string directoryPath) : IAsyncDisposable
    {
        public DefaultRunnerKit Kit { get; } = kit;
        public Guid PriorId { get; } = priorId;
        public Guid AgentId { get; } = agentId;
        public string DirectoryPath { get; } = directoryPath;

        public CreateAgentTaskRequest Follow() => new("c1037 follow-up", Role: AgentTaskRole.Code,
            FollowUpOnTask: PriorId.ToString("D"));

        public async Task<(int Tasks, int Events, int Sessions)> CountsAsync()
        {
            await using var db = Kit.Context();
            return (await db.AgentTasks.CountAsync(), await db.AgentTaskEvents.CountAsync(),
                await db.AgentSessions.CountAsync());
        }

        public static async Task<PoolPredecessor> CreateAsync(
            string connectionString, AgentKind kind, string? agentRunner, string? priorRunner, bool taskDirectory)
        {
            var kit = DefaultRunnerKit.Create(connectionString, defaultRunnerId: null);
            var scratchRoot = Path.Combine(kit.RepoRoot, ".antiphon", "c1037-scratch", Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(scratchRoot, taskDirectory
                ? "card-task-" + Guid.NewGuid().ToString("N")[..8] : "pool-directory");
            Directory.CreateDirectory(directory);
            try
            {
                // Real remote Worktree admission first; only the prior dispatch attachment is seeded.
                await using var db = kit.Context();
                var created = await kit.Service(db).CreateAsync(new CreateAgentTaskRequest(
                    "c1037 predecessor", Role: AgentTaskRole.Code, AgentKind: kind,
                    Workspace: WorkspaceMode.Worktree, RunnerId: "server2"), kit.Caller, CancellationToken.None);
                var now = DateTime.UtcNow;
                var sessionId = Guid.NewGuid();
                var agentId = Guid.NewGuid();
                var name = "c1037-pool-" + agentId.ToString("N")[..8];
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = kind.ToString(), AgentKind = kind,
                    Status = SessionStatus.Running, Cwd = directory, Cols = 120, Rows = 30,
                    RunnerId = agentRunner, RunnerStoreId = Guid.NewGuid(), RunnerCwd = directory,
                    CreatedAt = now.AddMinutes(-10), StartedAt = now.AddMinutes(-10), LastSeenAt = now,
                });
                db.Agents.Add(new Agent
                {
                    Id = agentId, Name = name, Slug = name, Details = "CARD-1037 seeded prior launch",
                    WorkingDirectory = directory, Status = AgentStatus.Idle, Kind = kind,
                    ModelLevel = AgentModelLevel.Medium, IsPoolDelegate = true,
                    PoolIdleSince = now, PersistentSessionId = sessionId.ToString("D"), RunnerId = agentRunner,
                    CreatedAt = now.AddMinutes(-10), UpdatedAt = now,
                });
                var prior = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
                prior.AgentId = agentId;
                prior.AgentSessionId = sessionId;
                prior.RunnerId = priorRunner;
                prior.WorkingDirectory = directory;
                prior.WorktreePath = directory;
                prior.Status = AgentTaskStatus.Succeeded;
                prior.CompletedAt = now;
                await db.SaveChangesAsync();
                return new PoolPredecessor(kit, prior.Id, agentId, scratchRoot, directory);
            }
            catch
            {
                Directory.Delete(scratchRoot, recursive: true);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(scratchRoot, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
