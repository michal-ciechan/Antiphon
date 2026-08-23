using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0160 §10(d) — pure quad grouping.</summary>
public class HerdrPaneAllocatorTests
{
    [Test]
    public void zero_agents_creates_a_tab()
    {
        HerdrPaneAllocator.Allocate([])
            .ShouldBeOfType<HerdrPaneAllocator.CreateTab>();
    }

    [Test]
    public void one_to_four_fill_the_same_tab_with_the_measured_split_sequence()
    {
        var s1 = Guid.NewGuid();
        var one = new[] { Pane(s1, "w1:t1", "w1:p1", 1) };
        var d1 = HerdrPaneAllocator.Allocate(one).ShouldBeOfType<HerdrPaneAllocator.Split>();
        d1.Direction.ShouldBe("right");
        d1.Ratio.ShouldBe(0.5);
        d1.TargetPaneId.ShouldBe("w1:p1");

        var s2 = Guid.NewGuid();
        var two = new[] { Pane(s1, "w1:t1", "w1:p1", 1), Pane(s2, "w1:t1", "w1:p2", 1) };
        var d2 = HerdrPaneAllocator.Allocate(two).ShouldBeOfType<HerdrPaneAllocator.Split>();
        d2.Direction.ShouldBe("down");
        d2.TargetPaneId.ShouldBe("w1:p1");

        var s3 = Guid.NewGuid();
        var three = new[]
        {
            Pane(s1, "w1:t1", "w1:p1", 1),
            Pane(s2, "w1:t1", "w1:p2", 1),
            Pane(s3, "w1:t1", "w1:p3", 1),
        };
        var d3 = HerdrPaneAllocator.Allocate(three).ShouldBeOfType<HerdrPaneAllocator.Split>();
        d3.Direction.ShouldBe("down");
        d3.TargetPaneId.ShouldBe("w1:p3");

        var s4 = Guid.NewGuid();
        var four = three.Concat([Pane(s4, "w1:t1", "w1:p4", 1)]).ToArray();
        HerdrPaneAllocator.Allocate(four)
            .ShouldBeOfType<HerdrPaneAllocator.CreateTab>("5th agent opens a second tab");
    }

    [Test]
    public void a_gap_from_a_stopped_pane_is_refilled_before_opening_a_new_tab()
    {
        // Four slots, one stopped (gap kept — no reflow). Next launch fills the same tab.
        var live = new[]
        {
            Pane(Guid.NewGuid(), "w1:t1", "w1:p1", 1),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p2", 1),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p4", 1), // p3 gap
        };
        var decision = HerdrPaneAllocator.Allocate(live).ShouldBeOfType<HerdrPaneAllocator.Split>();
        decision.TargetPaneId.ShouldBe("w1:p4"); // three panes → split last down
    }

    [Test]
    public void lowest_numbered_tab_with_a_free_slot_wins()
    {
        var live = new[]
        {
            Pane(Guid.NewGuid(), "w1:t2", "w1:p10", 2),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p1", 1),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p2", 1),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p3", 1),
            Pane(Guid.NewGuid(), "w1:t1", "w1:p4", 1), // tab 1 full
        };
        // Tab 1 full → tab 2 (one pane) gets the split.
        var decision = HerdrPaneAllocator.Allocate(live).ShouldBeOfType<HerdrPaneAllocator.Split>();
        decision.TargetPaneId.ShouldBe("w1:p10");
        decision.Direction.ShouldBe("right");
    }

    [Test]
    public void operator_tabs_are_not_in_the_input_so_they_are_never_split_into()
    {
        // Allocator only receives Antiphon panes — operator tabs are filtered out by the caller.
        // Pin that an empty Antiphon set (operator-only workspace) still CreateTab's.
        HerdrPaneAllocator.Allocate([])
            .ShouldBeOfType<HerdrPaneAllocator.CreateTab>();
    }

    private static HerdrPaneAllocator.LivePane Pane(Guid sessionId, string tabId, string paneId, int tabNumber) =>
        new(sessionId, tabId, paneId, tabNumber);
}
