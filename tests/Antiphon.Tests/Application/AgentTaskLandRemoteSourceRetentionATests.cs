using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRemoteSourceRetentionATests
{
    [Test]
    [Arguments("V01")]
    [Arguments("V02")]
    [Arguments("V03")]
    [Arguments("V04")]
    [Arguments("V05")]
    [Arguments("V06")]
    [Arguments("V07")]
    [Arguments("V08")]
    public async Task C448_V21_RefusalNeverDeletesRemoteSource(string family)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await Card0452LandingCases.AssertSeededRemoteSourceAsync(h);
        string? rejectMarker = null;
        switch (family)
        {
            case "V01":
                await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "--detach");
                break;
            case "V02":
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "pack-refs", "--all");
                var other = h.Fixture.SourceRef.Replace("feat/", "Feat/", StringComparison.Ordinal);
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", other, h.Fixture.SeedSha);
                await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD", other);
                break;
            case "V04":
                Directory.CreateDirectory(Path.Combine(h.Fixture.Source, ".claude"));
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, ".claude", "settings.json"), "protected\n");
                break;
            case "V05":
                var localSource = await h.AddSourceAsync();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "merge", "--ff-only", localSource);
                break;
            case "V06":
                await h.AddSourceAsync();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "local divergent base");
                var remoteTip = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree",
                    h.Fixture.SeedSha + "^{tree}", "-p", h.Fixture.SeedSha, "-m", "remote advanced")).Trim();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", remoteTip + ":" + h.Fixture.TargetRef);
                break;
            case "V07":
                var source = await h.AddSourceAsync();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "merge", "--ff-only", source);
                rejectMarker = await Card0452LandingCases.InstallRejectingTargetHookAsync(h);
                break;
            case "V08":
                await h.AddSourceAsync();
                rejectMarker = await Card0452LandingCases.InstallRejectingTargetHookAsync(h);
                break;
        }
        var fired = false;
        if (family is "V03")
            h.Fixture.Git.BeforeCommand = (_, args) =>
            {
                if (args.Contains("worktree") && args.Contains("list"))
                {
                    fired = true;
                    return Task.FromResult<LandingGitResult?>(new(128, "", "registration query failed"));
                }
                return Task.FromResult<LandingGitResult?>(null);
            };
        if (family is "V05")
            h.Fixture.Git.BeforeCommand = (_, args) =>
            {
                if (args[0] == "ls-remote" && args.Contains(h.Fixture.TargetRef))
                {
                    fired = true;
                    return Task.FromResult<LandingGitResult?>(new(128, "", "target read failed"));
                }
                return Task.FromResult<LandingGitResult?>(null);
            };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        var op = await h.OperationAsync();
        switch (family)
        {
            case "V01":
            case "V02":
            case "V03":
            case "V04":
                op.ShouldNotBeNull();
                op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
                op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
                op.LastReason.ShouldBe(family switch
                {
                    "V01" => "detached_head", "V02" => "source_branch_mismatch",
                    "V03" => "cleanup_inspection_error", _ => "ignored_content_preserved",
                });
                Directory.Exists(h.Fixture.Source).ShouldBeTrue();
                break;
            case "V05":
            case "V06":
                op.ShouldBeNull();
                await using (var db = h.CreateContext())
                    (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId))
                        .SourceRefusalReason.ShouldBe(family == "V05" ? "remote_read_failed" : "target_local_ahead");
                break;
            case "V07":
            case "V08":
                File.Exists(rejectMarker).ShouldBeTrue("the real target pre-receive hook must reject the push");
                op.ShouldNotBeNull();
                op.LastReason.ShouldBe("push_rejected");
                op.RemoteConfirmedAt.ShouldBeNull();
                op.VerifiedSourceSha.ShouldNotBeNull();
                h.Fixture.Git.Trace.ShouldContain(a => a[0] == "push" && a.Contains(h.Fixture.TargetRef));
                break;
        }
        if (family is "V03" or "V05") fired.ShouldBeTrue("the family-specific fault must fire");
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
        h.Fixture.Git.BeforeCommand = null;
        h.Fixture.Git.HooksPathOverride = null;
        await h.RestartServicesAsync();
        await h.SweepAsync();
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
    }
}
