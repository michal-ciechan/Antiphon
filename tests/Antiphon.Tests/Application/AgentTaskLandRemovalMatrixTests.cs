using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRemovalMatrixTests
{
    [Test]
    [Arguments(1, "staged")]
    [Arguments(1, "unstaged")]
    [Arguments(1, "untracked")]
    [Arguments(1, ".antiphon/report.md")]
    [Arguments(1, ".claude/settings.json")]
    [Arguments(1, "bin-private/keep.txt")]
    [Arguments(1, "detach")]
    [Arguments(1, "switch")]
    [Arguments(1, "advance")]
    [Arguments(1, "metadata")]
    [Arguments(1, "registration")]
    [Arguments(1, "submodule")]
    [Arguments(2, "staged")]
    [Arguments(2, "unstaged")]
    [Arguments(2, "untracked")]
    [Arguments(2, ".antiphon/report.md")]
    [Arguments(2, ".claude/settings.json")]
    [Arguments(2, "bin-private/keep.txt")]
    [Arguments(2, "detach")]
    [Arguments(2, "switch")]
    [Arguments(2, "advance")]
    [Arguments(2, "metadata")]
    [Arguments(2, "registration")]
    [Arguments(2, "submodule")]
    public async Task C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(int reading, string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (change == "submodule")
        {
            await h.Fixture.RequiredAsync(h.Fixture.Source, "-c", "protocol.file.allow=always", "submodule", "add", h.Fixture.Remote, "nested");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-am", "submodule fixture");
        }
        var source = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, h.Fixture.Coordinates,
            op.CommonDirectory, op.GitDirectory, source, op.TargetBeforeSha, op.Id, lease);
        var seen = 0;
        var fired = false;
        string? file = null;
        byte[]? index = null;
        var retainedSha = source;
        h.Fixture.Git.BeforeCommand = async (repo, command) =>
        {
            if (repo != h.Fixture.Source || command[0] != "status" || ++seen != reading) return null;
            fired = true;
            switch (change)
            {
                case "detach": await h.Fixture.RequiredAsync(repo, "checkout", "--detach"); break;
                case "switch": await h.Fixture.RequiredAsync(repo, "checkout", "-b", "new-owner"); break;
                case "advance":
                    await h.Fixture.RequiredAsync(repo, "commit", "--allow-empty", "-m", "new owner");
                    retainedSha = (await h.Fixture.RequiredAsync(repo, "rev-parse", "HEAD")).Trim();
                    break;
                case "metadata":
                    await using (var db = h.CreateContext())
                    {
                        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                        task.WorktreeBranch = "another-recorded-source";
                        await db.SaveChangesAsync();
                    }
                    break;
                case "registration":
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "lock", h.Fixture.Source);
                    break;
                default:
                    file = Path.Combine(repo, change switch { "staged" or "unstaged" => "keep.txt", "untracked" => "new.txt", "submodule" => "nested/keep.txt", _ => change });
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, "new owner bytes\n");
                    if (change == "staged") await h.Fixture.RequiredAsync(repo, "add", "keep.txt");
                    break;
            }
            index = await File.ReadAllBytesAsync(Path.Combine(op.GitDirectory, "index"));
            return null;
        };
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        fired.ShouldBeTrue();
        removed.IsClean.ShouldBeFalse();
        if (change is ".antiphon/report.md" or ".claude/settings.json" or "bin-private/keep.txt")
            seen.ShouldBe(reading, "each ignored-content guard must refuse at its own boundary, before another source status read");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a[0] == "update-ref" && a.Contains("-d"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.BeforeCommand = null;
        if (file is not null) (await File.ReadAllTextAsync(file)).ShouldBe("new owner bytes\n");
        (await File.ReadAllBytesAsync(Path.Combine(op.GitDirectory, "index"))).ShouldBe(index!);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(retainedSha);
        (await h.OperationAsync())!.RemoteConfirmedAt.ShouldBe(op.RemoteConfirmedAt);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("missing-receipt")]
    [Arguments("task")]
    [Arguments("repository")]
    [Arguments("path")]
    [Arguments("git-directory")]
    [Arguments("source-ref")]
    [Arguments("source-sha")]
    [Arguments("target-sha")]
    [Arguments("target-ref")]
    [Arguments("forged-lease")]
    [Arguments("missing-lease")]
    [Arguments("disposed-lease")]
    [Arguments("unconfirmed")]
    [Arguments("operation-namespace")]
    [Arguments("destination")]
    [Arguments("schema")]
    [Arguments("inactive")]
    [Arguments("cleanup-intent")]
    [Arguments("local-purpose")]
    public async Task C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var provider = h.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var lease = await provider.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, h.Fixture.Coordinates,
            op.CommonDirectory, op.GitDirectory, source, op.TargetBeforeSha, op.Id, lease);
        request = variant switch
        {
            "missing-receipt" => request with { LandingId = Guid.NewGuid() },
            "task" => request with { Source = request.Source with { TaskId = Guid.NewGuid() } },
            "repository" => request with { Source = request.Source with { RepositoryPath = h.Fixture.Remote } },
            "path" => request with { Source = request.Source with { WorktreePath = Path.Combine(h.Fixture.Root, "trees", "other") } },
            "git-directory" => request with { GitDirectory = Path.Combine(h.Fixture.Repository, ".git") },
            "source-ref" => request with { Source = request.Source with { SourceFullRef = "refs/heads/equal-sha" } },
            "source-sha" => request with { ExpectedSourceSha = h.Fixture.SeedSha },
            "target-sha" => request with { ExpectedTargetSha = source },
            "target-ref" => request with { Source = request.Source with { TargetFullRef = "refs/heads/equal-sha" } },
            "forged-lease" => request with { Lease = new ForgedLease(op.CommonDirectory) },
            "missing-lease" => request with { Lease = null! },
            "local-purpose" => request with { Purpose = WorktreeRemovalPurpose.LocalMerge },
            _ => request,
        };
        if (variant == "disposed-lease") await lease.DisposeAsync();
        if (variant is "destination" or "schema" or "inactive" or "cleanup-intent" or "unconfirmed" or "operation-namespace")
        {
            await using var db = h.CreateContext();
            var receipt = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            if (variant == "destination") receipt.DestinationFullRef = "refs/heads/equal-sha";
            if (variant == "schema") receipt.SchemaVersion = 999;
            if (variant == "inactive") receipt.Active = false;
            if (variant == "cleanup-intent") receipt.CleanupStartedAt = null;
            if (variant == "unconfirmed") receipt.RemoteConfirmedAt = null;
            if (variant == "operation-namespace") receipt.RecoveryRefPrefix = $"refs/antiphon/land/{receipt.TaskId:N}/{Guid.NewGuid():N}";
            await db.SaveChangesAsync();
        }
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "branch", "equal-sha", source);
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        removed.IsClean.ShouldBe(variant == "valid", "only the exact current, committed authority may delete this checkout");
        if (variant == "valid")
        {
            h.Fixture.Git.Trace.Single(a => a.Contains("worktree") && a.Contains("remove"))
                .ShouldBe(["worktree", "remove", "--", h.Fixture.Source]);
            h.Fixture.Git.Trace.Single(a => a[0] == "update-ref" && a.Contains("-d"))
                .ShouldBe(["update-ref", "--no-deref", "-d", h.Fixture.SourceRef, source]);
        }
        else
        {
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("-d"));
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
        }
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "refs/heads/equal-sha")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("remove-error")]
    [Arguments("remove-timeout")]
    [Arguments("windows-file-handle")]
    [Arguments("branch-moved")]
    [Arguments("new-checkout")]
    [Arguments("registration-error")]
    public async Task C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.CleanupStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync())!;
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, h.Fixture.Coordinates,
            op.CommonDirectory, op.GitDirectory, source, op.TargetBeforeSha, op.Id, lease);
        var directoryRemoved = false;
        var fired = false;
        var retained = source;
        var newCheckout = Path.Combine(h.Fixture.Root, "another-checkout");
        using var heldFile = variant == "windows-file-handle"
            ? new FileStream(Path.Combine(h.Fixture.Source, "feature.txt"), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        if (variant == "windows-file-handle" && !OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Windows sharing semantics required");
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
            {
                if (variant == "remove-error") { fired = true; return new(128, "", "fixture_remove_error"); }
                if (variant == "remove-timeout") { fired = true; throw new TimeoutException("fixture_remove_timeout"); }
            }
            if (directoryRemoved && variant == "registration-error" && args.Contains("worktree") && args.Contains("list"))
            { fired = true; return new(128, "", "fixture_registration_error"); }
            if (!fired && variant == "branch-moved" && args[0] == "update-ref" && args.Contains("-d"))
            {
                fired = true;
                var tree = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", source + "^{tree}")).Trim();
                retained = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", tree, "-p", source, "-m", "new branch owner")).Trim();
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", h.Fixture.SourceRef, retained, source);
            }
            return null;
        };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
            {
                if (variant == "windows-file-handle") fired = true;
                if (!result.Succeeded) return;
                directoryRemoved = true;
                if (variant == "new-checkout")
                {
                    fired = true;
                    await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", newCheckout, h.Fixture.SourceRef[11..]);
                }
            }
        };
        h.Fixture.Git.Trace.Clear();
        var removed = await h.Services.GetRequiredService<IWorktreeManager>().TryRemoveAsync(request, CancellationToken.None);
        fired.ShouldBeTrue("the designated destructive boundary must be reached");
        removed.IsClean.ShouldBeFalse();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--force") || a.Contains("prune"));
        if (variant != "branch-moved") h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "update-ref" && a.Contains("-d"));
        h.Fixture.Git.BeforeCommand = null;
        h.Fixture.Git.AfterCommand = null;
        var retainedRef = await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["rev-parse", "--verify", h.Fixture.SourceRef], CancellationToken.None);
        retainedRef.Succeeded.ShouldBeTrue("a refused/failed component must retain the source branch, including a raced newer tip");
        retainedRef.Output.Trim().ShouldBe(retained);
        if (variant.StartsWith("remove", StringComparison.Ordinal) || variant == "windows-file-handle")
        {
            File.Exists(Path.Combine(h.Fixture.Source, "feature.txt")).ShouldBeTrue("failed ordinary removal must preserve the remaining bytes without recursive fallback");
            (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "feature.txt"))).ShouldBe("valuable feature\n");
        }
        if (variant == "new-checkout")
            (await File.ReadAllTextAsync(Path.Combine(newCheckout, "feature.txt"))).ShouldBe("valuable feature\n");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class ForgedLease(string common) : RepositoryLease
    {
        public override string CommonDirectory => common;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
