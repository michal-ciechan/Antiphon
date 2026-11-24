using System.Net;
using System.Net.Sockets;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1305 D-2: the dev-stack listener probe lists a real loopback port without Get-NetTCPConnection. CARD-1323 S3: the
/// full Aspire smoke refuses a non-Windows host before any external command. Since unit 31 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md verify-dev-stack.ps1 is a thin launcher of the
/// verify-dev-stack command (DevStackVerify), so these run in process: the refusal and the check order on AppHostWorld
/// (the script ran under a pwsh wrapper whose sentinels replaced Start-Job, docker and Invoke-WebRequest), and the listener
/// probe on this machine's production platform (AppHostOs) with a real loopback listener.
/// </summary>
[Category("Unit")]
public sealed class VerifyDevStackSmokeTests
{
    private static string Row(string name, bool ok, string detail) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0,-28} {1,-5} {2}", name, ok ? "OK" : "FAIL", detail);

    private static (int Exit, string Text) Verify(AppHostWorld world, params string[] arguments)
    {
        var exit = DevStackVerify.Run(DevStackVerifyOptions.Parse(arguments, world.Root), world.Host(), world);
        return (exit, world.Out.ToString() + world.Error.ToString());
    }

    [Test]
    public void Linux_full_smoke_refuses_before_desktop_side_effects()
    {
        using var world = new AppHostWorld { IsWindows = false };
        var r = Verify(world);
        r.Exit.ShouldNotBe(0, r.Text);
        r.Text.ShouldContain("desktop-platform-required");
        world.Trace.ShouldBeEmpty("full Aspire smoke must refuse before jobs, Docker, HTTP, listener or build commands");

        using var windows = new AppHostWorld();
        Verify(windows, "--skip-browser");
        windows.Trace[0].ShouldBe("run docker version", "Windows keeps full Aspire smoke admission");
    }

    [Test]
    public void Linux_listener_probe_lists_ports_without_GetNetTCPConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var world = new AppHostWorld();
        var platform = new AppHostOs(world.Host(), world.Files, world.Root, world.PathOf("no-token"));
        var output = new StringWriter();
        var host = world.Host();
        var exit = DevStackVerify.Run(DevStackVerifyOptions.Parse(["--probe-listening-ports"], world.Root), new NightlyHost
        {
            Clock = host.Clock, Git = host.Git, Launcher = host.Launcher, Processes = host.Processes, Probe = host.Probe, Out = output, Error = host.Error, Trace = host.Trace,
        }, platform);
        exit.ShouldBe(0, world.Error.ToString());
        output.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ShouldContain(port.ToString());
    }

    [Test]
    public void A_healthy_aspire_stack_passes_every_check()
    {
        using var world = new AppHostWorld();
        world.Commands = (file, args) => args[0] switch
        {
            "version" => new AppHostCommandResult(false, 0, "Client:\n Version: 27\nServer: Docker Desktop\n"),
            "ps" => new AppHostCommandResult(false, 0, "Up 3 hours (healthy)\n"),
            _ => new AppHostCommandResult(false, 0, ""),
        };
        world.Listening.AddRange([17200, 17202, 17203, 17204, 17205, 17206, 9999]);
        world.Http = url => new AppHostHttpAnswer(200, url.EndsWith("/health", StringComparison.Ordinal) ? "Healthy" : "[]", null);
        var r = Verify(world, "--skip-browser");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("=== Verifying Antiphon dev stack (Aspire) ===");
        r.Text.ShouldContain(Row("Docker daemon", true, "reachable"));
        r.Text.ShouldContain(Row("Postgres container", true, "Up 3 hours (healthy)"));
        r.Text.ShouldContain(Row("Port 17206", true, "listening"));
        r.Text.ShouldContain(Row("Frontend SignalR", true, "HTTP 200"));
        r.Text.ShouldContain(Row("Git index lock", true, "absent"));
        r.Text.ShouldContain("Antiphon dev stack verified - all checks passed.");
        world.Trace.ShouldContain("post http://localhost:17203/hubs/antiphon/negotiate?negotiateVersion=1");
        world.Trace.ShouldContain(row => row.StartsWith("run docker network rm verify-hns-", StringComparison.Ordinal));
    }

    [Test]
    public void A_failed_core_check_fails_the_stack_but_a_stale_index_lock_does_not()
    {
        using var world = new AppHostWorld();
        world.Listening.AddRange([17280, 17281, 17282]);
        world.Http = url => url.Contains(":17204/", StringComparison.Ordinal) ? new AppHostHttpAnswer(null, null, "connection refused") : new AppHostHttpAnswer(200, "Healthy", null);
        var r = Verify(world, "--simple-mode", "--skip-browser");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("Stack NOT healthy - 1 check(s) failed:");
        r.Text.ShouldContain("  - Session runner health: connection refused");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal), "the compose stack has no Docker checks");

        using var stale = new AppHostWorld();
        stale.Listening.AddRange([17280, 17281, 17282]);
        stale.Http = url => new AppHostHttpAnswer(200, "Healthy", null);
        var lockPath = stale.PathOf(".git", "index.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllBytes(lockPath, []);
        File.SetLastWriteTimeUtc(lockPath, stale.UtcNow.AddHours(-1));
        stale.IndexLockPath = lockPath;
        var s = Verify(stale, "--simple-mode", "--skip-browser");
        s.Exit.ShouldBe(0, s.Text);
        s.Text.ShouldContain(Row("Git index lock", false, "stale (60.0 min) at " + lockPath + " - Remove-Item '" + lockPath + "'"));
    }
}
