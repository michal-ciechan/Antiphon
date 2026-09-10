using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandHoldVisibilityTests
{
    [Test]
    [Arguments("shared", AgentTaskStatus.Dispatched)]
    [Arguments("shared", AgentTaskStatus.Working)]
    [Arguments("shared", AgentTaskStatus.Blocked)]
    [Arguments("source", AgentTaskStatus.Blocked)]
    [Arguments("lease", AgentTaskStatus.Blocked)]
    [Arguments("unrelated", AgentTaskStatus.Blocked)]
    [Arguments("shared-alias", AgentTaskStatus.Blocked)]
    [Arguments("source-alias", AgentTaskStatus.Blocked)]
    [Arguments("inaccessible", AgentTaskStatus.Blocked)]
    public async Task C467_V03_RealWriterLeaseAndEpisodeMatrix(string writer, AgentTaskStatus status)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        using var alias = writer.EndsWith("-alias", StringComparison.Ordinal)
            ? new OwnedJunction(Path.Combine(h.Fixture.Root, "writer-alias"), writer == "source-alias" ? h.Fixture.Source : h.Fixture.Repository) : null;
        var inaccessible = Path.Combine(h.Fixture.Root, "unknown-writer");
        if (writer == "inaccessible") Directory.CreateDirectory(inaccessible);
        var failedIdentity = 0;
        h.Fixture.Git.BeforeCommand = (path, _) => {
            if (path == inaccessible) { failedIdentity++; throw new IOException("owned writer identity inaccessible"); }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        var ownerId = Guid.NewGuid();
        await using var unrelated = new LandingGitFixture();
        if (writer == "unrelated") await unrelated.InitializeAsync();
        await using (var db = h.CreateContext())
        {
            if (writer != "lease") db.AgentTasks.Add(new AgentTask
            {
                Id = ownerId, RootTaskId = ownerId, Title = "C467 writer", Goal = "owned fixture",
                Status = status, Role = AgentTaskRole.Code, ReplyTo = AgentTaskReplyTo.None,
                Workspace = writer.StartsWith("source", StringComparison.Ordinal) ? WorkspaceMode.Worktree : WorkspaceMode.Shared,
                WorkingDirectory = alias?.Path ?? (writer == "inaccessible" ? inaccessible : writer == "unrelated" ? unrelated.Repository : writer == "source" ? h.Fixture.Source : h.Fixture.Repository),
                RepoPath = writer == "inaccessible" ? inaccessible : writer == "shared-alias" ? alias!.Path : writer == "unrelated" ? unrelated.Repository : h.Fixture.Repository,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var lease = writer == "lease" ? await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None) : null;
        try
        {
            h.Fixture.Git.Trace.Clear();
            var result = await h.RunAsync();
            if (writer == "unrelated")
            {
                result.ShouldBe(LandRunResult.Complete);
                await h.Fixture.AssertRemoteSourceAsync();
                return;
            }
            result.ShouldBe(LandRunResult.Held);
            if (writer == "inaccessible") failedIdentity.ShouldBeGreaterThan(0);
            h.Verifier.Calls.ShouldBe(0);
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a.Contains("--ff-only") || a[0] == "push");
            await using (var observer = h.CreateContext())
            {
                var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
                request.Attempt.ShouldBe(0);
                request.State.ShouldBe(LandRequestState.Held);
                request.HoldEpisode.ShouldBe(1);
                request.HoldingTaskId.ShouldBe(writer == "lease" ? null : ownerId);
                request.HoldingTaskStatus.ShouldBe(writer == "lease" ? null : status);
                request.HoldReasonCode.ShouldBe(writer == "lease" ? "repository_mutation_lease_busy" : "repository_or_source_writer");
                (await observer.AgentTaskLandings.CountAsync(o => o.TaskId == h.Fixture.TaskId)).ShouldBe(0);
                (await observer.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id)).Kind.ShouldBe(LandNotificationKind.Held);
            }
            await h.RestartServicesAsync();
            await h.RunAsync();
            await using var again = h.CreateContext();
            (await again.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId)).ShouldBe(1);
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == ownerId);
            if (owner is not null) { owner.Status = AgentTaskStatus.Succeeded; await db.SaveChangesAsync(); }
        }
        await h.RunAsync();
        await h.Fixture.AssertRemoteSourceAsync();
        await using var released = h.CreateContext();
        var completed = await released.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        completed.State.ShouldBe(LandRequestState.Completed);
        completed.HoldingTaskId.ShouldBeNull();
        completed.Attempt.ShouldBe(1);
        (await released.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.HeldReleased)).ShouldBe(1);
    }

    [Test]
    public async Task C467_V04_HoldAndAgeAreAtomicBeforePublish()
    {
        await using var h = new LandingSafetyHarness();
        var observed = 0;
        h.Events = new ObservingBus(async () =>
        {
            await using var observer = h.CreateContext();
            var request = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.State.ShouldBe(LandRequestState.Held);
            var held = await observer.AgentTaskEvents.SingleAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.Held);
            (await observer.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == held.Id)).Kind.ShouldBe(LandNotificationKind.Held);
            observed++;
        });
        await h.InitializeAsync();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        await h.RunAsync();
        observed.ShouldBe(1);
    }

    private sealed class ObservingBus(Func<Task> observe) : IEventBus
    {
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => observe();
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default) => observe();
    }

    [Test]
    [Arguments(AgentTaskEventType.Held, "before-save")]
    [Arguments(AgentTaskEventType.Held, "after-save")]
    [Arguments(AgentTaskEventType.Held, "commit")]
    [Arguments(AgentTaskEventType.Held, "after-commit")]
    [Arguments(AgentTaskEventType.LandAged, "before-save")]
    [Arguments(AgentTaskEventType.LandAged, "after-save")]
    [Arguments(AgentTaskEventType.LandAged, "commit")]
    [Arguments(AgentTaskEventType.LandAged, "after-commit")]
    public async Task C467_V04_HoldAndAgeFaultCuts(AgentTaskEventType kind, string cut)
    {
        await using var h = new LandingSafetyHarness(); await h.InitializeAsync();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        if (kind == AgentTaskEventType.LandAged) await h.RunAsync();
        h.Fault.TerminalCut = cut; h.Fault.EventKind = kind;
        async Task ActAsync()
        {
            if (kind == AgentTaskEventType.Held) await h.RunAsync();
            else
            {
                await using var db = h.CreateContext();
                var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
                await new AgentTaskLandMonitorService(db, new FakeTimeProvider(request.LastProgressAt.AddSeconds(301)),
                    Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None);
            }
        }
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(ActAsync); h.Fault.Triggered.ShouldBeTrue();
        await using var observer = h.CreateContext();
        var eventIds = await observer.AgentTaskEvents.Where(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == kind).Select(e => e.Id).ToListAsync();
        eventIds.Count.ShouldBe(cut == "after-commit" ? 1 : 0);
        (await observer.AgentTaskLandNotifications.CountAsync(n => eventIds.Contains(n.SourceEventId))).ShouldBe(eventIds.Count);
        await h.RestartServicesAsync(); await ActAsync();
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == kind)).ShouldBe(1);
    }

    private sealed class OwnedJunction : IDisposable
    {
        public string Path { get; }
        public OwnedJunction(string path, string target)
        {
            Path = path;
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit(); process.ExitCode.ShouldBe(0, process.StandardError.ReadToEnd());
        }
        public void Dispose() => Directory.Delete(Path, false);
    }
}
