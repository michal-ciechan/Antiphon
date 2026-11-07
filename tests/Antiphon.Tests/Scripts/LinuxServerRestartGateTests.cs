using System.Diagnostics;
using Antiphon.TestSupport;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1745. The Linux server SHA gate is off until a config names the server.
/// -Execute probes first and does not stop a serving container. The desktop
/// AppHost scripts stay uncoupled. The harness is a Linux shell fake.
/// </summary>
[NativeLinuxOnly("Linux-only by construction: the SHA restart gate harness is a #!/bin/sh fake docker plus chmod. The case remains required on Linux.")]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxServerRestartGateTests
{
    private static readonly string[] DesktopPaths =
    [
        "scripts/restart-apphost.ps1",
        "scripts/apphost-common.ps1",
        "scripts/start-apphost.ps1",
        "scripts/autostart-apphost.ps1",
        "dev-aspire.ps1",
        "Antiphon.AppHost/Program.cs",
    ];

    [Test]
    public async Task Unconfigured_refuses_and_restarts_nothing()
        => await AssertCase("W1", "W1-exit-3", "W1-says-not-configured", "W1-says-nothing-restarted", "W1-no-docker", "W1-no-lock");

    [Test]
    public async Task WhatIf_match_exits_0_without_docker()
        => await AssertCase("W2", "W2-exit-0", "W2-verified", "W2-no-docker", "W2-no-lock");

    [Test]
    public async Task WhatIf_mismatch_exits_5_and_leaves_lock()
        => await AssertCase("W3", "W3-exit-5", "W3-lock-remains", "W3-lock-names-sha", "W3-no-docker", "W3-not-verified");

    [Test]
    public async Task WhatIf_health_failure_is_not_verified()
        => await AssertCase("W4", "W4-exit-1", "W4-no-lock", "W4-no-docker", "W4-not-verified");

    [Test]
    public async Task Execute_match_reports_already_at_expected_sha_without_stopping()
        => await AssertCase("W5", "W5-exit-0", "W5-already", "W5-verified-execute", "W5-no-docker", "W5-lock-cleared");

    [Test]
    public async Task Execute_mismatch_refuses_before_stopping()
        => await AssertCase("W6", "W6-exit-5", "W6-nothing-restarted", "W6-left-running", "W6-recovery", "W6-lock-remains", "W6-lock-records-image", "W6-inspected-same-image", "W6-no-stop", "W6-not-left-stopped");

    [Test]
    public async Task Malformed_sha_refuses_before_docker()
        => await AssertCase("W7", "W7-exit-3", "W7-no-docker", "W7-no-http", "W7-nothing-restarted");

    [Test]
    public async Task Desktop_apphost_port_refuses()
        => await AssertCase("W8", "W8-exit-3", "W8-names-desktop-port", "W8-no-docker", "W8-no-http");

    [Test]
    public async Task Runner_container_refuses()
        => await AssertCase("W9", "W9-exit-3", "W9-names-runner", "W9-no-docker", "W9-nothing-restarted");

    [Test]
    public async Task Omitted_switches_are_whatif()
        => await AssertCase("W10", "W10-exit-0", "W10-verified-whatif", "W10-no-docker");

    [Test]
    public async Task Malformed_version_exits_5_and_leaves_lock()
        => await AssertCase("W13", "W13-exit-5", "W13-lock-remains", "W13-lock-malformed", "W13-no-docker");

    [Test]
    public async Task Execute_health_failure_exits_5_without_docker()
        => await AssertCase("W14", "W14-exit-5", "W14-left-running", "W14-nothing-restarted", "W14-lock-remains", "W14-lock-health", "W14-no-docker");

    [Test]
    public async Task Execute_unknown_version_exits_5_without_docker()
        => await AssertCase("W15", "W15-exit-5", "W15-nothing-restarted", "W15-lock-remains", "W15-lock-unavailable", "W15-no-docker");

    [Test]
    public async Task Execute_malformed_version_exits_5_without_docker()
        => await AssertCase("W16", "W16-exit-5", "W16-left-running", "W16-lock-remains", "W16-lock-malformed", "W16-no-docker");

    [Test]
    public async Task Execute_moved_image_refuses_before_stopping()
        => await AssertCase("W17", "W17-exit-5", "W17-image-moved", "W17-names-new-image", "W17-no-recreate", "W17-left-running", "W17-nothing-restarted", "W17-recovery", "W17-lock-remains", "W17-lock-records-running-image", "W17-no-stop");

    [Test]
    public void AppHost_files_do_not_reference_the_linux_gate()
    {
        foreach (var relative in DesktopPaths)
        {
            var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));
            text.Contains("restart-linux-server", StringComparison.Ordinal).ShouldBeFalse(relative);
        }

        var gate = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "restart-linux-server.ps1"));
        foreach (var banned in new[] { "restart-apphost", "deploy-server2", "schtasks", "phone-home", "PhoneHome" })
            gate.Contains(banned, StringComparison.Ordinal).ShouldBeFalse(banned);
    }

    [Test]
    public void Docker_stack_doc_names_the_inert_gate()
    {
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "docker-stack.md"));
        text.ShouldContain("scripts/restart-linux-server.ps1");
        text.ShouldContain("-Execute");
        text.ShouldContain("WhatIf");
        text.ShouldContain("exit 5");
        text.ShouldContain("not configured");
        text.ShouldContain("already at expected SHA");
        text.ShouldContain("left running");
        text.ShouldContain("docker start");
    }

    private static async Task AssertCase(string caseName, params string[] rows)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-restart-linux-server.ps1"));
        start.ArgumentList.Add("-Case");
        start.ArgumentList.Add(caseName);
        start.Environment["TEMP"] = "/tmp";
        start.Environment["TMP"] = "/tmp";
        start.Environment.Remove("ANTIPHON_LINUX_SERVER_CONFIG");

        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanup.Token);
            throw;
        }

        var output = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, output);
        foreach (var row in rows)
            output.ShouldContain("PASS " + row, Case.Sensitive, output);
        output.ShouldNotContain("FAIL ", Case.Sensitive, output);
    }
}
