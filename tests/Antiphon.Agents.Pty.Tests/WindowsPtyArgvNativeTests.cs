using System.Diagnostics;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WindowsPtyArgvNativeTests
{
    [Test]
    [Arguments("inbox")]
    [Arguments("modern")]
    public async Task Windows_backends_keep_exact_native_argv(string backend)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows checkpoint selected on Unix");
        var node = ResolveNode();
        // A pwsh-hosted test can have a different AppContext.BaseDirectory. Use the test
        // assembly's output for both the probe and its staged modern ConPTY pair.
        var outputDir = Path.GetDirectoryName(typeof(WindowsPtyArgvNativeTests).Assembly.Location)!;
        var probeDir = Path.Combine(outputDir, "probes");
        var root = Path.Combine(Path.GetTempPath(), "c863-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var capture = Path.Combine(root, "argv.json");
        var expected = new[] { "line one\r\n\"line two\"", "", "C:\\space path\\", "--session-id", Guid.NewGuid().ToString("D") };
        var previousConPtyDir = Environment.GetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar);
        await using var runner = new PtyAgentRunner(backend);
        try
        {
            File.Exists(Path.Combine(probeDir, "argv-echo.js"))
                .ShouldBeTrue("windows-argv-probe-prerequisite: staged argv-echo.js is missing");
            if (backend == "modern")
            {
                var conPtyDir = Path.Combine(outputDir, ConPtyRedistributable.RelativeDirectory);
                File.Exists(Path.Combine(conPtyDir, ConPtyRedistributable.DllName))
                    .ShouldBeTrue("modern-conpty-prerequisite: staged conpty.dll is missing");
                File.Exists(Path.Combine(conPtyDir, ConPtyRedistributable.ConsoleHostName))
                    .ShouldBeTrue("modern-conpty-prerequisite: staged OpenConsole.exe is missing");
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, conPtyDir);
            }

            await runner.StartAsync(node, new[] { "argv-echo.js" }.Concat(expected).ToArray(), probeDir,
                new Dictionary<string, string> { ["ANTIPHON_ARGV_CAPTURE"] = capture });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(capture))
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(20, deadline.Token);
            }
            JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(capture, deadline.Token))
                .ShouldBe(expected, "windows-native-argv-exact");
            runner.Backend.ShouldNotBeNull().Backend.ShouldBe(
                backend == "modern" ? PtyBackend.ModernConPty : PtyBackend.InboxConhost,
                "backend-no-fallback");
        }
        finally
        {
            try
            {
                if (runner.Pid is { } pid)
                {
                    try
                    {
                        using var child = Process.GetProcessById(pid);
                        if (!child.HasExited) child.Kill(entireProcessTree: true);
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await child.WaitForExitAsync(deadline.Token);
                    }
                    catch (ArgumentException) { /* already exited */ }
                }
            }
            finally
            {
                if (backend == "modern")
                    Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, previousConPtyDir);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ResolveNode()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir, "node.exe");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Node executable prerequisite is missing");
    }
}
