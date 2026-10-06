using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.Application;

internal sealed partial class RunnerSeatReleaseFixture
{
    public string SourcePath { get; private set; } = "";
    public string SourceOrigin { get; private set; } = "";
    public TaskParkPublicationTests.ParkGit SourceGit =>
        (TaskParkPublicationTests.ParkGit)Harness.Provider.GetRequiredService<ITaskProgressGit>();

    public async Task CreateSourceAsync(WorkspaceMode mode = WorkspaceMode.Worktree)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1065-release-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        System.IO.Directory.CreateDirectory(root);
        var repository = Path.Combine(root, "repo");
        SourceOrigin = Path.Combine(root, "origin.git");
        SourcePath = Path.Combine(root, "task");
        await GitAsync(root, "init", "--bare", "-b", "master", SourceOrigin);
        await GitAsync(root, "init", "-b", "master", repository);
        await GitAsync(repository, "config", "user.name", "park test");
        await GitAsync(repository, "config", "user.email", "park@example.invalid");
        await GitAsync(repository, "config", "commit.gpgsign", "false");
        await File.WriteAllTextAsync(Path.Combine(repository, "source.txt"), "retained source");
        await GitAsync(repository, "add", "source.txt");
        await GitAsync(repository, "commit", "-m", "retained baseline");
        await GitAsync(repository, "remote", "add", "origin", SourceOrigin);
        await GitAsync(repository, "push", "origin", "master");
        var sha = await GitAsync(repository, "rev-parse", "HEAD");
        var branch = RemoteWorkspaceService.OwnedBranch(TaskId);
        await GitAsync(repository, "worktree", "add", "-b", branch, SourcePath, sha);
        if (mode == WorkspaceMode.Shared) await GitAsync(SourcePath, "push", "origin", branch);
        var source = new ProgressSourceBaseline(repository, await SourceGit.CommonDirectoryAsync(repository, default),
            TaskId, SourcePath, "refs/heads/" + branch, sha,
            new(ProgressRemoteState.Missing, EndpointFingerprint: BlockedTaskParkingService.Digest(SourceOrigin)));
        await EditAsync((task, _) =>
        {
            task.Workspace = mode; task.RepoPath = repository; task.WorktreePath = SourcePath;
            task.WorktreeBranch = branch; task.WorktreeBaseSha = sha; task.RemoteWorktreePath = null;
            task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, Now, Now, source, null));
        });
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Cwd, SourcePath));
        Directory.LocalBinding = true;
        if (Live is not null)
        {
            Live.Checkout = SourcePath;
            Live.Session.RetainCheckout(SourcePath);
        }
        else
        {
            var verifier = new RunnerWorkspaceParkService();
            Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: not null }
                && (await verifier.VerifySessionCheckoutAsync(command.Publication, SourcePath, default)).Receipt is not null;
        }
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
