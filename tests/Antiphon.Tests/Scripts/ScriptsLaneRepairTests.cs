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
        var result = RunWitness(scenario, path: null);
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        result.Stdout.ShouldContain("PASS " + scenario + ":");
    }

    // The replaced case proves a foreign directory object survived cleanup. Hiding
    // coreutils stat is the Windows failure. setsid stays because the runner wraps
    // the child with it; stat must be absent so the harness cannot shell out.
    [Test]
    public void Replaced_directory_identity_does_not_shell_out_to_stat()
    {
        var hidden = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "scripts-lane-nostat-" + Guid.NewGuid().ToString("N")));
        try
        {
            if (OperatingSystem.IsLinux())
                File.CreateSymbolicLink(Path.Combine(hidden.FullName, "setsid"), ResolveTool("setsid"));
            var result = RunWitness("replaced", hidden.FullName);
            result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
            result.Stdout.ShouldContain("PASS replaced:");
            (result.Stdout + result.Stderr).ShouldNotContain("stat");
        }
        finally
        {
            hidden.Delete(true);
        }
    }

    private static CheckpointFixtureProcess.Output RunWitness(string scenario, string? path)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : ResolveTool("pwsh"));
        foreach (var argument in new[] { "-NoProfile", "-File",
                     Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-scripts-lane-repairs.ps1"),
                     "-Case", scenario, "-HostCleanupDll",
                     typeof(Antiphon.HostCleanup.Checkpoints.CheckpointNativeIo).Assembly.Location })
            start.ArgumentList.Add(argument);
        if (path is not null)
            start.Environment["PATH"] = path;
        // Provisional bound: observed portable script bodies take 1-4 seconds; allow startup
        // and a bounded native Git preflight. No wall-clock sleeps or shared host fixture.
        return CheckpointFixtureProcess.Run(start, 30_000);
    }

    private static string ResolveTool(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException(name);
    }
}
