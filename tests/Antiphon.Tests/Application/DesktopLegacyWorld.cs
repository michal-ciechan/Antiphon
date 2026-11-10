using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S4a S-4. One real repository with an <c>origin</c> remote, an isolated schema and the production
/// worktree graph (<see cref="SettledRemovalHarness"/>), plus the desktop legacy release policy on a fake clock
/// and mutable settings. <see cref="AddTaskAsync"/> adds a <c>card-task-&lt;id8&gt;</c> worktree and its task row.
/// </summary>
internal sealed class DesktopLegacyWorld : IAsyncDisposable
{
    public SettledRemovalHarness Harness { get; private set; } = null!;
    public LandingSafetyHarness Host => Harness.Host;
    public LandingGitFixture Fixture => Host.Fixture;
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public MutableMonitor<WorktreeLegacyReleaseSettings> Settings { get; } = new(new WorktreeLegacyReleaseSettings
    {
        Enabled = true, Execute = true, PauseBetweenSeconds = 0, MinTerminalHours = 48, MaxReleasesPerTick = 25,
    });
    public MutableMonitor<RunnerWorktreeRetentionSettings> Retention { get; } = new(new RunnerWorktreeRetentionSettings());
    public string Root => Path.Combine(Fixture.Root, "trees");
    public string RetainedRoot => Path.Combine(Fixture.Root, "trees", ".retained");
    private LandingGitFixture.FixtureGit _reader = null!;

    public static async Task<DesktopLegacyWorld> CreateAsync(Action<WorktreeLegacyReleaseSettings>? tweak = null,
        Action<IServiceCollection>? configure = null)
    {
        var world = new DesktopLegacyWorld();
        tweak?.Invoke(world.Settings.Value);
        world.Harness = await SettledRemovalHarness.CreateAsync(namedLeaf: false, services =>
        {
            services.AddSingleton<TimeProvider>(world.Clock);
            services.AddSingleton<IOptionsMonitor<WorktreeLegacyReleaseSettings>>(world.Settings);
            services.AddSingleton<IOptionsMonitor<RunnerWorktreeRetentionSettings>>(world.Retention);
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IWorktreeEvidenceRetention, FileSystemEvidenceRetention>();
            services.AddScoped<DesktopLegacyReleasePolicyService>();
            configure?.Invoke(services);
        });
        world._reader = new LandingGitFixture.FixtureGit(Path.Combine(world.Fixture.Root, "home"), world.Fixture.TaskId);
        // The harness's own fixture task and its seeded release are not part of this world.
        await using (var db = world.Host.CreateContext())
        {
            await db.TaskWorktreeRetirements.Where(r => r.TaskId == world.Fixture.TaskId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => t.Id == world.Fixture.TaskId).ExecuteDeleteAsync();
        }
        return world;
    }

    public sealed record Handle(Guid Id, string Id8, string Path, string Branch)
    {
        public string FullRef => "refs/heads/" + Branch;
    }

