using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677RetentionScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-1677 S1 CP-1 (portable). The read-only inventory and the unmatched-name refusal (plan §3.4).</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(15_000)]
public sealed class C1677RetentionInventoryTests
{
    [Test]
    public async Task Inventory_lists_root_and_retiring_entries(CancellationToken ct)
    {
        // V-18. The task-cffa0c4b-uA shape (a registered detached worktree), a slot leaf, a symlink, a mirror, a residue.
        using var s = Create();
        var unmatched = s.Worktrees + "/task-cffa0c4b-uA";
        var head = Git(s.Repository, "rev-parse", "HEAD").Trim();
        Git(s.Repository, "worktree", "add", "--detach", unmatched, head);
        Directory.CreateDirectory(s.Worktrees + "/ws-01");
        File.CreateSymbolicLink(s.Worktrees + "/task-dddddddd", Path.GetTempPath());
        var m = s.Mirror();
        var residue = s.Mirror("c1677003");
        Directory.CreateDirectory(s.Retiring);
        Git(s.Repository, "worktree", "move", residue.Path, s.Retiring + "/task-c1677003.0badf00d");

        var inventory = await s.Engine().InventoryAsync(ct);
        inventory.Complete.ShouldBeTrue();
        var byName = inventory.Entries.ToDictionary(e => e.Root + "/" + e.Name);
        byName.Keys.Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            "retiring/task-c1677003.0badf00d", "worktrees/task-a1677001", "worktrees/task-cffa0c4b-uA",
            "worktrees/task-dddddddd", "worktrees/ws-01",
        });
        byName["worktrees/task-cffa0c4b-uA"].ShouldSatisfyAllConditions(
            e => e.Registered.ShouldBeTrue(), e => e.SlotLeaf.ShouldBeFalse(),
            e => e.IsDirectory.ShouldBeTrue(), e => e.IsSymlink.ShouldBeFalse());
        byName["worktrees/ws-01"].ShouldSatisfyAllConditions(
            e => e.Registered.ShouldBeFalse(), e => e.SlotLeaf.ShouldBeTrue(), e => e.IsDirectory.ShouldBeTrue());
        byName["worktrees/task-dddddddd"].ShouldSatisfyAllConditions(
            e => e.IsSymlink.ShouldBeTrue(), e => e.IsDirectory.ShouldBeFalse(), e => e.Registered.ShouldBeFalse());
        byName["worktrees/task-a1677001"].ShouldSatisfyAllConditions(
            e => e.Registered.ShouldBeTrue(), e => e.IsDirectory.ShouldBeTrue(), e => e.SlotLeaf.ShouldBeFalse());
        byName["retiring/task-c1677003.0badf00d"].ShouldSatisfyAllConditions(
            e => e.Registered.ShouldBeTrue(), e => e.IsDirectory.ShouldBeTrue());
        Directory.Exists(m.Path).ShouldBeTrue();
    }

    [Test]
    public async Task Inventory_is_read_only(CancellationToken ct)
    {
        // V-18b (G-40). Only `worktree list --porcelain` runs; a stale registration survives.
        using var s = Create();
        var stale = s.StaleRegistration("a16770ff");
        var m = s.Mirror();
        var before = Tree(m.Path);
        await s.Engine().InventoryAsync(ct);
        s.Argv.Select(call => string.Join(' ', Strip(call.Args))).Distinct().ShouldBe(["worktree list --porcelain"]);
        Tree(m.Path).ShouldBe(before);
        s.Registered(stale).ShouldBeTrue();
    }

    [Test]
    public async Task Unmatched_name_is_refused_in_execute(CancellationToken ct)
    {
        // V-19. Retention never acts on a name outside task-<8 hex>, in any mode.
        using var s = Create();
        var path = s.Worktrees + "/task-cffa0c4b-uA";
        var head = Git(s.Repository, "rev-parse", "HEAD").Trim();
        Git(s.Repository, "worktree", "add", "--detach", path, head);
        var m = new MirrorTree(path, "feat/card-task-cffa0c4b", Guid.Parse("cffa0c4b-0000-4000-8000-000000001677"), head);
        var before = Tree(path);
        var response = await s.Engine().RetainAsync(Request(m), ct);
        s.Kept(response, WorkspaceRetentionCodes.OutsideScope, m, before);
    }
}
