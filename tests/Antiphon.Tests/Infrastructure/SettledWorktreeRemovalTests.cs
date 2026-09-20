using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SettledWorktreeRemovalTests
{
    [Test]
    public async Task C459_InterfaceDefaultCannotDeleteSettledTask()
    {
        var implementation = new LegacyOnlyManager();
        IWorktreeManager manager = implementation;
        var source = new LandSourceCoordinates(Guid.NewGuid(), "fixture-repo", "fixture-tree",
            "refs/heads/source", "refs/heads/master");
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.SettledTask, source,
            "fixture-common", "fixture-admin", new string('a', 40), new string('b', 40),
            null, null!, RetirementId: Guid.NewGuid());
        var result = await manager.TryRemoveAsync(request, CancellationToken.None);
        implementation.LegacyRemovalCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        result.Residue.ShouldBe("guarded_removal_not_implemented");
    }

    [Test]
    public async Task C459_PurposeCannotBorrowAuthority()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await using var lease = await h.LeaseAsync();
        var request = h.Request(lease) with { Purpose = (WorktreeRemovalPurpose)999 };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync(request, lease);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(h.Host.Fixture.Source, "keep.txt"))).ShouldBe("seed\n");
        await h.AssertRemoteUnchangedAsync();
    }

    [Test]
    [Arguments("task")]
    [Arguments("repo")]
    [Arguments("path")]
    [Arguments("sibling-prefix")]
    [Arguments("branch")]
    [Arguments("common")]
    [Arguments("admin")]
    [Arguments("sha")]
    public async Task C459_CoordinatesMatchReceipt(string field)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await using var lease = await h.LeaseAsync();
        var sibling = h.Host.Fixture.Source + "-sibling";
        var request = field switch
        {
            "task" => h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { TaskId = Guid.NewGuid() } },
            "repo" => h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { RepositoryPath = h.Host.Fixture.Remote } },
            "path" => h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { WorktreePath = Path.Combine(h.Host.Fixture.Root, "trees", "other") } },
            "sibling-prefix" => h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { WorktreePath = sibling } },
            "branch" => h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { SourceFullRef = "refs/heads/other-source" } },
            "common" => h.Request(lease) with { CommonDirectory = h.Host.Fixture.Remote },
            "admin" => h.Request(lease) with { GitDirectory = Path.Combine(h.Host.Fixture.Repository, ".git") },
            _ => h.Request(lease) with { ExpectedSourceSha = h.Host.Fixture.SeedSha == h.SourceSha ? new string('b', 40) : h.Host.Fixture.SeedSha },
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync(request, lease);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
        await h.AssertRemoteUnchangedAsync();
    }

    [Test]
    [Arguments("outside")]
    [Arguments("sibling-prefix")]
    [Arguments("dot-segment")]
    [Arguments("junction")]
    [Arguments("in-root")]
    public async Task C459_ManagedRootRequired(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await using var lease = await h.LeaseAsync();
        WorktreeRemovalRequest request;
        if (shape == "in-root")
        {
            request = h.Request(lease);
            var result = await h.RemoveAsync(request, lease);
            result.IsClean.ShouldBeTrue();
            h.RemoveCalls.ShouldBe(1);
            Directory.Exists(h.Host.Fixture.Source).ShouldBeFalse();
            return;
        }

        if (shape == "junction")
        {
            if (!OperatingSystem.IsWindows())
                throw new TUnit.Core.Exceptions.SkipTestException("Requires Windows junctions");
            var alias = Path.Combine(h.ManagedRoot, "alias-junction");
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/c", "mklink", "/J", alias, h.Host.Fixture.Repository },
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync();
            proc.ExitCode.ShouldBe(0);
            request = h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { WorktreePath = alias } };
            try
            {
                h.Host.Fixture.Git.Trace.Clear();
                var junctioned = await h.RemoveAsync(request, lease);
                h.RemoveCalls.ShouldBe(0);
                junctioned.IsClean.ShouldBeFalse();
                Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
            }
            finally
            {
                using var rm = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    ArgumentList = { "/c", "rmdir", alias },
                });
                rm?.WaitForExit();
            }
            return;
        }
        else
        {
            var path = shape switch
            {
                "outside" => Path.Combine(h.Host.Fixture.Root, "outside"),
                "sibling-prefix" => h.ManagedRoot + "-sibling",
                _ => Path.Combine(h.ManagedRoot, "..", "canonical"),
            };
            Directory.CreateDirectory(path);
            request = h.Request(lease) with { Source = h.Host.Fixture.Coordinates with { WorktreePath = path } };
        }

        h.Host.Fixture.Git.Trace.Clear();
        var refused = await h.RemoveAsync(request, lease);
        h.RemoveCalls.ShouldBe(0);
        refused.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C459_NestedRegistrationHolds()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var nested = Path.Combine(h.Host.Fixture.Source, "nested-tree");
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "add", "--detach", nested, "HEAD");
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
        Directory.Exists(nested).ShouldBeTrue();
    }

    [Test]
    [Arguments("present-unregistered")]
    [Arguments("list-failure")]
    public async Task C459_RegistrationRequired(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (shape == "present-unregistered")
        {
            var gitFile = Path.Combine(h.Host.Fixture.Source, ".git");
            if (File.Exists(gitFile)) File.Delete(gitFile);
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "prune");
        }
        else
        {
            h.Host.Fixture.Git.BeforeCommand = (_, args) =>
                Task.FromResult(args[0] == "worktree" && args.Contains("list")
                    ? new LandingGitResult(128, "", "list_failed")
                    : null);
        }

        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.InspectionCalls.ShouldBe(0);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("initial")]
    [Arguments("between")]
    public async Task C459_LockedRegistrationHolds(string when)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (when == "initial")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "lock", h.Host.Fixture.Source);
        else
        {
            var seen = 0;
            h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
            {
                if (args[0] == "status" && ++seen == 1)
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "lock", h.Host.Fixture.Source);
                return null;
            };
        }

        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C459_PrunableRegistrationHolds()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "worktree" && args.Contains("list"))
            {
                var porcelain = $"worktree {h.Host.Fixture.Source}\0HEAD {h.SourceSha}\0branch {h.Host.Fixture.SourceRef}\0prunable gitdir file points to non-existent location\0";
                return Task.FromResult<LandingGitResult?>(new(0, porcelain, ""));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("ahead")]
    [Arguments("divergent")]
    [Arguments("patch-only")]
    [Arguments("task-remote-only")]
    [Arguments("stale-master")]
    [Arguments("uncontained")]
    [Arguments("descendant")]
    public async Task C459_RemoteContainmentRequired(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (shape == "descendant")
        {
            var result = await h.RemoveAsync();
            result.IsClean.ShouldBeTrue();
            h.RemoveCalls.ShouldBe(1);
            return;
        }

        await File.WriteAllTextAsync(Path.Combine(h.Host.Fixture.Source, "ahead.txt"), shape + "\n");
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "add", "ahead.txt");
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "commit", "-m", shape);
        h.SourceSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.SeedRetirementAsync();
        if (shape == "task-remote-only")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "push", "origin", h.Host.Fixture.SourceRef);
        if (shape == "stale-master")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Remote, "update-ref", h.Host.Fixture.TargetRef, h.Host.Fixture.SeedSha);
        h.Host.Fixture.Git.Trace.Clear();
        var refused = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        refused.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("pushurl")]
    [Arguments("target")]
    public async Task C459_DestinationIdentityRequired(string field)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (field == "pushurl")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "remote", "set-url", "--push", "origin", h.Host.Fixture.Observer);
        else
        {
            await using var db = h.Host.CreateContext();
            var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == h.RetirementId);
            row.DestinationFullRef = "refs/heads/other-target";
            await db.SaveChangesAsync();
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "branch", "other-target", h.TargetSha);
        }

        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("rewrite")]
    [Arguments("delete")]
    [Arguments("error")]
    public async Task C459_RemoteRefreshBeforeDirectory(string change)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var seen = 0;
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] != "ls-remote" || ++seen != 2) return null;
            switch (change)
            {
                case "rewrite":
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "checkout", "--orphan", "c459-orphan");
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "orphan");
                    var orphan = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Remote, "update-ref", h.Host.Fixture.TargetRef, orphan);
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "checkout", "-f", "master");
                    break;
                case "delete":
                    await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Remote, "update-ref", "-d", h.Host.Fixture.TargetRef);
                    break;
                default:
                    return new LandingGitResult(128, "", "remote_error");
            }
            return null;
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("lost-source")]
    [Arguments("unreadable")]
    public async Task C459_RemoteRefreshBeforeBranch(string change)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var directoryRemoved = false;
        h.Host.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args.Contains("worktree") && args.Contains("remove") && result.Succeeded)
                directoryRemoved = true;
        };
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (!directoryRemoved || args[0] != "ls-remote") return null;
            if (change == "lost-source")
            {
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Remote, "update-ref", "-d", h.Host.Fixture.TargetRef);
                return null;
            }
            return new LandingGitResult(128, "", "branch_remote_error");
        };

        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.BranchDeleteCalls.ShouldBe(0);
        result.BranchDeleted.ShouldBeFalse();
    }

    [Test]
    [Arguments("null")]
    [Arguments("forged")]
    [Arguments("disposed")]
    [Arguments("other-repo")]
    public async Task C459_GenuineLeaseRequired(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        RepositoryLease? lease = shape switch
        {
            "null" => null,
            "forged" => new ForgedLease(h.CommonDirectory),
            "other-repo" => await h.Host.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(h.Host.Fixture.Observer, CancellationToken.None),
            _ => await h.LeaseAsync(),
        };
        if (shape == "disposed") await lease!.DisposeAsync();
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.Host.Services.GetRequiredService<IWorktreeManager>()
            .TryRemoveAsync(h.Request(lease!), CancellationToken.None);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
        if (lease is not null && shape != "disposed" && shape != "forged") await lease.DisposeAsync();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("revoked")]
    [Arguments("changed")]
    [Arguments("schema")]
    public async Task C459_CommittedReceiptRequired(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await using var db = h.Host.CreateContext();
        var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == h.RetirementId);
        if (shape == "missing") db.TaskWorktreeRetirements.Remove(row);
        if (shape == "revoked") { row.State = Antiphon.Server.Domain.Enums.WorktreeRetirementState.Revoked; row.Active = false; }
        if (shape == "changed") row.SourceSha = new string('c', 40);
        if (shape == "schema") row.SchemaVersion = 999;
        await db.SaveChangesAsync();
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
    }

    [Test]
    public async Task C459_FinalAuthorityRequired()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var statuses = 0;
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "status" && ++statuses == 2)
            {
                await using var db = h.Host.CreateContext();
                var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == h.RetirementId);
                row.Active = false;
                await db.SaveChangesAsync();
            }
            return null;
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("staged")]
    [Arguments("unstaged")]
    [Arguments("untracked")]
    [Arguments("submodule")]
    [Arguments("sequencer")]
    [Arguments("unreadable")]
    public async Task C459_FirstContentInspectionRequired(string change)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (change == "submodule")
        {
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "-c", "protocol.file.allow=always", "submodule", "add",
                h.Host.Fixture.Remote, "nested");
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "commit", "-am", "submodule fixture");
            h.SourceSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Source, "rev-parse", "HEAD")).Trim();
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "merge", "--ff-only", h.Host.Fixture.SourceRef);
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "push", "origin", h.Host.Fixture.TargetRef);
            await h.SeedRetirementAsync();
        }
        if (change == "sequencer")
            await ApplyDirtyAsync(h, "sequencer");

        h.Host.Fixture.Git.BeforeCommand = async (repo, args) =>
        {
            if (repo != h.Host.Fixture.Source || args[0] != "status") return null;
            if (change != "sequencer") await ApplyDirtyAsync(h, change);
            if (change == "unreadable") return new LandingGitResult(128, "", "status_failed");
            return null;
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.InspectionCalls.ShouldBe(1);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("staged")]
    [Arguments("unstaged")]
    [Arguments("untracked")]
    [Arguments("sequencer")]
    public async Task C459_FinalContentInspectionRequired(string change)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var seen = 0;
        h.Host.Fixture.Git.BeforeCommand = async (repo, args) =>
        {
            if (repo != h.Host.Fixture.Source || args[0] != "status" || ++seen != 2) return null;
            await ApplyDirtyAsync(h, change);
            return null;
        };
        if (change == "sequencer")
        {
            var ignored = 0;
            h.Host.Fixture.Git.AfterCommand = async (_, args, _) =>
            {
                if (args[0] != "ls-files" || ++ignored != 1) return;
                await ApplyDirtyAsync(h, "sequencer");
            };
        }
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.json")]
    [Arguments("bin-private/keep.txt")]
    public async Task C459_FirstIgnoredInspectionRequired(string relative)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var path = Path.Combine(h.Host.Fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "private bytes");
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.InspectionCalls.ShouldBe(1);
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        (await File.ReadAllTextAsync(path)).ShouldBe("private bytes");
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.json")]
    [Arguments("bin-private/keep.txt")]
    public async Task C459_FinalIgnoredInspectionRequired(string relative)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var path = Path.Combine(h.Host.Fixture.Source, relative);
        var seen = 0;
        h.Host.Fixture.Git.BeforeCommand = async (repo, args) =>
        {
            if (repo != h.Host.Fixture.Source || args[0] != "status" || ++seen != 2) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "late private bytes");
            return null;
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.RemoveCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        (await File.ReadAllTextAsync(path)).ShouldBe("late private bytes");
    }

    [Test]
    [Arguments("directory")]
    [Arguments("registration")]
    [Arguments("ref")]
    [Arguments("no-intent")]
    [Arguments("wrong-intent")]
    [Arguments("matching-intent")]
    public async Task C459_AbsenceNeedsOwnIntent(string shape)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        if (shape is "directory" or "no-intent" or "wrong-intent" or "matching-intent")
        {
            foreach (var file in Directory.EnumerateFiles(h.Host.Fixture.Source, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(h.Host.Fixture.Source, true);
        }
        if (shape is "matching-intent" or "no-intent" or "wrong-intent")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "prune");
        if (shape == "registration")
        {
            var gitFile = Path.Combine(h.Host.Fixture.Source, ".git");
            if (File.Exists(gitFile)) File.Delete(gitFile);
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "prune");
        }
        if (shape == "ref")
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "update-ref", "-d", h.Host.Fixture.SourceRef);

        var intent = shape is "matching-intent";
        await using var lease = await h.LeaseAsync();
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync(h.Request(lease, intent: intent), lease);
        if (shape == "matching-intent")
        {
            result.DirectoryGone.ShouldBeTrue();
            return;
        }

        result.IsClean.ShouldBeFalse();
        h.RemoveCalls.ShouldBe(0);
    }

    [Test]
    [Arguments("directory")]
    [Arguments("registration")]
    [Arguments("ref")]
    public async Task C459_RecreatedTreeHolds(string component)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        h.Host.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (!(args.Contains("worktree") && args.Contains("remove") && result.Succeeded)) return;
            if (component == "directory") Directory.CreateDirectory(h.Host.Fixture.Source);
            if (component == "registration")
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "add", h.Host.Fixture.Source, h.Host.Fixture.SourceRef);
            if (component == "ref")
            {
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "recreated");
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "branch", "-f",
                    h.Host.Fixture.SourceRef[11..], "HEAD");
            }
        };
        var result = await h.RemoveAsync();
        result.IsClean.ShouldBeFalse();
    }

    [Test]
    public async Task C459_BranchPrecheckRequired()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        h.Host.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (!(args.Contains("worktree") && args.Contains("remove") && result.Succeeded)) return;
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "moved");
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "branch", "-f",
                h.Host.Fixture.SourceRef[11..], "HEAD");
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.BranchDeleteCalls.ShouldBe(0);
        result.BranchDeleted.ShouldBeFalse();
    }

    [Test]
    public async Task C459_BranchDeleteUsesCas()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var concurrentSha = "";
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "update-ref" && args.Contains("-d"))
            {
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "race");
                concurrentSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "branch", "-f",
                    h.Host.Fixture.SourceRef[11..], concurrentSha);
            }
            return null;
        };
        var result = await h.RemoveAsync();
        result.IsClean.ShouldBeFalse();
        var actualBranchSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", h.Host.Fixture.SourceRef)).Trim();
        actualBranchSha.ShouldBe(concurrentSha);
    }

    [Test]
    public async Task C459_NewCheckoutHoldsBranch()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var other = Path.Combine(h.ManagedRoot, "other-checkout");
        var removed = false;
        h.Host.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args.Contains("worktree") && args.Contains("remove") && result.Succeeded)
                removed = true;
        };
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (removed && args[0] == "worktree" && args.Contains("list") && !Directory.Exists(other))
                await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "worktree", "add", other, h.Host.Fixture.SourceRef);
            return null;
        };
        h.Host.Fixture.Git.Trace.Clear();
        var result = await h.RemoveAsync();
        h.BranchDeleteCalls.ShouldBe(0);
        result.BranchDeleted.ShouldBeFalse();
        Directory.Exists(other).ShouldBeTrue();
    }

    [Test]
    public async Task C459_RemovalNeverForces()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var sentinel = Path.Combine(h.Host.Fixture.Source, "bin-private", "keep.txt");
        var seenStatus = 0;
        h.Host.Fixture.Git.BeforeCommand = async (repo, args) =>
        {
            if (repo == h.Host.Fixture.Source && args[0] == "status" && ++seenStatus == 2)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
                await File.WriteAllTextAsync(sentinel, "owned ignored");
            }
            if (args.Contains("worktree") && args.Contains("remove"))
                return new LandingGitResult(128, "", "ordinary_remove_failed");
            return null;
        };
        var result = await h.RemoveAsync();
        h.ForceOrRecursiveDeleteCalls.ShouldBe(0);
        result.IsClean.ShouldBeFalse();
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("heads")]
    [Arguments("tags")]
    [Arguments("other-retirement")]
    [Arguments("land")]
    [Arguments("existing-wrong-namespace")]
    public async Task C459_RetirementRefsCannotBorrowNamespaces(string ns)
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var git = h.Host.Fixture.Git;
        var pin = ns switch
        {
            "heads" => "master",
            "tags" => "v1",
            "other-retirement" => "refs/antiphon/retirement/" + Guid.NewGuid().ToString("N") + "/source",
            "land" => "refs/antiphon/land/" + h.Host.Fixture.TaskId.ToString("N") + "/source",
            "existing-wrong-namespace" => "refs/antiphon/land/" + h.Host.Fixture.TaskId.ToString("N") + "/source",
            _ => "source",
        };
        if (ns == "existing-wrong-namespace")
        {
            var landRef = $"refs/antiphon/land/{h.Host.Fixture.TaskId:N}/{h.RetirementId:N}/source";
            await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "update-ref", landRef, h.SourceSha);
        }

        var pinned = await git.PinRetirementAsync(h.Host.Fixture.Repository, h.RetirementId, pin, h.SourceSha, CancellationToken.None);
        var wrongNamespaceAccepted = pinned.Succeeded;
        wrongNamespaceAccepted.ShouldBeFalse();
        if (ns is "heads" or "tags" or "other-retirement" or "land")
            pinned.Succeeded.ShouldBeFalse();
        var observed = await git.ObserveRetirementAsync(h.Host.Fixture.Repository,
            new("origin", h.Host.Fixture.TargetRef, h.Fingerprint), h.SourceSha, h.RetirementId, pin, CancellationToken.None);
        (observed.Reason is "invalid_observation_identity" or "invalid_observation_ref" || ns == "existing-wrong-namespace")
            .ShouldBeTrue();
    }

    private static async Task ApplyDirtyAsync(SettledRemovalHarness h, string change)
    {
        var repo = h.Host.Fixture.Source;
        switch (change)
        {
            case "staged":
                await File.WriteAllTextAsync(Path.Combine(repo, "keep.txt"), "staged bytes\n");
                await h.Host.Fixture.RequiredAsync(repo, "add", "keep.txt");
                break;
            case "unstaged":
                await File.WriteAllTextAsync(Path.Combine(repo, "keep.txt"), "unstaged bytes\n");
                break;
            case "untracked":
                await File.WriteAllTextAsync(Path.Combine(repo, "new.txt"), "untracked\n");
                break;
            case "submodule":
                await File.WriteAllTextAsync(Path.Combine(repo, "nested", "keep.txt"), "dirty submodule\n");
                break;
            case "sequencer":
                await File.WriteAllBytesAsync(Path.Combine(h.GitDirectory, "MERGE_HEAD"),
                    System.Text.Encoding.ASCII.GetBytes(h.SourceSha + "\n"));
                break;
        }
    }

    private sealed class LegacyOnlyManager : IWorktreeManager
    {
        public int LegacyRemovalCalls { get; private set; }
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct)
            => throw new NotSupportedException();
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
        { LegacyRemovalCalls++; return Task.CompletedTask; }
        public Task TouchAsync(string worktreePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ForgedLease(string common) : RepositoryLease
    {
        public override string CommonDirectory => common;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
