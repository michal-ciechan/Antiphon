using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Application;

internal sealed partial class RunnerSeatReleaseFixture
{
    // CARD-1065 replaces the legacy publication-free Blocked release fixture.
    // Seed real clean/pushed source and prepare its receipt, leaving reservation and
    // the conditional command to the calling test (including its crash/race hooks).
    public async Task PreparePublishedBlockedSourceAsync()
    {
        await CreateSourceAsync();
        using var scope = Harness.Provider.CreateScope();
        var task = await TaskAsync();
        await using var db = Db();
        var block = await db.AgentTaskEvents.Where(e => e.AgentTaskId == TaskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).Select(e => e.Id).FirstAsync();
        var id = (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
            .RegisterAsync(TaskId, task.Attempt, block, task.ConcurrencyToken, default)).ShouldNotBeNull();
        (await scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
            .PrepareAsync(id, default)).Evidence.ShouldNotBeNull("legacy Blocked release requires real publication");
        Wire.ConditionalCommands.ShouldBe(0, "publication setup cannot release the seat");
    }

    public string SourcePath { get; private set; } = "";
    public string SourceOrigin { get; private set; } = "";
    public string DesktopSourcePath { get; private set; } = "";
    public TaskParkPublicationTests.ParkGit SourceGit =>
        (TaskParkPublicationTests.ParkGit)Harness.Provider.GetRequiredService<ITaskProgressGit>();

