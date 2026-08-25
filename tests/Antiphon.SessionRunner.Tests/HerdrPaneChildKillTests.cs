using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0186 S2: KillAsync refusal when a foreign foreground process is in the pane.
/// P8: herdr itself would have closed the pane and killed whatever was in it — the refusal is ours.
/// </summary>
[NotInParallel("SessionLiveness")]
public class HerdrPaneChildKillTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Foreign_foreground_process_kills_our_child_by_pid_leaves_pane_open_and_returns_true()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();

        var settings = new SessionRunnerSettings
        {
            SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-kill-{Guid.NewGuid():N}"),
            PtyHostLingerHours = 0.02,
        };
        var client = new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await fake.WaitUntilListeningAsync(cts.Token);
        await client.WorkspaceCreateAsync(settings.SessionLogPath, "card0186-kill", cts.Token);

        var pane = fake.Workspaces[0].Tabs[0].Panes[0];
        using var dummy = StartDummy();
        try
        {
            fake.SetPaneProcessInfo(
                pane.PaneId,
                shellPid: 1,
                (dummy.Id, "cmd.exe"),
                (99999, "pwsh.exe"));

            var sessionId = Guid.NewGuid();
            var launched = DateTime.UtcNow;
            var sidecar = new HerdrPaneSidecar
            {
                SessionId = sessionId,
                WorkspaceKey = "none",
                WorkspaceId = pane.WorkspaceId,
                TabId = pane.TabId,
                PaneId = pane.PaneId,
                ChildPid = dummy.Id,
                ShellPid = 1,
                LaunchedAtUtc = launched,
                Cwd = settings.SessionLogPath,
                UpdatedAtUtc = launched,
            };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId));

            var child = new HerdrPaneChild(
                client,
                settings,
                NullLogger.Instance,
                () => [],
                new StubProbe(alive: true));
            string? reason = null;
            child.Exited += exit => reason = exit.Reason;
            await child.AttachExistingAsync(sidecar, cts.Token);

            var killed = await child.KillAsync(cts.Token);
            killed.ShouldBeTrue("our child is gone; the pane is not ours to close");
            reason.ShouldBe(HerdrExitReasons.PaneLeftOpen);
            dummy.WaitForExit(5_000).ShouldBeTrue();
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
            fake.Workspaces[0].Tabs[0].Panes.ShouldContain(p => p.PaneId == pane.PaneId);
            fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.close")
                .ShouldBeFalse("foreign process → we must not ask herdr to close the pane");
        }
        finally
        {
            KillBestEffort(dummy);
            try
            {
                if (Directory.Exists(settings.SessionLogPath))
                    Directory.Delete(settings.SessionLogPath, recursive: true);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private static Process StartDummy()
    {
        var psi = new ProcessStartInfo(Cmd, "/d /q /k @echo off & prompt $G")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi) ?? throw new InvalidOperationException("failed to start dummy");
    }

    private static void KillBestEffort(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }

    private sealed class StubProbe(bool alive) : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => alive;
        public string? TryGetProcessName(int pid) => "powershell";
    }
}
