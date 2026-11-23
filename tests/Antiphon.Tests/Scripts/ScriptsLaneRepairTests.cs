using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Nightly;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The portable scripts-lane witnesses. The bin-* cleanup rows (opaque, owned, replaced) run the cleanup-build-junk command in
/// process (Antiphon.Nightly.BuildJunkCleanup, unit 21 of the 2026-11-22 plan) with the real HostCleanup ledger as the producer's
/// removal; origin and state still run scripts/test-scripts-lane-repairs.ps1 (the nightly origin reader and the native state
/// move it compiles), which go with their own waves.
/// </summary>
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
        if (scenario is "opaque" or "owned" or "replaced")
        {
            InProcess(scenario).ShouldStartWith("PASS " + scenario + ":");
            return;
        }
        var result = RunWitness(scenario, path: null);
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
        result.Stdout.ShouldContain("PASS " + scenario + ":");
    }

    // The replaced case proves a foreign directory object survived cleanup. Its directory identity is
    // CheckpointNativeIo.CustodyStat, read in this process: there is no stat child to hide (the PowerShell witness hid
    // coreutils stat from PATH to prove the same, the Windows failure).
    [Test]
    public void Replaced_directory_identity_does_not_shell_out_to_stat()
    {
        var pass = InProcess("replaced");
        pass.ShouldStartWith("PASS replaced:");
        pass.ShouldNotContain("stat");
    }

    // The witness bodies the driver ran for opaque, owned and replaced, on BuildJunkCleanup.Run: a separate process holds no
    // creation records (removeOwned null), a producer that holds them removes through its ledger.
    private static string InProcess(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "scripts-lane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = CheckpointCleanupDefaults.Context;
            string? Removal(string path) => new CheckpointOutputLedger(context).Remove(path, false);
            if (scenario == "replaced")
            {
                var owned = CreateSealedOutput(root);
                var copy = Path.Combine(root, "bin-foreign");
                CopyTree(owned, copy);
                File.Exists(Path.Combine(copy, ".checkpoint-output-ledger.json")).ShouldBeTrue("the copy keeps the ledger");
                var foreignIdentity = Identity(copy);
                var aside = Path.Combine(root, "bin-owned-real");
                Directory.Move(owned, aside);
                Directory.Move(copy, owned);
                Directory.SetLastWriteTime(owned, DateTime.Now.AddDays(-10));
                Directory.SetLastWriteTime(aside, DateTime.Now.AddDays(-10));
                Clean(root, Removal);
                Directory.Exists(owned).ShouldBeTrue("replacement directory was deleted");
                Directory.Exists(aside).ShouldBeTrue("original owned directory was deleted");
                Identity(owned).ShouldBe(foreignIdentity, "replacement identity was not preserved");
                File.ReadAllText(Path.Combine(owned, "payload.txt")).ShouldBe("regenerable");
                File.ReadAllText(Path.Combine(aside, "payload.txt")).ShouldBe("regenerable");
                return "PASS replaced: foreign directory survived a stale custody record";
            }
            var opaque = Path.Combine(root, "bin-private", "nested", "bin-archive", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(opaque)!);
            File.WriteAllText(opaque, "irreplaceable");
            Directory.SetLastWriteTime(Path.Combine(root, "bin-private"), DateTime.Now.AddDays(-10));
            if (scenario == "owned")
            {
                var owned = CreateSealedOutput(root);
                var lines = Clean(root, Removal);
                Directory.Exists(owned).ShouldBeFalse("owned output was retained: " + lines);
                lines.ShouldContain("removed: " + owned);
            }
            else
            {
                Clean(root, null).ShouldContain("skip: producer creation records unavailable; outputs left in place");
            }
            File.Exists(opaque).ShouldBeTrue("unowned bin-* bytes were deleted");
            File.ReadAllText(opaque).ShouldBe("irreplaceable", "unowned bytes changed");
            return "PASS " + scenario + ": unowned preserved" + (scenario == "owned" ? ", owned removed" : "");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // The producer's side: a fresh owned output with a payload, sealed into the ledger.
    private static string CreateSealedOutput(string root)
    {
        var owned = Path.Combine(root, "bin-owned");
        var ownership = SafeWrite.Under(root);
        SafeWrite.CreateFreshDirectory(owned, ownership).ShouldBeNull("create");
        File.WriteAllText(Path.Combine(owned, "payload.txt"), "regenerable");
        OwnedOutputLedger.Seal(owned, DateTimeOffset.UtcNow).ShouldBeNull("seal");
        return owned;
    }

    private static string Clean(string root, Func<string, string?>? removal)
    {
        var output = new StringWriter();
        BuildJunkCleanup.Run(BuildJunkCleanupOptions.Parse(["--repo-root", root, "--skip-if-modified-within-minutes", "0"], null),
            NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), removal is null ? null : path => removal(path)!);
        return output.ToString();
    }

    // Directory identity is the Linux inode or the NTFS file index, read in process.
    private static string Identity(string path)
    {
        var observed = new Antiphon.HostCleanup.Checkpoints.CheckpointNativeIo().CustodyStat(path);
        observed.Kind.ShouldBe("directory");
        observed.Inode.ShouldNotBe(0UL, "directory identity unavailable for " + path);
        return observed.Device + ":" + observed.Inode;
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private static CheckpointFixtureProcess.Output RunWitness(string scenario, string? path)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : ResolveTool("pwsh"));
        foreach (var argument in new[] { "-NoProfile", "-File",
                     Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-scripts-lane-repairs.ps1"), "-Case", scenario })
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
