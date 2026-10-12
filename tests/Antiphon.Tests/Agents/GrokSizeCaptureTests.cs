using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>Explicit, zero-model-input native capture harness for CARD-0861.</summary>
[Category("Integration")]
[Category("OptIn")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokSizeCaptureTests
{
    [Test]
    public async Task Capture_native_sizes()
    {
        Environment.GetEnvironmentVariable("C861_CAPTURE").ShouldBe("1");
        Environment.GetEnvironmentVariable("ANTIPHON_PTY_AUDIT").ShouldNotBe("1");
        var repo = Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot;
        var output = Path.Combine(repo, ".antiphon", "c861-captures");
        Directory.CreateDirectory(output);
        var captured = new List<object>();
        foreach (var (cols, rows) in new[] { (80, 24), (100, 30), (120, 30), (160, 50), (200, 60), (40, 20) })
        {
            foreach (var mode in new[] { "authenticated", "empty-home" })
            {
                var root = Path.Combine(Path.GetTempPath(), "c861-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var env = new Dictionary<string, string> { ["ANTIPHON_PTY_AUDIT"] = "0" };
                if (mode == "empty-home") env["GROK_HOME"] = Path.Combine(root, "home");
                var session = Guid.NewGuid();
                await using var runner = new PtyAgentRunner();
                Process? child = null;
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await runner.StartAsync("grok", ["--always-approve", "--no-alt-screen", "--session-id", session.ToString("D")],
                        cwd: root, env: env, cols: cols, rows: rows, ct: deadline.Token);
                    child = Process.GetProcessById(runner.Pid!.Value);
                    var trustRecorded = false;
                    var clock = Stopwatch.StartNew();
                    string screen = "";
                    while (clock.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        await Task.Delay(100, deadline.Token);
                        screen = runner.SnapshotScreen();
                        // Welcome has no device code; never export OAuth approval screens.
                        if (GrokSignInPromptDetector.IsVisibleOnScreen(screen)) break;
                        if (GrokTrustPromptDetector.IsVisibleOnScreen(screen) && !trustRecorded)
                        {
                            captured.Add(new { cols, rows, mode, kind = "trust", screen, session, capturedAt = DateTime.UtcNow });
                            trustRecorded = true;
                            await runner.WriteAsync("y", deadline.Token);
                        }
                        if (clock.Elapsed >= TimeSpan.FromSeconds(3)
                            && screen.Contains(GrokStartupScreen.EnabledHint, StringComparison.Ordinal)) break;
                    }
                    // No auth interaction occurs in the empty home. Reject any unexpected
                    // device approval screen before writing even a rendered frame to disk.
                    screen.ShouldNotContain("Waiting for approval", customMessage: "no-device-flow");
                    screen.ShouldNotContain("browser shows this code", customMessage: "no-device-code");
                    screen.Split('\n').Length.ShouldBe(rows, "native row count");
                    captured.Add(new { cols, rows, mode, kind = "final", screen, session, capturedAt = DateTime.UtcNow });
                }
                finally
                {
                    (await runner.KillAsync(TimeSpan.FromSeconds(3))).ShouldBeTrue("owned Grok joined");
                    if (child is not null)
                    {
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                        child.HasExited.ShouldBeTrue();
                        child.Dispose();
                    }
                    await runner.DisposeAsync();
                    Directory.Delete(root, true);
                }
                await File.WriteAllTextAsync(Path.Combine(output, "captures.json"), JsonSerializer.Serialize(captured));
                Console.WriteLine($"C861 CAPTURE {cols}x{rows} mode={mode} childJoined=true modelInputs=0");
            }
        }
        captured.Count.ShouldBeGreaterThanOrEqualTo(12);
    }
}
