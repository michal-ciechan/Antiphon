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
using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// Each crossed coordinate remains otherwise valid. A mutation-capable downstream
// fake exposes the first unauthorized deletion even when real Git has another guard.
[Category("Integration")]
public sealed class LandingRemovalPolicyControlTests
{
    [Test]
    [Timeout(10_000)]
    public async Task C1737_MatchingContentReplacementIsNotDeleted()
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "run");
        using var f = new RemovalFixture();
        const string evidence = ".antiphon/checkpoint-note.md";
        Directory.CreateDirectory(Path.Combine(f.Source, ".antiphon"));
        File.WriteAllText(Path.Combine(f.Source, evidence), "approved checkpoint evidence\n");
        f.IgnoredPaths.Add(evidence);
        f.AfterUnregister = () => throw new IOException("c1737 cut after unregister");

        (await f.RemoveAsync(gate: true)).Residue.ShouldBe("worktree_removal_incomplete");
        Directory.Exists(f.Source).ShouldBeFalse();
        f.Registered.ShouldBeFalse();
        var receipt = WorktreeSetAside.Read(f.Root, f.Source).ShouldNotBeNull();
        receipt.DirectoryIdentity.ShouldNotBeNullOrEmpty();
        receipt.EntryManifest.ShouldNotBeNullOrEmpty();
        var entries = JsonSerializer.Deserialize<WorktreeApprovedEntry[]>(receipt.EntryManifest!)!;
        entries.Select(e => e.Path).ShouldBe([".antiphon", evidence, "keep.txt"]);
        var pin = JsonSerializer.Deserialize<EvidencePin[]>(receipt.EvidenceManifest!)!.ShouldHaveSingleItem();
        pin.Relative.ShouldBe(evidence);
        var approved = entries.Where(e => e.Kind == "file").ToDictionary(e => e.Path,
            e => File.ReadAllBytes(Path.Combine(receipt.SetAsidePath, e.Path)), StringComparer.Ordinal);
        approved.Keys.Order(StringComparer.Ordinal).ShouldBe(new[] { evidence, "keep.txt" });
        var retained = File.ReadAllBytes(pin.RetainedPath);
        Convert.ToHexStringLower(SHA256.HashData(retained)).ShouldBe(pin.Sha256);
        retained.ShouldBe(approved[evidence]);

        f.AfterUnregister = null;
        var rescue = Path.Combine(f.Root, "rescued-original");
        Directory.Move(receipt.SetAsidePath, rescue);
        Directory.CreateDirectory(receipt.SetAsidePath);
        foreach (var entry in entries.Where(e => e.Kind == "dir"))
            Directory.CreateDirectory(Path.Combine(receipt.SetAsidePath, entry.Path));
        foreach (var (relative, bytes) in approved)
            File.WriteAllBytes(Path.Combine(receipt.SetAsidePath, relative), bytes);

        void AssertApproved(string tree)
        {
            var paths = Directory.EnumerateFileSystemEntries(tree, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(tree, p).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal).ToArray();
            paths.ShouldBe(entries.Select(e => e.Path));
            foreach (var entry in entries)
            {
                var path = Path.Combine(tree, entry.Path);
                File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint).ShouldBeFalse();
                Directory.Exists(path).ShouldBe(entry.Kind == "dir");
                if (entry.Kind != "file") continue;
                var bytes = File.ReadAllBytes(path);
                bytes.ShouldBe(approved[entry.Path]);
                Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(entry.Hash);
            }
        }

        var replacementIdentity = WorktreeNoFollowDelete.DirectoryIdentity(receipt.SetAsidePath);
        replacementIdentity.ShouldNotBe(receipt.DirectoryIdentity);
        WorktreeNoFollowDelete.DirectoryIdentity(rescue).ShouldBe(receipt.DirectoryIdentity);
        AssertApproved(receipt.SetAsidePath);
        AssertApproved(rescue);
        Directory.Exists(f.Source).ShouldBeFalse();
        f.Registered.ShouldBeFalse();
        Console.WriteLine("reached:W-U9:pending-unregistered-equal-content");

        var result = await f.RemoveAsync(gate: true);
        Directory.Exists(receipt.SetAsidePath).ShouldBeTrue("preserved:W-U9:replacement-directory");
        WorktreeNoFollowDelete.DirectoryIdentity(receipt.SetAsidePath).ShouldBe(replacementIdentity);
        WorktreeNoFollowDelete.DirectoryIdentity(rescue).ShouldBe(receipt.DirectoryIdentity);
        AssertApproved(receipt.SetAsidePath);
        AssertApproved(rescue);
        File.ReadAllBytes(pin.RetainedPath).ShouldBe(retained);
        f.BranchPresent.ShouldBeTrue();
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("set_aside_identity_mismatch");
        WorktreeSetAside.Read(f.Root, f.Source).ShouldBe(receipt);
    }

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
        // CARD-0688 D-4/D-5: the target decision is the canonical checkout step after publication. It reads HEAD
        // files (no git) and then the main checkout's lock, sequencer and status; each changed component is a
        // named residue, never a refusal. HEAD/target movement is the fast-forward's ancestry concern, not the
        // decision's, and a HEAD file naming another branch means the target is checked out nowhere.
        var common = Path.Combine(f.Root, ".git");
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(common, "HEAD"), change == "symbolic" ? "ref: refs/heads/other\n" : "ref: refs/heads/master\n");
        if (change == "checkout")
        {
            var linked = Path.Combine(common, "worktrees", "other");
            Directory.CreateDirectory(linked);
            File.WriteAllText(Path.Combine(linked, "HEAD"), "ref: refs/heads/master\n");
            File.WriteAllText(Path.Combine(linked, "gitdir"), Path.Combine(f.Root, "other", ".git") + "\n");
        }
        f.Operation.CommonDirectory = common;
        var protocol = new AgentTaskLandingProtocol(null!, f, f, null!, null!, TimeProvider.System);
        // Exercise the existing decision directly without adding a production API for tests.
        // Real-Git boundary cases separately assert the target index, files and remote state.
        var method = typeof(AgentTaskLandingProtocol).GetMethod("CanonicalDecisionAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var decision = (Task)method.Invoke(protocol, [f.Operation, CancellationToken.None])!;
        await decision;
        var (reason, checkout) = ((string?, string?))decision.GetType().GetProperty("Result")!.GetValue(decision)!;
        reason.ShouldBe(change switch
        {
            "dirty" or "status-error" => "canonical_checkout_dirty",
            "sequencer" => "canonical_active_sequencer",
            "checkout" => "target_checked_out_elsewhere",
            _ => null,
        }, "each changed component decides independently: " + change);
        checkout.ShouldBe(change switch
        {
            "symbolic" => null,
            "checkout" => Path.Combine(f.Root, "other"),
            _ => f.Root,
        });
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
            // The residue names the refusing step: on a loaded Windows host a scanner briefly holding the
            // fixture's fresh files can refuse the set-aside rename, record write or delete (fail closed;
            // each is retried, bounded, and a lasting delete refusal names its Win32 code).
            using var safe = new RemovalFixture();
            var baseline = await safe.RemoveAsync();
            baseline.IsClean.ShouldBeTrue($"the unchanged command path must be reachable: {baseline.Residue} {baseline.Detail}");
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
            result.IsClean.ShouldBeTrue($"{result.Residue} {result.Detail}");
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
        // The fixture's inspection is already clean. The held file is part of that tree, so the
        // post-proof census must see it as tracked rather than as a file written after the proof.
        f.TrackedListing = "keep.txt\0zz-held.txt\0";
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
        // C448 V36: the lasting sharing violation at the held file's no-follow open (ERROR_SHARING_VIOLATION)
        // outlives the bounded retry and is named in the residue.
        first.Detail.ShouldBe("set-aside: " + aside + "; delete_error_32");
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

    // CARD-0721: a registration list that still names the path after the administrative entry
    // is gone is stale. Restoring from it leaves an unregistered directory and no set-aside record.
    [Test]
    public async Task C721_StaleRegistrationListAfterUnregisterDoesNotRestoreAnOrphan()
    {
        using var f = new RemovalFixture();
        // Every list after the drop still names the source, as a worktree-list cache that
        // file-I/O unregister does not invalidate.
        f.StaleListsAfterDrop = 8;
        var result = await f.RemoveAsync(gate: true);
        Directory.Exists(f.Source).ShouldBeFalse("a stale list must not put an unregistered tree back at its path");
        f.Registered.ShouldBeFalse();
        f.SetAsideTrees().ShouldBeEmpty("the dropped tree's bytes are deleted, not restored");
        f.SetAsideRecords().ShouldBeEmpty();
        result.Unregistered.ShouldBeTrue();
        result.DirectoryGone.ShouldBeTrue();
        result.IsClean.ShouldBeTrue(result.Residue);
    }

    // A handle held inside the tree refuses the set-aside rename. The registration stays, so a
    // later pass can retry. An unregistered directory with no set-aside record is the failure.
    [Test]
    public async Task C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded()
    {
        if (!OperatingSystem.IsWindows()) { Skip.Test("Sharing-mode locks are a Windows file-system behaviour."); return; }
        using var f = new RemovalFixture();
        await using var held = new FileStream(Path.Combine(f.Source, "keep.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await f.RemoveAsync(gate: true);
        var unregisteredOrphan = Directory.Exists(f.Source) && !f.Registered && f.SetAsideRecords().Length == 0;
        unregisteredOrphan.ShouldBeFalse(result.Residue);
        (result.IsClean || f.Registered || f.SetAsideRecords().Length > 0).ShouldBeTrue(result.Residue);
        if (!result.IsClean) Directory.Exists(f.Source).ShouldBe(f.Registered);
        if (Directory.Exists(f.Source))
            File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
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

    // A scanner or indexer that briefly holds a fresh file refuses the set-aside renames at once on
    // Windows (rc run 766e3df5, V36 'operation' baseline). Linux cannot raise a sharing violation, so
    // the rename seam throws the exception Windows would.
    private static Exception Transient(string kind) => kind switch
    {
        "sharing" => new IOException("held by a scanner", unchecked((int)0x80070020)),
        "lock" => new IOException("range locked", unchecked((int)0x80070021)),
        "denied-io" => new IOException("directory rename denied", unchecked((int)0x80070005)),
        _ => new UnauthorizedAccessException("file replace denied"),
    };

    private static Exception Lasting(string kind) => kind switch
    {
        "not-found" => new DirectoryNotFoundException("path not found"),
        "not-empty" => new IOException("directory not empty", unchecked((int)0x80070091)),
        "exists" => new IOException("destination exists", unchecked((int)0x800700B7)),
        _ => new IOException("not a directory", unchecked((int)0x8007010B)),
    };

    [Test]
    [Arguments("move", "sharing")]
    [Arguments("move", "lock")]
    [Arguments("move", "denied-io")]
    [Arguments("move", "denied")]
    [Arguments("record", "sharing")]
    [Arguments("record", "lock")]
    [Arguments("record", "denied-io")]
    [Arguments("record", "denied")]
    public async Task C448_V36_TransientSharingOnSetAsideRenameIsRetriedToAClean(string rename, string kind)
    {
        using var f = new RemovalFixture();
        f.FailRename(rename, _ => Transient(kind), failures: 1);
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeTrue($"{rename}/{kind}: {result.Residue} {result.Detail}");
        f.RenameCalls(rename).ShouldBe(2, "one refused rename, then the retried one");
        f.Pauses.ShouldBe([WorktreeRenames.SharingPause]);
        f.Mutations.Count.ShouldBe(2);
        Directory.Exists(f.Source).ShouldBeFalse();
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
    }

    [Test]
    [Arguments("move", "worktree_remove_failed")]
    [Arguments("record", "cleanup_inspection_error")]
    public async Task C448_V36_LastingSharingOnSetAsideRenameStillRefuses(string rename, string residue)
    {
        using var f = new RemovalFixture();
        f.FailRename(rename, _ => Transient("sharing"));
        var pass = await f.RemoveDirectoryAsync();
        pass.Result.Residue.ShouldBe(residue);
        pass.Result.Detail.ShouldBeNull();
        if (rename == "move") pass.Outcome.ShouldNotBeNull().GeneratedCode.ShouldBe("move_error_32");
        else pass.Outcome.ShouldBeNull();
        f.RenameCalls(rename).ShouldBe(WorktreeRenames.SharingAttempts, "the retry is bounded");
        f.Pauses.Count.ShouldBe(WorktreeRenames.SharingAttempts - 1);
        f.Pauses.ShouldAllBe(p => p == WorktreeRenames.SharingPause);
        f.RenameCalls(rename == "move" ? "record" : "move").ShouldBe(rename == "move" ? 1 : 0);
        f.Mutations.ShouldBeEmpty("a refused rename never reaches a destructive request");
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
        f.SetAsideTrees().ShouldBeEmpty();
        WorktreeSetAside.Read(f.Root, f.Source).ShouldBeNull("nothing moved, so no record survives");
        f.BranchPresent.ShouldBeTrue();
    }

    [Test]
    [Arguments("move", "not-found", "move_error_3")]
    [Arguments("move", "not-empty", "move_error_145")]
    [Arguments("move", "exists", "move_error_183")]
    [Arguments("move", "not-directory", "move_error_267")]
    [Arguments("record", "not-found", null)]
    [Arguments("record", "not-empty", null)]
    [Arguments("record", "exists", null)]
    [Arguments("record", "not-directory", null)]
    public async Task C448_V36_LastingRenameErrorIsNeverRetried(string rename, string kind, string? code)
    {
        using var f = new RemovalFixture();
        f.FailRename(rename, _ => Lasting(kind));
        var pass = await f.RemoveDirectoryAsync();
        pass.Result.Residue.ShouldBe(rename == "move" ? "worktree_remove_failed" : "cleanup_inspection_error");
        pass.Outcome?.GeneratedCode.ShouldBe(code);
        f.RenameCalls(rename).ShouldBe(1, "a non-transient error is the refusal");
        f.Pauses.ShouldBeEmpty();
        f.Mutations.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe("private work");
    }

    [Test]
    [Arguments("move", "sharing")]
    [Arguments("move", "denied")]
    [Arguments("record", "sharing")]
    [Arguments("record", "denied")]
    public async Task C448_V36_SharingCodesAreOnlyTransientOnWindows(string rename, string kind)
    {
        // Off Windows an HResult carries errno, and access denied is a lasting permission.
        using var f = new RemovalFixture { Windows = false };
        f.FailRename(rename, _ => Transient(kind), failures: 1);
        var pass = await f.RemoveDirectoryAsync();
        pass.Result.Residue.ShouldBe(rename == "move" ? "worktree_remove_failed" : "cleanup_inspection_error");
        f.RenameCalls(rename).ShouldBe(1);
        f.Pauses.ShouldBeEmpty();
        f.Mutations.ShouldBeEmpty();
        WorktreeRenames.System.Windows.ShouldBe(OperatingSystem.IsWindows());
    }

    // C448 V36 (Windows qualification b19a9880, 'verified-sha' baseline): the move aside succeeded and
    // the set-aside tree's delete was refused at once, in 49 ms. A scanner or indexer holding a fresh
    // entry refuses it with a raw Win32 code (the no-follow walk) or a 0x8007xxxx HResult (.NET's own
    // I/O); the whole walk is retried, bounded, and only on Windows. The seam throws what Windows would.
    private static Exception DeleteRefusal(string kind) => kind switch
    {
        "dir-not-empty" => new IOException("no_follow_delete_failed: directory not empty", 145),
        "denied" => new IOException("no_follow_delete_failed: access denied", 5),
        "sharing" => new IOException("no_follow_open_failed: sharing violation", 32),
        "lock" => new IOException("no_follow_open_failed: lock violation", 33),
        "denied-hresult" => new UnauthorizedAccessException("access to the path is denied"),
        "sharing-hresult" => new IOException("being used by another process", unchecked((int)0x80070020)),
        "not-empty-hresult" => new IOException("directory not empty", unchecked((int)0x80070091)),
        "disk-full" => new IOException("no_follow_delete_failed: disk full", 112),
        "disk-full-hresult" => new IOException("disk full", unchecked((int)0x80070070)),
        "not-found" => new DirectoryNotFoundException("path not found"),
        _ => new IOException("no_follow_delete_failed: invalid name", 123),
    };

    [Test]
    [Arguments("dir-not-empty")]
    [Arguments("denied")]
    [Arguments("sharing")]
    [Arguments("lock")]
    [Arguments("denied-hresult")]
    [Arguments("sharing-hresult")]
    [Arguments("not-empty-hresult")]
    public async Task C448_V36_TransientRefusalOfSetAsideDeleteIsRetriedToAClean(string kind)
    {
        using var f = new RemovalFixture();
        f.FailDelete(_ => DeleteRefusal(kind), failures: 1);
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeTrue($"{kind}: {result.Residue} {result.Detail}");
        f.DeleteCalls.ShouldBe(2, "one refused delete, then the retried walk");
        f.Pauses.ShouldBe([WorktreeRenames.SharingPause]);
        f.Mutations.Count.ShouldBe(2);
        Directory.Exists(f.Source).ShouldBeFalse();
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.BranchPresent.ShouldBeFalse();
    }

    [Test]
    [Arguments("dir-not-empty", 145)]
    [Arguments("sharing", 32)]
    [Arguments("lock", 33)]
    [Arguments("denied-hresult", 5)]
    public async Task C448_V36_LastingRefusalOfSetAsideDeleteKeepsTheRecordForTheNextPass(string kind, int code)
    {
        using var f = new RemovalFixture();
        f.FailDelete(_ => DeleteRefusal(kind));
        var first = await f.RemoveAsync();
        var aside = f.SetAsideTrees().ShouldHaveSingleItem("the set-aside tree is kept for the next pass");
        first.Residue.ShouldBe("worktree_removal_incomplete");
        first.Detail.ShouldBe($"set-aside: {aside}; delete_error_{code}", "the refusal names its Win32 code");
        f.DeleteCalls.ShouldBe(WorktreeRenames.SharingAttempts, "the retry is bounded");
        f.Pauses.Count.ShouldBe(WorktreeRenames.SharingAttempts - 1);
        f.Pauses.ShouldAllBe(p => p == WorktreeRenames.SharingPause);
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work", "nothing is forced");
        WorktreeSetAside.Read(f.Root, f.Source).ShouldNotBeNull().SetAsidePath.ShouldBe(aside);
        f.Mutations.ShouldHaveSingleItem().ShouldBe(f.UnregisterVector);
        f.Registered.ShouldBeFalse();
        f.BranchPresent.ShouldBeTrue("the branch waits for the directory");

        // The product's resume path: a later pass finishes the recorded tree.
        f.HealDelete();
        var second = await f.RemoveAsync();
        second.IsClean.ShouldBeTrue($"{second.Residue} {second.Detail}");
        f.DeleteCalls.ShouldBe(WorktreeRenames.SharingAttempts + 1, "the resumed delete runs once");
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.Mutations.Count.ShouldBe(2);
        f.BranchPresent.ShouldBeFalse();
    }

    [Test]
    [Arguments("disk-full", 112)]
    [Arguments("disk-full-hresult", 112)]
    [Arguments("not-found", 3)]
    [Arguments("invalid-name", 123)]
    public async Task C448_V36_LastingDeleteErrorIsNeverRetried(string kind, int code)
    {
        using var f = new RemovalFixture();
        f.FailDelete(_ => DeleteRefusal(kind));
        var pass = await f.RemoveDirectoryAsync();
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        pass.Result.Residue.ShouldBe("worktree_removal_incomplete");
        pass.Result.Detail.ShouldBe($"set-aside: {aside}; delete_error_{code}");
        f.DeleteCalls.ShouldBe(1, "a non-transient error is the refusal");
        f.Pauses.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
        f.SetAsideRecords().ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("dir-not-empty", 145)]
    [Arguments("sharing", 32)]
    [Arguments("denied-hresult", 5)]
    public async Task C448_V36_DeleteRefusalIsOnlyTransientOnWindows(string kind, int code)
    {
        // Off Windows an HResult carries errno, and access denied is a lasting permission.
        using var f = new RemovalFixture { Windows = false };
        f.FailDelete(_ => DeleteRefusal(kind), failures: 1);
        var pass = await f.RemoveDirectoryAsync();
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        pass.Result.Residue.ShouldBe("worktree_removal_incomplete");
        pass.Result.Detail.ShouldBe($"set-aside: {aside}; delete_error_{code}");
        f.DeleteCalls.ShouldBe(1);
        f.Pauses.ShouldBeEmpty();
    }

    // A delete that returns can leave the name visible: with the classic disposition an entry stays
    // delete-pending while another handle (a scanner, a directory watcher) is open on it.
    [Test]
    public async Task C448_V36_SetAsideTreeStillVisibleAfterDeleteIsWaitedOut()
    {
        using var f = new RemovalFixture();
        f.FailDelete(_ => null, failures: 1);
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeTrue($"{result.Residue} {result.Detail}");
        f.DeleteCalls.ShouldBe(2);
        f.Pauses.ShouldBe([WorktreeRenames.SharingPause]);
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.Mutations.Count.ShouldBe(2);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C448_V36_SetAsideTreeLastingVisibleAfterDeleteStillRefuses(bool windows)
    {
        using var f = new RemovalFixture { Windows = windows };
        f.FailDelete(_ => null);
        var pass = await f.RemoveDirectoryAsync();
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        pass.Result.Residue.ShouldBe("worktree_removal_incomplete");
        pass.Result.Detail.ShouldBe("set-aside: " + aside, "the delete raised nothing, so no code is named");
        f.DeleteCalls.ShouldBe(windows ? WorktreeRenames.SharingAttempts : 1, "the wait is bounded and Windows-only");
        f.Pauses.Count.ShouldBe(windows ? WorktreeRenames.SharingAttempts - 1 : 0);
        f.Pauses.ShouldAllBe(p => p == WorktreeRenames.SharingPause);
        f.SetAsideRecords().ShouldHaveSingleItem();
    }

    // The resume of a recorded set-aside tree is the second delete site; it retries the same way.
    [Test]
    [Arguments(1)]
    [Arguments(int.MaxValue)]
    public async Task C448_V36_ResumedSetAsideDeleteIsRetriedToo(int failures)
    {
        using var f = new RemovalFixture();
        f.FailDelete(_ => DeleteRefusal("dir-not-empty"));
        (await f.RemoveAsync()).Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        f.FailDelete(_ => DeleteRefusal("sharing"), failures);
        var second = await f.RemoveAsync();
        if (failures == 1)
        {
            second.IsClean.ShouldBeTrue($"{second.Residue} {second.Detail}");
            f.DeleteCalls.ShouldBe(2);
            f.Pauses.ShouldBe([WorktreeRenames.SharingPause]);
            f.SetAsideTrees().ShouldBeEmpty();
            f.SetAsideRecords().ShouldBeEmpty();
            f.BranchPresent.ShouldBeFalse();
        }
        else
        {
            second.Residue.ShouldBe("worktree_removal_incomplete");
            second.Detail.ShouldBe($"set-aside: {aside}; delete_error_32");
            f.DeleteCalls.ShouldBe(WorktreeRenames.SharingAttempts);
            f.Pauses.Count.ShouldBe(WorktreeRenames.SharingAttempts - 1);
            File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
            f.SetAsideRecords().ShouldHaveSingleItem();
            f.BranchPresent.ShouldBeTrue();
        }
    }

    // A delete-pending tree can finish going during the pause. Walking it again would open a missing
    // root (Win32 code 3, not transient) and refuse a removal that is already done.
    [Test]
    [Arguments("sharing")]
    [Arguments("visible")]
    public async Task C448_V36_SetAsideTreeGoneAfterThePauseIsClean(string first)
    {
        using var f = new RemovalFixture();
        f.FailDelete(call => call == 1 && first == "sharing" ? DeleteRefusal("sharing")
            : call == 1 ? null : DeleteRefusal("not-found"));
        f.OnPause = () => Directory.Delete(f.SetAsideTrees().ShouldHaveSingleItem(), recursive: true);
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeTrue($"{first}: {result.Residue} {result.Detail}");
        f.DeleteCalls.ShouldBe(1, "a tree gone after the pause is not walked again");
        f.Pauses.ShouldBe([WorktreeRenames.SharingPause]);
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.Mutations.Count.ShouldBe(2);
        f.BranchPresent.ShouldBeFalse();
    }

    // A nested repository is preserved on purpose: it is never a transient refusal. It appears in the
    // set-aside tree after the last reading, so only the delete's own census finds it.
    [Test]
    public async Task C448_V36_PreservedNestedRepositoryIsNeverRetried()
    {
        using var f = new RemovalFixture();
        f.BeforeUnregister = () =>
        {
            var nested = Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "vendor", "lib");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: elsewhere\n");
        };
        f.HealDelete();
        var pass = await f.RemoveDirectoryAsync();
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        pass.Result.Residue.ShouldBe("ignored_content_preserved");
        f.DeleteCalls.ShouldBe(1);
        f.Pauses.ShouldBeEmpty();
        File.Exists(Path.Combine(aside, "vendor", "lib", ".git")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
    }

    // CARD-1717 D-4: a retry must not delete bytes added under the recorded set-aside tree.
    [Test]
    public async Task C1717_D4_RetryKeepsBytesAddedAfterThePartialRelease()
    {
        using var f = new RemovalFixture();
        f.AfterUnregister = () => throw new IOException("audit cut after real unregister");
        var first = await f.RemoveAsync();
        first.Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        f.AfterUnregister = null;
        var notes = Path.Combine(aside, "operator-recovery-notes.txt");
        File.WriteAllText(notes, "only-copy-created-between-attempts");
        var second = await f.RemoveAsync();
        second.IsClean.ShouldBeFalse();
        second.Residue.ShouldBe("set_aside_content_changed");
        File.ReadAllText(notes).ShouldBe("only-copy-created-between-attempts");
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 D-4: a different directory at the recorded path is not the set-aside tree.
    [Test]
    public async Task C1717_D4_RetryKeepsAReplacementDirectory()
    {
        using var f = new RemovalFixture();
        f.AfterUnregister = () => throw new IOException("audit cut after real unregister");
        (await f.RemoveAsync()).Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        f.AfterUnregister = null;
        var rescued = aside + ".rescued";
        Directory.Move(aside, rescued);
        Directory.CreateDirectory(aside);
        var foreign = Path.Combine(aside, "foreign.txt");
        File.WriteAllText(foreign, "foreign-only-copy");
        var second = await f.RemoveAsync();
        second.IsClean.ShouldBeFalse();
        second.Residue.ShouldBe("set_aside_identity_mismatch");
        File.ReadAllText(foreign).ShouldBe("foreign-only-copy");
        File.ReadAllText(Path.Combine(rescued, "keep.txt")).ShouldBe("private work");
        Directory.Exists(rescued).ShouldBeTrue();
    }

    // CARD-1717 D-4: a receipt with no directory identity is not authority to delete.
    [Test]
    public async Task C1717_D4_UnsealedReceiptIsNotDeleted()
    {
        using var f = new RemovalFixture();
        var aside = WorktreeSetAside.SetAsidePath(f.Source);
        WorktreeNoFollowDelete.MoveAside(f.Source, aside);
        WorktreeSetAside.Record(f.Root, new(f.Source, aside, f.Operation.GitDirectory, DateTime.UtcNow));
        f.Registered = false;
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("set_aside_unsealed");
        File.ReadAllText(Path.Combine(aside, "keep.txt")).ShouldBe("private work");
        Directory.Exists(aside).ShouldBeTrue();
    }

    // CARD-1717 D-3: bytes changed after the retention copy are not deleted with the tree.
    [Test]
    public async Task C1717_D3_ChangedEvidenceBytesAreKept()
    {
        using var f = new RemovalFixture();
        var ev = Path.Combine(f.Source, ".antiphon", "checkpoint-note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(ev)!);
        File.WriteAllText(ev, "retained-old");
        f.IgnoredPaths.Add(".antiphon/checkpoint-note.md");
        f.AfterInspection = count =>
        {
            if (count == 2) File.WriteAllText(ev, "new-only-copy-after-retention");
        };
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("evidence_bytes_changed");
        File.ReadAllText(ev).ShouldBe("new-only-copy-after-retention");
        Directory.Exists(f.Source).ShouldBeTrue();
    }

    // CARD-1717 D-2: a non-ignored file written after the final clean inspection is not deleted.
    [Test]
    public async Task C1717_D2_LateUntrackedFileIsKept()
    {
        using var f = new RemovalFixture();
        var late = Path.Combine(f.Source, "late-unsaved.txt");
        f.AfterInspection = count =>
        {
            if (count == 2) File.WriteAllText(late, "only-copy-late-bytes");
        };
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("untracked_after_proof");
        File.ReadAllText(late).ShouldBe("only-copy-late-bytes");
        Directory.Exists(f.Source).ShouldBeTrue();
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-1: a retained copy that disappears after retention is not a durable receipt.
    [Test]
    public async Task C1717_B1_MissingRetainedCopyIsNotDeleted()
    {
        using var f = new RemovalFixture();
        var ev = Path.Combine(f.Source, ".antiphon", "note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(ev)!);
        File.WriteAllText(ev, "only-retained-byte-copy");
        f.IgnoredPaths.Add(".antiphon/note.md");
        f.AfterInspection = count =>
        {
            if (count != 2) return;
            var retained = Path.Combine(f.Root, "retained", ".antiphon", "note.md");
            if (File.Exists(retained)) File.Delete(retained);
        };
        var result = await f.RemoveAsync(gate: true);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("evidence_retained_missing");
        File.ReadAllText(ev).ShouldBe("only-retained-byte-copy");
        Directory.Exists(f.Source).ShouldBeTrue();
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-1: a crash after unregister must not delete evidence whose bytes changed
    // while the listing seal stayed equal. The retained digest is the authority, not the seal.
    [Test]
    public async Task C1717_B1_CrashRetryKeepsChangedEvidence()
    {
        using var f = new RemovalFixture();
        var ev = Path.Combine(f.Source, ".antiphon", "note.md");
        Directory.CreateDirectory(Path.GetDirectoryName(ev)!);
        File.WriteAllText(ev, "retained-old");
        f.IgnoredPaths.Add(".antiphon/note.md");
        f.AfterUnregister = () => throw new IOException("review crash after actual unregister");
        var first = await f.RemoveAsync(gate: true);
        first.Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        var source = Path.Combine(aside, ".antiphon", "note.md");
        var seal = WorktreeSetAside.ListingSeal(aside);
        var written = File.GetLastWriteTimeUtc(source);
        File.WriteAllText(source, "CHANGED-new!");
        File.SetLastWriteTimeUtc(source, written);
        WorktreeSetAside.ListingSeal(aside).ShouldBe(seal, "equal length and mtime must not authorize the new bytes");
        f.AfterUnregister = null;
        var retry = await f.RemoveAsync(gate: true);
        retry.IsClean.ShouldBeFalse();
        retry.Residue.ShouldBe("evidence_bytes_changed");
        File.ReadAllText(source).ShouldBe("CHANGED-new!");
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-2: a tracked edit that preserves length and mtime keeps the listing seal
    // and must still be refused. Those bytes were never committed.
    [Test]
    public async Task C1717_B2_EqualStatTrackedEditIsNotDeleted()
    {
        using var f = new RemovalFixture();
        f.AfterUnregister = () => throw new IOException("review cut after real unregister");
        (await f.RemoveAsync()).Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        var file = Path.Combine(aside, "keep.txt");
        var before = WorktreeSetAside.ListingSeal(aside);
        var written = File.GetLastWriteTimeUtc(file);
        var changed = new string('Z', "private work".Length);
        File.WriteAllText(file, changed);
        File.SetLastWriteTimeUtc(file, written);
        WorktreeSetAside.ListingSeal(aside).ShouldBe(before);
        f.AfterUnregister = null;
        var second = await f.RemoveAsync();
        second.IsClean.ShouldBeFalse();
        second.Residue.ShouldBe("set_aside_content_changed");
        File.ReadAllText(file).ShouldBe(changed);
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-3: bytes written into an already tracked file after the clean status are not
    // the pushed blob. The census sees a tracked name and must not bless the new contents.
    [Test]
    public async Task C1717_B3_TrackedWriteAfterCleanStatusIsKept()
    {
        using var f = new RemovalFixture();
        const string changed = "unpushed-bytes-after-clean-status";
        f.AfterInspection = count =>
        {
            if (count == 2) File.WriteAllText(Path.Combine(f.Source, "keep.txt"), changed);
        };
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("tracked_bytes_changed");
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe(changed);
        Directory.Exists(f.Source).ShouldBeTrue();
        f.BranchPresent.ShouldBeTrue();
        f.SetAsideTrees().ShouldBeEmpty();
    }

    // CARD-1717 B-3: the same late bytes staged into the index after the clean status still are not the
    // pushed blob. The index then agrees with the file; only the proven commit's tree does not.
    [Test]
    public async Task C1717_B3_RestagedTrackedWriteAfterCleanStatusIsKept()
    {
        using var f = new RemovalFixture();
        const string changed = "unpushed-bytes-staged-after-clean-status";
        f.AfterInspection = count =>
        {
            if (count != 2) return;
            File.WriteAllText(Path.Combine(f.Source, "keep.txt"), changed);
            f.Restaged["keep.txt"] = RemovalFixture.GitBlob(System.Text.Encoding.UTF8.GetBytes(changed));
        };
        var result = await f.RemoveAsync();
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("tracked_bytes_changed");
        File.ReadAllText(Path.Combine(f.Source, "keep.txt")).ShouldBe(changed);
        f.SetAsideTrees().ShouldBeEmpty();
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-3: a file created at the real delete call, after every census, is an only copy.
    [Test]
    public async Task C1717_B3_FileAtDeleteBoundaryIsKept()
    {
        using var f = new RemovalFixture();
        string? late = null;
        f.BeforeRealDelete = path =>
        {
            if (late is not null || !Directory.Exists(path)) return;
            late = Path.Combine(path, "new-only-copy.txt");
            File.WriteAllText(late, "only-copy-created-after-final-check");
        };
        var result = await f.RemoveAsync();
        late.ShouldNotBeNull();
        File.Exists(late).ShouldBeTrue("the only copy created at the delete call survives");
        File.ReadAllText(late).ShouldBe("only-copy-created-after-final-check");
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("set_aside_content_changed");
        File.ReadAllText(Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "keep.txt")).ShouldBe("private work");
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-4: a foreign directory installed at the set-aside path is not moved back onto
    // the registered worktree when unregister refuses.
    [Test]
    public async Task C1717_B4_ForeignDirectoryIsNotRestored()
    {
        using var f = new RemovalFixture();
        string? saved = null;
        f.BeforeUnregister = () =>
        {
            var aside = f.SetAsideTrees().ShouldHaveSingleItem();
            // The set-aside search is ".source.removing-*", so a ".rescued" suffix is still a hit.
            saved = Path.Combine(Path.GetDirectoryName(aside)!, "rescued-real-tree");
            Directory.Move(aside, saved);
            Directory.CreateDirectory(aside);
            File.WriteAllText(Path.Combine(aside, "foreign.txt"), "foreign-only-bytes");
            f.RemoveExitCode = 128;
        };
        var result = await f.RemoveAsync();
        File.Exists(Path.Combine(f.Source, "foreign.txt")).ShouldBeFalse("the foreign directory is not restored");
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("set_aside_identity_mismatch");
        Directory.Exists(f.Source).ShouldBeFalse();
        File.ReadAllText(Path.Combine(saved!, "keep.txt")).ShouldBe("private work");
        File.ReadAllText(Path.Combine(f.SetAsideTrees().ShouldHaveSingleItem(), "foreign.txt")).ShouldBe("foreign-only-bytes");
        f.SetAsideRecords().ShouldNotBeEmpty();
        f.BranchPresent.ShouldBeTrue();
    }

    // CARD-1717 B-5: an approved file already unlinked by an interrupted delete is completed work.
    // The unchanged remainder must resume instead of refusing the whole listing forever.
    [Test]
    public async Task C1717_B5_PartialUnlinkOfApprovedFileResumes()
    {
        using var f = new RemovalFixture();
        Directory.CreateDirectory(Path.Combine(f.Source, "notes"));
        File.WriteAllText(Path.Combine(f.Source, "notes", "keep-too.txt"), "also private");
        f.TrackedListing = "keep.txt\0notes/keep-too.txt\0";
        f.AfterUnregister = () => throw new IOException("review cut after real unregister");
        var first = await f.RemoveAsync();
        first.Residue.ShouldBe("worktree_removal_incomplete");
        var aside = f.SetAsideTrees().ShouldHaveSingleItem();
        File.Delete(Path.Combine(aside, "keep.txt"));
        f.AfterUnregister = null;
        var second = await f.RemoveAsync();
        second.IsClean.ShouldBeTrue(second.Residue);
        f.SetAsideTrees().ShouldBeEmpty();
        f.SetAsideRecords().ShouldBeEmpty();
        f.BranchPresent.ShouldBeFalse();
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
            if (Refusal is not null) return Task.FromResult(new WorktreeEvidenceRetentionResult(null, [], Refusal));
            var files = new List<RetainedWorktreeFile>();
            foreach (var relative in relativePaths)
            {
                var source = Path.Combine(worktreePath, relative.Replace('/', Path.DirectorySeparatorChar));
                var retained = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
                var bytes = File.Exists(source) ? File.ReadAllBytes(source) : [];
                File.WriteAllBytes(retained, bytes);
                files.Add(new RetainedWorktreeFile(relative, retained, bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes))));
            }
            return Task.FromResult(new WorktreeEvidenceRetentionResult(root, [.. files], null));
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
        /// <summary>Stable <c>git ls-files -z</c> answer. Not a live listing of the fixture tree.</summary>
        public string TrackedListing { get; set; } = "keep.txt\0";
        // Index blob of the original keep.txt. A later edit of that file must not refresh this oid.
        private readonly Dictionary<string, string> stageBlobs = new(StringComparer.Ordinal)
        {
            ["keep.txt"] = GitBlob(System.Text.Encoding.UTF8.GetBytes("private work")),
        };
        public int RemoveExitCode { get; set; }
        public bool Registered { get; set; } = true;
        /// <summary>Runs between the move-aside and the registration drop.</summary>
        public Action? BeforeUnregister { get; set; }
        public Action? AfterUnregister { get; set; }
        public Action? BeforeRegistrationQuery { get; set; }
        /// <summary>Lists after the drop that still name the source, modelling a cached worktree list.</summary>
        public int StaleListsAfterDrop { get; set; }
        public string[] UnregisterVector => ["worktree", "remove", "--registration-only", Source];
        public string[] SetAsideTrees() => Directory.GetDirectories(Root, ".source.removing-*");
        public string[] SetAsideRecords()
        {
            var records = Path.Combine(Root, "antiphon", "worktree-removal");
            return Directory.Exists(records) ? Directory.GetFileSystemEntries(records) : [];
        }
        public RecordingRetention Retention { get; }
        /// <summary>Whether an injected rename fault carries Windows HResults (production reads the OS).</summary>
        public bool Windows { get; init; } = true;
        public List<TimeSpan> Pauses { get; } = [];
        /// <summary>Runs inside each pause, after it is recorded.</summary>
        public Action? OnPause { get; set; }
        private readonly Dictionary<string, int> renameCalls = new() { ["move"] = 0, ["record"] = 0 };
        private (string Rename, Func<int, Exception> Fault, int Failures)? renameFault;
        public int RenameCalls(string rename) => renameCalls[rename];
        /// <summary>The rename throws <paramref name="fault"/> for its first <paramref name="failures"/> calls.</summary>
        public void FailRename(string rename, Func<int, Exception> fault, int failures = int.MaxValue) =>
            renameFault = (rename, fault, failures);
        private void Rename(string rename, Action real)
        {
            var call = ++renameCalls[rename];
            if (renameFault is { } f && f.Rename == rename && call <= f.Failures) throw f.Fault(call);
            real();
        }
        /// <summary>Runs immediately before the real no-follow delete, after any injected fault.</summary>
        public Action<string>? BeforeRealDelete { get; set; }
        public int DeleteCalls { get; private set; }
        private (Func<int, Exception?> Fault, int Failures)? deleteFault;
        /// <summary>
        /// The set-aside delete throws <paramref name="fault"/> for its first <paramref name="failures"/>
        /// calls; a null fault returns without deleting, as a delete-pending name stays visible on Windows.
        /// </summary>
        public void FailDelete(Func<int, Exception?> fault, int failures = int.MaxValue)
        { deleteFault = (fault, failures); DeleteCalls = 0; Pauses.Clear(); }
        /// <summary>Counts the deletes without a fault; the counters keep running.</summary>
        public void HealDelete() => deleteFault = (_ => null, 0);
        private void Delete(string path, bool preserveNestedRepositories)
        {
            var call = ++DeleteCalls;
            if (deleteFault is { } f && call <= f.Failures)
            {
                if (f.Fault(call) is { } fault) throw fault;
                return;
            }
            BeforeRealDelete?.Invoke(path);
            WorktreeNoFollowDelete.Delete(path, preserveNestedRepositories);
        }
        // Without an injected fault the fixture renames and deletes exactly as production does.
        // A before-delete hook still has to wrap that delete; the system delegate never sees it.
        private WorktreeRenames Renames => renameFault is null && deleteFault is null && BeforeRealDelete is null
            ? WorktreeRenames.System
            : new(
            (s, d) => Rename("move", () => Directory.Move(s, d)),
            (s, d) => Rename("record", () => File.Move(s, d, overwrite: true)),
            p => { Pauses.Add(p); OnPause?.Invoke(); },
            renameFault is null && deleteFault is null ? OperatingSystem.IsWindows() : Windows) { DeleteTree = Delete };
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
                new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings())), Retention, this)) { Renames = Renames }
            : new GuardedWorktreeRemoval(this, this, this) { Renames = Renames }).RemoveAsync(Request, default);
        internal Task<WorktreeDirectoryPass> RemoveDirectoryAsync() => new GuardedWorktreeRemoval(this, this, this)
            { Renames = Renames }.RemoveDirectoryAsync(Request, null, TimeProvider.System, default);
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
                var stale = !Registered && StaleListsAfterDrop > 0;
                if (stale) StaleListsAfterDrop--;
                result = new(0, Registered || stale
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
            else if (args[0] == "ls-files" && args.Contains("--stage")) result = new(0, StageListing(staged: true), "");
            else if (args[0] == "ls-tree") result = new(0, StageListing(staged: false), "");
            else if (args[0] == "ls-files" && !args.Contains("--ignored")) result = new(0, TrackedListing, "");
            // CARD-1717 A-B-7: this fixture has no line-ending conversion or filter, so the clean form is the raw bytes.
            else if (args.Contains("hash-object"))
                result = new(0, string.Concat(args.SkipWhile(a => a != "--").Skip(1)
                    .Select(path => GitBlob(File.ReadAllBytes(Path.Combine(repository, path))) + "\n")), "");
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
        /// <summary>Restaged index blobs: <c>ls-files --stage</c> reports these, the commit tree never does.</summary>
        public Dictionary<string, string> Restaged { get; } = new(StringComparer.Ordinal);
        private string StageListing(bool staged)
        {
            var listing = new System.Text.StringBuilder();
            foreach (var name in TrackedListing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!stageBlobs.TryGetValue(name, out var oid))
                {
                    var path = Path.Combine(Source, name.Replace('/', Path.DirectorySeparatorChar));
                    oid = GitBlob(File.ReadAllBytes(path));
                    stageBlobs[name] = oid;
                }
                if (staged && Restaged.TryGetValue(name, out var restaged)) oid = restaged;
                listing.Append(staged ? $"100644 {oid} 0\t" : $"100644 blob {oid}\t").Append(name).Append('\0');
            }
            return listing.ToString();
        }

        public static string GitBlob(byte[] bytes)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hash.AppendData(System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"));
            hash.AppendData(bytes);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
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
