using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[Category("Headed")]
[Category("OptIn")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelFollowLiveTests
{
    [Test]
    public async Task Owned_tab_and_workspace_getters_support_rename_follow_validation()
    {
        var selected = Environment.GetEnvironmentVariable("ANTIPHON_C462_HERDR_SESSION");
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1" || string.IsNullOrWhiteSpace(selected))
            throw new SkipTestException("V-21 needs ANTIPHON_HEADED_TESTS=1 and ANTIPHON_C462_HERDR_SESSION naming an already test-owned instance; no default socket is used.");
        var client = new HerdrClient(new HerdrSettings { Enabled = true, Session = selected });
        await client.ConnectAndValidateAsync(CancellationToken.None);
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c462-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? ownedWorkspace = null;
        try
        {
            var label = "c462-" + Guid.NewGuid().ToString("N");
            var created = await client.WorkspaceCreateAsync(root, label, CancellationToken.None);
            ownedWorkspace = created.WorkspaceId;
            var tabId = created.Tab.TabId;
            foreach (var stage in new[] { "before", "after" })
            {
                if (stage == "after")
                {
                    await client.TabRenameAsync(tabId, label + "-tab", CancellationToken.None);
                    // Operator-fixture action, never part of the production observer.
                    await client.SendRequestAsync("workspace.rename", new { workspace_id = ownedWorkspace, label = label + "-workspace" }, CancellationToken.None);
                }
                var rawTab = await client.SendRequestAsync("tab.get", new { tab_id = tabId }, CancellationToken.None);
                var rawWorkspace = await client.SendRequestAsync("workspace.get", new { workspace_id = ownedWorkspace }, CancellationToken.None);
                Console.WriteLine($"V-21 {stage} tab.get {rawTab.GetRawText()}");
                Console.WriteLine($"V-21 {stage} workspace.get {rawWorkspace.GetRawText()}");
                var tab = await client.TabGetAsync(tabId, CancellationToken.None);
                var ws = await client.WorkspaceGetAsync(ownedWorkspace, CancellationToken.None);
                tab.TabId.ShouldBe(tabId); tab.WorkspaceId.ShouldBe(ownedWorkspace); ws.WorkspaceId.ShouldBe(ownedWorkspace); tab.PaneCount.ShouldBe(1);
                var pick = new HerdrNamedTabResolver(HerdrNamedTabResolver.HostLabelComparer).PickUniqueSinglePaneTab(
                    await client.TabListAsync(ownedWorkspace, CancellationToken.None), await client.PaneListAsync(ownedWorkspace, CancellationToken.None), ownedWorkspace, tab.Label);
                pick!.Pane.PaneId.ShouldBe(created.RootPane.PaneId);
                if (stage == "after")
                {
                    tab.Label.ShouldBe(label + "-tab"); ws.Label.ShouldBe(label + "-workspace");
                    await using var runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings { SessionLogPath = root }), NullLogger<SessionRunnerRuntime>.Instance, client);
                    var result = await runtime.CheckHerdrPlacementAsync(new(Guid.NewGuid(), new("c462-unused", ws.Label, root, "fixture", TabLabel: tab.Label)), CancellationToken.None);
                    result.TabId.ShouldBe(tabId); result.PaneId.ShouldBe(created.RootPane.PaneId);
                }
            }
        }
        finally
        {
            if (ownedWorkspace is not null) await client.SendRequestAsync("workspace.close", new { workspace_id = ownedWorkspace }, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }
}
