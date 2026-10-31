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
    [Test]
    public void Rollup_matches_the_parent_chain_walk_for_depth_branches_missing_parents_and_cycles()
    {
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
    public void Rollup_of_two_thousand_direct_children_stays_under_a_tenth_of_a_second()
    {
        const int children = 2000;
        var rootId = Guid.NewGuid();
        var root = Node(rootId, rootId, null, 1m, "root");
        var family = new List<AgentTask>(children + 1) { root };
        for (var i = 0; i < children; i++)
            family.Add(Node(Guid.NewGuid(), rootId, rootId, 0.01m, "child"));

        var watch = Stopwatch.StartNew();
        var rollup = AgentTaskCostWalk.Rollup(family);
        watch.Stop();

        rollup[rootId].SubtreeCostUsd.ShouldBe(1m + (0.01m * children));
        rollup[rootId].ChildCount.ShouldBe(children);
        rollup[family[1].Id].SubtreeCostUsd.ShouldBe(0.01m);
        rollup[family[1].Id].ChildCount.ShouldBe(0);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
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
