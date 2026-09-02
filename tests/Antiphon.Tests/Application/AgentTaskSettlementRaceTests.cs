using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0319: KillAsync's own SaveChanges on the shared scoped context used to flush an
/// uncommitted settlement; the pool sweeper then deleted the agent; SettleAsync's later
/// SaveChanges threw; OnTurnEndAsync swallowed it and skipped the parent '[task done]' note.
///
/// CARD-0320: two concurrent OnTurnEndAsync calls (live observer + CARD-0288 arm 0) used to
/// both persist and both DeliverToParent. Per-session settle lock + enqueue digest skip.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskSettlementRaceTests
{
    [Test]
    public async Task concurrent_on_turn_end_for_the_same_task_enqueues_one_parent_note()
    {
        using var workspace = new TempWorkspace();
        await using var provider = BuildHarness();

        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var (task, sessionId) = await SeedSharedTaskAsync(workspace.Path, parentSessionId);
        var report = "Wrote the slice. 12 passed, 0 failed.";
        await SeedMarkedTurnAsync(sessionId, task.Id, report);

        var replies = provider.GetRequiredService<AgentTaskReplyService>();
        await Task.WhenAll(
            replies.OnTurnEndAsync(sessionId, CancellationToken.None),
            replies.OnTurnEndAsync(sessionId, CancellationToken.None));

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);

        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId && m.Origin == QueuedMessageOrigin.Delegation)
            .ToListAsync();
        queued.Count.ShouldBe(1, "CARD-0320: two OnTurnEndAsync calls must not double-enqueue");
        queued[0].SourceTaskId.ShouldBe(task.Id);
        queued[0].Body.ShouldContain($"[task {DelegationReportFormatter.Short(task.Id)} done]");
    }

    [Test]
    public async Task enqueue_skips_a_second_delegation_note_with_the_same_source_task_and_digest()
    {
        using var workspace = new TempWorkspace();
        await using var provider = BuildHarness();

        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var taskId = Guid.NewGuid();
        var report = "Wrote the slice.";
        var digest = DelegationNoteDigest.Compute(report);
        var body = $"[task {DelegationReportFormatter.Short(taskId)} done]\n{report}";
        var queue = provider.GetRequiredService<SessionMessageQueueService>();

        await queue.EnqueueAsync(
            parentSessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, $"task:{taskId:N}", taskId, digest);
        await queue.EnqueueAsync(
            parentSessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, $"task:{taskId:N}", taskId, digest);

        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId && m.Origin == QueuedMessageOrigin.Delegation)
            .ToListAsync();
        queued.Count.ShouldBe(1, "CARD-0320: SourceTaskId+ContentDigest must not enqueue twice");
        queued[0].SourceTaskId.ShouldBe(taskId);
        queued[0].ContentDigest.ShouldBe(digest);
    }

    [Test]
    public async Task worktree_pool_settle_delivers_parent_note_when_kill_savechanges_races_retire()
    {
        using var workspace = new TempWorkspace();
        await using var provider = BuildHarness();

        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var worktreePath = Path.Combine(workspace.Path, "worktrees", $"card-task-{shortId}");
        Directory.CreateDirectory(worktreePath);

        var (task, sessionId) = await SeedWorktreePoolTaskAsync(
            workspace.Path, worktreePath, parentSessionId);
        var report = "Wrote the slice. 12 passed, 0 failed.";
        await SeedMarkedTurnAsync(sessionId, task.Id, report);

        var replies = provider.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);

        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync();
        queued.Count.ShouldBe(1, "exactly one parent note — the race used to skip it entirely");
        queued[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued[0].SourceTaskId.ShouldBe(task.Id);
        queued[0].Body.ShouldContain($"[task {DelegationReportFormatter.Short(task.Id)} done]");
    }

    private static ServiceProvider BuildHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
            ReplyInlineMaxChars = 20_000,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-settle-race-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();
        // Production-shaped: real SaveChanges on the scoped AppDbContext, then the concurrent
        // RetireIdleWarmAgentsAsync tick (own scope, like the 5s hosted sweep).
        services.AddScoped<IDelegateSessionStopper, FlushingSessionStopper>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Two SaveChanges like AgentSessionService.KillAsync (session → Stopping, then Stopped),
    /// then a concurrent pool-sweeper tick on a fresh context. Not a RecordingSessionStopper
    /// no-op: today's code reproduces the miss against this stopper.
    /// </summary>
    private sealed class FlushingSessionStopper : IDelegateSessionStopper
    {
        private static readonly AsyncLocal<bool> Retiring = new();

        private readonly AppDbContext _db;
        private readonly IServiceScopeFactory _scopes;

        public FlushingSessionStopper(AppDbContext db, IServiceScopeFactory scopes)
        {
            _db = db;
            _scopes = scopes;
        }

        public async Task KillAsync(Guid sessionId, CancellationToken ct)
        {
            var session = await _db.AgentSessions.FirstAsync(s => s.Id == sessionId, ct);
            session.Status = SessionStatus.Stopping;
            session.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            session.Status = SessionStatus.Stopped;
            session.EndedAt = DateTime.UtcNow;
            session.LastSeenAt = session.EndedAt.Value;
            await _db.SaveChangesAsync(ct);

            if (Retiring.Value)
                return;

            Retiring.Value = true;
            try
            {
                await using var tick = _scopes.CreateAsyncScope();
                var dispatcher = tick.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                await dispatcher.RetireIdleWarmAgentsAsync(ct);
            }
            finally
            {
                Retiring.Value = false;
            }
        }
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedSharedTaskAsync(
        string workingDirectory, Guid parentSessionId)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"task-{taskId:N}"[..13];
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            ParentSessionId = parentSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Title = "CARD-0320 concurrent settle",
            Goal = "Settle once.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Ephemeral = true,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now,
            DispatchedAt = now,
        };

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = workingDirectory,
            Details = "CARD-0320 shared delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedWorktreePoolTaskAsync(
        string workingDirectory, string worktreePath, Guid parentSessionId)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var agentName = $"task-{taskId:N}"[..13];
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            ParentSessionId = parentSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Title = "CARD-0319 race",
            Goal = "Settle, and the parent must hear about it.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = workingDirectory,
            WorktreePath = worktreePath,
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Ephemeral = true,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now,
            DispatchedAt = now,
        };

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = worktreePath,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = worktreePath,
            Details = "CARD-0319 worktree pool delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    private static async Task<Guid> SeedSessionAsync(string cwd)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
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
        return sessionId;
    }

    private static async Task SeedMarkedTurnAsync(Guid sessionId, Guid taskId, string report)
    {
        var prompt = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.";
        var body = report.TrimEnd() + "\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, body));
        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = TranscriptKinds.StopReasons.EndTurn;
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-settle-race").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
