using System.Text.Json.Nodes;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
public class HerdrLabelObservationTests
{
    [Test]
    public async Task Rename_follows_only_the_same_binding()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var valid = await f.CollectAsync(); valid.ResultCode.ShouldBe("validated");
        valid.TabLabel.ShouldBe("New"); valid.WorkspaceLabel.ShouldBe("New workspace");
        f.Pane.TabId = "moved";
        (await f.CollectAsync()).TabLabel.ShouldBeNull();
    }

    [Test]
    [Arguments("pane")][Arguments("pane-tab")][Arguments("pane-workspace")]
    [Arguments("tab")][Arguments("tab-workspace")][Arguments("workspace")]
    public async Task Binding_identity_components_must_match(string component)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        // Exercise the strict wire getter as well as the observer's independent identity fence.
        // Otherwise the downstream fence could conceal a missing getter check.
        if (component is "tab" or "workspace")
        {
            f.Fake.TransformResult = (method, json) =>
            {
                if (method != component + ".get") return json;
                var node = JsonNode.Parse(json)!;
                node[component]![component + "_id"] = "other";
                return node.ToJsonString();
            };
            if (component == "tab")
                await Should.ThrowAsync<HerdrProtocolException>(() => f.Client.TabGetAsync(f.Tab.TabId, CancellationToken.None));
            else
                await Should.ThrowAsync<HerdrProtocolException>(() => f.Client.WorkspaceGetAsync(f.Workspace.WorkspaceId, CancellationToken.None));
            f.Fake.TransformResult = null;
        }
        f.Reader.Pane = (p, _) => component switch { "pane" => p with { PaneId = "other" }, "pane-tab" => p with { TabId = "other" }, "pane-workspace" => p with { WorkspaceId = "other" }, _ => p };
        f.Reader.Tab = (t, _) => component switch { "tab" => t with { TabId = "other" }, "tab-workspace" => t with { WorkspaceId = "other" }, _ => t };
        f.Reader.Workspace = (w, _) => component == "workspace" ? w with { WorkspaceId = "other" } : w;
        var result = await f.CollectAsync(); result.ResultCode.ShouldBe("binding_changed"); result.TabLabel.ShouldBeNull(); result.WorkspaceLabel.ShouldBeNull();
    }

    [Test]
    [Arguments("tab.get", "missing")][Arguments("workspace.get", "missing")]
    [Arguments("tab.get", "missing-label")][Arguments("workspace.get", "missing-label")]
    [Arguments("tab.get", "id")][Arguments("workspace.get", "id")]
    [Arguments("tab.get", "failed")][Arguments("workspace.get", "failed")]
    [Arguments("tab.list", "failed")][Arguments("pane.list", "failed")][Arguments("workspace.list", "failed")]
    public async Task Malformed_or_failed_getter_never_yields_a_candidate(string method, string fault)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        (await f.CollectAsync()).ResultCode.ShouldBe("validated");
        if (fault == "failed") f.Fake.FailMethod(method, "unavailable");
        else f.Fake.TransformResult = (m, json) =>
        {
            if (m != method) return json;
            var node = JsonNode.Parse(json)!; var name = method.Split('.')[0];
            if (fault.StartsWith("missing", StringComparison.Ordinal))
                node[name]!.AsObject().Remove(fault == "missing-label" ? "label" : "pane_count");
            else node[name]![name + "_id"] = "other";
            return node.ToJsonString();
        };
        if (fault != "failed")
        {
            if (method == "tab.get")
                await Should.ThrowAsync<HerdrProtocolException>(() => f.Client.TabGetAsync(f.Tab.TabId, CancellationToken.None));
            else
                await Should.ThrowAsync<HerdrProtocolException>(() => f.Client.WorkspaceGetAsync(f.Workspace.WorkspaceId, CancellationToken.None));
        }
        var result = await f.CollectAsync(); result.ResultCode.ShouldBe("read_failed"); result.TabLabel.ShouldBeNull(); result.WorkspaceLabel.ShouldBeNull();
    }

    [Test]
    public async Task Duplicate_tab_label_is_ambiguous()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Fake.SeedTab("w1", "new");
        (await f.CollectAsync()).ResultCode.ShouldBe(HerdrProblemTypes.TabAmbiguous);
    }

    [Test]
    public async Task Case_only_rename_uses_the_host_comparer()
    {
        await using var f = new HerdrLabelFollowFixture("NEW"); await f.StartAsync();
        (await f.CollectAsync()).TabLabel.ShouldBe("New");
        f.Fake.SeedTab("w1", "new");
        (await f.CollectAsync(StringComparer.OrdinalIgnoreCase)).ResultCode.ShouldBe(HerdrProblemTypes.TabAmbiguous);
        (await f.CollectAsync(StringComparer.Ordinal)).TabLabel.ShouldBe("New");
    }

    [Test]
    public async Task Missing_tab_match_is_not_a_rename()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Reader.Tabs = _ => [];
        (await f.CollectAsync()).ResultCode.ShouldBe("tab_missing");
    }

    [Test][Arguments(0)][Arguments(2)]
    public async Task Reported_pane_count_must_be_one(int count)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Tab.ReportedPaneCount = count;
        (await f.CollectAsync()).ResultCode.ShouldBe(HerdrProblemTypes.TabInvalid);
    }

    [Test][Arguments(0)][Arguments(2)]
    public async Task Enumerated_pane_count_must_be_one(int count)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Reader.Panes = p => count == 0 ? [] : [p[0], p[0] with { PaneId = "other" }];
        (await f.CollectAsync()).ResultCode.ShouldBe(HerdrProblemTypes.TabInvalid);
    }

    [Test]
    public async Task Selected_tab_must_be_bound()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Reader.Tabs = t => [t[0] with { TabId = "other" }];
        f.Reader.Panes = p => [p[0] with { TabId = "other" }];
        (await f.CollectAsync()).ResultCode.ShouldBe("binding_changed");
    }

    [Test]
    public async Task Only_the_bound_pane_can_validate_the_tab()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Reader.Panes = p => [p[0] with { PaneId = "other" }];
        (await f.CollectAsync()).ResultCode.ShouldBe("binding_changed");
    }

    [Test]
    public async Task Final_read_change_invalidates_collection() => await Final_reads_must_match_initial_and_list_values("tab-label");

    [Test]
    [Arguments("pane")][Arguments("pane-tab")][Arguments("pane-workspace")]
    [Arguments("tab-label")][Arguments("workspace-label")][Arguments("tab-list-label")]
    [Arguments("tab-list-count")][Arguments("workspace-list-label")][Arguments("workspace-list-token")]
    public async Task Final_reads_must_match_initial_and_list_values(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Reader.Pane = (p, n) => n != 2 ? p : arm switch { "pane" => p with { PaneId = "other" }, "pane-tab" => p with { TabId = "other" }, "pane-workspace" => p with { WorkspaceId = "other" }, _ => p };
        f.Reader.Tab = (t, n) => arm == "tab-label" && n == 2 ? t with { Label = "later" }
            : arm == "tab-list-count" ? t with { PaneCount = 2 } : t;
        f.Reader.Workspace = (w, n) => arm == "workspace-label" && n == 2 ? w with { Label = "later" } : w;
        f.Reader.Tabs = t => arm == "tab-list-label" ? [t[0] with { Label = "NEW" }] : t;
        f.Reader.Workspaces = w => arm switch { "workspace-list-label" => [w[0] with { Label = "later" }], "workspace-list-token" => [w[0] with { Tokens = new Dictionary<string, string> { ["antiphon-ws"] = "foreign" } }], _ => w };
        var result = await f.CollectAsync(); result.ResultCode.ShouldBe("read_changed"); result.TabLabel.ShouldBeNull();
    }

    [Test]
    public async Task Workspace_follow_requires_original_untagged_selection()
    {
        await using var f = new HerdrLabelFollowFixture(provenance: HerdrWorkspaceSelection.ManagedToken); await f.StartAsync();
        var result = await f.CollectAsync(); result.TabLabel.ShouldBe("New"); result.WorkspaceLabel.ShouldBeNull();
    }

    [Test][Arguments("none")][Arguments("foreign")]
    public async Task Workspace_follow_requires_current_untagged_state(string token)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); f.Workspace.Tokens["antiphon-ws"] = token;
        var result = await f.CollectAsync(); result.TabLabel.ShouldBe("New"); result.WorkspaceLabel.ShouldBeNull();
    }

    [Test][Arguments("duplicate")][Arguments("preemption")][Arguments("other-id")]
    public async Task Workspace_follow_requires_unique_next_launch_resolution(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        if (arm == "duplicate") f.Fake.SeedWorkspace("w2", f.Workspace.Label);
        else if (arm == "preemption") f.Fake.SeedWorkspace("w2", "other", new Dictionary<string, string> { ["antiphon-ws"] = "none" });
        else f.Reader.Workspaces = w => [w[0] with { WorkspaceId = "other" }];
        var result = await f.CollectAsync(); result.TabLabel.ShouldBeNull(); result.WorkspaceLabel.ShouldBeNull();
    }

    [Test]
    public async Task Ambiguous_workspace_blocks_simultaneous_tab_rename() => await Workspace_follow_requires_unique_next_launch_resolution("duplicate");

    [Test]
    public async Task Workspace_resolution_remains_ordinal()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); f.Fake.SeedWorkspace("w2", "NEW WORKSPACE");
        (await f.CollectAsync()).WorkspaceLabel.ShouldBe("New workspace");
    }

    [Test][Arguments("managed")][Arguments("foreign")]
    public async Task Managed_workspace_keeps_snapshot_while_tab_follows(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(provenance: arm == "managed" ? HerdrWorkspaceSelection.ManagedToken : HerdrWorkspaceSelection.UniqueUntaggedLabel);
        await f.StartAsync(); if (arm == "foreign") f.Workspace.Tokens["antiphon-ws"] = "other";
        var result = await f.CollectAsync(); result.TabLabel.ShouldBe("New"); result.WorkspaceLabel.ShouldBeNull();
        HerdrPaneSidecar.TryLoad(f.Path)!.WorkspaceLabel.ShouldBe("Old workspace");
    }

    [Test]
    [Arguments("")][Arguments(" ")][Arguments(" leading")][Arguments("trailing ")]
    [Arguments("N\0UL")][Arguments("L\nF")][Arguments("D\u007fEL")]
    [Arguments("255")][Arguments("256")][Arguments("257")][Arguments("日本語😀")]
    public async Task Unrepresentable_labels_preserve_pins(string label)
    {
        if (int.TryParse(label, out var length)) label = new string('x', length);
        var valid = label == "日本語😀" || label.Length is 255 or 256;
        foreach (var workspace in new[] { false, true })
        {
            await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
            if (workspace) f.Workspace.Label = label; else f.Tab.Label = label;
            var result = await f.CollectAsync(); result.ResultCode.ShouldBe(valid ? "validated" : "label_invalid");
            if (!valid) { result.TabLabel.ShouldBeNull(); result.WorkspaceLabel.ShouldBeNull(); }
        }
    }

    [Test][Arguments("attached")][Arguments("none")][Arguments("workspace")][Arguments("packed")]
    public async Task Attached_or_unpinned_binding_never_becomes_named(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(arm is "workspace" or "packed" or "none" ? null : "Old", arm == "none" ? null : "Old workspace");
        if (arm == "attached") f.Binding = f.Binding with { Origin = HerdrPaneOrigins.Attached };
        await f.StartAsync(); if (arm == "packed") f.Tab.ReportedPaneCount = 2;
        var result = await f.CollectAsync(); result.TabLabel.ShouldBeNull();
        if (arm == "workspace") result.WorkspaceLabel.ShouldBe("New workspace"); else result.WorkspaceLabel.ShouldBeNull();
        if (arm is "attached" or "none") f.GetterCount.ShouldBe(0);
    }

    [Test][Arguments("absent")][Arguments("state")][Arguments("intent")]
    public async Task Missing_or_unknown_intent_is_ineligible(string arm)
    {
        await using var f = new HerdrLabelFollowFixture();
        f.Binding = f.Binding with { LabelFollow = arm switch { "absent" => null, "state" => f.Binding.LabelFollow! with { Version = 99 }, _ => f.Binding.LabelFollow! with { Intent = f.Binding.LabelFollow!.Intent with { Version = 99 } } } };
        await f.StartAsync(); (await f.CollectAsync()).TabLabel.ShouldBeNull(); f.GetterCount.ShouldBe(0);
    }

    [Test]
    public async Task Missing_generation_is_not_inferred()
    {
        await using var f = new HerdrLabelFollowFixture(); f.Binding = f.Binding with { AcceptedStartedAt = null };
        await f.StartAsync(); (await f.CollectAsync()).TabLabel.ShouldBeNull(); f.GetterCount.ShouldBe(0);
    }

    [Test][Arguments("null")][Arguments("zero")][Arguments("absent")]
    public async Task Collection_requires_positive_recorded_child(string arm)
    {
        await using var f = new HerdrLabelFollowFixture();
        f.Binding = f.Binding with { ChildPid = arm == "null" ? null : arm == "zero" ? 0 : 9000 };
        await f.StartAsync(); (await f.CollectAsync()).ResultCode.ShouldBe("child_unverified");
    }
}
