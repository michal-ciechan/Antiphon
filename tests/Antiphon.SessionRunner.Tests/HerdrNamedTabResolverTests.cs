using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class HerdrNamedTabResolverTests
{
    [Test]
    public void Exact_label_selects_the_single_pane_tab()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.Ordinal);
        var tab = Tab("w1:t2", "w1", "Orch", paneCount: 1);
        var pane = Pane("w1:p2", "w1:t2", "w1");
        var pick = resolver.PickUniqueSinglePaneTab(
            [Tab("w1:t1", "w1", "1", 1), tab],
            [Pane("w1:p1", "w1:t1", "w1"), pane],
            "w1",
            "Orch");
        pick.ShouldNotBeNull();
        pick!.Tab.TabId.ShouldBe("w1:t2");
        pick.Pane.PaneId.ShouldBe("w1:p2");
    }

    [Test]
    public void Comparer_is_explicit_ignore_case_matches_orch_and_ordinal_does_not()
    {
        var ignore = new HerdrNamedTabResolver(StringComparer.OrdinalIgnoreCase);
        var ordinal = new HerdrNamedTabResolver(StringComparer.Ordinal);
        var tabs = new[] { Tab("w1:t1", "w1", "Orch", 1) };
        var panes = new[] { Pane("w1:p1", "w1:t1", "w1") };

        ignore.PickUniqueSinglePaneTab(tabs, panes, "w1", "orch").ShouldNotBeNull();
        ordinal.PickUniqueSinglePaneTab(tabs, panes, "w1", "orch").ShouldBeNull();
    }

    [Test]
    public void Two_matches_are_herdr_tab_ambiguous_listing_both_ids()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.OrdinalIgnoreCase);
        var tabs = new[]
        {
            Tab("w1:t1", "w1", "Orch", 1),
            Tab("w1:t2", "w1", "orch", 1),
        };
        var panes = new[]
        {
            Pane("w1:p1", "w1:t1", "w1"),
            Pane("w1:p2", "w1:t2", "w1"),
        };
        var ex = Should.Throw<HerdrLaunchException>(() =>
            resolver.PickUniqueSinglePaneTab(tabs, panes, "w1", "Orch"));
        ex.Code.ShouldBe(HerdrProblemTypes.TabAmbiguous);
        ex.Message.ShouldContain("w1:t1");
        ex.Message.ShouldContain("w1:t2");
        ex.Message.ShouldContain("Orch");
        ex.Message.ShouldContain("orch");
    }

    [Test]
    public void Multi_pane_tab_or_count_disagreement_is_herdr_tab_invalid()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.Ordinal);
        var multi = Should.Throw<HerdrLaunchException>(() =>
            resolver.PickUniqueSinglePaneTab(
                [Tab("w1:t1", "w1", "Orch", 2)],
                [Pane("w1:p1", "w1:t1", "w1"), Pane("w1:p2", "w1:t1", "w1")],
                "w1",
                "Orch"));
        multi.Code.ShouldBe(HerdrProblemTypes.TabInvalid);

        var disagree = Should.Throw<HerdrLaunchException>(() =>
            resolver.PickUniqueSinglePaneTab(
                [Tab("w1:t1", "w1", "Orch", 2)],
                [Pane("w1:p1", "w1:t1", "w1")],
                "w1",
                "Orch"));
        disagree.Code.ShouldBe(HerdrProblemTypes.TabInvalid);
    }

    [Test]
    public void Tab_in_another_workspace_is_never_selected()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.Ordinal);
        resolver.PickUniqueSinglePaneTab(
            [Tab("w2:t1", "w2", "Orch", 1)],
            [Pane("w2:p1", "w2:t1", "w2")],
            "w1",
            "Orch").ShouldBeNull();
    }

    [Test]
    public async Task Tab_list_failure_propagates_and_is_not_absence()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.Ordinal);
        var boom = new HerdrApiException("unavailable", "tab.list failed");
        var ex = await Should.ThrowAsync<HerdrApiException>(() =>
            resolver.TryPickAsync(
                "w1",
                "Orch",
                _ => Task.FromException<IReadOnlyList<HerdrTabInfo>>(boom),
                _ => Task.FromResult<IReadOnlyList<HerdrPaneInfo>>([]),
                CancellationToken.None));
        ex.Code.ShouldBe("unavailable");
    }

    [Test]
    public void Substring_and_trimmed_labels_do_not_match()
    {
        var resolver = new HerdrNamedTabResolver(StringComparer.OrdinalIgnoreCase);
        var tabs = new[] { Tab("w1:t1", "w1", "Orchid", 1), Tab("w1:t2", "w1", " Orch ", 1) };
        var panes = new[] { Pane("w1:p1", "w1:t1", "w1"), Pane("w1:p2", "w1:t2", "w1") };
        resolver.PickUniqueSinglePaneTab(tabs, panes, "w1", "Orch").ShouldBeNull();
    }

    private static HerdrTabInfo Tab(string id, string workspaceId, string label, int paneCount) =>
        new(id, workspaceId, label, Number: 1, paneCount);

    private static HerdrPaneInfo Pane(string id, string tabId, string workspaceId) =>
        new(id, tabId, workspaceId);
}
