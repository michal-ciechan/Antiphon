using System.Diagnostics;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptsLaneRepairTests
{
    [Test]
    [Arguments("opaque")]
    [Arguments("owned")]
    [Arguments("origin")]
    [Arguments("state")]
    [Arguments("replaced")]
    public void Portable_scripts_lane_witness(string scenario)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : "pwsh");
        foreach (var argument in new[] { "-NoProfile", "-File",
                     Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-scripts-lane-repairs.ps1"),
                     "-Case", scenario, "-HostCleanupDll",
                     typeof(Antiphon.HostCleanup.Checkpoints.CheckpointNativeIo).Assembly.Location })
            start.ArgumentList.Add(argument);
        // Provisional bound: observed portable script bodies take 1-4 seconds; allow startup
        // and a bounded native Git preflight. No wall-clock sleeps or shared host fixture.
        var result = CheckpointFixtureProcess.Run(start, 30_000);
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        result.Stdout.ShouldContain("PASS " + scenario + ":");
    }
}
