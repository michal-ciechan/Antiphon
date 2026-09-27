using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
public sealed class AgentTaskLandRemoteSourceRetentionBTests
{
    [Test]
    [Arguments("V09")]
    [Arguments("V10")]
    [Arguments("V11")]
    [Arguments("V12")]
    [Arguments("V13")]
    [Arguments("V14")]
    [Arguments("V15")]
    [Arguments("V16")]
    public async Task C448_V21_RefusalNeverDeletesRemoteSource(string family)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await Card0452LandingCases.AssertSeededRemoteSourceAsync(h);
        await h.AddSourceAsync();
        var fired = false;
        string? rival = null;
        IAsyncDisposable? lease = null;
        if (family == "V12")
        {
            h.LandSettings.LandTargetRaceRetries = 0;
            rival = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", h.Fixture.SeedSha + "^{tree}",
                "-p", h.Fixture.SeedSha, "-m", "other remote writer")).Trim();
        }
        if (family == "V13")
        {
            lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(h.Fixture.Source, CancellationToken.None);
            lease.ShouldNotBeNull();
        }
        if (family == "V14")
        {
            await using var db = h.CreateContext();
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "active source writer", Goal = "write",
                Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Working,
                RepoPath = h.Fixture.Repository, WorkingDirectory = h.Fixture.Repository,
                WorktreePath = h.Fixture.Source, FollowUpOfTaskId = h.Fixture.TaskId,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        if (family == "V09")
        {
            var pushed = false;
            h.Fixture.Git.BeforeCommand = (_, args) =>
            {
                if (args[0] == "push") pushed = true;
                if (pushed && args[0] == "ls-remote" && args.Contains(h.Fixture.TargetRef))
                {
                    fired = true;
                    return Task.FromResult<LandingGitResult?>(new(0, h.Fixture.SeedSha + "\t" + h.Fixture.TargetRef + "\n", ""));
                }
                return Task.FromResult<LandingGitResult?>(null);
            };
        }
        if (family == "V10")
            h.Fault.AfterAcknowledged = async phase =>
            {
                if (phase != LandPhase.Verified || fired) return;
                fired = true;
                await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new source after verified");
            };
        if (family == "V11")
            h.Fixture.Git.AfterCommand = async (_, args, result) =>
            {
                if (fired || !result.Succeeded || !args.Contains("--ff-only")) return;
                fired = true;
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new target writer");
            };
        if (family == "V12")
            h.Fixture.Git.BeforeCommand = async (_, args) =>
            {
                if (args[0] == "push" && !fired)
                {
                    fired = true;
                    await h.Fixture.RequiredAsync(h.Fixture.Remote, "fetch", h.Fixture.Repository, rival + ":" + h.Fixture.TargetRef);
                }
                return null;
            };
        if (family == "V15")
        {
            h.Fault.Phase = LandPhase.PushStarted;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
            h.Fault.Triggered.ShouldBeTrue();
            (await h.OperationAsync()).ShouldNotBeNull().Phase.ShouldBe(LandPhase.PushStarted);
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "new source after push intent");
            await h.RepostAsync();
            h.Fault.AfterCommit = false;
            await h.RestartServicesAsync();
        }
        if (family == "V16")
        {
            h.Fault.Phase = LandPhase.PublicationConfirmed;
            h.Fault.AfterCommit = false;
        }
        h.Fixture.Git.Trace.Clear();
        try
        {
            if (family == "V16")
                await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
            else
                (await h.RunAsync()).ShouldBe(family is "V13" or "V14" ? LandRunResult.Held : LandRunResult.Complete);
            var op = await h.OperationAsync();
            switch (family)
            {
                case "V09":
                    fired.ShouldBeTrue();
                    op.ShouldNotBeNull().LastReason.ShouldBe("push_unconfirmed");
                    op.RemoteConfirmedAt.ShouldBeNull();
                    break;
                case "V10":
                    fired.ShouldBeTrue();
                    op.ShouldNotBeNull().LastReason.ShouldBe("source_changed");
                    op.RemoteConfirmedAt.ShouldBeNull();
                    break;
                case "V11":
                    fired.ShouldBeTrue();
                    op.ShouldNotBeNull().CanonicalAdvanceReason.ShouldBe("canonical_advance_failed");
                    op.RemoteConfirmedAt.ShouldNotBeNull();
                    break;
                case "V12":
                    fired.ShouldBeTrue();
                    op.ShouldNotBeNull().LastReason.ShouldBe("remote_changed_before_push");
                    op.RemoteConfirmedAt.ShouldBeNull();
                    (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(rival);
                    break;
                case "V13":
                case "V14":
                    op.ShouldBeNull();
                    await using (var db = h.CreateContext())
                        (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId))
                            .HoldReasonCode.ShouldBe(family == "V14" ? "repository_or_source_writer" : "repository_lease_held");
                    break;
                case "V15":
                    op.ShouldNotBeNull().Phase.ShouldBe(LandPhase.PushStarted);
                    op.RemoteConfirmedAt.ShouldBeNull();
                    h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
                    break;
                case "V16":
                    h.Fault.Triggered.ShouldBeTrue();
                    op.ShouldNotBeNull().RemoteConfirmedAt.ShouldBeNull();
                    Directory.Exists(h.Fixture.Source).ShouldBeTrue();
                    break;
            }
            await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
            if (family is "V13" or "V14")
            {
                await h.RestartServicesAsync();
                (await h.RunAsync()).ShouldBe(LandRunResult.Held);
                await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
            }
        }
        finally
        {
            h.Fixture.Git.BeforeCommand = null;
            h.Fixture.Git.AfterCommand = null;
            h.Fault.AfterAcknowledged = null;
            if (lease is not null) await lease.DisposeAsync();
        }
        if (family is not ("V13" or "V14"))
        {
            await h.RestartServicesAsync();
            await h.SweepAsync();
            await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
        }
    }
}
