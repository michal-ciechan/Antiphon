using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRemoteSourceRetentionCTests
{
    [Test]
    [Arguments("V17")]
    [Arguments("V18")]
    [Arguments("V19")]
    [Arguments("V20")]
    [Arguments("V23")]
    [Arguments("V24")]
    [Arguments("V25")]
    [Arguments("V27")]
    public async Task C448_V21_RefusalNeverDeletesRemoteSource(string family)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await Card0452LandingCases.AssertSeededRemoteSourceAsync(h);
        if (family != "V24") await h.AddSourceAsync();
        h.Fixture.Git.Trace.Clear();
        switch (family)
        {
            case "V17": await MissingRebaseAsync(h); break;
            case "V18": await ChangedCleanupAsync(h); break;
            case "V19": await InvalidRemovalReceiptAsync(h); break;
            case "V20": await RefusedRemovalBoundaryAsync(h); break;
            case "V23": await TerminalSaveFailureAsync(h); break;
            case "V24": await LegacyEventWithoutAuthorityAsync(h); break;
            case "V25": await LocalMergeChangedSourceAsync(h); break;
            case "V27": await TargetRewrittenBeforeCleanupAsync(h); break;
        }
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
        await h.RestartServicesAsync();
        await h.SweepAsync();
        if (family is "V18" or "V19" or "V20" or "V25" or "V27")
            await Card0452LandingCases.RunScopedResidueAsync(h);
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
    }

    private static async Task MissingRebaseAsync(LandingSafetyHarness h)
    {
        var fired = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Contains("rebase") && !args.Contains("--abort"))
            {
                fired = true;
                return Task.FromResult<LandingGitResult?>(new(0, "", ""));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        await h.RunAsync();
        fired.ShouldBeTrue();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Phase.ShouldBe(LandPhase.Refused);
        op.LastReason.ShouldBe("interrupted_rebase");
        op.RemoteConfirmedAt.ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.BeforeCommand = null;
    }

    private static async Task ChangedCleanupAsync(LandingSafetyHarness h)
    {
        var report = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllTextAsync(report, "retained report\n");
        await h.RunAsync();
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        receipt.Publication.ShouldBe(LandPublicationOutcome.Landed);
        receipt.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        File.Delete(report);
        var changed = Path.Combine(h.Fixture.Source, "feature.txt");
        await File.WriteAllTextAsync(changed, "new writer bytes\n");
        await h.RepostAsync();
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Id.ShouldBe(receipt.Id);
        op.Mode.ShouldBe(LandOperationMode.CleanupRetry);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        (await File.ReadAllTextAsync(changed)).ShouldBe("new writer bytes\n");
    }

    private static async Task InvalidRemovalReceiptAsync(LandingSafetyHarness h)
    {
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, h.Fixture.Coordinates,
            op.CommonDirectory, op.GitDirectory, op.OriginalSourceSha, op.TargetBeforeSha, op.Id, lease);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", "--force", h.Fixture.Source);
        await using (var db = h.CreateContext())
        {
            var receipt = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            receipt.SchemaVersion = 999;
            await db.SaveChangesAsync();
        }
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        removed.IsClean.ShouldBeFalse();
        removed.Residue.ShouldBe("publication_receipt_mismatch");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "update-ref" && a.Contains("-d"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim()
            .ShouldBe(op.OriginalSourceSha);
    }

    private static async Task RefusedRemovalBoundaryAsync(LandingSafetyHarness h)
    {
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, h.Fixture.Coordinates,
            op.CommonDirectory, op.GitDirectory, op.OriginalSourceSha, op.TargetBeforeSha, op.Id, lease);
        var fired = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
            {
                fired = true;
                return Task.FromResult<LandingGitResult?>(new(128, "", "fixture_remove_error"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        fired.ShouldBeTrue();
        removed.IsClean.ShouldBeFalse();
        removed.Residue.ShouldBe("worktree_remove_failed");
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim()
            .ShouldBe(op.OriginalSourceSha);
    }

    private static async Task TerminalSaveFailureAsync(LandingSafetyHarness h)
    {
        h.Fault.TerminalCut = "before-save";
        h.Fault.EventKind = AgentTaskEventType.Landed;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldNotBeNull();
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal)).ShouldBe(0);
        h.Fault.TerminalCut = null;
    }

    private static async Task LegacyEventWithoutAuthorityAsync(LandingSafetyHarness h)
    {
        var opaque = Path.Combine(h.Fixture.Source, ".antiphon", "legacy-report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(opaque)!);
        await File.WriteAllTextAsync(opaque, "opaque legacy bytes\n");
        await using (var db = h.CreateContext())
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = h.Fixture.TaskId, Type = AgentTaskEventType.Landed,
                Detail = "legacy landed", At = DateTime.UtcNow, IsLandTerminal = true,
            });
            await db.SaveChangesAsync();
        }
        await Card0452LandingCases.RunScopedResidueAsync(h);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await File.ReadAllTextAsync(opaque)).ShouldBe("opaque legacy bytes\n");
        (await h.OperationAsync()).ShouldBeNull();
    }

    private static async Task LocalMergeChangedSourceAsync(LandingSafetyHarness h)
    {
        var fired = false;
        string? changed = null;
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (fired || !result.Succeeded || !args.Contains("rebase") || args.Contains("--abort")) return;
            fired = true;
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "another local writer");
            changed = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        };
        await using var scope = h.Services.CreateAsyncScope();
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
        var result = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>()
            .TryMergeBackAsync(task, CancellationToken.None);
        fired.ShouldBeTrue();
        result.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(changed);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        h.Fixture.Git.AfterCommand = null;
    }

    private static async Task TargetRewrittenBeforeCleanupAsync(LandingSafetyHarness h)
    {
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        receipt.RemoteConfirmedAt.ShouldNotBeNull();
        await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha);
        h.Fault.AfterCommit = false;
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.Id.ShouldBe(receipt.Id);
        retry.RemoteConfirmedAt.ShouldBe(receipt.RemoteConfirmedAt);
        retry.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
    }

}
