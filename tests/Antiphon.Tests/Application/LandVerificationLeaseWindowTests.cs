using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandVerificationLeaseWindowTests
{
    [Test]
    public async Task C1379_StartRefAndWorktreeFinishBeforeVerificationRelease()
    {
        await using var child = new LandVerificationWindowHarness();
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, landingVerifier: child.Verifier);
        using var originOnly = new ScratchGitRepo("c1379-origin-only");
        await originOnly.CommitFileAsync("fresh.txt", "only on origin\n");
        await originOnly.GitAsync("remote", "add", "origin", world.Remote);
        await originOnly.GitAsync("push", "origin", "HEAD:refs/heads/c1379-origin-only");
        var startSha = (await originOnly.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "cat-file", "-e", startSha)).Ok.ShouldBeFalse();
        await using (var db = world.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.Repair.Id);
            task.WorktreeBaseRequestedRef = startSha;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Services.CreateAsyncScope();
        var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        await lands.RequestAsync(world.Owner.Id, new LandAgentTaskRequest("c1379-controlled-child", world.OwnerSha),
            CancellationToken.None);
        var landing = lands.RunAsync(world.Owner.Id, null, CancellationToken.None);
        try
        {
            await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var probe = await world.Leases.TryAcquirePreflightAsync(world.Repo.Path,
                             RepositoryPreflight.Fetch(Guid.NewGuid(), world.OwnerSha),
                             new(null, RepositoryLeasePurposes.StartRefFetch), CancellationToken.None))
                probe.ShouldNotBeNull("c1379-dispatch-before-release: real verification must release R while retaining L");
            await world.Services.GetRequiredService<StartRefAvailability>()
                .EnsureAvailableAsync(world.Repo.Path, startSha, CancellationToken.None);
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "cat-file", "-e", startSha)).Ok.ShouldBeTrue();
            var (dispatched, _) = await world.DispatchAsync();
            dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
            Directory.Exists(dispatched.WorktreePath).ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(startSha);
            await using var observer = world.CreateContext();
            (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == dispatched.Id)).AgentSessionId
                .ShouldNotBeNull("a separate connection sees the committed worktree claim before the verifier is released");
        }
        finally
        {
            child.Release();
            await landing;
        }
        await using var final = world.CreateContext();
        (await final.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id))
            .RemoteConfirmedAt.ShouldNotBeNull();
    }
}
