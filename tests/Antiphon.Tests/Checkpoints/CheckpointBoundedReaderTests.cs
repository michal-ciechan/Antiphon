using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[NativeLinuxOnly("Linux FIFO and process-group qualification")]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointBoundedReaderTests : CheckpointTestBase
{
    [Test]
    [Arguments("project", "ordinary")]
    [Arguments("project", "held")]
    [Arguments("tool-git", "ordinary")]
    [Arguments("tool-git", "held")]
    [Arguments("script-git", "ordinary")]
    [Arguments("script-git", "held")]
    [Arguments("tool-eval", "ordinary")]
    [Arguments("tool-eval", "held")]
    [Arguments("script-eval", "ordinary")]
    [Arguments("script-eval", "held")]
    public async Task changed_readers_bound_fifo_and_exited_child_pipes(string mode, string shape)
    {
        await RunProbeAsync(mode, shape);
    }

    [Test]
    public async Task C1381_Project_read_budget_excludes_startup_delay()
    {
        using var result = JsonDocument.Parse(await RunProbeAsync("project", "held", 5000));
        var probe = result.RootElement;
        probe.GetProperty("expired").GetBoolean().ShouldBeFalse();
        probe.GetProperty("survivor").GetBoolean().ShouldBeFalse();
        probe.GetProperty("code").GetInt32().ShouldBe(0);
        probe.GetProperty("timingValid").GetBoolean().ShouldBeTrue();
        probe.GetProperty("bound").GetInt32().ShouldBe(4000);
        probe.GetProperty("watchdogMs").GetInt32().ShouldBe(30000);
        probe.GetProperty("delayElapsedMs").GetInt64().ShouldBeGreaterThanOrEqualTo(5000);
        probe.GetProperty("elapsed").GetInt64().ShouldBeGreaterThanOrEqualTo(5000);
        probe.GetProperty("readElapsedMs").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
        probe.GetProperty("readElapsedMs").GetInt64().ShouldBeLessThan(4000);
    }

    private async Task<string> RunProbeAsync(string mode, string shape, int? preReadDelayMs = null)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux FIFO and process-group qualification");
        var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-bounded-readers.mjs"),
            CheckpointFixtures.RepoRoot, typeof(Antiphon.Checkpoints.Program).Assembly.Location, mode, shape }) psi.ArgumentList.Add(arg);
        if (preReadDelayMs is { } delay) psi.ArgumentList.Add(delay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); // The supervisor checks read acceptance and independent emergency cleanup.
        var output = await stdout;
        Console.WriteLine(output);
        process.ExitCode.ShouldBe(0, output + await stderr);
        return output;
    }
}