    /// <summary>
    /// Shapes: <c>pushed</c> (one commit, pushed to its own origin branch), <c>base-only</c> (no commit, nothing
    /// pushed), <c>unpushed</c> (a commit, nothing pushed), <c>pushed-then-commit</c> (pushed, then one more local
    /// commit), <c>none</c> (no directory at all). An eligible task: Succeeded 49 h ago, Result "done", Role Plan.
    /// </summary>
    public async Task<Handle> AddTaskAsync(Action<AgentTask>? seed = null, string shape = "pushed", double ageHours = 49,
        Guid? id = null, string? path = null, string? branch = null)
    {
        var taskId = id ?? Guid.NewGuid();
        var id8 = taskId.ToString("N")[..8];
        var handle = new Handle(taskId, id8, path ?? System.IO.Path.Combine(Root, "card-task-" + id8), branch ?? "feat/card-task-" + id8);
        if (shape != "none")
            await CreateTreeAsync(handle, shape);

        await using var db = Host.CreateContext();
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C0824 S4a " + id8, Goal = "fixture",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Plan, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Fixture.Repository, RepoPath = Fixture.Repository, WorktreePath = handle.Path,
            WorktreeBranch = handle.Branch, MergeTargetRef = "master", Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = Now.AddHours(-ageHours - 1), CompletedAt = Now.AddHours(-ageHours),
            Result = "done",
        };
        seed?.Invoke(task);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return handle;
    }

    /// <summary>The worktree of an <see cref="AddTaskAsync"/> shape other than <c>none</c>; also used to make a tree appear later.</summary>
    public async Task CreateTreeAsync(Handle handle, string shape)
    {
        await Fixture.RequiredAsync(Fixture.Repository, "worktree", "add", "-b", handle.Branch, handle.Path, "master");
        if (shape is "pushed" or "unpushed" or "pushed-then-commit")
            await CommitAsync(handle, handle.Id8 + ".txt");
        if (shape is "pushed" or "pushed-then-commit")
            Push(handle.FullRef);
        if (shape == "pushed-then-commit")
            await CommitAsync(handle, handle.Id8 + "-local.txt");
    }

    // Setup commits and pushes run in process (LibGit2Sharp) with the fixture git's identity: the same commit and
    // the same remote branch as "git add/commit" and "git push origin <ref>", without three git processes (a local
    // push alone was ~0.4 s). The worktree itself is still made by "git worktree add", so its metadata is git's.
    private static readonly LibGit2Sharp.Identity FixtureIdentity = new("C448 Fixture", "fixture@example.invalid");

    public async Task CommitAsync(Handle handle, string file, string? content = null)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(handle.Path, file), content ?? file + "\n");
        using var repo = new LibGit2Sharp.Repository(handle.Path);
        LibGit2Sharp.Commands.Stage(repo, file);
        var signature = new LibGit2Sharp.Signature(FixtureIdentity, DateTimeOffset.Now);
        repo.Commit("c0824 " + file, signature, signature, new LibGit2Sharp.CommitOptions());
    }

    private void Push(string fullRef)
    {
        using var repo = new LibGit2Sharp.Repository(Fixture.Repository);
        repo.Network.Push(repo.Network.Remotes["origin"], fullRef + ":" + fullRef);
    }

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public async Task<DesktopLegacyReleasePolicyService.TickResult> TickAsync(bool preview = false,
        Action<Guid>? onRow = null)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var policy = scope.ServiceProvider.GetRequiredService<DesktopLegacyReleasePolicyService>();
        policy.OnRow = onRow;
        return await policy.RunTickAsync(CancellationToken.None, preview);
    }

    public AppDbContext Db() => Host.CreateContext();

    public async Task<List<WorktreeResidueRunCandidate>> RowsAsync(Guid? runId)
    {
        runId.ShouldNotBeNull("the tick wrote a run");
        await using var db = Db();
        return await db.WorktreeResidueRunCandidates.AsNoTracking().Where(c => c.RunId == runId).ToListAsync();
    }

    public async Task<WorktreeResidueRunCandidate> RowAsync(Guid? runId, Handle handle) =>
        (await RowsAsync(runId)).Single(r => r.TaskId == handle.Id);

    public async Task<List<TaskWorktreeRetirement>> ReleasesAsync(Handle handle)
    {
        await using var db = Db();
        return await db.TaskWorktreeRetirements.AsNoTracking().Where(r => r.TaskId == handle.Id).ToListAsync();
    }

    public async Task<List<TaskWorktreeRetirementAttempt>> AttemptsAsync(Handle handle)
    {
        await using var db = Db();
        var ids = await db.TaskWorktreeRetirements.Where(r => r.TaskId == handle.Id).Select(r => r.Id).ToListAsync();
        return await db.TaskWorktreeRetirementAttempts.AsNoTracking().Where(a => ids.Contains(a.RetirementId))
            .OrderBy(a => a.AttemptNumber).ToListAsync();
    }

    public async Task<WorktreeResidueCandidateCursor?> RetryCursorAsync(Handle handle)
    {
        await using var db = Db();
        var key = DesktopLegacyReleasePolicyService.RetryCursorKey(handle.Id);
        return await db.WorktreeResidueCandidateCursors.AsNoTracking().SingleOrDefaultAsync(c => c.CandidateKey == key);
    }

    /// <summary>The D-2 pre-check cursor (<c>check|kind|repeats|fingerprint|reason</c>).</summary>
    public async Task<WorktreeResidueCandidateCursor?> CheckCursorAsync(Handle handle)
    {
        await using var db = Db();
        var key = DesktopLegacyReleasePolicyService.CheckCursorKey(handle.Id);
        return await db.WorktreeResidueCandidateCursors.AsNoTracking().SingleOrDefaultAsync(c => c.CandidateKey == key);
    }

    /// <summary>Whether this trace's commands include the policy's pre-check <c>status</c> in the task's tree.</summary>
    public bool Prechecked(Handle handle) => Fixture.Git.Commands.Any(c => Subcommand(c.Arguments) == "status"
        && LandingGit.PathsEqual(c.Directory, handle.Path));

    /// <summary>The git subcommand after leading global options (the pre-check runs <c>-c core.longpaths=true status</c>).</summary>
    public static string? Subcommand(IReadOnlyList<string> arguments)
    {
        var index = LandingGitProfile.SubcommandIndex(arguments);
        return index < arguments.Count ? arguments[index] : null;
    }

    public async Task<bool> RegisteredAsync(string path)
    {
        var listed = await _reader.RunAsync(Fixture.Repository, ["worktree", "list", "--porcelain", "-z"], CancellationToken.None);
        listed.Succeeded.ShouldBeTrue(listed.Diagnostic);
        return LandingGit.ParseRegistrations(listed.Output).Any(r => LandingGit.PathsEqual(r.Path, path));
    }

    public async Task<bool> BranchExistsAsync(Handle handle)
    {
        var exists = await _reader.RunAsync(Fixture.Repository, ["show-ref", "--exists", handle.FullRef], CancellationToken.None);
        return exists.ExitCode != 2;
    }

    public async Task<string> OriginRefsAsync()
    {
        var refs = await _reader.RunAsync(Fixture.Remote, ["show-ref"], CancellationToken.None);
        return refs.Output;
    }

    public async Task<string> ReadAsync(string directory, params string[] arguments) =>
        (await _reader.RunAsync(directory, arguments, CancellationToken.None)).Output;

    /// <summary>The "intact" evidence: HEAD, status and a SHA-256 over every file of the tree.</summary>
    public async Task<string> SnapshotAsync(string path)
    {
        if (!Directory.Exists(path)) return "absent";
        var files = Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Order(StringComparer.Ordinal)
            .Select(f => System.IO.Path.GetRelativePath(path, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        var head = await ReadAsync(path, "rev-parse", "HEAD");
        var status = await ReadAsync(path, "status", "--porcelain=v1", "--untracked-files=all");
        return head + "\n" + status + "\n" + string.Join("\n", files);
    }

    public async Task AssertRetiredAsync(Handle handle, string originBefore)
    {
        Directory.Exists(handle.Path).ShouldBeFalse("the tree is gone");
        (await RegisteredAsync(handle.Path)).ShouldBeFalse("the registration is gone");
        (await BranchExistsAsync(handle)).ShouldBeFalse("the local branch is gone");
        (await OriginRefsAsync()).ShouldBe(originBefore, "origin is never changed");
        var releases = await ReleasesAsync(handle);
        releases.Count.ShouldBe(1);
        releases[0].State.ShouldBe(WorktreeRetirementState.Complete);
    }

    public async Task AssertIntactAsync(Handle handle, string snapshot)
    {
        (await SnapshotAsync(handle.Path)).ShouldBe(snapshot, "the tree is byte-intact");
        if (Directory.Exists(handle.Path))
            (await RegisteredAsync(handle.Path)).ShouldBeTrue("the registration is intact");
        (await ReleasesAsync(handle)).ShouldBeEmpty("no release was filed");
    }

    /// <summary>"No git": no command ran in, or named, the task's path or branch.</summary>
    public void AssertNoGit(Handle handle)
    {
        Fixture.Git.Commands.ShouldNotContain(c => LandingGit.PathsEqual(c.Directory, handle.Path),
            "no git command ran in the refused tree");
        Fixture.Git.Trace.ShouldNotContain(a => a.Any(x => x.Contains(handle.Path, StringComparison.Ordinal)
            || x.Contains(handle.Branch, StringComparison.Ordinal)), "no git command named the refused tree");
    }

    public void ClearTrace()
    {
        Fixture.Git.Trace.Clear();
        Fixture.Git.Commands.Clear();
    }

    public ValueTask DisposeAsync() => Harness.DisposeAsync();
}

/// <summary>The C1677 Monitor pattern: a settable CurrentValue that can also throw a validation failure.</summary>
internal sealed class MutableMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T Value { get; set; } = value;
    public OptionsValidationException? Fault { get; set; }
    public T CurrentValue => Fault is not null ? throw Fault : Value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
