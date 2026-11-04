using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1702 A-2: attach compares the occupant's directory with the admitted workspace. A side that cannot be
/// resolved is never the admitted workspace, so two unresolvable directories are refused, not equal.
/// </summary>
public sealed class HerdrAttachWorkspaceGuardTests
{
    [Test]
    [Arguments("control")]
    [Arguments("both-unresolvable")]
    [Arguments("occupant-unresolvable")]
    [Arguments("expected-unresolvable")]
    public async Task C1702_Attach_refuses_an_unresolvable_workspace(string shape)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var root = Path.Combine(h.Settings.SessionLogPath, "c1702");
        var real = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        // A dangling link: every path through it is unresolvable.
        var dangling = Path.Combine(root, "gone");
        Directory.CreateSymbolicLink(dangling, Path.Combine(root, "missing"));
        RunnerWorkspaceUseGate.Normalize(Path.Combine(dangling, "a")).ShouldBeNull("fixture: unresolvable directory");
        var occupantCwd = shape is "both-unresolvable" or "occupant-unresolvable" ? Path.Combine(dangling, "a") : real;
        var expected = shape is "both-unresolvable" or "expected-unresolvable" ? Path.Combine(dangling, "b") : real;

        h.Fake.Workspaces[0].Tokens!["antiphon-ws"] = "owned-test";
        h.Fake.SeedDetectedAgent(h.PaneId, "claude");
        h.Fake.SetPaneProcessInfo(h.PaneId, 4242,
            [(4243, "claude.exe", ["claude", "--session-id", h.SessionId.ToString("D")], occupantCwd)]);
        var tab = h.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == h.PaneId));
        await using var child = new HerdrPaneChild(h.Client, h.Settings, NullLogger.Instance,
            () => [new(h.SessionId, tab.TabId, h.PaneId, tab.Number)], new HerdrPaneDisposalConcurrencyTests.Probe(),
            h.Runtime.Placement) { RequiredWorkspacePath = expected };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var attach = child.AttachAsync(new(h.SessionId, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test",
            ExpectedNativeSessionId: h.SessionId), timeout.Token);

        if (shape == "control")
        {
            await attach;
            child.PaneId.ShouldBe(h.PaneId, "control: the admitted workspace attaches");
            return;
        }
        var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => attach);
        refused.Code.ShouldBe("workspace_owner_changed", shape);
        File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse(shape + ": nothing bound");
    }
}
