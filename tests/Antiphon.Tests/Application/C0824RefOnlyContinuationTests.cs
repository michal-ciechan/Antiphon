using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(120_000)] // CARD-0824: provisional real park/queue/schema multi-variant budget.
public sealed class C0824RefOnlyContinuationTests
{
    [Test]
    public async Task Pending_park_debt_keeps_exact_publication_and_mode()
    {
        await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true);
        await using (var db = w.Fixture.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == w.Fixture.TaskId);
            await RecordAsync(db, task, w.Git, w.Root);
            // The original Requested park has not crossed publication. Rebind its identity
            // to the recorded representation before exercising the producer.
            var park = await db.AgentTaskParks.SingleAsync(p => p.Id == w.ParkId);
            park.WorktreePath = task.WorktreePath;
            await db.SaveChangesAsync();
        }
        await w.GitTextAsync(w.Repository, "worktree", "remove", w.LocalPath);
        var source = await w.CommitAsync("park-source.txt");
        var result = await w.PrepareAsync();
        result.Evidence.ShouldNotBeNull("reached:C2:RefOnly:publication").SourceSha.ShouldBe(source);
        (await w.RemoteShaAsync()).ShouldBe(source, "park-origin-exact-S");
        var row = await w.RowAsync();
        row.SyncState.ShouldBe(AgentTaskParkSyncState.Pending, "park-debt-pending");
        row.SyncSourceSha.ShouldBe(source);
        await w.RestartAsync();
        w.Fixture.Options.Enabled = false;
        var verified = await w.VerifyAsync();
        verified.Evidence.ShouldNotBeNull("accepted-park-survives-gate-off").SourceSha.ShouldBe(source);
    }

    [Test]
    public async Task Park_resume_never_adds_a_desktop_worktree()
    {
        foreach (var decoy in new[] { false, true })
        {
            await using var f = await RefOnlySyncFixture.CreateAsync();
            if (decoy) { Directory.CreateDirectory(f.Identity); await File.WriteAllTextAsync(Path.Combine(f.Identity, "foreign"), "retain"); }
            var source = await f.Git.RunnerPushAsync("park.txt", "parked source");
            (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue();
            var park = new AgentTaskPark { TaskId = f.Task.Id, Attempt = f.Task.Attempt,
                WorktreePath = f.Identity, RemoteWorktreePath = f.Task.RemoteWorktreePath,
                RunnerId = f.Task.RunnerId, FullRef = f.Git.FullRef, SourceSha = source,
                PublicationReceiptId = Guid.NewGuid(), EndpointFingerprint = BlockedTaskParkingService.Digest(f.Git.Origin) };
            var registry = await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain");
            f.Git.Git.Clear();
            (await f.Service().RefuseParkResumeAsync(f.Task, park, default)).ShouldBeNull("ref-only-resume-at-S");
            f.AssertNoDesktopCommands();
            (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registry, "no-desktop-worktree");
            if (decoy) (await File.ReadAllTextAsync(Path.Combine(f.Identity, "foreign"))).ShouldBe("retain");
            else Directory.Exists(f.Identity).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Runner_redirect_requires_old_custody_and_rejects_desktop()
    {
        foreach (var runner in new string?[] { null, "desktop", "local" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Queued);
            await f.CreateSourceAsync(remote: true);
            await using (var db = f.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                await RecordAsync(db, task, f.SourceGit, f.Harness.TempRoot);
                task.AgentSessionId = null; task.RunnerId = runner;
                await db.SaveChangesAsync();
            }
            await f.DispatchAsync();
            f.Launches.Calls.ShouldBeEmpty("redirect-refused:desktop");
            await using var verify = f.Db();
            (await verify.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId).Select(e => e.Detail).ToListAsync())
                .ShouldContain(d => d.Contains("ref_only_requires_remote_runner", StringComparison.Ordinal));
        }
    }

    internal static async Task RecordAsync(Antiphon.Server.Infrastructure.Data.AppDbContext db, AgentTask task,
        Antiphon.Server.Application.Interfaces.ITaskProgressGit git, string root)
    {
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!;
        var binding = new TaskRefWorkspaceBinding { Id = Guid.NewGuid(), TaskId = task.Id,
            RepositoryPath = task.RepoPath!, CommonDirectory = await git.CommonDirectoryAsync(task.RepoPath!, default),
            IdentityPath = Path.Combine(root, ".ref-only", task.Id.ToString("N")), Branch = task.WorktreeBranch!,
            FullRef = baseline.Primary.FullRef, SelectedBaseSha = task.WorktreeBaseSha!,
            SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
            PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 1 };
        db.TaskRefWorkspaceBindings.Add(binding);
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly; task.TaskRefWorkspaceBindingId = binding.Id;
        task.WorktreePath = binding.IdentityPath; task.WorktreeBaseRef = binding.SelectedBaseRef;
        task.WorktreeBaseSource = binding.SelectedBaseSource;
        task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { SchemaVersion = 2,
            Primary = baseline.Primary with { RegisteredCheckout = null, DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly,
                TaskRefWorkspaceBindingId = binding.Id } });
        await db.SaveChangesAsync();
    }
}
