using System.Data.Common;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    public async Task C540_IdenticalTipsCollapse()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a, alias: true);
        b.CreatedAt = a.CreatedAt.AddMinutes(1);
        await w.RunAsync([(b, 1)]);
    }

    [Test]
    public async Task C540_AncestorChainCollapses()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a); var alias = await w.AddAsync(b, alias: true);
        var c = await w.AddAsync(b);
        a.CreatedAt = DateTime.UtcNow.AddMinutes(-1); b.CreatedAt = a.CreatedAt.AddMinutes(-1);
        alias.CreatedAt = b.CreatedAt.AddMinutes(-1); c.CreatedAt = alias.CreatedAt.AddMinutes(-1);
        await w.RunAsync([(c, 3)]);
    }

    [Test]
    public async Task C540_DivergentTipsRemainVisible()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync();
        await w.RunAsync([(a, 0), (b, 0)]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C540_ForkKeepsBothTips(bool merged)
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a); var c = await w.AddAsync(a);
        b.CreatedAt = c.CreatedAt.AddMinutes(1);
        if (!merged) { await w.RunAsync([(b, 1), (c, 0)]); return; }
        var d = await w.AddAsync(b, alias: true);
        await w.Repo.GitAsync("checkout", d.WorktreeBranch!);
        await w.Repo.GitAsync("merge", "--no-ff", c.WorktreeBranch!, "-m", "merge both divergent histories");
        await w.Repo.GitAsync("checkout", "master");
        await w.RunAsync([(d, 3)]);
    }

    [Test]
    public async Task C540_PatchEquivalentSiblingsStillWarn()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(alias: true);
        await w.Repo.GitAsync("checkout", b.WorktreeBranch!);
        await w.Repo.GitAsync("cherry-pick", a.WorktreeBranch!);
        await w.Repo.GitAsync("commit", "--amend", "-m", "same patch, distinct history");
        await w.Repo.GitAsync("checkout", "master");
        await w.RunAsync([(a, 0), (b, 0)]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C540_RedundantSiblingStillHolds(bool alias)
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a, alias);
        a.Status = AgentTaskStatus.Blocked; a.LandRequestedAt = DateTime.UtcNow;
        b.CreatedAt = a.CreatedAt.AddMinutes(1); w.Task.Role = AgentTaskRole.TestDesign;
        await w.Db.SaveChangesAsync();
        await using (var provider = CreateProvider(w.Connection, w.Repo.WorktreeRoot))
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
        var held = await w.Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.Task.Id);
        held.Status.ShouldBe(AgentTaskStatus.Queued); held.WorktreePath.ShouldBeNull();
        (await w.Db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == w.Task.Id)).ShouldBe(0);
        a.LandRequestedAt = null;
        await w.RunAsync([(b, 1)]);
    }

    [Test]
    [Arguments("card")]
    [Arguments("shared")]
    [Arguments("Queued")]
    [Arguments("Dispatched")]
    [Arguments("Working")]
    [Arguments("Failed")]
    [Arguments("Canceled")]
    [Arguments("repository")]
    public async Task C540_ExcludedSiblingCannotCoverWarning(string exclusion)
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a);
        switch (exclusion)
        {
            case "card": b.CardId = (await SeedCardAsync(w.Db, "OTHER-CARD")).Id; break;
            case "shared": b.Workspace = WorkspaceMode.Shared; break;
            case "repository":
                // The local containing branch remains present, despite foreign repository metadata.
                b.RepoPath = Path.Combine(Path.GetTempPath(), "foreign-" + Guid.NewGuid().ToString("N")); break;
            default:
                b.Status = Enum.Parse<AgentTaskStatus>(exclusion);
                // The excluded Queued row cannot itself claim in this tick.
                b.CreatedAt = w.Task.CreatedAt.AddMinutes(1);
                break;
        }
        await w.RunAsync([(a, 0)], onLease: exclusion == "Queued" ? async () =>
        {
            await using var db = w.Fresh();
            await db.AgentTasks.Where(t => t.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
        } : null);
    }

    [Test]
    public async Task C540_ReducedWarningsKeepBaseDiagnostics()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var alias = await w.AddAsync(a, alias: true);
        var b = await w.AddAsync(a); var c = await w.AddAsync();
        w.Task.ProjectId = w.Project.Id; w.Project.BaseBranch = "master";
        await w.RunAsync([(b, 2), (c, 0)], onLease: async () =>
        {
            await using var db = w.Fresh();
            await db.Projects.Where(p => p.Id == w.Project.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.BaseBranch, "missing-c540"));
        }, diagnostics: [DispatchBaseNotificationPayload.MismatchKey, DispatchBaseNotificationPayload.DefaultUnresolvedKey]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C540_ClaimRollbackHasNoCollapsedIntent(bool afterSave)
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); await w.AddAsync(a, alias: true);
        await w.Db.SaveChangesAsync();
        var interceptor = new ClaimCommitFailure(w.Task.Id);
        var boundary = new ClaimFailure(w.Task.Id);
        await using var provider = CreateProvider(w.Connection, w.Repo.WorktreeRoot,
            interceptor: afterSave ? interceptor : null, boundary: afterSave ? null : boundary);
        await using var scope = provider.CreateAsyncScope();
        // Cancellation exits the real producer without its unrelated failure-notification writes.
        using var canceled = new CancellationTokenSource();
        interceptor.Cancel = boundary.Cancel = canceled;
        await Should.ThrowAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(canceled.Token));
        (afterSave ? interceptor.Fired : boundary.Fired).ShouldBeTrue();
        await using var check = w.Fresh();
        (await check.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == w.Task.Id)).ShouldBe(0);
        (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == w.Task.Id && e.Type == AgentTaskEventType.Dispatched)).ShouldBe(0);
        var task = await check.AgentTasks.SingleAsync(t => t.Id == w.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Queued); task.AgentSessionId.ShouldBeNull();
        (await check.AgentSessions.CountAsync()).ShouldBe(1, "only the original caller remains");
    }

    [Test]
    public async Task C540_ClaimLoserOwesNoIntent()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); await w.AddAsync(a, alias: true);
        await w.Db.SaveChangesAsync();
        await using var provider = CreateProvider(w.Connection, w.Repo.WorktreeRoot, onLeaseAcquired: async () =>
        {
            await using var edit = w.Fresh();
            await edit.AgentTasks.Where(t => t.Id == w.Task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
        });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
        await using var db = w.Fresh();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == w.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Queued); task.WorktreePath.ShouldBeNull(); task.AgentSessionId.ShouldBeNull();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C540_ObservationsRemainPinned()
    {
        await using var w = await SiblingWorld.CreateAsync();
        var a = await w.AddAsync(); var b = await w.AddAsync(a);
        await w.Db.SaveChangesAsync();
        var before = new Dictionary<string, string>();
        foreach (var row in new[] { a, b }) before.Add(row.WorktreeBranch!, (await w.Repo.GitReadAsync("rev-parse", row.WorktreeBranch!)).Trim());
        var git = new MovingSiblingGit(before.Keys.ToHashSet());
        await using var provider = CreateProvider(w.Connection, w.Repo.WorktreeRoot, git: git);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
        await using var db = w.Fresh();
        var intent = (await db.AgentTaskDispatchWarningIntents.Where(i => i.TaskId == w.Task.Id).ToListAsync()).ShouldHaveSingleItem();
        intent.WarningKey.ShouldBe(DispatchBaseNotificationPayload.SiblingKey(b.Id));
        intent.Detail.ShouldContain(before[b.WorktreeBranch!]); intent.Detail.ShouldContain("also covers 1");
        foreach (var tip in before.Values)
        {
            git.Commands.ShouldContain(args => args.SequenceEqual(new[] { "cherry", "master", tip }));
            git.Commands.ShouldContain(args => args.SequenceEqual(new[] { "log", "-1", "--format=%s", tip }));
        }
        git.Commands.ShouldContain(args => args.SequenceEqual(new[] { "merge-base", "--is-ancestor", before[a.WorktreeBranch!], before[b.WorktreeBranch!] }));
    }

    private sealed class MovingSiblingGit(HashSet<string> branches) : LandingGit
    {
        public List<string[]> Commands { get; } = [];
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add(arguments.ToArray());
            var result = await base.RunAsync(repository, arguments, ct);
            foreach (var branch in branches)
                if (arguments.SequenceEqual(new[] { "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}^{{commit}}" }))
                    (await base.RunAsync(repository, new[] { "update-ref", "refs/heads/" + branch, "master" }, ct)).Succeeded.ShouldBeTrue();
            return result;
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(20)]
    public async Task C540_ProbeCensus(int uniqueTips)
    {
        await using var w = await SiblingWorld.CreateAsync();
        var expected = new List<(AgentTask, int)>();
        for (var i = 0; i < uniqueTips; i++) expected.Add((await w.AddAsync(), 0));
        if (uniqueTips == 1)
        {
            var alias = await w.AddAsync(expected[0].Item1, alias: true);
            alias.CreatedAt = expected[0].Item1.CreatedAt.AddMinutes(1);
            expected[0] = (alias, 1);
        }
        var git = new AncestryCensus();
        var watch = Stopwatch.StartNew();
        await w.RunAsync(expected.ToArray(), git: git);
        git.Pairs.Count.ShouldBe(uniqueTips * (uniqueTips - 1));
        git.Pairs.Distinct().Count().ShouldBe(git.Pairs.Count);
        Console.WriteLine($"C540 probe census: tips={uniqueTips}; strict calls={git.Pairs.Count}; guard elapsed={git.GuardElapsed.TotalMilliseconds:F1}ms; dispatch plus projection={watch.Elapsed.TotalMilliseconds:F1}ms");
    }

    private sealed class AncestryCensus : LandingGit
    {
        public List<string> Pairs { get; } = [];
        private readonly Stopwatch _guard = new();
        public TimeSpan GuardElapsed { get; private set; }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (!_guard.IsRunning) _guard.Start();
            var result = await base.RunAsync(repository, arguments, ct);
            if (arguments.Take(2).SequenceEqual(new[] { "merge-base", "--is-ancestor" }))
            {
                Pairs.Add(string.Join(" ", arguments.Skip(2))); GuardElapsed = _guard.Elapsed;
            }
            return result;
        }
    }

    private sealed class ClaimFailure(Guid taskId) : LandDeliveryBoundary
    {
        public CancellationTokenSource? Cancel { get; set; }
        public bool Fired { get; private set; }
        public override Task ReachedAsync(string boundary, Guid task, Guid related, CancellationToken ct)
        {
            if (task == taskId && boundary == "dispatch-warning-claim-before-commit")
            { Fired = true; Cancel!.Cancel(); ct.ThrowIfCancellationRequested(); }
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class ClaimCommitFailure(Guid taskId) : DbTransactionInterceptor
    {
        public CancellationTokenSource? Cancel { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            if (!Fired && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTaskDispatchWarningIntent>().Any(e => e.Entity.TaskId == taskId))
            { Fired = true; Cancel!.Cancel(); ct.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SiblingWorld : IAsyncDisposable
    {
        public ScratchGitRepo Repo { get; } = new("c540-guard");
        private IsolatedTestSchema _schema = null!;
        public AppDbContext Db { get; private set; } = null!;
        public AgentTask Task { get; private set; } = null!;
        public Card Card { get; private set; } = null!;
        public Project Project { get; private set; } = null!;
        private readonly List<AgentTask> _siblings = [];
        public string Connection => _schema.ConnectionString;
        public AppDbContext Fresh() => CreateContext(_schema);
        public static async Task<SiblingWorld> CreateAsync()
        {
            var w = new SiblingWorld();
            await w.Repo.CommitFileAsync("README.md", "base\n");
            w._schema = await TestDbFixture.CreateIsolatedSchemaAsync(); w.Db = w.Fresh();
            (w.Card, w.Project) = await SeedCardWithProjectAsync(w.Db, "CARD-0540");
            var parent = Guid.NewGuid(); await SeedParentSessionAsync(w.Db, parent);
            w.Task = await SeedQueuedWorktreeTaskAsync(w.Db, w.Repo.Path, w.Card.Id, parent);
            return w;
        }
        public async Task<AgentTask> AddAsync(AgentTask? ancestor = null, bool alias = false)
        {
            var sibling = await SeedKeptSiblingAsync(Db, Repo, Card.Id, $"sibling {_siblings.Count}",
                ancestor?.WorktreeBranch ?? "master", alias);
            _siblings.Add(sibling); return sibling;
        }
        public async Task RunAsync((AgentTask Task, int Covered)[] expected, Func<Task>? onLease = null,
            string[]? diagnostics = null, LandingGit? git = null)
        {
            diagnostics ??= [];
            await Db.SaveChangesAsync();
            var tips = new Dictionary<Guid, string>();
            foreach (var sibling in _siblings)
                tips.Add(sibling.Id, (await Repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim());
            // Real dirty/untracked content on an older kept checkout is outside commit containment.
            var sentinel = Path.Combine(Repo.WorktreeRoot, "sentinel");
            if (_siblings.Count > 0)
            {
                await Repo.GitAsync("worktree", "add", sentinel, _siblings[0].WorktreeBranch!);
                await File.WriteAllTextAsync(Path.Combine(sentinel, "README.md"), "dirty tracked sentinel");
                await File.WriteAllTextAsync(Path.Combine(sentinel, "untracked.txt"), "untracked sentinel");
            }
            await using var provider = CreateProvider(Connection, Repo.WorktreeRoot, onLeaseAcquired: onLease, git: git);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
            await using var check = Fresh();
            var dispatched = await check.AgentTasks.SingleAsync(t => t.Id == Task.Id);
            dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
            var intents = await check.AgentTaskDispatchWarningIntents.Where(i => i.TaskId == Task.Id).ToListAsync();
            intents.Count.ShouldBe(expected.Length + diagnostics.Length);
            foreach (var (task, covered) in expected)
            {
                var intent = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(task.Id));
                intent.Detail.ShouldContain(task.WorktreeBranch!); intent.Detail.ShouldContain(tips[task.Id]);
                intent.Detail.ShouldContain("Land " + DelegationReportFormatter.Short(task.Id));
                if (covered == 0) intent.Detail.ShouldNotContain("also covers");
                else intent.Detail.ShouldContain($"At the observed tips, this warning also covers {covered} other kept sibling branches.");
            }
            foreach (var key in diagnostics) intents.ShouldContain(i => i.WarningKey == key);
            await MaterializeAndDeliverAsync(scope.ServiceProvider, Task.Id, default);
            foreach (var intent in intents)
            {
                (await check.AgentTaskEvents.SingleAsync(e => e.Id == intent.Id)).Detail.ShouldBe(intent.Detail);
                var note = await check.AgentTaskLandNotifications.SingleAsync(n => n.Id == intent.NotificationId);
                note.Body.ShouldBe(intent.Body); note.SourceEventId.ShouldBe(intent.Id);
            }
            (await check.AgentTaskLandNotifications.CountAsync(n => n.TaskId == Task.Id)).ShouldBe(intents.Count);
            foreach (var sibling in _siblings)
                (await Repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim().ShouldBe(tips[sibling.Id]);
            if (_siblings.Count > 0)
            {
                (await File.ReadAllTextAsync(Path.Combine(sentinel, "README.md"))).ShouldBe("dirty tracked sentinel");
                (await File.ReadAllTextAsync(Path.Combine(sentinel, "untracked.txt"))).ShouldBe("untracked sentinel");
            }
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _schema.DisposeAsync(); Repo.Dispose(); }
    }
}
