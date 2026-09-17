using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0544 shared fixture: an owned cloned PostgreSQL database, a real scratch Git repository
/// holding a committed selection table and an original Code/Worktree landing owner branch, a card
/// on a real board/project, a running caller session, and the production service graph —
/// <see cref="AgentTaskService"/>, <see cref="AgentTaskDispatcher"/>, <see cref="AgentTaskReplyService"/>,
/// <see cref="SessionMessageQueueService"/>, <see cref="AgentTaskLandNotificationService"/> and the
/// concrete <see cref="InterimVerificationPolicy"/>. Only the external readiness file read
/// (<see cref="ControlledInterimReadiness"/>) and the caller's terminal are substitutes.
///
/// <para>Baselines are produced through the real boundary: a Final Review task is created through
/// <see cref="AgentTaskService.CreateAsync"/>, its report turn is seeded, and
/// <see cref="AgentTaskReplyService.OnTurnEndAsync"/> settles it. Negative rows then vary exactly one
/// stored field of that real outcome.</para>
/// </summary>
internal sealed class C544World : IAsyncDisposable
{
    public const string SelectionPath = "docs/plans/c544-selection.md";
    public const string Section = "Round selection";

    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ScratchGitRepo Repo { get; } = new("c544-world");
    public C544Clock Clock { get; } = new();
    public ControlledInterimReadiness Readiness { get; } = new();
    public ServiceProvider Services { get; private set; } = null!;
    public IInterceptor? Interceptor { get; private set; }
    public Project Project { get; private set; } = null!;
    public Board Board { get; private set; } = null!;
    public Card Card { get; private set; } = null!;
    public Guid CallerSessionId { get; private set; }
    public AgentTask Owner { get; private set; } = null!;
    public string OwnerSha { get; private set; } = "";
    public string SelectionSha { get; private set; } = "";
    public string BaseSha { get; private set; } = "";
    public DelegationSettings Delegation { get; } = new()
    {
        ReplyInlineMaxChars = 20_000,
        MaxTasksPerRoot = 400,
        MaxDepth = 5,
        PoolEnabled = false,
        OutputDistillerEnabled = false,
        PtySingleChunkBytes = 43_200,
    };

    public static async Task<C544World> CreateAsync(IInterceptor? interceptor = null, bool cardAllowsInterim = true)
    {
        var world = new C544World { Interceptor = interceptor };
        await world.InitializeAsync(cardAllowsInterim);
        return world;
    }

    private async Task InitializeAsync(bool cardAllowsInterim)
    {
        Delegation.AllowedRoots = [Path.GetDirectoryName(Repo.Path)!];
        await Repo.CommitFileAsync("README.md", "c544 base\n");
        BaseSha = (await Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        Directory.CreateDirectory(Path.Combine(Repo.Path, "docs", "plans"));
        await Repo.CommitFileAsync(SelectionPath.Replace('/', Path.DirectorySeparatorChar), SelectionMarkdown);
        SelectionSha = (await Repo.GitReadAsync("rev-parse", "HEAD")).Trim();

        Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        BuildServices();

        await using var db = CreateContext();
        var now = Clock.GetUtcNow().UtcDateTime;
        Project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c544-{Guid.NewGuid():N}", GitRepositoryUrl = "https://example.test/c544.git",
            CreatedAt = now, UpdatedAt = now,
        };
        Board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = Project.Id, Name = $"C544 board {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = Board.Id, StateKey = "backlog", Name = "Backlog", ColumnOrder = 0,
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        Card = new Card
        {
            Id = Guid.NewGuid(), BoardId = Board.Id, BoardColumnId = column.Id, Identifier = "CARD-0544",
            Title = "CARD-0544 fixture", Description = "Interim/Final verification fixture.",
            CodeVerificationPolicy = cardAllowsInterim ? CardVerificationPolicy.AllowInterim : CardVerificationPolicy.FullOnly,
            ReviewVerificationPolicy = cardAllowsInterim ? CardVerificationPolicy.AllowInterim : CardVerificationPolicy.FullOnly,
            CreatedAt = now, UpdatedAt = now,
        };
        CallerSessionId = Guid.NewGuid();
        db.AddRange(Project, Board, column, Card);
        db.AgentSessions.Add(Session(CallerSessionId, "c544-caller", now));

        var ownerId = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(ownerId)}";
        await Repo.GitAsync("checkout", "-b", branch);
        await Repo.CommitFileAsync("owner.md", "owner work\n");
        OwnerSha = (await Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await Repo.GitAsync("checkout", "master");
        Owner = new AgentTask
        {
            Id = ownerId, RootTaskId = ownerId, Title = "CARD-0544 owner", Goal = "Implement.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High, Workspace = WorkspaceMode.Worktree, WorkingDirectory = Repo.Path,
            RepoPath = Repo.Path, WorktreePath = Repo.Path, WorktreeBranch = branch, MergeTargetRef = "master",
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None, CardId = Card.Id,
            ProjectId = Project.Id, CreatedAt = now.AddHours(-2), CompletedAt = now.AddHours(-1),
            VerificationProfileVersion = 1, VerificationRound = VerificationRound.Final,
        };
        db.AgentTasks.Add(Owner);
        await db.SaveChangesAsync();
    }

