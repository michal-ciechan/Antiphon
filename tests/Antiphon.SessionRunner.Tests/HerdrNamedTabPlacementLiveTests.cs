using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0384 V-37: placement-only live probe. Skips when headed herdr is not eligible.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrNamedTabPlacementLiveTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Live_herdr_creates_reuses_and_refuses_on_a_labelled_tab(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await HerdrLiveSession.SkipIfNotEligibleAsync();
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-c384-live-{Guid.NewGuid():N}");
        var logs = Path.Combine(root, "logs");
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(cwd);
        var label = $"antiphon-c384-{Guid.NewGuid():N}"[..28];
        var herdr = new HerdrClient(new HerdrSettings { Enabled = true, ConnectTimeoutMs = 2_000 });
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings { SessionLogPath = logs, PtyHostLingerHours = 0.02 }),
            NullLogger<SessionRunnerRuntime>.Instance,
            herdr);
        var createdTabs = new List<string>();
        var childPids = new List<int>();
        try
        {
            var created = await herdr.WorkspaceCreateAsync(cwd, label, CancellationToken.None);
            createdTabs.Add(created.Tab.TabId);
            var orch = await herdr.TabCreateAsync(
                created.WorkspaceId, cwd, env: null, "Orch", CancellationToken.None);
            createdTabs.Add(orch.TabId);
            try
            {
                await herdr.TabCloseAsync(created.Tab.TabId, CancellationToken.None);
                createdTabs.Remove(created.Tab.TabId);
            }
            catch (HerdrApiException)
            {
                await herdr.TabRenameAsync(created.Tab.TabId, "c384-root", CancellationToken.None);
            }

            var listed = await herdr.TabListAsync(created.WorkspaceId, CancellationToken.None);
            var orchTabs = listed.Where(t => string.Equals(t.Label, "Orch", StringComparison.OrdinalIgnoreCase)).ToList();
            orchTabs.Count.ShouldBe(1);
            orchTabs[0].PaneCount.ShouldBe(1);
            var pane = await herdr.PaneGetAsync(orch.InitialPaneId, CancellationToken.None);
            (pane.Cwd ?? "").Replace('/', '\\')
                .Contains(cwd.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue("pane cwd should be the disposable temp dir");

            var opts = new HerdrLaunchOptions(
                WorkspaceKey: $"c384-{Guid.NewGuid():N}",
                WorkspaceLabel: label,
                WorkspaceCwd: cwd,
                PaneTitle: "c384-live",
                AgentKind: HerdrAgentKinds.Claude,
                TabLabel: "Orch");
            var relaunch = await runtime.CheckHerdrPlacementAsync(
                new HerdrPlacementCheckRequest(Guid.NewGuid(), opts), CancellationToken.None);
            relaunch.Action.ShouldBe("relaunch");
            relaunch.TabId.ShouldBe(orch.TabId);

            await herdr.PaneSendTextAsync(orch.InitialPaneId, "pwsh -NoExit\r", CancellationToken.None);
            await Task.Delay(800);
            var proc = await herdr.PaneProcessInfoAsync(orch.InitialPaneId, CancellationToken.None);
            foreach (var p in proc.ForegroundProcesses ?? [])
            {
                if (proc.ShellPid is int shell && p.Pid == shell)
                    continue;
                childPids.Add(p.Pid);
            }

            var occupied = await Should.ThrowAsync<HerdrLaunchException>(() =>
                runtime.CheckHerdrPlacementAsync(
                    new HerdrPlacementCheckRequest(Guid.NewGuid(), opts), CancellationToken.None));
            occupied.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
            var still = await herdr.TabListAsync(created.WorkspaceId, CancellationToken.None);
            still.Any(t => t.TabId == orch.TabId).ShouldBeTrue();
        }
        finally
        {
            foreach (var pid in childPids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (!p.HasExited)
                        p.Kill(entireProcessTree: true);
                }
                catch
                {
                    // already gone
                }
            }

            foreach (var tabId in createdTabs)
            {
                try { await herdr.TabCloseAsync(tabId, CancellationToken.None); }
                catch { /* best-effort */ }
            }

            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
