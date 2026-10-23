using Antiphon.Agents.Pty;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1518 S1: existing dispatcher, queue and CARD-0680 observation on a private schema.
/// No tool process is launched. The captured refusal is diagnostic input, never a command.
/// </summary>
internal sealed class PermissionWaitFixture : IAsyncDisposable
{
    internal const string CapturedRefusal =
        "Compound command contains cd with write operation - manual approval required to prevent path resolution bypass";
    internal const string CapturedFrame = CapturedRefusal + "\nDo you want to proceed?\n❯ 1. Yes\n  2. No\nEsc to cancel · Tab to amend";
    internal const string Refinement = "Preserve the incident evidence and report the permission decision needed.";
    internal const string SourceContents = "CARD-1518: uncommitted delegate work must survive a diagnostic and deadline.\n";

    private readonly IsolatedTestSchema _schema;
    internal BridgeQueueHarness Harness { get; }
    internal ControlledTimeProvider Clock { get; }
    internal RecordingSessionStopper Stopper { get; }
    internal RecordingGitWorkspaceService Git { get; }
    internal RecordingLaunchFactory Launches { get; }
    internal Guid TaskId { get; } = Guid.NewGuid();
    internal Guid ParentSessionId { get; } = Guid.NewGuid();
    internal Guid SessionId => Harness.SessionId;
    internal Guid RefinementId { get; private set; }
    internal DateTime DispatchedAt { get; private set; }
    internal DateTime Generation { get; private set; }
    internal string WorktreePath => Path.Combine(Harness.TempRoot, "incident-worktree");
    internal string SourceMarker => Path.Combine(WorktreePath, "unfinished.txt");
    internal string Branch => "fixture/c1518-" + TaskId.ToString("N");

    private PermissionWaitFixture(IsolatedTestSchema schema, BridgeQueueHarness harness,
        ControlledTimeProvider clock, RecordingSessionStopper stopper,
        RecordingGitWorkspaceService git, RecordingLaunchFactory launches)
    {
        _schema = schema;
        Harness = harness;
        Clock = clock;
        Stopper = stopper;
        Git = git;
        Launches = launches;
    }

