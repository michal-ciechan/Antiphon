using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class RepairSourceWorld : IAsyncDisposable
{
    public ScratchGitRepo Repo { get; }
    public string Remote { get; }
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public ControlledTaskProgressGit Git { get; private set; } = null!;
    public ControlledGitWorkspaceService FilesGit { get; private set; } = null!;
    public RepositoryMutationLease Leases { get; private set; } = null!;
    public DispatchSaveFault Fault { get; } = new();
    public AgentTask Owner { get; private set; } = null!;
    public AgentTask Repair { get; internal set; } = null!;
    public Guid CallerSessionId { get; private set; }
    public string OwnerRef { get; private set; } = "";
    public string OwnerSha { get; private set; } = "";
    public bool ExplicitIntegration { get; init; }
    public bool OrdinaryCodeTask { get; init; }

    public RepairSourceWorld()
    {
        Repo = new ScratchGitRepo("card0499-repair");
        Remote = Path.Combine(Repo.Path, "..", "remote-" + Guid.NewGuid().ToString("N")[..8] + ".git");
        Remote = Path.GetFullPath(Remote);
    }

    public static async Task<RepairSourceWorld> CreateAsync(
        bool explicitIntegration = false, bool ordinaryCodeTask = false)
    {
        var world = new RepairSourceWorld
        {
            ExplicitIntegration = explicitIntegration,
            OrdinaryCodeTask = ordinaryCodeTask,
        };
        await world.InitializeAsync();
        return world;
    }

    public async Task InitializeAsync()
    {
        await Repo.CommitFileAsync("README.md", "base\n");
        Directory.CreateDirectory(Remote);
        (await ScratchGitRepo.GitInAsync(Remote, "init", "--bare")).Ok.ShouldBeTrue();
        await Repo.GitAsync("remote", "add", "origin", Remote);
        await Repo.GitAsync("push", "-u", "origin", "master");

        Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Leases = new RepositoryMutationLease(new LandingGit());
        Git = new ControlledTaskProgressGit(Leases);
        FilesGit = new ControlledGitWorkspaceService();
        BuildServices();

        await using var db = CreateContext();
        CallerSessionId = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = CallerSessionId,
            DefinitionName = "caller",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Repo.Path,
            Cols = 120, Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });

        var ownerId = Guid.NewGuid();
        Owner = new AgentTask
        {
            Id = ownerId,
            RootTaskId = ownerId,
            Title = "owner code",
            Goal = "owner work",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Repo.Path,
            RepoPath = Repo.Path,
            Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
        };
        db.AgentTasks.Add(Owner);
        await db.SaveChangesAsync();

        await using var scope = Services.CreateAsyncScope();
        var worktrees = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        await worktrees.CreateForTaskAsync(Owner, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(Owner.WorktreePath!, "owner.md"), "owner work\n");
        (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "add", "owner.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "commit", "-m", "owner work")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "push", "origin", Owner.WorktreeBranch!)).Ok.ShouldBeTrue();
        OwnerSha = (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        OwnerRef = "refs/heads/" + Owner.WorktreeBranch!;
        Owner.Status = AgentTaskStatus.Succeeded;
        Owner.CompletedAt = DateTime.UtcNow.AddMinutes(-5);
        Owner.WorktreeBaseSha = OwnerSha;
        await db.SaveChangesAsync();

        var repairId = Guid.NewGuid();
        Repair = new AgentTask
        {
            Id = repairId,
            RootTaskId = repairId,
            Title = "repair",
            Goal = "attribute the other worktree",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Repo.Path,
            RepoPath = Repo.Path,
            RepairSourceTaskId = OrdinaryCodeTask ? null : Owner.Id,
            MergeTargetRef = ExplicitIntegration ? Owner.WorktreeBranch : null,
            ParentSessionId = CallerSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(Repair);
        await db.SaveChangesAsync();
    }

    private void BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(DeliverySettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<ApiErrorRecoveryService>();
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<IRepositoryMutationLease>(Leases);
        services.AddSingleton<ILandingGit>(Git);
        services.AddSingleton<ITaskProgressGit>(Git);
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Repo.WorktreeRoot,
            WorktreeAddTimeoutSeconds = 180,
        }, FilesGit);
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(Schema.ConnectionString);
            o.AddInterceptors(Fault);
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<AgentFilesService>();
        services.AddScoped<IWorkspaceProgressProbe>(sp => sp.GetRequiredService<AgentFilesService>());
        services.AddScoped<TaskCompletionProgressService>();
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<ModelAvailability>();
        services.AddSingleton(Options.Create(new DeliverablesSettings
        {
            BrowserPath = Path.Combine(Path.GetTempPath(), "antiphon-missing-browser", "msedge.exe"),
        }));
        services.AddSingleton<MarkdownPdfRenderer>();
        services.AddSingleton<DeliverableBundleService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddSingleton<AgentTaskLandQueue>();
        services.AddScoped<AgentTaskLandService>();
        Services = services.BuildServiceProvider();
    }

    public DelegationSettings DeliverySettings() => new()
    {
        MaxConcurrentTasks = 512,
        AllowedRoots = [Repo.Path],
        BriefInlineMaxBytes = 1_000_000,
        ModernPtyBriefInlineMaxBytes = 1_000_000,
        HerdrPaneBriefInlineMaxBytes = 1_000_000,
    };

    public AppDbContext CreateContext() =>
        new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public string ClaimLine(string sha) => $"[antiphon-progress:{Repair.Id:D} commit={sha}]";

    public async Task<string> BriefTextAsync(AgentTask repair, SessionQueuedMessage queued)
    {
        var spill = Path.Combine(repair.WorkingDirectory ?? Repo.Path, ".antiphon",
            $"task-{DelegationReportFormatter.Short(repair.Id)}-brief.md");
        return File.Exists(spill) ? await File.ReadAllTextAsync(spill) : queued.Body;
    }

    public string DoneReport(string body, string? claimSha = null)
    {
        var claim = claimSha is null ? "" : ClaimLine(claimSha) + "\n";
        return body + "\n" + claim + "--- next stage ---\nnext: review\nhandoff: x\n";
    }

    public async Task AmendOwnerCommitDateAsync(DateTimeOffset when)
    {
        var stamp = when.ToString("o");
        var env = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_DATE"] = stamp,
            ["GIT_COMMITTER_DATE"] = stamp,
        };
        (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, env, "commit", "--amend", "--no-edit", "--date", stamp))
            .Ok.ShouldBeTrue();
        OwnerSha = (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(Owner.WorktreePath!, "push", "--force", "origin", Owner.WorktreeBranch!))
            .Ok.ShouldBeTrue();
        await using var db = CreateContext();
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == Owner.Id);
        owner.WorktreeBaseSha = OwnerSha;
        await db.SaveChangesAsync();
        Owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Owner.Id);
    }

    public async Task<string> CommitInPrimaryTreeAsync(string message)
    {
        var path = Repair.WorktreePath!;
        await File.WriteAllTextAsync(Path.Combine(path, "direct.md"), message + "\n");
        (await ScratchGitRepo.GitInAsync(path, "add", "direct.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(path, "commit", "-m", message)).Ok.ShouldBeTrue();
        return (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim();
    }

    public async Task<(AgentTask Repair, Guid SessionId)> DispatchAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(CancellationToken.None);
        await using var db = CreateContext();
        Repair = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Repair.Id);
        Owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Owner.Id);
        return (Repair, Repair.AgentSessionId ?? Guid.Empty);
    }

    public async Task SettleAsync(string report)
    {
        var sessionId = Repair.AgentSessionId ?? throw new InvalidOperationException("not dispatched");
        await TurnSeeding.SeedTurnAsync(CreateContext, sessionId, DelegationReportFormatter.TaskMarker(Repair.Id), report);
        var replies = Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using var db = CreateContext();
        Repair = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Repair.Id);
    }

    public async Task<string> CommitInOwnerTreeAsync(string message, bool push)
    {
        var path = Owner.WorktreePath!;
        await File.WriteAllTextAsync(Path.Combine(path, "repair.md"), message + "\n");
        (await ScratchGitRepo.GitInAsync(path, "add", "repair.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(path, "commit", "-m", message)).Ok.ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim();
        if (push)
            (await ScratchGitRepo.GitInAsync(path, "push", "origin", Owner.WorktreeBranch!)).Ok.ShouldBeTrue();
        return sha;
    }

    public async Task<string> CommitFromSecondCloneAsync(string fullRef, string message)
    {
        var branch = fullRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? fullRef[11..] : fullRef;
        if (!(await ScratchGitRepo.GitInAsync(Remote, "rev-parse", fullRef)).Ok)
            (await ScratchGitRepo.GitInAsync(Repo.Path, "push", "origin", branch)).Ok.ShouldBeTrue();
        var clone = Path.Combine(Repo.WorktreeRoot, "clone-" + Guid.NewGuid().ToString("N")[..8]);
        (await ScratchGitRepo.GitInAsync(Repo.WorktreeRoot, "clone", "--branch", branch, Remote, clone)).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(clone, "elsewhere.md"), message + "\n");
        (await ScratchGitRepo.GitInAsync(clone, "add", "elsewhere.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(clone, "commit", "-m", message)).Ok.ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(clone, "rev-parse", "HEAD")).StdOut.Trim();
        (await ScratchGitRepo.GitInAsync(clone, "push", "origin", branch)).Ok.ShouldBeTrue();
        return sha;
    }

    public async Task<WorldSnapshot> Snapshot()
    {
        await using var db = CreateContext();
        var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Owner.Id);
        var repair = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Repair.Id);
        var ownerHead = Owner.WorktreePath is { } p && Directory.Exists(p)
            ? (await ScratchGitRepo.GitInAsync(p, "rev-parse", "HEAD")).StdOut.Trim() : null;
        var repairHead = repair.WorktreePath is { } rp && Directory.Exists(rp)
            ? (await ScratchGitRepo.GitInAsync(rp, "rev-parse", "HEAD")).StdOut.Trim() : null;
        var localOwner = (await ScratchGitRepo.GitInAsync(Repo.Path, "rev-parse", OwnerRef)).StdOut.Trim();
        var remoteOwner = (await ScratchGitRepo.GitInAsync(Remote, "rev-parse", OwnerRef)).StdOut.Trim();
        var list = (await ScratchGitRepo.GitInAsync(Repo.Path, "worktree", "list", "--porcelain", "-z")).StdOut;
        return new WorldSnapshot(owner, repair, ownerHead, repairHead, localOwner, remoteOwner, list);
    }

    public async Task<IReadOnlyList<string>> ProgressPins()
    {
        var result = await ScratchGitRepo.GitInAsync(Repo.Path, "for-each-ref", $"refs/antiphon/progress/{Repair.Id:N}/");
        return result.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<SessionQueuedMessage?> Note()
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.AgentSessionId == CallerSessionId && m.SourceTaskId == Repair.Id);
    }

    public async Task<List<AgentTaskEvent>> Warnings()
    {
        await using var db = CreateContext();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == Repair.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        Repo.Dispose();
        try { Directory.Delete(Remote, true); } catch { }
    }

    internal sealed record WorldSnapshot(
        AgentTask Owner, AgentTask Repair, string? OwnerHead, string? RepairHead,
        string LocalOwnerTip, string RemoteOwnerTip, string WorktreeList);

    internal sealed class DispatchSaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed) throw new LandingSafetyHarness.InjectedSaveFailure();
            return ValueTask.FromResult(result);
        }
    }
}
