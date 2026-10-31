using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1622. The one-pass rollup has to agree with the parent-chain walk it replaces:
/// depth past two, branches, a missing parent, and a cycle.
/// </summary>
[Category("Unit")]
public class AgentTaskSubtreeRollupTests
{
    // 30s matches the chain test. A missing cycle guard has to fail this test; the body is
    // milliseconds, and a budget near twice that sample would flake on a loaded host (CARD-1622).
    [Test]
    [Timeout(30_000)]
    public void Rollup_matches_the_parent_chain_walk_for_depth_branches_missing_parents_and_cycles(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootId = Guid.NewGuid();
        var root = Node(rootId, rootId, null, 10m, "root");
        var a = Node(Guid.NewGuid(), rootId, rootId, 1m, "a");
        var a1 = Node(Guid.NewGuid(), rootId, a.Id, 0.5m, "a1");
        var a1a = Node(Guid.NewGuid(), rootId, a1.Id, 0.25m, "a1a");
        var a2 = Node(Guid.NewGuid(), rootId, a.Id, 0.75m, "a2");
        var b = Node(Guid.NewGuid(), rootId, rootId, 2m, "b");
        var b1 = Node(Guid.NewGuid(), rootId, b.Id, 0.1m, "b1");
        var missingParent = Guid.NewGuid();
        var orphan = Node(Guid.NewGuid(), rootId, missingParent, 4m, "orphan");
        var dangling = Node(Guid.NewGuid(), rootId, Guid.NewGuid(), 3m, "dangling");
        var self = Node(Guid.NewGuid(), rootId, null, 6m, "self");
        self.ParentTaskId = self.Id;
        var cycleA = Node(Guid.NewGuid(), rootId, null, 8m, "cycleA");
        var cycleB = Node(Guid.NewGuid(), rootId, null, 9m, "cycleB");
        var cycleC = Node(Guid.NewGuid(), rootId, cycleA.Id, 7m, "cycleC");
        var side = Node(Guid.NewGuid(), rootId, cycleB.Id, 0.01m, "side");
        cycleA.ParentTaskId = cycleB.Id;
        cycleB.ParentTaskId = cycleC.Id;

        var family = new List<AgentTask>
        {
            root, a, a1, a1a, a2, b, b1, orphan, dangling, self, cycleA, cycleB, cycleC, side,
        };

        var rollup = AgentTaskCostWalk.Rollup(family);
        var costs = AgentTaskCostWalk.Calculate(family, family);

        rollup[root.Id].SubtreeCostUsd.ShouldBe(14.6m, "root keeps every present descendant and nothing else");
        rollup[root.Id].ChildCount.ShouldBe(2);
        rollup[a.Id].SubtreeCostUsd.ShouldBe(2.5m);
        rollup[a.Id].ChildCount.ShouldBe(2);
        rollup[a1.Id].SubtreeCostUsd.ShouldBe(0.75m);
        rollup[a1.Id].ChildCount.ShouldBe(1);
        rollup[a1a.Id].SubtreeCostUsd.ShouldBe(0.25m);
        rollup[a1a.Id].ChildCount.ShouldBe(0);
        rollup[b.Id].SubtreeCostUsd.ShouldBe(2.1m);
        rollup[b.Id].ChildCount.ShouldBe(1);
        rollup[orphan.Id].SubtreeCostUsd.ShouldBe(4m, "a missing parent contributes no further cost");
        rollup[orphan.Id].ChildCount.ShouldBe(0);
        rollup[dangling.Id].SubtreeCostUsd.ShouldBe(3m);
        rollup[self.Id].SubtreeCostUsd.ShouldBe(6m, "a self-parent is not its own child or a second copy of its cost");
        rollup[self.Id].ChildCount.ShouldBe(0);
        rollup[cycleA.Id].SubtreeCostUsd.ShouldBe(24.01m);
        rollup[cycleA.Id].ChildCount.ShouldBe(1);
        rollup[cycleB.Id].SubtreeCostUsd.ShouldBe(24.01m);
        rollup[cycleB.Id].ChildCount.ShouldBe(2);
        rollup[cycleC.Id].SubtreeCostUsd.ShouldBe(24.01m);
        rollup[cycleC.Id].ChildCount.ShouldBe(1);
        rollup[side.Id].SubtreeCostUsd.ShouldBe(0.01m);
        rollup[side.Id].ChildCount.ShouldBe(0);

        foreach (var task in family)
        {
            var expected = Legacy(task, family);
            rollup[task.Id].SubtreeCostUsd.ShouldBe(expected.Cost, task.Title);
            rollup[task.Id].ChildCount.ShouldBe(expected.Children, task.Title);
            costs[task.Id].ShouldBe(expected.Cost, task.Title);
        }

        AgentTaskService.IsDescendantOf(a1a, root.Id, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(a1a, b.Id, family).ShouldBeFalse();
        AgentTaskService.IsDescendantOf(orphan, root.Id, family).ShouldBeFalse();
        AgentTaskService.IsDescendantOf(orphan, missingParent, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(dangling, root.Id, family).ShouldBeFalse();
        AgentTaskService.IsDescendantOf(side, cycleA.Id, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(side, cycleB.Id, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(side, cycleC.Id, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(cycleA, cycleA.Id, family).ShouldBeTrue();
        AgentTaskService.IsDescendantOf(cycleA, orphan.Id, family).ShouldBeFalse();
        AgentTaskService.IsDescendantOf(self, self.Id, family).ShouldBeTrue();

        var withoutMiddle = family.Where(task => task.Id != a1.Id).ToList();
        var partial = AgentTaskCostWalk.Rollup(withoutMiddle);
        partial[root.Id].SubtreeCostUsd.ShouldBe(Legacy(root, withoutMiddle).Cost);
        partial[root.Id].SubtreeCostUsd.ShouldBe(13.85m, "a1a stops at the missing middle node");
        partial[a.Id].SubtreeCostUsd.ShouldBe(1.75m);
        partial[a1a.Id].SubtreeCostUsd.ShouldBe(0.25m);

        var external = Node(missingParent, missingParent, null, 1m, "external");
        AgentTaskCostWalk.Calculate([external], family)[external.Id].ShouldBe(5m);

        var drifted = Node(root.Id, rootId, null, 50m, "drifted-root");
        AgentTaskCostWalk.Calculate([drifted], family)[root.Id].ShouldBe(54.6m);
    }

    [Test]
    public void ToSummary_uses_the_caller_row_cost_when_the_family_copy_differs()
    {
        // Get reads the caller row and the family in separate queries. The old walk summed
        // task.CostUsd from the caller plus descendant costs from the family (git 2b471b21c).
        var rootId = Guid.NewGuid();
        var stored = Node(rootId, rootId, null, 1m, "stored");
        var child = Node(Guid.NewGuid(), rootId, rootId, 2m, "child");
        var family = new List<AgentTask> { stored, child };
        var caller = Node(rootId, rootId, null, 5m, "caller");

        var summary = AgentTaskService.ToSummary(
            caller, AgentTaskCostWalk.Rollup(family), AgentTaskScopeLabels.Empty);

        summary.CostUsd.ShouldBe(5m, "the response row keeps the caller's own cost");
        summary.SubtreeCostUsd.ShouldBe(Legacy(caller, family).Cost);
        summary.SubtreeCostUsd.ShouldBe(7m, "5 from the caller plus the child's 2, not the stored 1");
        summary.ChildCount.ShouldBe(1);

        var lower = Node(rootId, rootId, null, 0.25m, "lower-caller");
        var lowerSummary = AgentTaskService.ToSummary(
            lower, AgentTaskCostWalk.Rollup(family), AgentTaskScopeLabels.Empty);
        lowerSummary.SubtreeCostUsd.ShouldBe(Legacy(lower, family).Cost);
        lowerSummary.SubtreeCostUsd.ShouldBe(2.25m);
    }

    [Test]
    public void Rollup_matches_the_parent_chain_walk_when_an_id_is_duplicated()
    {
        // The primary key makes this unreachable from a database read. The old scan kept
        // walking the first stored row when a later row reused its id (CARD-1622 A-1).
        var shared = Guid.NewGuid();
        var xId = Guid.NewGuid();
        var x = Node(xId, xId, null, 100m, "X");
        var a1 = Node(shared, xId, xId, 1m, "A1");
        var c = Node(Guid.NewGuid(), xId, shared, 4m, "C");
        var a2 = Node(shared, xId, c.Id, 2m, "A2");

        AssertDuplicateFamily(new List<AgentTask> { x, a1, a2, c }, x, a1, c);
        AssertDuplicateFamily(new List<AgentTask> { x, a2, a1, c }, x, a2, c);
    }

    private static void AssertDuplicateFamily(
        List<AgentTask> family, params AgentTask[] uniqueRows)
    {
        var rollup = AgentTaskCostWalk.Rollup(family);
        foreach (var task in uniqueRows)
        {
            var expected = Legacy(task, family);
            rollup[task.Id].SubtreeCostUsd.ShouldBe(expected.Cost, task.Title);
            rollup[task.Id].ChildCount.ShouldBe(expected.Children, task.Title);
        }
    }

    [Test]
    [Timeout(30_000)]
    public void Rollup_of_a_two_thousand_task_chain_matches_the_closed_form(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const int count = 2000;
        var rootId = Guid.NewGuid();
        var family = new List<AgentTask>(count);
        Guid? parent = null;
        for (var i = 0; i < count; i++)
        {
            var id = i == 0 ? rootId : Guid.NewGuid();
            family.Add(Node(id, rootId, parent, 1m, "n" + i));
            parent = id;
        }

        var rollup = AgentTaskCostWalk.Rollup(family);

        for (var i = 0; i < count; i++)
        {
            var figures = rollup[family[i].Id];
            figures.SubtreeCostUsd.ShouldBe(count - i);
            figures.ChildCount.ShouldBe(i == count - 1 ? 0 : 1);
        }
    }

    [Test]
    [Timeout(30_000)]
    public void Rollup_of_a_wide_fan_matches_the_closed_form_and_stays_near_linear(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Warm the JIT, then compare two widths of the same shape. The review measured about
        // 7 ms for 2,000 direct children on a loaded host; an absolute 100 ms cap flakes on a
        // pause. Four times the nodes must stay under 12x the narrower run, plus a second.
        // The replaced per-row walk was about 1.9 s at 2,000 and about 170 s at 10,000.
        RollupFan(256);

        var narrow = TimeFan(2_000);
        var wide = TimeFan(8_000);

        narrow.Cost.ShouldBe(1m + (0.01m * 2_000));
        narrow.Children.ShouldBe(2_000);
        wide.Cost.ShouldBe(1m + (0.01m * 8_000));
        wide.Children.ShouldBe(8_000);
        var limitMs = (Math.Max(narrow.Elapsed.TotalMilliseconds, 1) * 12) + 1_000;
        wide.Elapsed.TotalMilliseconds.ShouldBeLessThan(limitMs);
    }

    private readonly record struct FanSample(decimal Cost, int Children, TimeSpan Elapsed);

    private static void RollupFan(int children) => _ = AgentTaskCostWalk.Rollup(Fan(children));

    private static FanSample TimeFan(int children)
    {
        var family = Fan(children);
        var watch = Stopwatch.StartNew();
        var rollup = AgentTaskCostWalk.Rollup(family);
        watch.Stop();
        var root = rollup[family[0].Id];
        return new FanSample(root.SubtreeCostUsd, root.ChildCount, watch.Elapsed);
    }

    private static List<AgentTask> Fan(int children)
    {
        var rootId = Guid.NewGuid();
        var family = new List<AgentTask>(children + 1)
        {
            Node(rootId, rootId, null, 1m, "root"),
        };
        for (var i = 0; i < children; i++)
            family.Add(Node(Guid.NewGuid(), rootId, rootId, 0.01m, "child"));
        return family;
    }

    private static AgentTask Node(Guid id, Guid rootId, Guid? parentId, decimal cost, string title) =>
        new()
        {
            Id = id,
            RootTaskId = rootId,
            ParentTaskId = parentId,
            CostUsd = cost,
            Title = title,
        };

    private readonly record struct LegacyFigures(decimal Cost, int Children);

    /// <summary>The pre-CARD-1622 walk: linear parent lookup and a per-row family scan.</summary>
    private static LegacyFigures Legacy(AgentTask task, IReadOnlyList<AgentTask> family)
    {
        var total = task.CostUsd;
        var children = 0;
        foreach (var other in family)
        {
            if (other.Id == task.Id)
                continue;
            if (LegacyIsDescendant(other, task.Id, family))
                total += other.CostUsd;
            if (other.ParentTaskId == task.Id)
                children++;
        }

        return new LegacyFigures(total, children);
    }

    private static bool LegacyIsDescendant(AgentTask candidate, Guid ancestorId, IReadOnlyList<AgentTask> family)
    {
        var seen = 0;
        var current = candidate;
        while (current.ParentTaskId is { } parentId)
        {
            if (parentId == ancestorId)
                return true;
            if (++seen > family.Count)
                return false;
            var next = family.FirstOrDefault(t => t.Id == parentId);
            if (next is null)
                return false;
            current = next;
        }

        return false;
    }
}