    internal static async Task<PermissionWaitFixture> CreateAsync(CancellationToken ct)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new ControlledTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var stopper = new RecordingSessionStopper();
        var git = new RecordingGitWorkspaceService { OverrideRun = _ => (0, "", "") };
        var launches = new RecordingLaunchFactory();
        var settings = new DelegationSettings
        {
            Enabled = true,
            MaxConcurrentTasks = 0,
            DefaultTimeoutMinutes = 240,
            ModelWaitDeadlineMinutes = 20,
            LocalExecutionDeadlineMinutes = 90,
            BootModelWaitDeadlineMinutes = 8,
            CheckEnabled = false,
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
            PoolIdleRetireMinutes = 240,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = new() { TimeoutMinutes = 240 },
            },
        };
        // Affirmative modal detection must not depend on the generic 30-minute novelty sweep.
        settings.StallDetection.Enabled = false;
        BridgeQueueHarness? harness = null;
        try
        {
            harness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                PreserveDatabaseOnDispose = true,
                AlwaysOn = false,
                TimeProvider = clock,
                Delegation = settings,
                ConfigureServices = services =>
                {
                    services.RemoveAll<GitWorkspaceService>();
                    services.AddDelegationWorktreeGraph(workspaceGit: git);
                    services.AddSingleton<IDelegateSessionStopper>(stopper);
                    services.AddSingleton<IAgentProtocolAdapterFactory>(launches);
                    services.AddSingleton<ProviderDialogObservationService>();
                    services.AddSingleton<TaskProviderWaitReader>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddScoped<AgentTaskService>();
                    services.AddSingleton<AgentTaskReplyService>();
                    services.AddScoped<AgentFilesService>();
                    services.AddScoped<AgentTaskDispatcher>();
                },
            });
            var fixture = new PermissionWaitFixture(schema, harness, clock, stopper, git, launches);
            await fixture.SeedAsync(ct);
            return fixture;
        }
        catch
        {
            if (harness is not null) await harness.DisposeAsync();
            await schema.DisposeAsync();
            throw;
        }
    }

    internal TaskProviderWaitReader Reader => Harness.Provider.GetRequiredService<TaskProviderWaitReader>();

    internal AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

    private async Task SeedAsync(CancellationToken ct)
    {
        DispatchedAt = Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        Generation = DispatchedAt.AddMinutes(-1);
        Directory.CreateDirectory(WorktreePath);
        await File.WriteAllTextAsync(SourceMarker, SourceContents, ct);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.StartedAt, Generation)
            .SetProperty(s => s.Cwd, WorktreePath), ct);
        await db.Agents.Where(a => a.Id == Harness.AgentId).ExecuteUpdateAsync(u => u
            .SetProperty(a => a.Status, AgentStatus.Running)
            .SetProperty(a => a.IsPoolDelegate, true), ct);
        db.AgentSessions.Add(new AgentSession
        {
            Id = ParentSessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, Cwd = Harness.TempRoot,
            StartedAt = Generation, CreatedAt = Generation, LastSeenAt = Generation,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = TaskId, RootTaskId = TaskId, Title = "Permission incident regression",
            Goal = "Preserve the incident", Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode, Status = AgentTaskStatus.Working,
            AgentId = Harness.AgentId, AgentSessionId = SessionId,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = ParentSessionId,
            WorkingDirectory = WorktreePath, Workspace = WorkspaceMode.Worktree,
            WorktreePath = WorktreePath, WorktreeBranch = Branch,
            Attempt = 1, DispatchedAt = DispatchedAt, CreatedAt = DispatchedAt,
        });
        // Deliberately sparse: one unresolved call, no synthetic receipt or tool result.
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = SessionId, Sequence = 1,
            Uuid = "c1518-call-" + TaskId.ToString("N"), Kind = TranscriptKinds.ToolCall,
            ToolUseId = "toolu_01HErjZiaozvAqGVotStUFGv", ToolName = "Bash",
            ToolInput = "{\"command\":\"incident fixture; never executed\",\"timeout\":600000}",
            Timestamp = DispatchedAt, CreatedAt = DispatchedAt,
        });
        // A busy caller keeps failure/decision delivery in the real WhenIdle queue.
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = ParentSessionId, Sequence = 1,
            Uuid = "c1518-parent-" + TaskId.ToString("N"), Kind = TranscriptKinds.ToolCall,
            ToolName = "Read", Timestamp = DispatchedAt, CreatedAt = DispatchedAt,
        });
        await db.SaveChangesAsync(ct);
        Harness.Adapter.DialogGuardEnabled = true;
        Harness.Adapter.AcceptedStartedAt = Generation;
        Harness.Adapter.RenderedScreenOverride = CapturedFrame;
        await Harness.Queue.EnqueueAsync(SessionId, Refinement, MessageSendMode.WhenIdle, ct);
        RefinementId = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId)
            .Select(m => m.Id).SingleAsync(ct);
    }

    internal async Task AssertIncidentInputsAsync(CancellationToken ct)
    {
        var observation = await Harness.Runtime.ObserveDialogAsync(SessionId, ct);
        observation.ShouldNotBeNull("fixture: CARD-0680 must expose the actual observation contract");
        observation.State.ShouldBe(ProviderDialogState.Blocked);
        observation.Kind.ShouldBe("ToolPermission");
        observation.RuntimeEpoch.ShouldNotBe(Guid.Empty);
        observation.InputHistoryKnown.ShouldBeTrue();
        SessionGeneration.Equal(observation.AcceptedStartedAt, Generation).ShouldBeTrue();
        ProviderDialogObservationService.Refusal(observation, SessionId, Generation)
            .ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        await using var db = Db();
        (await SessionMessageQueueService.IsWorkingAsync(db, SessionId, ct)).ShouldBeTrue();
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == SessionId, ct)).ShouldBe(1);
        await AssertCustodyAsync(AgentTaskStatus.Working, ct);
    }

    internal async Task<AgentTaskDispatcher.TickResult> TickAsync(CancellationToken ct)
    {
        await using var scope = Harness.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
    }

    internal async Task<int> DeadlineAsync(CancellationToken ct)
    {
        await using var scope = Harness.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailOverdueTasksAsync(ct);
    }

    internal async Task<AgentTask> TaskAsync(CancellationToken ct)
    {
        await using var db = Db();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId, ct);
    }

    internal async Task AssertCustodyAsync(AgentTaskStatus status, CancellationToken ct)
    {
        await using var db = Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId, ct);
        task.Status.ShouldBe(status);
        task.Attempt.ShouldBe(1);
        task.DispatchedAt.ShouldBe(DispatchedAt, "a dialog cannot reset the task clock");
        task.WorktreePath.ShouldBe(WorktreePath);
        task.WorktreeBranch.ShouldBe(Branch);
        (await File.ReadAllTextAsync(SourceMarker, ct)).ShouldBe(SourceContents);
        (await db.AgentSessions.SingleAsync(s => s.Id == SessionId, ct)).Status.ShouldBe(SessionStatus.Running);
        var owner = await db.Agents.SingleAsync(a => a.Id == Harness.AgentId, ct);
        owner.PersistentSessionId.ShouldBe(SessionId.ToString("D"));
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == RefinementId, ct);
        queued.Body.ShouldBe(Refinement);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.DeliveryAttempts.ShouldBe(0);
        queued.SentAt.ShouldBeNull();
        (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == SessionId
            && t.Kind == TranscriptKinds.UserPrompt, ct)).ShouldBeFalse("queue insertion is not prompt receipt");
        Harness.Adapter.Inputs.ShouldBeEmpty("no approval, answer, cancellation or Enter");
        Harness.Adapter.ConditionalInputs.ShouldBeEmpty();
        Harness.Adapter.Started.ShouldBeFalse();
        Harness.Adapter.KillCount.ShouldBe(0);
        Harness.Adapter.KillGenerationCalls.ShouldBeEmpty();
        Stopper.Killed.ShouldBeEmpty();
        Launches.Calls.ShouldBe(0);
        Git.Calls.ShouldBeEmpty("no source mutation or cleanup");
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
        await _schema.DisposeAsync();
        // Only the private fixture scratch, after all service scopes have finished.
        if (Directory.Exists(Harness.TempRoot)) Directory.Delete(Harness.TempRoot, recursive: true);
    }

    internal sealed class RecordingLaunchFactory : IAgentProtocolAdapterFactory
    {
        internal int Calls { get; private set; }
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            Calls++;
            throw new InvalidOperationException("Permission diagnostics must not launch a session or tool.");
        }
    }
}
