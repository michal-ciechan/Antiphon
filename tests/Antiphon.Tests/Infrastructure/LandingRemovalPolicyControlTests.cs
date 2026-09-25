using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// Each crossed coordinate remains otherwise valid. A mutation-capable downstream
// fake exposes the first unauthorized deletion even when real Git has another guard.
[Category("Unit")]
public sealed class LandingRemovalPolicyControlTests
{
    [Test]
    [Arguments(1, ".antiphon/report.md")]
    [Arguments(1, ".claude/settings.json")]
    [Arguments(1, ".claude/settings.local.json")]
    [Arguments(2, ".antiphon/report.md")]
    [Arguments(2, ".claude/settings.json")]
    [Arguments(2, ".claude/settings.local.json")]
    [Arguments(1, "head")]
    [Arguments(2, "head")]
    public async Task C448_V18_EachContentReadingRefusesBeforeItsNextCommand(int reading, string change)
    {
        using var f = new RemovalFixture();
        f.AfterInspection = count =>
        {
            if (count < reading) return;
            if (change == "head") f.InspectedSha = RemovalFixture.Other;
            else
            {
                f.IgnoredPath = change;
                var path = Path.Combine(f.Source, change);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "new private bytes");
            }
        };
        var result = await f.RemoveAsync();
        f.Mutations.ShouldBeEmpty("each content/identity reading must refuse before deletion");
        f.InspectionCount.ShouldBe(reading, "a later guard cannot replace the refusal at this reading");
        result.IsClean.ShouldBeFalse();
        if (change != "head") File.ReadAllText(Path.Combine(f.Source, change)).ShouldBe("new private bytes");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C448_V18_DurableAuthorityIsReadAfterTheFinalInspection()
    {
        using var f = new RemovalFixture();
        f.AfterInspection = count =>
        {
            if (count != 2) return;
            f.Operation.TaskId = Guid.NewGuid();
            f.Operation.RecoveryRefPrefix = $"refs/antiphon/land/{f.Operation.TaskId:N}/{f.Operation.Id:N}";
        };
        var result = await f.RemoveAsync();
        f.InspectionCount.ShouldBe(2);
        f.Mutations.ShouldBeEmpty("a receipt change during final inspection must prevent directory deletion");
        result.IsClean.ShouldBeFalse();
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("head")]
    [Arguments("target")]
    [Arguments("symbolic")]
    [Arguments("dirty")]
    [Arguments("status-error")]
    [Arguments("sequencer")]
    [Arguments("checkout")]
    public async Task C448_V11_EachTargetDecisionRefusesIndependently(string change)
    {
        using var f = new RemovalFixture();
        f.LocalChange = change;
        f.Operation.TargetCheckoutRecorded = true;
        f.Operation.TargetCheckoutPath = f.Root;
        var protocol = new AgentTaskLandingProtocol(null!, f, f, null!, null!, TimeProvider.System);
        // Exercise the existing decision directly without adding a production API for tests.
        // Real-Git boundary cases separately assert the target index, files and remote state.
        var method = typeof(AgentTaskLandingProtocol).GetMethod("CheckTargetAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Exception? failure = null;
        try { await (Task)method.Invoke(protocol, [f.Operation, RemovalFixture.Sha, CancellationToken.None])!; }
        catch (Exception ex) { failure = ex; }
        if (change == "valid") failure.ShouldBeNull();
        else failure.ShouldNotBeNull("the target decision must refuse this independently changed component: " + change)
            .GetType().Name.ShouldBe("LandingRefusal");
        f.Mutations.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
    }

    [Test]
    [Arguments("valid")]
    [Arguments("head")]
    [Arguments("target")]
    [Arguments("symbolic")]
    [Arguments("target-symbolic")]
    [Arguments("dirty")]
    [Arguments("status-error")]
    [Arguments("sequencer")]
    [Arguments("checkout")]
    [Arguments("ancestry")]
    public async Task C448_V25_LocalAuthorityChecksTheCurrentParent(string change)
    {
        using var f = new RemovalFixture();
        f.Request = f.Request with { Purpose = WorktreeRemovalPurpose.LocalMerge,
            TargetCheckoutRecorded = true, TargetCheckoutPath = f.Root };
        f.LocalChange = change;
        var result = await f.RemoveAsync();
        if (change == "valid")
        {
            result.IsClean.ShouldBeTrue();
            f.Mutations.Count.ShouldBe(2);
        }
        else
        {
            f.Mutations.ShouldBeEmpty("local merge proof cannot authorize deletion after a parent change: " + change);
            result.IsClean.ShouldBeFalse();
            File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
            f.BranchPresent.ShouldBeTrue();
        }
    }

    [Test]
    [Arguments("valid")]
    [Arguments("task")]
    [Arguments("operation")]
    [Arguments("repository")]
    [Arguments("path")]
    [Arguments("git-directory")]
    [Arguments("common-directory")]
    [Arguments("source-ref")]
    [Arguments("target-ref")]
    [Arguments("target-sha")]
    [Arguments("deletion-sha")]
    [Arguments("verified-sha")]
    [Arguments("cleanup-intent")]
    [Arguments("phase")]
    [Arguments("inactive")]
    [Arguments("schema")]
    [Arguments("unconfirmed")]
    [Arguments("operation-namespace")]
    [Arguments("destination")]
    [Arguments("fingerprint")]
    [Arguments("confirmation-method")]
    [Arguments("observed-sha")]
    [Arguments("lease")]
    public async Task C448_V36_EachAuthorityCoordinatePrecedesMutation(string change)
    {
        using var f = new RemovalFixture();
        if (change != "valid")
        {
            using var safe = new RemovalFixture();
            (await safe.RemoveAsync()).IsClean.ShouldBeTrue("the unchanged command path must be reachable");
            safe.Mutations.Count.ShouldBe(2);
        }
        var op = f.Operation;
        switch (change)
        {
            case "task": op.TaskId = Guid.NewGuid(); op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}"; break;
            case "operation": op.Id = Guid.NewGuid(); op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}"; break;
            case "repository": op.RepositoryPath = Path.Combine(f.Root, "other"); break;
            case "path": op.WorktreePath = Path.Combine(f.Root, "other"); break;
            case "git-directory": op.GitDirectory = Path.Combine(f.Root, "other"); break;
            case "common-directory": op.CommonDirectory = Path.Combine(f.Root, "other"); break;
            case "source-ref": op.SourceFullRef = "refs/heads/other"; break;
            case "target-ref": op.TargetFullRef = op.DestinationFullRef = "refs/heads/other"; break;
            case "target-sha": op.TargetBeforeSha = RemovalFixture.Other; break;
            case "deletion-sha": op.ExpectedDeletionSha = RemovalFixture.Other; break;
            case "verified-sha": op.VerifiedSourceSha = op.RebasedSourceSha = RemovalFixture.Other; op.PreparedPinned = true; break;
            case "cleanup-intent": op.CleanupStartedAt = null; break;
            case "phase": op.Phase = LandPhase.PublicationConfirmed; break;
            case "inactive": op.Active = false; break;
            case "schema": op.SchemaVersion = 999; break;
            case "unconfirmed": op.RemoteConfirmedAt = null; break;
            case "operation-namespace": op.RecoveryRefPrefix += "/crossed"; break;
            case "destination": op.DestinationFullRef = "refs/heads/other"; break;
            case "fingerprint": op.RemoteFingerprint = "malformed"; break;
            case "confirmation-method": op.ConfirmationMethod = "report says pushed"; break;
            case "observed-sha": op.ObservedRemoteTargetSha = "unknown"; break;
            case "lease": f.ValidLease = false; break;
        }
        var result = await f.RemoveAsync();
        if (change == "valid")
        {
            result.IsClean.ShouldBeTrue();
            f.Mutations[0].ShouldBe(f.UnregisterVector);
            f.Mutations[1].ShouldBe(["update-ref", "--no-deref", "-d", f.Request.Source.SourceFullRef, RemovalFixture.Sha]);
        }
        else
        {
            f.Mutations.ShouldBeEmpty("crossed authority must refuse before issuing any destructive request: " + change);
            result.IsClean.ShouldBeFalse();
            File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
            f.BranchPresent.ShouldBeTrue();
        }
    }

    // CARD-0665 V-2. Null gate keeps today's total refusal; the real gate lets disposable content
    // go with the tree, retains evidence between the two readings and names protected paths.
    [Test]
    public async Task C665_NullGateProtectsEveryIgnoredPath()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.Add("obj/a.json");
        var result = await f.RemoveAsync();
        result.Residue.ShouldBe("ignored_content_preserved");
        f.Mutations.ShouldBeEmpty("an unwired composition must stay fail-closed");
        f.InspectionCount.ShouldBe(1);
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C665_DisposableOnlyProceedsToRemoval()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.AddRange(["obj/a.json", "bin-x/a.dll"]);
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeTrue(result.Residue);
        f.Mutations[0].ShouldBe(f.UnregisterVector);
        f.InspectionCount.ShouldBe(2);
        f.Retention.Calls.ShouldHaveSingleItem().ShouldBe((0, 1), "an evidence-free tree still passes the artifact-pointer check once");
        result.Detail.ShouldBeNull();
    }

    [Test]
    public async Task C665_ProtectedRefusalNamesPaths()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.AddRange(["x.user", ".claude/settings.json", "obj/a.json"]);
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("ignored_content_preserved");
        result.Detail.ShouldNotBeNull().ShouldContain(".claude/settings.json");
        result.Detail.ShouldContain("x.user");
        result.Detail.ShouldNotContain("obj/a.json");
        f.Mutations.ShouldBeEmpty();
        f.Retention.Calls.ShouldBeEmpty("protected content refuses before anything is copied");
        f.InspectionCount.ShouldBe(1);
    }

    [Test]
    public async Task C665_EvidenceRetainedBeforeSecondReading()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.AddRange([".antiphon/task-0123abcd.md", "obj/a.json"]);
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeTrue(result.Residue);
        f.Retention.Paths.Single().ShouldBe([".antiphon/task-0123abcd.md"]);
        f.Retention.Calls.ShouldHaveSingleItem().ShouldBe((1, 1), "evidence is retained once, after reading 1 and before reading 2");
        f.InspectionCount.ShouldBe(2);
        f.Mutations[0].ShouldBe(f.UnregisterVector);
        result.Detail.ShouldNotBeNull().ShouldStartWith("retained=1");
    }

    [Test]
    public async Task C665_RetentionRefusalPreservesTree()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.Add(".antiphon/task-0123abcd.md");
        f.Retention.Refusal = "evidence_retention_exceeded";
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("evidence_retention_exceeded");
        f.Mutations.ShouldBeEmpty();
        f.InspectionCount.ShouldBe(1);
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C665_EvidenceAppearingAtSecondReadingRefuses()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.Add("obj/a.json");
        f.AfterInspection = count => { if (count == 2) f.IgnoredPaths.Add(".antiphon/task-0123abcd.md"); };
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("ignored_content_changed");
        f.Mutations.ShouldBeEmpty("evidence that was never retained must not be deleted");
        f.InspectionCount.ShouldBe(2);
        f.BranchPresent.ShouldBeTrue();
    }

    // Review 9a0c7fb8 item 1: a protected name inside a disposable directory makes that directory
    // not wholly disposable, and `git worktree remove` cannot delete only part of a tree.
    [Test]
    public async Task C665_ProtectedNameInsideDisposableDirectoryRefuses()
    {
        using var f = new RemovalFixture();
        f.IgnoredPaths.AddRange(["server/bin-c665/appsettings.Development.json", "server/bin-c665/a.dll"]);
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("ignored_content_preserved");
        result.Detail.ShouldBe("protected: server/bin-c665/appsettings.Development.json");
        f.Mutations.ShouldBeEmpty();
        f.Retention.Calls.ShouldBeEmpty();
        f.InspectionCount.ShouldBe(1);
        f.BranchPresent.ShouldBeTrue();
    }

    // Review 9a0c7fb8 item 2: Git follows a junction when it lists and removes ignored content, so a
    // disposable directory swapped for a link between the readings would delete outside bytes.
    [Test]
    public async Task C665_JunctionSwappedInBetweenReadingsRefuses()
    {
        using var f = new RemovalFixture();
        var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "a.dll"), "outside bytes");
        var directory = Directory.CreateDirectory(Path.Combine(f.Source, "bin-x")).FullName;
        File.WriteAllText(Path.Combine(directory, "a.dll"), "build output");
        using var link = DirectoryLink.TryCreate(Path.Combine(f.Root, "staged-link"), outside);
        if (link is null) { Skip.Test("This host cannot create a directory junction or symbolic link."); return; }
        f.IgnoredPaths.Add("bin-x/a.dll");
        f.AfterInspection = count =>
        {
            if (count != 2) return;
            Directory.Delete(directory, recursive: true);
            link.MoveTo(directory);
        };
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("ignored_reparse_point");
        result.Detail.ShouldBe("reparse: bin-x");
        f.InspectionCount.ShouldBe(2);
        f.Mutations.ShouldBeEmpty("a link at the removal boundary must refuse before any deletion");
        File.ReadAllText(Path.Combine(outside, "a.dll")).ShouldBe("outside bytes");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C665_JunctionAtFirstReadingRefusesBeforeRetention()
    {
        using var f = new RemovalFixture();
        var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "run.trx"), "outside bytes");
        Directory.CreateDirectory(Path.Combine(f.Source, ".antiphon"));
        using var link = DirectoryLink.TryCreate(Path.Combine(f.Source, ".antiphon", "c665-checkpoints"), outside);
        if (link is null) { Skip.Test("This host cannot create a directory junction or symbolic link."); return; }
        f.IgnoredPaths.Add(".antiphon/c665-checkpoints/run.trx");
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("ignored_reparse_point");
        result.Detail.ShouldBe("reparse: .antiphon/c665-checkpoints");
        f.Retention.Calls.ShouldBeEmpty("evidence is never copied through a link");
        f.InspectionCount.ShouldBe(1);
        f.Mutations.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(outside, "run.trx")).ShouldBe("outside bytes");
    }

    // Review 5b79328d item 1: the tree is deleted by guarded removal itself, never through a link,
    // and Git only drops the registration of the absent directory. A link Git does not list is
    // removed as a link; its target and descendants stay. Review 0c0b9a4e item 3: this fixture's
    // path removal descends through links as Git for Windows does, so handing Git the populated
    // tree (the Round A deletion) empties the link's target and fails here.
    [Test]
    public async Task C665_UnlistedLinkIsRemovedWithoutTraversal()
    {
        using var f = new RemovalFixture();
        var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        File.WriteAllText(Path.Combine(outside, "a.dll"), "outside bytes");
        File.WriteAllText(Path.Combine(outside, "nested", "b.dll"), "outside nested bytes");
        Directory.CreateDirectory(Path.Combine(f.Source, "bin-x"));
        using var link = DirectoryLink.TryCreate(Path.Combine(f.Source, "bin-x", "link"), outside);
        if (link is null) { Skip.Test("This host cannot create a directory junction or symbolic link."); return; }
        var result = await f.RemoveAsync(gate: true);
        File.ReadAllText(Path.Combine(outside, "a.dll")).ShouldBe("outside bytes");
        File.ReadAllText(Path.Combine(outside, "nested", "b.dll")).ShouldBe("outside nested bytes");
        result.IsClean.ShouldBeTrue(result.Residue);
        Directory.Exists(f.Source).ShouldBeFalse();
        f.SetAsideTrees().ShouldBeEmpty("no set-aside copy of the tree remains");
        f.SetAsideRecords().ShouldBeEmpty("the finished removal leaves no record");
    }

    // A read-only file does not stop the removal (Git removes one too).
    [Test]
    public async Task C665_ReadOnlyFileIsRemovedWithTheTree()
    {
        using var f = new RemovalFixture();
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(f.Source, "obj")).FullName, "a.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        f.IgnoredPaths.Add("obj/a.json");
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeTrue(result.Residue);
        Directory.Exists(f.Source).ShouldBeFalse();
    }

    // A failed registration removal puts the tree back at its path, so a later pass (the CARD-0443
    // retry or a repost) inspects exactly the tree the refused pass saw.
    [Test]
    public async Task C665_FailedRegistrationRemovalRestoresTree()
    {
        using var f = new RemovalFixture();
        Directory.CreateDirectory(Path.Combine(f.Source, "bin-x"));
        File.WriteAllText(Path.Combine(f.Source, "bin-x", "a.dll"), "build output");
        f.IgnoredPaths.Add("bin-x/a.dll");
        f.RemoveExitCode = 128;
        var result = await f.RemoveAsync(gate: true);
        result.Residue.ShouldBe("worktree_remove_failed");
        f.Mutations.ShouldHaveSingleItem().ShouldBe(f.UnregisterVector);
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
        File.ReadAllText(Path.Combine(f.Source, "bin-x", "a.dll")).ShouldBe("build output");
        f.SetAsideTrees().ShouldBeEmpty("no set-aside copy of the tree remains");
        f.SetAsideRecords().ShouldBeEmpty("a restored tree leaves no record");
        f.BranchPresent.ShouldBeTrue();
    }

    // Review 0c0b9a4e item 1: a locked file stops the no-follow deletion after the registration is
    // gone and part of the set-aside tree is deleted. The later pass must finish that same tree;
    // an absent, unregistered path alone is not a completed cleanup.
    [Test]
    public async Task C665_LockedFileMidDeleteResumesOnLaterPass()
    {
        if (!OperatingSystem.IsWindows()) { Skip.Test("Sharing-mode locks are a Windows file-system behaviour."); return; }
        using var f = new RemovalFixture();
        File.WriteAllText(Path.Combine(f.Source, "zz-held.txt"), "held by a scanner");
        FileStream? held = null;
        f.BeforeUnregister = () =>
        {
            var aside = f.SetAsideTrees().ShouldHaveSingleItem();
            held = new FileStream(Path.Combine(aside, "zz-held.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        };
        WorktreeRemoval first;
        string aside;
        try
        {
            first = await f.RemoveAsync(gate: true);
            aside = f.SetAsideTrees().ShouldHaveSingleItem("the partial set-aside tree is still on disk");
            File.Exists(Path.Combine(aside, "keep.txt")).ShouldBeFalse("deletion had started before the lock stopped it");
        }
        finally { held?.Dispose(); }
        first.Residue.ShouldBe("worktree_removal_incomplete");
        first.Detail.ShouldBe("set-aside: " + aside);
        Directory.Exists(f.Source).ShouldBeFalse();
        f.Registered.ShouldBeFalse();
        f.BranchPresent.ShouldBeTrue("the branch waits for the directory");

        f.BeforeUnregister = null;
        var second = await f.RemoveAsync(gate: true);
        f.SetAsideTrees().ShouldBeEmpty("the later pass finishes the recorded set-aside tree");
        f.SetAsideRecords().ShouldBeEmpty();
        second.IsClean.ShouldBeTrue(second.Residue);
        f.BranchPresent.ShouldBeFalse();
    }

    // Review 0c0b9a4e item 2: once the tree has moved aside its path is free. Whatever occupies it
    // before the registration is dropped is not this tree: nothing is deleted through it, and the
    // set-aside tree stays whole with its registration.
    [Test]
    public async Task C665_PathRecreatedBeforeUnregistrationIsNeverTouched()
    {
        using var f = new RemovalFixture();
        var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName;
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        File.WriteAllText(Path.Combine(outside, "a.dll"), "outside bytes");
        File.WriteAllText(Path.Combine(outside, "nested", "b.dll"), "outside nested bytes");
        using var link = DirectoryLink.TryCreate(Path.Combine(f.Root, "staged-link"), outside);
        if (link is null) { Skip.Test("This host cannot create a directory junction or symbolic link."); return; }
        var recreated = false;
        f.BeforeUnregister = () =>
        {
            Directory.Exists(f.Source).ShouldBeFalse("the tree has moved aside");
            link.MoveTo(f.Source);
            recreated = true;
        };
        var result = await f.RemoveAsync(gate: true);
        recreated.ShouldBeTrue();
        File.ReadAllText(Path.Combine(outside, "a.dll")).ShouldBe("outside bytes");
        File.ReadAllText(Path.Combine(outside, "nested", "b.dll")).ShouldBe("outside nested bytes");
        File.GetAttributes(f.Source).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue("the new path is left as it was");
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("worktree_removal_incomplete");
        f.Registered.ShouldBeTrue("the registration belongs to the set-aside tree, which is kept");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();

        // A later pass sees both and refuses rather than choosing one.
        f.BeforeUnregister = null;
        var later = await f.RemoveAsync(gate: true);
        later.Residue.ShouldBe("set_aside_pending");
        File.ReadAllText(Path.Combine(outside, "a.dll")).ShouldBe("outside bytes");
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C665_PostDropQueryFailureKeepsSetAsideAndRecord(bool persistent)
    {
        using var f = new RemovalFixture();
        var queries = 0;
        f.BeforeRegistrationQuery = () =>
        {
            if (!f.Registered && (++queries == 1 || persistent)) throw new IOException("registration read unavailable");
        };
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeFalse();
        queries.ShouldBeGreaterThan(0);
        f.Registered.ShouldBeFalse();
        Directory.Exists(f.Source).ShouldBeFalse("a completed drop must never restore an unregistered source tree");
        File.ReadAllText(Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "keep.txt")).ShouldBe("private work");
        f.SetAsideRecords().ShouldHaveSingleItem();
        f.BranchPresent.ShouldBeTrue();
        f.BeforeRegistrationQuery = null;
        (await f.RemoveAsync(gate: true)).IsClean.ShouldBeTrue();
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.BranchPresent.ShouldBeFalse();
    }

    [Test]
    [Arguments("io")]
    [Arguments("timeout")]
    [Arguments("cancel")]
    public async Task C665_FaultAfterActualDropKeepsRecoverableTree(string fault)
    {
        using var f = new RemovalFixture();
        f.AfterUnregister = () => throw fault switch
        {
            "io" => new IOException("lost drop acknowledgement"),
            "timeout" => new TimeoutException("lost drop acknowledgement"),
            _ => new OperationCanceledException("lost drop acknowledgement"),
        };
        if (fault == "cancel") await Should.ThrowAsync<OperationCanceledException>(() => f.RemoveAsync(gate: true));
        else (await f.RemoveAsync(gate: true)).IsClean.ShouldBeFalse();
        f.Registered.ShouldBeFalse();
        Directory.Exists(f.Source).ShouldBeFalse();
        File.ReadAllText(Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "keep.txt")).ShouldBe("private work");
        f.SetAsideRecords().ShouldHaveSingleItem();
        f.BranchPresent.ShouldBeTrue();
        f.AfterUnregister = null;
        (await f.RemoveAsync(gate: true)).IsClean.ShouldBeTrue();
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
    }

    [Test]
    public async Task C665_UnknownRegistrationAfterFaultKeepsLiveTreeSetAside()
    {
        using var f = new RemovalFixture();
        f.BeforeUnregister = () => throw new IOException("drop outcome unavailable");
        f.BeforeRegistrationQuery = () =>
        {
            if (!Directory.Exists(f.Source)) throw new IOException("registration unavailable");
        };
        (await f.RemoveAsync(gate: true)).IsClean.ShouldBeFalse();
        f.Registered.ShouldBeTrue();
        Directory.Exists(f.Source).ShouldBeFalse("an unknown outcome cannot authorize restoration");
        f.SetAsideRecords().ShouldHaveSingleItem();
        File.ReadAllText(Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "keep.txt")).ShouldBe("private work");
        f.BeforeUnregister = null;
        f.BeforeRegistrationQuery = null;
        (await f.RemoveAsync(gate: true)).IsClean.ShouldBeTrue();
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
    }

    private sealed class RecordingRetention(RemovalFixture fixture) : IWorktreeEvidenceRetention
    {
        public List<(int Evidence, int AtInspection)> Calls { get; } = [];
        public List<string[]> Paths { get; } = [];
        public string? Refusal { get; set; }
        public Task<WorktreeEvidenceRetentionResult> RetainAsync(AgentTask task, string worktreePath,
            ImmutableArray<string> relativePaths, Guid? attemptId, CancellationToken ct)
        {
            Calls.Add((relativePaths.Length, fixture.InspectionCount));
            if (relativePaths.Length != 0) Paths.Add([.. relativePaths]);
            var root = Path.Combine(fixture.Root, "retained");
            return Task.FromResult(Refusal is not null ? new WorktreeEvidenceRetentionResult(null, [], Refusal)
                : new WorktreeEvidenceRetentionResult(root, [.. relativePaths.Select(p =>
                    new RetainedWorktreeFile(p, Path.Combine(root, p), 1, new string('0', 64)))], null));
        }
    }

    private sealed class RemovalFixture : ILandingGit, IRepositoryMutationLease, IWorktreeRemovalEvidence, IDisposable
    {
        public static readonly string Sha = new('a', 40);
        public static readonly string Other = new('b', 40);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c448-removal-policy-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public AgentTaskLanding Operation { get; }
        public WorktreeRemovalRequest Request { get; set; }
        public string? LocalChange { get; set; }
        public List<string[]> Mutations { get; } = [];
        public bool BranchPresent { get; set; } = true;
        public bool ValidLease { get; set; } = true;
        public Action<int>? AfterInspection { get; set; }
        public int InspectionCount { get; private set; }
        public string InspectedSha { get; set; } = Sha;
        public string? IgnoredPath { get; set; }
        public List<string> IgnoredPaths { get; } = [];
        public int RemoveExitCode { get; set; }
        public bool Registered { get; set; } = true;
        /// <summary>Runs between the move-aside and the registration drop.</summary>
        public Action? BeforeUnregister { get; set; }
        public Action? AfterUnregister { get; set; }
        public Action? BeforeRegistrationQuery { get; set; }
        public string[] UnregisterVector => ["worktree", "remove", "--registration-only", Source];
        public string[] SetAsideTrees() => Directory.GetDirectories(Root, ".source.removing-*");
        public string[] SetAsideRecords()
        {
            var records = Path.Combine(Root, "antiphon", "worktree-removal");
            return Directory.Exists(records) ? Directory.GetFileSystemEntries(records) : [];
        }
        public RecordingRetention Retention { get; }
        public RemovalFixture()
        {
            Retention = new(this);
            Directory.CreateDirectory(Source);
            File.WriteAllText(Path.Combine(Source, "keep.txt"), "private work");
            Operation = new() { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Active = true,
                SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
                RepositoryPath = Root, CommonDirectory = Root, WorktreePath = Source, GitDirectory = Path.Combine(Root, "admin"),
                OriginalSourceSha = Sha, VerifiedSourceSha = Sha, TargetBeforeSha = Sha, ObservedRemoteTargetSha = Sha,
                ExpectedDeletionSha = Sha, SourcePinned = true, TargetPinned = true, VerificationPassed = true,
                RemoteFingerprint = new string('c', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
                CleanupStartedAt = DateTime.UtcNow, Publication = LandPublicationOutcome.Landed,
                Phase = LandPhase.CleanupStarted, ConfirmationMethod = "push-endpoint-read-fetch-ancestry" };
            Operation.RecoveryRefPrefix = $"refs/antiphon/land/{Operation.TaskId:N}/{Operation.Id:N}";
            Request = new(WorktreeRemovalPurpose.Publication,
                new(Operation.TaskId, Root, Source, Operation.SourceFullRef, Operation.TargetFullRef), Root,
                Operation.GitDirectory, Sha, Sha, Operation.Id, new Lease(Root));
        }
        public Task<WorktreeRemoval> RemoveAsync(bool gate = false) => (gate
            ? new GuardedWorktreeRemoval(this, this, this, null, new WorktreeIgnoredContentGate(
                new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings())), Retention, this))
            : new GuardedWorktreeRemoval(this, this, this)).RemoveAsync(Request, default);
        public Task<AgentTask?> ReadTaskAsync(Guid taskId, CancellationToken ct) =>
            Task.FromResult<AgentTask?>(new AgentTask { Id = taskId });
        public Task<AgentTaskLanding?> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult<AgentTaskLanding?>(Operation);
        public bool Owns(RepositoryLease lease, string commonDirectory) => ValidLease && ReferenceEquals(lease, Request.Lease) && commonDirectory == Root;
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) => Task.FromResult<RepositoryLease?>(Request.Lease);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(Root);
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct)
            => Task.FromResult(new LandingRemoteObservation(Sha, true, null));
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct)
            => Task.FromResult(new LandingSourceObservation(Sha, observationPrefix + "/pin", new string('c', 64), null));
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct)
        {
            InspectionCount++;
            AfterInspection?.Invoke(InspectionCount);
            return Task.FromResult(new LandSourceInspection(new(coordinates, Root, Source, Request.GitDirectory,
                coordinates.SourceFullRef, InspectedSha, InspectedSha, "",
                IgnoredPath is null ? [.. IgnoredPaths] : [.. IgnoredPaths, IgnoredPath]), null));
        }
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            LandingGitResult result;
            if (args[0] == "worktree" && args[1] == "list")
            {
                BeforeRegistrationQuery?.Invoke();
                result = new(0, Registered
                    ? $"worktree {Source}\0HEAD {Sha}\0branch {Request.Source.SourceFullRef}\0\0"
                    : $"worktree {Root}\0HEAD {Sha}\0branch refs/heads/master\0\0", "");
            }
            else if (args[0] == "worktree" && args[1] == "remove")
            {
                // Retained so a regression to `git worktree remove <path>` is caught: like Git for
                // Windows it deletes whatever is at the path, descending through a directory link.
                Mutations.Add(args.ToArray());
                BeforeUnregister?.Invoke();
                if (RemoveExitCode == 0)
                {
                    if (Directory.Exists(Source)) DeleteFollowingLinks(Source);
                    Registered = false;
                }
                result = new(RemoveExitCode, "", "");
            }
            else if (args[0] == "update-ref" && args.Contains("-d"))
            { Mutations.Add(args.ToArray()); BranchPresent = false; result = new(0, "", ""); }
            else if (args[0] == "show-ref" && args.Contains("--exists")) result = new(BranchPresent ? 0 : 2, "", "");
            else if (args[0] == "show-ref")
                result = new(0, args[^1].EndsWith("/target-before", StringComparison.Ordinal) ? Operation.TargetBeforeSha
                    : args[^1].EndsWith("/prepared", StringComparison.Ordinal) ? Operation.RebasedSourceSha! : Sha, "");
            else if (args[0] == "symbolic-ref") result = args[^1] == "HEAD"
                ? new(0, LocalChange == "symbolic" ? "refs/heads/other" : Request.Source.TargetFullRef, "")
                : new(LocalChange == "target-symbolic" ? 0 : 1, "", "");
            else if (args[0] == "rev-parse") result = new(0,
                LocalChange == "head" && args.Contains("HEAD^{commit}")
                    || LocalChange == "target" && args.Contains(Request.Source.TargetFullRef + "^{commit}") ? Other : Sha, "");
            else if (args[0] == "status") result = new(LocalChange == "status-error" ? 128 : 0,
                LocalChange == "dirty" ? " M keep.txt\0" : "", "");
            else if (args[0] == "merge-base") result = new(LocalChange == "ancestry" ? 1 : 0, "", "");
            else throw new InvalidOperationException("Unexpected cleanup command: " + args[0]);
            return Task.FromResult(result);
        }
        public Task<LandingGitResult> UnregisterWorktreeAsync(string repository, string worktreePath, string gitDirectory,
            CancellationToken ct)
        {
            Mutations.Add(UnregisterVector);
            BeforeUnregister?.Invoke();
            if (RemoveExitCode != 0) return Task.FromResult(new LandingGitResult(RemoveExitCode, "", "controlled_failure"));
            // Like production: a path that is occupied again is not this registration's tree.
            if (Directory.Exists(worktreePath) || File.Exists(worktreePath))
                return Task.FromResult(new LandingGitResult(1, "", "worktree_path_recreated"));
            Registered = false;
            AfterUnregister?.Invoke();
            return Task.FromResult(new LandingGitResult(0, "", ""));
        }
        private static void DeleteFollowingLinks(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            foreach (var child in Directory.EnumerateDirectories(directory)) DeleteFollowingLinks(child);
            Directory.Delete(directory);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
        private sealed class Lease(string common) : RepositoryLease
        { public override string CommonDirectory => common; public override ValueTask DisposeAsync() => ValueTask.CompletedTask; }
        public Task<LandingGitResult> RunOwnedAsync(string r, IReadOnlyList<string> a, Func<int,long,CancellationToken,Task> s, CancellationToken c) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int p, long s, CancellationToken c) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string p, CancellationToken c) => Task.FromResult(p);
        public Task<bool> HasActiveSequencerAsync(string r, CancellationToken c) => Task.FromResult(LocalChange == "sequencer");
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string r, CancellationToken c)
            => Task.FromResult<IReadOnlyList<LandingRegistration>>([new(LocalChange == "checkout" ? Path.Combine(Root, "other") : Root,
                Request.Source.TargetFullRef, Sha, false, false)]);
        public Task<LandingDestination> DestinationAsync(string r, string t, CancellationToken c) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct)
            => Task.FromResult(new LandingIndexLockObservation("", false, null, null, [], null));
        public Task<LandingGitResult> PinAsync(string r,string p,string s,CancellationToken c) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string r,LandingDestination d,string s,CancellationToken c) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string r,LandingDestination d,string s,Func<int,long,CancellationToken,Task> a,CancellationToken c) => throw new NotSupportedException();
    }
}
