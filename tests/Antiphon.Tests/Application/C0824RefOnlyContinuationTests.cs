using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true, refOnly: true);
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
        await using (var db = w.Fixture.Db())
        {
            var workspace = new RemoteWorkspaceService(w.Directory, w.Git,
                NullLogger<RemoteWorkspaceService>.Instance, w.Git, w.Leases, w.Reservations,
                workspaceContexts: new TaskWorkspaceContextResolver(db));
            var recovery = new BlockedTaskSyncRecoveryService(db, workspace, w.Service(db), new MockEventBus(),
                TimeProvider.System, NullLogger<BlockedTaskSyncRecoveryService>.Instance);
            (await recovery.SweepAsync(default)).ShouldBe(1, "reached:C2:accepted-debt:claim");
        }
        (await w.RowAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready, "park-ready=S:gate-off");
        (await w.GitTextAsync(w.Repository, "rev-parse", w.FullRef)).ShouldBe(source, "park-ready-exact-ref");
        (await w.GitTextAsync(w.Repository, "show", source + ":park-source.txt")).ShouldBe("park-source.txt", "park-ready-exact-bytes");
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
            park.BaselineSha = f.Git.Baseline; park.TaskConcurrencyToken = f.Task.ConcurrencyToken;
            park.WorkspaceSnapshotJson = await SettlementSyncWorkspaceSnapshot.CaptureAsync(f.Db, f.Task, default);
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
    public async Task Blocked_reply_preserves_runner_and_binding_when_gate_closes()
    {
        foreach (var busy in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await f.CreateSourceAsync(remote: true);
            await using (var db = f.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                await RefOnlyWorkspaceTestSetup.RecordAsync(db, task, f.SourceGit, f.Harness.TempRoot);
            }
            var before = await f.TaskAsync();
            await f.AttachRecipientAsync(f.SessionId, busy);
            const string answer = "Keep the bound runner and the immutable source.";
            await f.AnswerAsync(answer);
            var queued = (await f.AnswerQueueAsync()).ShouldNotBeNull("reached:C3:live:queue");
            queued.Body.ShouldContain(answer, Case.Sensitive, "answer-independent-content");
            if (busy) f.Submitted.ShouldBeEmpty("busy-answer-defers");
            await f.RestartAsync();
            await f.AttachRecipientAsync(f.SessionId);
            await f.EndTurnAsync(f.SessionId);
            await f.FlushAsync(f.SessionId);
            var after = await f.TaskAsync();
            after.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            after.TaskRefWorkspaceBindingId.ShouldBe(before.TaskRefWorkspaceBindingId);
            after.RunnerId.ShouldBe(before.RunnerId);
            await using var verify = f.Db();
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId
                && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt && t.Text == queued.Body))
                .ShouldBe(1, "answer-received-once");
            await f.FlushAsync(f.SessionId);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId
                && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt && t.Text == queued.Body))
                .ShouldBe(1, "answer-received-once:replay");
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
                await RefOnlyWorkspaceTestSetup.RecordAsync(db, task, f.SourceGit, f.Harness.TempRoot);
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

}