    public async Task CreateSourceAsync(WorkspaceMode mode = WorkspaceMode.Worktree, bool remote = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1065-release-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        System.IO.Directory.CreateDirectory(root);
        var repository = Path.Combine(root, "repo");
        SourceOrigin = Path.Combine(root, "origin.git");
        SourcePath = Path.Combine(root, "task");
        var branch = RemoteWorkspaceService.OwnedBranch(TaskId);
        var sha = await SourceTreeAsync(root, TaskId, remote);
        DesktopSourcePath = SourcePath;
        if (remote)
        {
            var runnerRepo = Path.Combine(root, "runner-repo");
            var allowed = Path.Combine(root, "runner");
            SourcePath = Path.Combine(allowed, "worktrees", RemoteWorkspaceService.MirrorName(TaskId));
            Wire.ParkRuntime = new(new RunnerWorkspaceService(runnerRepo, allowed, SourceOrigin, start =>
            {
                TaskParkPublicationTests.ParkGit.Isolate(start);
                return Process.Start(start);
            }));
            Directory.RemotePath = SourcePath;
            Directory.FeaturesOverride = [RunnerCapabilityFeatures.TerminalSeatReleaseV1,
                RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1, RunnerCapabilityFeatures.WorkspaceParkV1,
                RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1, RunnerCapabilityFeatures.WorkspaceParkSourceModesV1];
        }
        if (mode == WorkspaceMode.Shared) await GitAsync(SourcePath, "push", "origin", branch);
        var source = new ProgressSourceBaseline(repository, await SourceGit.CommonDirectoryAsync(repository, default),
            TaskId, DesktopSourcePath, "refs/heads/" + branch, sha,
            new(ProgressRemoteState.Missing, EndpointFingerprint: BlockedTaskParkingService.Digest(SourceOrigin)));
        await EditAsync((task, _) =>
        {
            task.Workspace = mode; task.RepoPath = repository; task.WorktreePath = DesktopSourcePath;
            task.WorktreeBranch = branch; task.WorktreeBaseSha = sha; task.RemoteWorktreePath = remote ? SourcePath : null;
            task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, Now, Now, source, null));
        });
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Cwd, SourcePath)
            .SetProperty(s => s.RunnerCwd, SourcePath));
        Directory.LocalBinding = !remote;
        if (Live is not null)
        {
            Live.Checkout = SourcePath;
            Live.Session.RetainCheckout(SourcePath);
        }
        else
        {
            var verifier = new RunnerWorkspaceParkService();
            Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: not null }
                && (remote ? await Wire.ParkRuntime!.VerifyAsync(command.Publication, default)
                    : await verifier.VerifySessionCheckoutAsync(command.Publication, SourcePath, default)).Receipt is not null;
        }
    }

    /// <summary>
    /// A second owned checkout for one legacy row. Does not retarget the fixture task,
    /// its session cwd, or the single-path release verifier.
    /// </summary>
    public async Task<string> CreateReclaimSourceAsync(Guid taskId, Guid sessionId, bool dirty = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1065-reclaim-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        System.IO.Directory.CreateDirectory(root);
        var origin = Path.Combine(root, "origin.git");
        var repository = Path.Combine(root, "repo");
        var checkout = Path.Combine(root, "task");
        var branch = RemoteWorkspaceService.OwnedBranch(taskId);
        var sha = await SourceTreeAsync(root, taskId, remote: false);
        if (dirty)
            await File.WriteAllTextAsync(Path.Combine(checkout, "dirty.txt"), "uncommitted");
        var source = new ProgressSourceBaseline(repository, await SourceGit.CommonDirectoryAsync(repository, default),
            taskId, checkout, "refs/heads/" + branch, sha,
            new(ProgressRemoteState.Missing, EndpointFingerprint: BlockedTaskParkingService.Digest(origin)));
        await using var db = Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Workspace = WorkspaceMode.Worktree;
        task.RepoPath = repository;
        task.WorktreePath = checkout;
        task.WorktreeBranch = branch;
        task.WorktreeBaseSha = sha;
        task.RemoteWorktreePath = null;
        task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, Now, Now, source, null));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Cwd = checkout;
        session.RunnerCwd = checkout;
        await db.SaveChangesAsync();
        Directory.LocalBinding = true;
        return checkout;
    }

    /// <summary>
    /// The owned source tree under <paramref name="root"/>: origin.git (bare), repo (one baseline commit, origin,
    /// pushed master), task (the owned branch's worktree) and, for <paramref name="remote"/>, runner-repo (a clone)
    /// with the runner mirror worktree. Cloned from a per-process template (GitTreeTemplate: the branch and the mirror
    /// name carry the task id's first 8 hex digits, the template's token) instead of 10-14 git processes; built with
    /// git when templates are off or the template is refused. Returns the baseline commit.
    /// </summary>
    private async Task<string> SourceTreeAsync(string root, Guid taskId, bool remote)
    {
        var token = taskId.ToString("N")[..8];
        if (GitTreeTemplate.Enabled)
        {
            try
            {
                var template = await GitTreeTemplate.BuildOnceAsync("c1065-park-source" + (remote ? "-remote" : ""), ["task"],
                    async (templateRoot, tokens) => new()
                    {
                        ["sha"] = await BuildSourceTreeAsync(templateRoot, "feat/card-task-" + tokens["task"],
                            "task-" + tokens["task"], remote),
                    }, tokenLength: 8);
                var clone = template.CloneTo(root, new Dictionary<string, string> { ["task"] = token });
                return clone.Map(template.Values["sha"]);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException)
            {
                Console.Error.WriteLine($"park source template not used: {ex.GetType().Name}: {ex.Message}");
                foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(root))
                {
                    if (System.IO.Directory.Exists(entry)) GitTreeTemplate.DeleteTree(entry);
                    else File.Delete(entry);
                }
            }
        }
        return await BuildSourceTreeAsync(root, RemoteWorkspaceService.OwnedBranch(taskId),
            RemoteWorkspaceService.MirrorName(taskId), remote);
    }

    private async Task<string> BuildSourceTreeAsync(string root, string branch, string mirror, bool remote)
    {
        var origin = Path.Combine(root, "origin.git");
        var repository = Path.Combine(root, "repo");
        await GitAsync(root, "init", "--bare", "-b", "master", origin);
        await GitAsync(root, "init", "-b", "master", repository);
        await GitAsync(repository, "config", "user.name", "park test");
        await GitAsync(repository, "config", "user.email", "park@example.invalid");
        await GitAsync(repository, "config", "commit.gpgsign", "false");
        // The runner's git children inherit this process's environment, so they read the machine's config (Git for
        // Windows' system core.autocrlf=true, the user's ~/.config/git/ignore) while libgit2 here is isolated from
        // it and this fixture's own git reads none of it. Pinned locally, every reader sees the same status config,
        // and the runner's clean-status reads are answered in process as they are in production. Worktrees share it.
        await PinStatusConfigAsync(root, repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "source.txt"), "retained source");
        await GitAsync(repository, "add", "source.txt");
        await GitAsync(repository, "commit", "-m", "retained baseline");
        await GitAsync(repository, "remote", "add", "origin", origin);
        await GitAsync(repository, "push", "origin", "master");
        var sha = await GitAsync(repository, "rev-parse", "HEAD");
        await GitAsync(repository, "worktree", "add", "-b", branch, Path.Combine(root, "task"), sha);
        if (remote)
        {
            var runnerRepo = Path.Combine(root, "runner-repo");
            await GitAsync(root, "clone", origin, runnerRepo);
            await GitAsync(runnerRepo, "config", "user.name", "park test");
            await GitAsync(runnerRepo, "config", "user.email", "park@example.invalid");
            await GitAsync(runnerRepo, "config", "commit.gpgsign", "false");
            await PinStatusConfigAsync(root, runnerRepo);
            await GitAsync(runnerRepo, "worktree", "add", "-b", branch, Path.Combine(root, "runner", "worktrees", mirror), sha);
        }
        return sha;
    }

    private async Task PinStatusConfigAsync(string root, string repository)
    {
        var empty = Path.Combine(root, "status-config-empty").Replace('\\', '/');
        if (!File.Exists(empty)) await File.WriteAllTextAsync(empty, "");
        await GitAsync(repository, "config", "core.autocrlf", "false");
        await GitAsync(repository, "config", "core.excludesFile", empty);
        await GitAsync(repository, "config", "core.attributesFile", empty);
    }

    public async Task<string> GitAsync(string path, params string[] arguments)
    {
        var result = await SourceGit.RunAsync(path, arguments, default);
        result.ExitCode.ShouldBe(0, result.Diagnostic);
        return result.Output.Trim();
    }

    public async Task HandleParkAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .TryHandleTaskAsync(TaskId, default)).ShouldBeTrue();
        Wire.CallbackFailure.ShouldBeNull();
    }

    /// <summary>
    /// The parked runner checkout is the mirror a later review repair inspects. A missing
    /// publisher leaves MirrorDirty null, and a confirmed park then cannot bind evidence.
    /// </summary>
    private sealed class SeatMirrorPublisher : IRunnerMirrorPublisher
    {
        public async Task<PhoneHomeWorkspacePublishResponse?> PublishAsync(
            AgentTask task, string baselineSha, string? remoteSha, CancellationToken ct)
        {
            var path = task.RemoteWorktreePath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                return null;
            var head = await RunAsync(path, ct, "rev-parse", "HEAD");
            var status = await RunAsync(path, ct, "status", "--porcelain");
            if (head.Code != 0 || status.Code != 0 || !GitObjectId.IsFull(head.Text))
                return new(null, "unknown", null, false, false, "inspection_unavailable");
            var dirty = status.Text.Length > 0;
            var remote = remoteSha ?? baselineSha;
            if (string.Equals(head.Text, remote, StringComparison.OrdinalIgnoreCase))
                return new(head.Text, "equal", true, dirty, false, null);
            var fromBase = await RunAsync(path, ct, "merge-base", "--is-ancestor", baselineSha, head.Text);
            if (fromBase.Code is not (0 or 1))
                return new(head.Text, "unknown", null, dirty, false, "inspection_unavailable");
            if (fromBase.Code != 0)
                return new(head.Text, "diverged", false, dirty, false, null);
            var forward = await RunAsync(path, ct, "merge-base", "--is-ancestor", remote, head.Text);
            if (forward.Code == 0)
                return new(head.Text, "descends", true, dirty, false, null);
            var reverse = await RunAsync(path, ct, "merge-base", "--is-ancestor", head.Text, remote);
            var relation = reverse.Code switch { 0 => "behind", 1 => "diverged", _ => "unknown" };
            return new(head.Text, relation, relation != "unknown", dirty, false, null);
        }

        private static async Task<(int Code, string Text)> RunAsync(
            string path, CancellationToken ct, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            TaskParkPublicationTests.ParkGit.Isolate(start);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            return (process.ExitCode, (await stdout).Trim());
        }
    }

    public async Task<AgentTaskPark> ParkAsync()
    {
        await using var db = Db();
        return await db.AgentTaskParks.AsNoTracking().Where(p => p.TaskId == TaskId)
            .OrderByDescending(p => p.CreatedAt).FirstAsync();
    }

    public async Task<string> ParkDiagnosticAsync()
    {
        await using var db = Db();
        var release = await db.RunnerSeatReleases.AsNoTracking().FirstOrDefaultAsync(r => r.TaskId == TaskId);
        return $"ledger={release?.State}/{release?.ReasonCode}/{release?.OutcomeCode}; "
            + string.Join("\n", AttentionLogs.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning)
                .Select(e => e.Message + " " + e.Exception));
    }
}
