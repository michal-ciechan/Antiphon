using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0824 S2 V-43 (CP-17). The origin branch deletion meets the real CARD-1008 recycle audit: a runner volume whose
/// local refs for a landed branch are deleted before origin's branch stays recyclable, while the same origin delete
/// without the runner step leaves refs whose tip origin no longer advertises, and the audit refuses. The runner repository
/// is seeded under the temp work volume in the rebase-land shape: origin's main holds a rebased copy, never the branch
/// commit itself. Runner operations run on host paths; a linked worktree's pointers are rewritten to <c>/work</c> only
/// for the preview that reads them, and no runner operation runs after that rewrite.
/// </summary>
[NativeLinuxOnly(NativeLinuxReason)]
[Category("Integration")]
[Timeout(600_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C0824BranchDeletionRecycleAuditTests
{
    internal const string NativeLinuxReason = "CARD-0824 S2 V-43: the CARD-1008 recycle audit requires native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly.";
    private const string Branch = "feat/card-task-0824cafe";
    private const string LocalRef = "refs/heads/" + Branch;
    private const string TrackingRef = "refs/remotes/origin/" + Branch;

    [Test]
    public async Task C0824_legacy_mirror_blocks_runner_refs_and_origin_kept(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var w = await World.SeedAsync(fixture);
        await w.AddLegacyMirrorAsync();

        var refused = await w.Runner().DeleteBranchRefsAsync(new(Branch, w.BranchSha), ct);
        refused.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused, "mirror-refuses");
        refused.Code.ShouldBe(WorkspaceBranchRefsCodes.BranchCheckedOut, "mirror-refuses");
        (await w.RefAsync(LocalRef)).ShouldBe(w.BranchSha, "runner-refs-kept");
        (await w.RefAsync(TrackingRef)).ShouldBe(w.BranchSha, "runner-refs-kept");
        // The server defers on that refusal, so origin is never asked: the branch is still advertised.
        (await w.OriginShaAsync()).ShouldBe(w.BranchSha, "origin-kept");

        w.RewriteMirrorPointersToWork();
        var preview = await VolumeRetirementFixture.Preview(fixture);
        var text = preview.Output + await VolumeRetirementFixture.AuditText(fixture);
        preview.Exit.ShouldBe(0, "c0824-v43: everything is still published; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    public async Task C0824_after_runner_refs_and_origin_delete_audit_passes(CancellationToken ct)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var w = await World.SeedAsync(fixture);
        await w.AddLegacyMirrorAsync();
        var runner = w.Runner();
        // S4 drains the legacy mirror first; then the runner step (B-3 step 3), then the leased origin delete.
        (await runner.RemoveAsync(new(w.Mirror, PublishedSha: w.BranchSha), ct)).Removed.ShouldBeTrue("mirror-removed");
        var deleted = await runner.DeleteBranchRefsAsync(new(Branch, w.BranchSha), ct);
        deleted.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Deleted, deleted.Code);
        deleted.Deleted.Order(StringComparer.Ordinal).ShouldBe([LocalRef, TrackingRef]);
        (await w.RefAsync(LocalRef)).ShouldBeNull("runner-refs-gone");
        (await w.RefAsync(TrackingRef)).ShouldBeNull("runner-refs-gone");
        await w.DeleteOriginBranchWithLeaseAsync();
        (await w.OriginShaAsync()).ShouldBeNull("origin-deleted");

        var preview = await VolumeRetirementFixture.Preview(fixture);
        var text = preview.Output + await VolumeRetirementFixture.AuditText(fixture);
        preview.Exit.ShouldBe(0, "c0824-v43: no ref names an unadvertised tip; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        text.ShouldNotContain("RecycleUnpublishedWork");
        fixture.Removed.ShouldBeEmpty();
    }

    [Test]
    public async Task C0824_origin_delete_without_runner_refs_audit_refuses(CancellationToken ct)
    {
        // The control that proves the oracle can go red: the same sequence without the runner step.
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var w = await World.SeedAsync(fixture);
        await w.AddLegacyMirrorAsync();
        (await w.Runner().RemoveAsync(new(w.Mirror, PublishedSha: w.BranchSha), ct)).Removed.ShouldBeTrue("mirror-removed");
        await w.DeleteOriginBranchWithLeaseAsync();
        (await w.OriginShaAsync()).ShouldBeNull("origin-deleted");
        (await w.RefAsync(LocalRef)).ShouldBe(w.BranchSha, "runner-refs-left");

        var preview = await VolumeRetirementFixture.Preview(fixture);
        var text = preview.Output + await VolumeRetirementFixture.AuditText(fixture);
        preview.Exit.ShouldBe(2, "c0824-v43: refs name a tip origin no longer advertises; " + VolumeRetirementFixture.Truncate(text));
        text.ShouldContain("RecycleUnpublishedWork");
        text.ShouldContain("gone-tracking=1");
        text.ShouldContain("unadvertised-local=1");
        text.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        fixture.Removed.ShouldBeEmpty();
    }

    private sealed class World
    {
        public required C1008HostFixture Fixture { get; init; }
        public required string Bare { get; init; }
        public required string Repo { get; init; }
        public required string Publisher { get; init; }
        public required string BranchSha { get; init; }
        public string Work => VolumeRetirementFixture.WorkMount(Fixture);
        public string Mirror => Path.Combine(Work, "worktrees", "task-0824cafe");

        /// <summary>
        /// Bare origin; a publisher pushes the task branch (commit T) and then a rebased copy of T onto main, so main never
        /// contains T. The runner primary under <c>repos/antiphon</c> only fetches: its own reflogs never name T outside
        /// the two refs the runner step deletes.
        /// </summary>
        public static async Task<World> SeedAsync(C1008HostFixture fixture)
        {
            var bare = Path.Combine(fixture.Root, "c0824-origin.git");
            var publisher = Path.Combine(fixture.Root, "c0824-publisher");
            var repo = Path.Combine(VolumeRetirementFixture.WorkMount(fixture), "repos", "antiphon");
            string Q(string value) => VolumeRetirementFixture.Quote(value);
            var setup = await VolumeRetirementFixture.Bash($$"""
                set -euo pipefail
                git init -q --bare {{Q(bare)}}
                git -C {{Q(bare)}} symbolic-ref HEAD refs/heads/main
                git init -q -b main {{Q(publisher)}}
                git -C {{Q(publisher)}} config user.name Fixture
                git -C {{Q(publisher)}} config user.email fixture@example.invalid
                git -C {{Q(publisher)}} config commit.gpgsign false
                printf '.antiphon/\n' > {{Q(publisher)}}/.gitignore
                printf 'published\n' > {{Q(publisher)}}/readme.txt
                git -C {{Q(publisher)}} add .gitignore readme.txt
                git -C {{Q(publisher)}} commit -qm base
                git -C {{Q(publisher)}} remote add origin {{Q(bare)}}
                git -C {{Q(publisher)}} push -q origin HEAD:refs/heads/main
                git -C {{Q(publisher)}} checkout -q -b {{Q(Branch)}}
                printf 'task work\n' > {{Q(publisher)}}/task.txt
                git -C {{Q(publisher)}} add task.txt
                git -C {{Q(publisher)}} commit -qm 'task work'
                git -C {{Q(publisher)}} push -q origin HEAD:{{Q(LocalRef)}}
                task=$(git -C {{Q(publisher)}} rev-parse HEAD)
                git -C {{Q(publisher)}} checkout -q main
                git -C {{Q(publisher)}} -c commit.gpgsign=false cherry-pick -q --allow-empty "$task" >/dev/null
                git -C {{Q(publisher)}} commit -q --amend -m 'task work (rebased by the land)'
                git -C {{Q(publisher)}} push -q origin HEAD:refs/heads/main
                if git -C {{Q(publisher)}} merge-base --is-ancestor "$task" main; then
                  printf 'main contains the branch commit\n'
                  exit 1
                fi
                git clone -q -b main {{Q(bare)}} {{Q(repo)}}
                git -C {{Q(repo)}} config user.name Fixture
                git -C {{Q(repo)}} config user.email fixture@example.invalid
                git -C {{Q(repo)}} branch -q {{Q(Branch)}} {{Q(TrackingRef)}}
                [ "$(git -C {{Q(repo)}} rev-parse {{Q(TrackingRef)}})" = "$task" ]
                printf 'T=%s\n' "$task"
                """);
            setup.Exit.ShouldBe(0, "c0824-v43: seed; " + setup.Output);
            var sha = setup.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("T=", StringComparison.Ordinal))[2..].Trim();
            return new World { Fixture = fixture, Bare = bare, Repo = repo, Publisher = publisher, BranchSha = sha };
        }

        /// <summary>The legacy mirror shape the runner's MirrorAsync creates: <c>worktree add -B &lt;b&gt;</c> at origin's tip.</summary>
        public async Task AddLegacyMirrorAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Mirror)!);
            var add = await C994ScriptProcess.Run("git", "-C", Repo, "worktree", "add", "-q", "-B", Branch, Mirror, TrackingRef);
            add.Exit.ShouldBe(0, "c0824-v43: legacy mirror; " + add.Output);
        }

        public RunnerWorkspaceService Runner() => new(Repo, Work, Bare, Process.Start);

        public async Task<string?> RefAsync(string name)
        {
            var read = await C994ScriptProcess.Run("git", "-C", Repo, "rev-parse", "--verify", "-q", name);
            return read.Exit == 0 ? read.Output.Trim() : null;
        }

        public async Task<string?> OriginShaAsync()
        {
            var read = await C994ScriptProcess.Run("git", "--git-dir", Bare, "rev-parse", "--verify", "-q", LocalRef);
            return read.Exit == 0 ? read.Output.Trim() : null;
        }

        /// <summary>The server's origin delete (B-2): an empty-source push leased on the published source SHA.</summary>
        public async Task DeleteOriginBranchWithLeaseAsync()
        {
            var push = await C994ScriptProcess.Run("git", "-C", Publisher, "push", "-q", Bare,
                "--force-with-lease=" + LocalRef + ":" + BranchSha, ":" + LocalRef);
            push.Exit.ShouldBe(0, "c0824-v43: leased origin delete; " + push.Output);
        }

        /// <summary>The audit reads the volume at <c>/work</c>; a linked worktree's two pointers must name that view.</summary>
        public void RewriteMirrorPointersToWork()
        {
            foreach (var path in new[] { Path.Combine(Repo, ".git", "worktrees", "task-0824cafe", "gitdir"), Path.Combine(Mirror, ".git") })
            {
                var text = File.ReadAllText(path);
                text.Contains(Work, StringComparison.Ordinal).ShouldBeTrue("c0824-v43: " + path + " records the host mount");
                File.WriteAllText(path, text.Replace(Work, "/work", StringComparison.Ordinal));
            }
        }
    }
}
