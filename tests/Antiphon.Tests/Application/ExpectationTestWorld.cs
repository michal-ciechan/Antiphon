using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 S3 fixture: one board, its standing agent, its audit card and a live owned session,
/// in a schema of its own. Rows use fresh IDs; nothing here is shared with another test.
/// </summary>
internal sealed record ExpectationTestWorld(
    DbContextOptions<AppDbContext> Options,
    Guid ProjectId,
    Guid BoardId,
    Guid ColumnId,
    Guid AgentId,
    Guid CardId,
    Guid OwnedSessionId,
    ExpectationDirectiveSettings Directive,
    string Digest,
    DateTime Now)
{
    public static async Task<ExpectationTestWorld> CreateAsync(string connectionString, int localTarget = 0, int remoteTarget = 0)
    {
        var options = TestDbFixture.CreateDbContextOptions(connectionString);
        var now = new DateTime(2026, 9, 24, 2, 34, 4, DateTimeKind.Utc);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var directive = new ExpectationDirectiveSettings
        {
            Id = "tonight",
            AgentId = agentId,
            BoardId = boardId,
            AuditCardId = cardId,
            OperatorChannelId = Guid.NewGuid(),
            Enabled = true,
            Targets =
            [
                new ExpectationTargetSettings
                {
                    InFlightTarget = Math.Max(1, localTarget),
                    Candidates =
                    [
                        new ExpectationCandidateSettings
                        {
                            AgentKind = AgentKind.ClaudeCode,
                            ModelLevel = AgentModelLevel.High,
                            SubscriptionKey = "local-key",
                        },
                    ],
                },
                new ExpectationTargetSettings
                {
                    RunnerId = "server2",
                    InFlightTarget = Math.Max(1, remoteTarget),
                    Candidates =
                    [
                        new ExpectationCandidateSettings
                        {
                            AgentKind = AgentKind.ClaudeCode,
                            ModelLevel = AgentModelLevel.High,
                            SubscriptionKey = "remote-key",
                        },
                    ],
                },
            ],
        };
        await using var db = new AppDbContext(options);
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = "c650",
            GitRepositoryUrl = "",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Boards.Add(new Board
        {
            Id = boardId,
            ProjectId = projectId,
            Name = "c650",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            StateKey = "backlog",
            Name = "Backlog",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Cards.Add(new Card
        {
            Id = cardId,
            BoardId = boardId,
            BoardColumnId = columnId,
            Identifier = "C650" + cardId.ToString("N")[..8],
            Title = "audit",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "Standing",
            Slug = "c650-" + agentId.ToString("N"),
            WorkingDirectory = "/src/antiphon",
            BoardId = boardId,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentSessions.Add(Session(sessionId, agentId, now.AddHours(-3), SessionStatus.Running));
        await db.SaveChangesAsync();
        return new ExpectationTestWorld(
            options,
            projectId,
            boardId,
            columnId,
            agentId,
            cardId,
            sessionId,
            directive,
            ExpectationDirectiveDigest.Compute(directive),
            now);
    }

    public AppDbContext Db() => new(Options);

    public ExpectationWatchdogService Service(
        AppDbContext db,
        TimeProvider clock,
        IExpectationCatchUp? catchUp = null,
        DelegationSettings? delegation = null) =>
        new(db, new ExpectationLedger(db, clock, new QuietBus()), clock, delegation, catchUp);

    public Card BacklogCard(Guid id, string identifier) => new()
    {
        Id = id,
        BoardId = BoardId,
        BoardColumnId = ColumnId,
        Identifier = identifier + id.ToString("N")[..6],
        Title = identifier,
        Status = CardStatus.Backlog,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    public static AgentSession Session(Guid id, Guid? agentId, DateTime startedAt, SessionStatus status) => new()
    {
        Id = id,
        StandingAgentId = agentId,
        DefinitionName = "claude",
        AgentKind = AgentKind.ClaudeCode,
        Status = status,
        Cwd = "/src/antiphon",
        CreatedAt = startedAt,
        StartedAt = startedAt,
        LastSeenAt = startedAt,
    };

    public AgentTask Task(
        Guid id,
        AgentTaskStatus status,
        DateTime? dispatchedAt,
        Guid? sessionId = null,
        string? runnerId = null,
        DateTime? createdAt = null,
        string? result = null) => new()
    {
        Id = id,
        RootTaskId = id,
        CardId = CardId,
        ProjectId = ProjectId,
        ParentSessionId = OwnedSessionId,
        Title = "task",
        Goal = "observe",
        Status = status,
        Role = AgentTaskRole.Code,
        RunnerId = runnerId,
        AgentSessionId = sessionId,
        WorkingDirectory = "/src/antiphon",
        CreatedAt = createdAt ?? (dispatchedAt ?? Now).AddMinutes(-1),
        DispatchedAt = dispatchedAt,
        Result = result,
        LaunchEnvOverrideJson = "{}",
        InheritedLaunchEnvJson = "{}",
    };

    public static AgentTaskEvent Event(Guid taskId, AgentTaskEventType type, DateTime at, string detail) => new()
    {
        Id = Guid.NewGuid(),
        AgentTaskId = taskId,
        Type = type,
        At = at,
        Detail = detail,
    };

    public static TranscriptEntry Transcript(Guid sessionId, long sequence, string kind, DateTime at, string? text = null,
        string? toolName = null, string? toolInput = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Timestamp = at,
        Text = text,
        ToolName = toolName,
        ToolInput = toolInput,
        CreatedAt = at,
    };

    /// <summary>A caller-bound note plus the task event it was minted from.</summary>
    public static (AgentTaskEvent Source, AgentTaskLandNotification Note) Note(
        Guid taskId,
        Guid? parentSessionId,
        LandNotificationState state,
        DateTime createdAt,
        LandNotificationKind kind = LandNotificationKind.Held,
        bool legacy = false,
        Guid? queueMessageId = null,
        int attempts = 0,
        DateTime? nextAttemptAt = null,
        string? lastError = null)
    {
        var source = Event(taskId, AgentTaskEventType.Warning, createdAt, "note source");
        return (source, new AgentTaskLandNotification
        {
            Id = Guid.NewGuid(),
            IsLegacy = legacy,
            TaskId = taskId,
            SourceEventId = source.Id,
            Kind = kind,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = parentSessionId,
            Body = "[task " + taskId.ToString("N")[..8] + "] held note " + Guid.NewGuid().ToString("N"),
            ContentDigest = Guid.NewGuid().ToString("N"),
            CreatedAt = createdAt,
            NextAttemptAt = nextAttemptAt ?? createdAt,
            EnqueueAttempts = attempts,
            State = state,
            LastErrorCode = lastError,
            LastErrorAt = lastError is null ? null : createdAt,
            QueueMessageId = queueMessageId,
            ConfirmedAt = state == LandNotificationState.Confirmed ? createdAt.AddMinutes(1) : null,
        });
    }

    public static SessionQueuedMessage Queued(Guid id, Guid sessionId, QueuedMessageStatus status, DateTime at, long sequence) => new()
    {
        Id = id,
        AgentSessionId = sessionId,
        Body = "queued note",
        Sequence = sequence,
        Origin = QueuedMessageOrigin.Delegation,
        Status = status,
        CreatedAt = at,
    };

    internal sealed class QuietBus : IEventBus
    {
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.CompletedTask;

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