    public const string SelectionMarkdown = """
        # CARD-0544 selection fixture

        ## Round selection

        | V/R ID | project | class/method | reason | filter/command | expected rows | round |
        |---|---|---|---|---|---|---|
        | V-5 | Antiphon.Tests | ReviewEvidenceParserTests.C544_ScopeGrammar | changed parser | exact method | 1 | interim |

        ## Deferred to final

        Everything else.
        """;

    public void BuildServices(IInterceptor? interceptor = null)
    {
        if (interceptor is not null) Interceptor = interceptor;
        var connection = Schema.ConnectionString;
        var faults = Interceptor;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connection);
            if (faults is not null) o.AddInterceptors(faults);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true, EvidenceTimeoutSeconds = 1, PollIntervalMs = 50, PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0, TranscriptConfirmTimeoutSeconds = 3, ReEnterIntervalSeconds = 1,
                PostFailureConfirmGraceSeconds = 3, UnobservableBaselineConfirmClockToleranceSeconds = 30,
                BootPromptRetryDelaySeconds = 0,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(Delegation));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new InterimVerificationSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Repo.WorktreeRoot, WorktreeStaleAfterDays = 7, WorktreeJanitorIntervalHours = 24,
        });
        services.AddSingleton<CapacityRecoveryService>();
        services.AddSingleton<ApiErrorRecoveryService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<AgentFilesService>();
        services.AddScoped<IWorkspaceProgressProbe>(sp => sp.GetRequiredService<AgentFilesService>());
        services.AddSingleton(Options.Create(new DeliverablesSettings
        {
            BrowserPath = Path.Combine(Path.GetTempPath(), "antiphon-missing-browser", "msedge.exe"),
        }));
        services.AddSingleton<MarkdownPdfRenderer>();
        services.AddSingleton<DeliverableBundleService>();
        services.AddSingleton<IInterimVerificationReadinessReader>(Readiness);
        services.AddScoped<InterimVerificationPolicy>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddSingleton<AgentTaskReplyService>();
        Services = services.BuildServiceProvider();
    }

    public async Task RestartAsync(IInterceptor? interceptor = null, bool keepInterceptor = false)
    {
        await Services.DisposeAsync();
        if (!keepInterceptor) Interceptor = interceptor;
        BuildServices();
    }

    public AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public AgentTaskService.Caller Caller(Guid? projectId = null) =>
        new(null, CallerSessionId, Repo.Path, ProjectId: projectId ?? Project.Id);

    public async Task<AgentTaskCreatedDto> CreateTaskAsync(CreateAgentTaskRequest request, AgentTaskService.Caller? caller = null)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .CreateAsync(request, caller ?? Caller(), CancellationToken.None);
    }

    public VerificationSelectionReference Selection() => new(SelectionPath, SelectionSha, Section);

    public CreateAgentTaskRequest FinalReview(string goal = "Review the owner.") => new(
        goal, Title: "CARD-0544 final review", Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Review,
        Workspace: WorkspaceMode.ReadOnly, WorkingDirectory: Repo.Path, Card: Card.Id.ToString());

    public CreateAgentTaskRequest InterimCode(Guid baselineId) => new(
        "Repair the owner.", Title: "CARD-0544 interim code", Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Code,
        Workspace: WorkspaceMode.Worktree, WorkingDirectory: Repo.Path, Card: Card.Id.ToString(),
        RepairSourceTaskId: Owner.Id, VerificationRound: VerificationRound.Interim,
        VerificationSubjectTaskId: Owner.Id, VerificationBaselineOutcomeId: baselineId,
        VerificationSelection: Selection());

    public CreateAgentTaskRequest InterimReview(Guid baselineId) => new(
        "Review the repair.", Title: "CARD-0544 interim review", Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Review,
        Workspace: WorkspaceMode.ReadOnly, WorkingDirectory: Repo.Path, Card: Card.Id.ToString(),
        VerificationRound: VerificationRound.Interim, VerificationSubjectTaskId: Owner.Id,
        VerificationBaselineOutcomeId: baselineId, VerificationSelection: Selection());

    /// <summary>
    /// A real Final Review settlement: create through the API service, bind a delegate session,
    /// seed the report turn, settle with the reply service. Returns the settled StageOutcome.
    /// </summary>
    public async Task<StageOutcome> SettleReviewAsync(CreateAgentTaskRequest? request = null, string scope = "Full",
        bool found = false, string? reviewedSha = null, Guid? subject = null, string next = "land")
    {
        var created = await CreateTaskAsync(request ?? FinalReview());
        var sessionId = await DispatchAsync(created.Id);
        var report = ReviewReport(created.Id, subject ?? Owner.Id, reviewedSha ?? OwnerSha, scope, found, next);
        await SeedTurnAsync(sessionId, created.Id, report);
        await Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
        await using var db = CreateContext();
        return await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == created.Id);
    }

    public static string ReviewReport(Guid taskId, Guid subject, string sha, string? scope, bool found, string next = "land",
        string? extraEvidenceLines = null) =>
        $"""
        Reviewed the owner.

        --- review evidence ---
        subjectTaskId: {subject:D}
        reviewedSourceSha: {sha}
        {(scope is null ? "" : "ordinaryScopeCompleted: " + scope)}{(extraEvidenceLines is null ? "" : "\n" + extraEvidenceLines)}

        {DelegationReportFormatter.FindingToken(taskId, found ? "found" : "clean")} {(found ? "a regression" : "")}
        --- next stage ---
        next: {next}
        handoff: C544 fixture handoff.
        """;

    /// <summary>Mark a queued task Dispatched on a fresh running delegate session, as the dispatcher would.</summary>
    public async Task<Guid> DispatchAsync(Guid taskId)
    {
        var sessionId = Guid.NewGuid();
        await using var db = CreateContext();
        var now = Clock.GetUtcNow().UtcDateTime;
        db.AgentSessions.Add(Session(sessionId, "c544-delegate", now));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Dispatched;
        task.AgentSessionId = sessionId;
        task.DispatchedAt = now;
        task.ParentSessionId ??= CallerSessionId;
        task.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        return sessionId;
    }

    public async Task SeedTurnAsync(Guid sessionId, Guid taskId, string report)
    {
        await TurnSeeding.SeedTurnAsync(CreateContext, sessionId,
            $"{DelegationReportFormatter.TaskMarker(taskId)} role=Review\n\nbrief", report);
    }

    public async Task SetCardPolicyAsync(CardVerificationPolicy code, CardVerificationPolicy review)
    {
        await using var db = CreateContext();
        var card = await db.Cards.SingleAsync(c => c.Id == Card.Id);
        card.CodeVerificationPolicy = code;
        card.ReviewVerificationPolicy = review;
        card.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    public async Task<int> TaskCountAsync(Func<IQueryable<AgentTask>, IQueryable<AgentTask>>? filter = null)
    {
        await using var db = CreateContext();
        var query = db.AgentTasks.AsNoTracking().Where(t => t.Id != Owner.Id);
        return await (filter?.Invoke(query) ?? query).CountAsync();
    }

    public async Task<AgentTask> TaskAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public static AgentSession Session(Guid id, string name, DateTime now) => new()
    {
        Id = id, DefinitionName = name, AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
        Cwd = Path.GetTempPath(), Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
    };

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        Repo.Dispose();
    }
}

/// <summary>A wall clock that can be moved forward; never backwards.</summary>
internal sealed class C544Clock : TimeProvider
{
    private TimeSpan _offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
    public void Advance(TimeSpan amount) => _offset += amount;
}

/// <summary>
/// The only substitute in the policy graph: the external readiness file read. Tests set the
/// verdict; <see cref="Reads"/> proves the recheck actually reread it.
/// </summary>
internal sealed class ControlledInterimReadiness : IInterimVerificationReadinessReader
{
    public static readonly InterimReadinessSnapshot ReadySnapshot = new(
        "docs/investigations/2026-09-20-card-0487-nightly-qualification.md",
        "1111111111111111111111111111111111111111", "policy-hash", "script-hash", "run-1", "job-1", ["recipient-1"],
        new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 17, 8, 1, 0, DateTimeKind.Utc));

    public InterimReadiness Verdict { get; set; } = new(true, "ready", ReadySnapshot);
    public int Reads { get; private set; }
    public Exception? Throw { get; set; }

    public Task<InterimReadiness> ReadAsync(string? repositoryPath, Guid? projectId, CancellationToken ct)
    {
        Reads++;
        if (Throw is not null) throw Throw;
        return Task.FromResult(Verdict);
    }
}
