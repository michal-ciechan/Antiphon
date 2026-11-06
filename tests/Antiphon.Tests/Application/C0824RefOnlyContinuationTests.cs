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
        result.Evidence.ShouldNotBeNull("reached:C2:RefOnly:publication:" + result.Reason).SourceSha.ShouldBe(source);
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
                NullLogger<RemoteWorkspaceService>.Instance, new TaskParkPublicationTests.ParkGit(w.Leases), w.Leases, w.Reservations,
                workspaceContexts: new TaskWorkspaceContextResolver(db));
            var recovery = new BlockedTaskSyncRecoveryService(db, workspace, w.Service(db), new MockEventBus(),
                TimeProvider.System, NullLogger<BlockedTaskSyncRecoveryService>.Instance);
            (await recovery.SweepAsync(default)).ShouldBe(1, "reached:C2:accepted-debt:claim");
        }
        var recovered = await w.RowAsync();
        recovered.SyncState.ShouldBe(AgentTaskParkSyncState.Ready, "park-ready=S:gate-off:" + recovered.SyncReasonCode);
        (await w.GitTextAsync(w.Repository, "rev-parse", w.FullRef)).ShouldBe(source, "park-ready-exact-ref");
        (await w.GitTextAsync(w.Repository, "show", source + ":park-source.txt")).ShouldBe("park-source.txt", "park-ready-exact-bytes");
        var accepted = await w.RowAsync();
        foreach (var dimension in new[] { "mode", "binding", "binding-revision", "digest", "store", "generation" })
        {
            Guid? originalBinding;
            Guid? originalStore;
            DateTime originalGeneration;
            long originalRevision;
            await using (var db = w.Fixture.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == w.Fixture.TaskId);
                var session = await db.AgentSessions.SingleAsync(s => s.Id == w.Fixture.SessionId);
                var park = await db.AgentTaskParks.SingleAsync(p => p.Id == w.ParkId);
                var binding = await db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == task.TaskRefWorkspaceBindingId);
                originalBinding = task.TaskRefWorkspaceBindingId; originalStore = session.RunnerStoreId;
                originalGeneration = session.StartedAt; originalRevision = binding.Revision;
                park.SyncState = AgentTaskParkSyncState.Pending; park.SourceReadyAt = null;
                park.SyncNextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
                if (dimension == "mode") task.DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout;
                if (dimension == "binding") task.TaskRefWorkspaceBindingId = Guid.NewGuid();
                if (dimension == "binding-revision") binding.Revision++;
                if (dimension == "digest") park.PublicationReceiptDigest = new string('0', 64);
                if (dimension == "store") session.RunnerStoreId = Guid.NewGuid();
                if (dimension == "generation") session.StartedAt = session.StartedAt.AddSeconds(1);
                await db.SaveChangesAsync();
            }
            await RecoverAsync(w);
            (await w.RowAsync()).SyncState.ShouldNotBe(AgentTaskParkSyncState.Ready, "park-not-ready:" + dimension);
            (await w.GitTextAsync(w.Repository, "rev-parse", w.FullRef)).ShouldBe(source, "source-retained:" + dimension);
            await using (var db = w.Fixture.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == w.Fixture.TaskId);
                var session = await db.AgentSessions.SingleAsync(s => s.Id == w.Fixture.SessionId);
                var park = await db.AgentTaskParks.SingleAsync(p => p.Id == w.ParkId);
                var binding = await db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == originalBinding);
                task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly; task.TaskRefWorkspaceBindingId = originalBinding;
                session.RunnerStoreId = originalStore; session.StartedAt = originalGeneration;
                binding.Revision = originalRevision; park.PublicationReceiptDigest = accepted.PublicationReceiptDigest;
                park.SyncState = AgentTaskParkSyncState.Pending; park.SyncNextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            await RecoverAsync(w);
            (await w.RowAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready, "park-ready=S:restored:" + dimension);
        }
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
            var pushed = await f.Service().PushBranchAsync(f.Task, default);
            pushed.Pushed.ShouldBeTrue("ref-only-preparation-push");
            pushed.Sha.ShouldBe(source, "ref-only-push-owned-S");
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
        // Continue through the production answer/dispatcher/queue callers. The modeled
        // recipient writes a UserPrompt only when the real queue actually submits input.
        await using var resumed = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
            syncRecovery: true, projectLaunch: true);
        await resumed.CreateSourceAsync(remote: true);
        await using (var db = resumed.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == resumed.TaskId);
            await RefOnlyWorkspaceTestSetup.RecordAsync(db, task, resumed.SourceGit, resumed.Harness.TempRoot);
            var agent = await db.Agents.SingleAsync(a => a.Id == task.AgentId);
            agent.RunnerId = task.RunnerId;
            await db.SaveChangesAsync();
            resumed.Harness.Provider.GetRequiredService<PhoneHomeLaunchPolicy>().IsRunnerBound(agent)
                .ShouldBeTrue("reached:C1:recorded-remote-pool-agent");
        }
        var before = await resumed.TaskAsync();
        await resumed.GitAsync(before.RepoPath!, "worktree", "remove", resumed.DesktopSourcePath);
        await File.WriteAllTextAsync(Path.Combine(resumed.SourcePath, "resumed.txt"), "independent parked bytes");
        await resumed.GitAsync(resumed.SourcePath, "add", "resumed.txt");
        await resumed.GitAsync(resumed.SourcePath, "commit", "-m", "parked remote source");
        var parkedSha = await resumed.GitAsync(resumed.SourcePath, "rev-parse", "HEAD");
        using (var scope = resumed.Harness.Provider.CreateScope())
        {
            await using var db = resumed.Db();
            var block = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == resumed.TaskId && e.Type == AgentTaskEventType.Blocked);
            var parkId = (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
                .RegisterAsync(before.Id, before.Attempt, block.Id, before.ConcurrencyToken, default)).ShouldNotBeNull();
            (await scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>().PrepareAsync(parkId, default))
                .Evidence.ShouldNotBeNull("reached:C1:park:published").SourceSha.ShouldBe(parkedSha);
            (await scope.ServiceProvider.GetRequiredService<BlockedTaskSyncRecoveryService>().SweepAsync(default)).ShouldBe(1);
        }
        (await resumed.ParkAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready, await resumed.ParkDiagnosticAsync());
        await resumed.HandleParkAsync();
        (await resumed.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked, await resumed.ParkDiagnosticAsync());
        const string answer = "Resume exactly the published source and retain its binding.";
        await resumed.AnswerAsync(answer);
        resumed.Launches.OnLaunch = id => resumed.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
        await resumed.DispatchAsync();
        resumed.Launches.Calls.ShouldHaveSingleItem().Cwd.ShouldBe(resumed.SourcePath, "runner-cwd=bound");
        var queued = (await resumed.AnswerQueueAsync()).ShouldNotBeNull("reached:C1:resume:queued");
        var logical = queued.RemoteSpillBody ?? queued.Body;
        logical.ShouldContain(answer, Case.Sensitive, "remote-prompt-answer");
        logical.ShouldContain(parkedSha, Case.Sensitive, "remote-prompt-source-S");
        logical.ShouldContain("refs/heads/" + before.WorktreeBranch, Case.Sensitive, "remote-prompt-owned-ref");
        resumed.Submitted.ShouldBeEmpty("busy-recipient-is-not-delivery");
        await resumed.EndTurnAsync(queued.AgentSessionId);
        await resumed.FlushAsync(queued.AgentSessionId);
        await resumed.DispatchAsync();
        await using var receiptDb = resumed.Db();
        (await receiptDb.TranscriptEntries.CountAsync(t => t.AgentSessionId == queued.AgentSessionId
            && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt && t.Text == queued.Body))
            .ShouldBe(1, "remote-prompt-received");
        var after = await resumed.TaskAsync();
        after.TaskRefWorkspaceBindingId.ShouldBe(before.TaskRefWorkspaceBindingId);
        after.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        Directory.Exists(after.WorktreePath).ShouldBeFalse("no-desktop-worktree");
    }

    [Test]
    public async Task Blocked_reply_preserves_runner_and_binding_when_gate_closes(CancellationToken ct)
    {
        foreach (var busy in new[] { false, true })
        {
            var admission = new RefOnlyDesktopSettings { Enabled = true, RunnerIds = ["fixture"], Roles = "Custom" };
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
                refOnlySettings: admission);
            await f.CreateSourceAsync(remote: true);
            await using (var db = f.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                await RefOnlyWorkspaceTestSetup.RecordAsync(db, task, f.SourceGit, f.Harness.TempRoot);
            }
            var before = await f.TaskAsync();
            f.Harness.Provider.GetRequiredService<RefOnlyDesktopGate>().Admits(before).ShouldBeTrue("reached:C3:gate:open");
            admission.Enabled = false;
            f.Harness.Provider.GetRequiredService<RefOnlyDesktopGate>().Admits(before).ShouldBeFalse("reached:C3:gate:closed");
            await f.AttachRecipientAsync(f.SessionId, busy);
            const string answer = "Keep the bound runner and the immutable source.";
            await f.DriveAnswerAsync(answer, ct);
            await using var lookup = f.Db();
            var replied = await lookup.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Replied, ct);
            var key = AgentTaskInputService.ConversationKey(f.TaskId, replied.Id);
            var queued = (await lookup.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m =>
                m.AgentSessionId == f.SessionId && m.ConversationKey == key, ct)).ShouldNotBeNull("reached:C3:live:queue");
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

    private static async Task<int> RecoverAsync(TaskParkPublicationTests.PublicationWorld w)
    {
        await using var db = w.Fixture.Db();
        var workspace = new RemoteWorkspaceService(w.Directory, w.Git,
            NullLogger<RemoteWorkspaceService>.Instance, new TaskParkPublicationTests.ParkGit(w.Leases), w.Leases, w.Reservations,
            workspaceContexts: new TaskWorkspaceContextResolver(db));
        return await new BlockedTaskSyncRecoveryService(db, workspace, w.Service(db), new MockEventBus(),
            TimeProvider.System, NullLogger<BlockedTaskSyncRecoveryService>.Instance).SweepAsync(default);
    }

}
