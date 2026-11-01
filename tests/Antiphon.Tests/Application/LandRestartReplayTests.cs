using System.Collections.Concurrent;
using System.Threading.Channels;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandRestartReplayTests
{
    [Test]
    public async Task C1379_OldHeldReplaysBeforeConcurrentNewRequest()
    {
        var boundary = new ReplayBoundary();
        await using var h = new LandingSafetyHarness { Boundary = boundary };
        await h.InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "old.txt"), "older\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "older source");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == old.CurrentLandRequestId);
            old.LandRequestedAt = request.RequestedAt = DateTime.UtcNow.AddMinutes(-5);
            request.State = LandRequestState.Held;
            request.HeldSince = request.RequestedAt;
            request.HoldReasonCode = "repository_lease_busy";
            await db.SaveChangesAsync();
        }

        // A fresh provider loses all volatile queue state. The next accepted request can
        // wake the reader before the periodic boot sweep has run.
        await h.RestartServicesAsync();
        boundary.Drop = false;
        var newer = Guid.NewGuid();
        var source = Path.Combine(h.Fixture.Root, "trees", newer.ToString("N"));
        var branch = "feat/card-task-" + newer.ToString("N");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "-b", branch, source, "master");
        await File.WriteAllTextAsync(Path.Combine(source, "new.txt"), "newer\n");
        await h.Fixture.RequiredAsync(source, "add", ".");
        await h.Fixture.RequiredAsync(source, "commit", "-m", "newer source");
        await h.Fixture.RequiredAsync(source, "push", "origin", branch);
        var sha = (await h.Fixture.RequiredAsync(source, "rev-parse", "HEAD")).Trim();
        await using (var db = h.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = newer, RootTaskId = newer, Title = "newer land", Goal = "fixture",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = h.Fixture.Repository, RepoPath = h.Fixture.Repository,
                WorktreePath = source, WorktreeBranch = branch, MergeTargetRef = "master",
                Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await using (var scope = h.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandService>()
                .RequestAsync(newer, new LandAgentTaskRequest(null, sha), CancellationToken.None);

        // Provisional Windows budget after the qualification's 30 s cancellation;
        // CARD-1379's owed native sample is recorded in the 2026-10-30 Debug investigation.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var hosted = new AgentTaskLandHostedService(h.Queue,
            h.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandHostedService>.Instance);
        var waitingFor = "first replay selection";
        LandingEvidence.Write(h.Fixture.TaskId, "replay_wait_started", new { deadlineSeconds = 60 });
        try
        {
            // The observer owns its deadline; StopAsync owns the worker's lifetime.
            await hosted.StartAsync(CancellationToken.None);
            var first = await boundary.Consumed.Reader.ReadAsync(timeout.Token);
            first.ShouldBe(h.Fixture.TaskId,
                "c1379-replay-order: an accepted newer wake must not overtake an older durable Held request");
            // Observe a committed admitted attempt and real publication, not a sorted helper.
            waitingFor = "first replay's committed terminal result";
            (await boundary.Terminal.Reader.ReadAsync(timeout.Token)).ShouldBe(first,
                "c1379-replay-publication: the first replay must finish before the newer request");
            await using (var observer = h.CreateContext())
            {
                var request = await observer.AgentTaskLandRequests.AsNoTracking()
                    .SingleAsync(r => r.TaskId == first, timeout.Token);
                request.IsPending.ShouldBeFalse();
                request.Attempt.ShouldBe(1);
                (await observer.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == first, timeout.Token))
                    .RemoteConfirmedAt.ShouldNotBeNull();
            }
            waitingFor = "second replay selection";
            var second = await boundary.Consumed.Reader.ReadAsync(timeout.Token);
            second.ShouldBe(newer);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"c1379-replay-deadline: waiting for {waitingFor}; pending={h.Queue.PendingCount}; "
                + $"boundaries={string.Join(",", boundary.Observed)}", ex);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ReplayBoundary : LandDeliveryBoundary
    {
        public bool Drop { get; set; } = true;
        public Channel<Guid> Consumed { get; } = Channel.CreateUnbounded<Guid>();
        public Channel<Guid> Terminal { get; } = Channel.CreateUnbounded<Guid>();
        public ConcurrentQueue<string> Observed { get; } = new();
        public override bool DropWakeup(string boundary, Guid identity) => Drop;
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            Observed.Enqueue($"{boundary}:{taskId:N}");
            LandingEvidence.Write(taskId, "replay_boundary", new { boundary, identity });
            if (boundary == "before-execution") Consumed.Writer.TryWrite(taskId);
            if (boundary == "terminal-committed") Terminal.Writer.TryWrite(taskId);
            return Task.CompletedTask;
        }
    }
}
