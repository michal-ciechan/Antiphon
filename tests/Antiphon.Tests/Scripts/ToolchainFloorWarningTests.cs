using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1698 slice 1. Each method runs one Test-C1698_* case of scripts/test-toolchain-floor.ps1
/// (Get-ToolchainFloorWarnings with an injected version source, and the restart entry points with
/// a fake git), or of the nightly run/tests harnesses for the summary and run wiring.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ToolchainFloorWarningTests
{
    [Test]
    public Task C1698_GitBelowFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_GitBelowFloor), 8,
            [
                "C1698 Below names the tool",
                "C1698 Below names the found version",
                "C1698 Below names the floor",
                "C1698 Below names what stops working",
                "C1698 Below names the fix command",
                "C1698 Below message carries all five fields"
            ]);

    [Test]
    public Task C1698_GitAtOrAboveFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_GitAtOrAboveFloor), 5,
            [
                "C1698 AtOrAbove no warning git version 2.43",
                "C1698 AtOrAbove no warning git version 2.43.0",
                "C1698 AtOrAbove no warning git version 2.100.1"
            ]);

    [Test]
    public Task C1698_UnreadableVersion() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_UnreadableVersion), 6,
            [
                "C1698 Unreadable warns, never throws: garbage",
                "C1698 Unreadable warns, never throws: empty",
                "C1698 Unreadable warns, never throws: throwing source",
                "C1698 Unreadable message carries floor, impact and fix: garbage",
                "C1698 Unreadable message carries floor, impact and fix: empty",
                "C1698 Unreadable message carries floor, impact and fix: throwing source"
            ]);

    [Test]
    public Task C1698_SdkFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_SdkFloor), 4,
            [
                "C1698 Sdk 11.0.100 [/x]",
                "C1698 Sdk 9.0.300 [/x] 10.0.204 [/x]"
            ]);

    [Test]
    public Task C1698_ManifestSources() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_ManifestSources), 5,
            [
                "C1698 Manifest git floor is the image guard declaration",
                "C1698 Manifest SDK floor is global.json",
                "C1698 Manifest pwsh node docker are listed, not enforced",
                "C1698 Manifest missing floor sources warn, not throw"
            ]);

    [Test]
    public Task C1698_RestartServerUnchanged() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartServerUnchanged), 5,
            [
                "C1698 RestartServer prints the warning",
                "C1698 RestartServer exit 0 with a warning",
                "C1698 RestartServer same stop build start with a warning",
                "C1698 RestartServer build failure still exit 1 with a warning"
            ]);

    [Test]
    public Task C1698_RestartRunnerUnchanged() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartRunnerUnchanged), 4,
            [
                "C1698 RestartRunner prints the warning",
                "C1698 RestartRunner same result with a warning",
                "C1698 RestartRunner same exit code with a warning"
            ]);

    /// <summary>A toolchain library that throws or cannot be parsed when dot-sourced (the only failure
    /// that reaches the entry wrapper) leaves restart-server at exit 0 with the same calls.</summary>
    [Test]
    public Task C1698_RestartServerLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartServerLibraryLoadFailure), 2,
            [
                "C1698 RestartServer survives a library that fails to load: throws",
                "C1698 RestartServer survives a library that fails to load: unparsable"
            ]);

    [Test]
    public Task C1698_RestartRunnerLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartRunnerLibraryLoadFailure), 2,
            [
                "C1698 RestartRunner survives a library that fails to load: throws",
                "C1698 RestartRunner survives a library that fails to load: unparsable"
            ]);

    /// <summary>A pwsh 7 only construct (ternary, ??, ?., pipeline chains, -Parallel) in the library
    /// or its entry callers; the controls prove the scan flags each one. A real powershell.exe 5.1
    /// parse on Windows is the fuller proof.</summary>
    [Test]
    public Task C1698_WindowsPowerShellSyntax() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_WindowsPowerShellSyntax), 17,
            [
                "C1698 Syntax scan flags ternary",
                "C1698 Syntax scan flags null-coalescing",
                "C1698 Syntax scan flags null-conditional member",
                "C1698 Syntax scan flags pipeline chain and",
                "C1698 Syntax scan flags ForEach-Object -Parallel",
                "C1698 Syntax scan passes the 5.1 spellings",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/toolchain-floor.ps1",
                "C1698 Windows PowerShell 5.1 syntax: restart-server.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/session-runner-restart-health.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/nightly-run-impl.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/antiphon-cli.ps1"
            ]);

    /// <summary>
    /// W2-nightly S19, in-process (was the scripts/test-nightly-run.ps1 case): NightlyRun.Run prints a toolchain floor warning
    /// before the tests hop and keeps the same exit code and phase; the versions come from the host probe, never the real git.
    /// </summary>
    [Test]
    public void C1698_RunPrintsToolchainWarning()
    {
        (string[] Lines, Antiphon.Nightly.NightlyRunResult Result) Run(string gitVersion)
        {
            using var w = new Antiphon.Tests.Scripts.Nightly.NightlyRunWorld("c1698-run");
            var repo = Antiphon.Tests.Scripts.Nightly.NightlyPlanCases.Repo;
            Directory.CreateDirectory(Path.Combine(w.Clone, "docker"));
            File.Copy(Path.Combine(repo, "docker", "git-minimum-version.sh"), Path.Combine(w.Clone, "docker", "git-minimum-version.sh"));
            File.Copy(Path.Combine(repo, "global.json"), Path.Combine(w.Clone, "global.json"));
            w.Probe.Versions["git"] = "git version " + gitVersion;
            var result = w.Run(o => o.NoSync = true);
            return (w.OutLines, result);
        }
        var below = Run("2.42.0");
        var at = Run("2.43.0");
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var warn = below.Lines.Count(l => Regex.IsMatch(l, @"WARNING: git 2\.42\.0 is below the minimum 2\.43; lands will fail"));
        rows.Check(warn == 1, "C1698 Run prints the git floor warning", string.Join(" | ", below.Lines));
        rows.Check(!at.Lines.Any(l => l.Contains("WARNING:", StringComparison.Ordinal)), "C1698 Run prints none at the floor", string.Join(" | ", at.Lines));
        var warnAt = Array.FindIndex(below.Lines, l => Regex.IsMatch(l, "WARNING: git"));
        var hop = Array.FindIndex(below.Lines, l => Regex.IsMatch(l, "running .*nightly-tests"));
        rows.Check(warnAt >= 0 && hop > warnAt, "C1698 Run warns before the tests hop", "warning=" + warnAt + " hop=" + hop);
        rows.Check(below.Result.ExitCode == at.Result.ExitCode && below.Result.Phase == at.Result.Phase, "C1698 Run exit code unchanged by the warning",
            "below=" + below.Result.ExitCode + "/" + below.Result.Phase + " at=" + at.Result.ExitCode + "/" + at.Result.Phase);
        rows.ShouldAllPass(4, "C1698 Run prints the git floor warning", "C1698 Run prints none at the floor", "C1698 Run warns before the tests hop",
            "C1698 Run exit code unchanged by the warning");
    }

    /// <summary>
    /// W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): NightlyTests.ToolchainPreflight records the
    /// versions the host probe reads (never the real git), the floors and the warning, and the run's preflight block calls it
    /// after the disk row and before the summary is built (the source-text row reads NightlyTests.cs).
    /// </summary>
    [Test]
    public void C1698_SummaryRecordsToolchain()
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("c1698-summary");
        var repo = Antiphon.Tests.Scripts.Nightly.NightlyPlanCases.Repo;
        Directory.CreateDirectory(fixture.PathOf("docker"));
        File.Copy(Path.Combine(repo, "docker", "git-minimum-version.sh"), fixture.PathOf("docker", "git-minimum-version.sh"));
        File.Copy(Path.Combine(repo, "global.json"), fixture.PathOf("global.json"));
        var host = fixture.Host();
        var toolchain = Antiphon.Nightly.NightlyTests.ToolchainPreflight(fixture.Root, host);
        var json = Antiphon.Nightly.NightlyJson.Text(new System.Collections.Specialized.OrderedDictionary
        {
            { "preflight", new System.Collections.Specialized.OrderedDictionary { { "toolchain", toolchain } } },
        }, 8) ?? "";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var back = doc.RootElement.GetProperty("preflight").GetProperty("toolchain");
        System.Text.Json.JsonElement? Tool(string name)
        {
            foreach (var t in back.GetProperty("tools").EnumerateArray()) if (t.GetProperty("tool").GetString() == name) return t;
            return null;
        }
        var git = Tool("git");
        var sdk = Tool("dotnet-sdk");
        var warnings = back.GetProperty("warnings").EnumerateArray().ToArray();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        rows.Check(git is { } g && g.GetProperty("found").GetString() == "2.42.0" && g.GetProperty("floor").GetString() == "2.43" && g.GetProperty("status").GetString() == "below-floor",
            "C1698 Summary records the git version and floor", json);
        rows.Check(sdk is { } s && s.GetProperty("found").GetString() == "10.0.204" && s.GetProperty("status").GetString() == "ok", "C1698 Summary records the SDK version", json);
        rows.Check(warnings.Length == 1 && (warnings[0].GetProperty("message").GetString() ?? "").StartsWith("WARNING: git 2.42.0 is below the minimum 2.43", StringComparison.Ordinal),
            "C1698 Summary records the warning", json);
        var run = File.ReadAllText(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "NightlyTests.cs"));
        var call = run.IndexOf("preflight[\"toolchain\"] = ToolchainPreflight(repoRoot, host);", StringComparison.Ordinal);
        var disk = run.IndexOf("preflight[\"disk\"] = new OrderedDictionary", StringComparison.Ordinal);
        var write = run.IndexOf("{ \"preflight\", preflight }", StringComparison.Ordinal);
        rows.Check(call > disk && disk > 0 && write > call, "C1698 Summary preflight block records the toolchain", "disk=" + disk + " call=" + call + " write=" + write);
        rows.ShouldAllPass(4, "C1698 Summary records the git version and floor", "C1698 Summary records the SDK version", "C1698 Summary records the warning",
            "C1698 Summary preflight block records the toolchain");
    }

    /// <summary>W2-nightly S03: the run core checks with the compiled twin (ToolchainFloor.cs), so a broken
    /// toolchain-floor.ps1 (kept for the restart chain) changes nothing in the run.</summary>
    [Test]
    public Task C1698_RunLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1698",
            nameof(C1698_RunLibraryLoadFailure), 2,
            [
                "C1698 Run does not load the toolchain library: throws",
                "C1698 Run does not load the toolchain library: unparsable"
            ]);

    /// <summary>
    /// W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): summary.json as a real NightlyTests.Run writes
    /// it to disk, the clone carrying every policy project and the two floor sources, the versions read through the host probe.
    /// </summary>
    [Test]
    public void C1698_SummaryJsonRecordsToolchain()
    {
        using var world = new Antiphon.Tests.Scripts.Nightly.NightlyTestsWorld("c1698-json");
        var repo = Antiphon.Tests.Scripts.Nightly.NightlyPlanCases.Repo;
        Directory.CreateDirectory(Path.Combine(world.Clone, "docker"));
        File.Copy(Path.Combine(repo, "docker", "git-minimum-version.sh"), Path.Combine(world.Clone, "docker", "git-minimum-version.sh"));
        File.Copy(Path.Combine(repo, "global.json"), Path.Combine(world.Clone, "global.json"));
        var r = world.Run();
        var summaryPath = Path.Combine(world.Logs, "summary.json");
        var json = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : "";
        System.Text.Json.JsonElement? git = null, sdk = null;
        System.Text.Json.JsonElement[] warnings = [];
        if (json.Length > 0)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var tc = doc.RootElement.GetProperty("preflight").GetProperty("toolchain").Clone();
            foreach (var t in tc.GetProperty("tools").EnumerateArray())
            {
                if (t.GetProperty("tool").GetString() == "git") git = t;
                if (t.GetProperty("tool").GetString() == "dotnet-sdk") sdk = t;
            }
            warnings = tc.GetProperty("warnings").EnumerateArray().ToArray();
        }
        var detail = "exit=" + r.ExitCode + " error=" + r.Error + " summary=" + (json.Length > 0 ? "present" : "missing");
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        rows.Check(git is { } g && g.GetProperty("found").GetString() == "2.42.0" && g.GetProperty("floor").GetString() == "2.43" && g.GetProperty("status").GetString() == "below-floor"
            && sdk is { } s && s.GetProperty("found").GetString() == "10.0.204" && s.GetProperty("floor").GetString() == "10.0.204" && s.GetProperty("status").GetString() == "ok",
            "C1698 Summary json on disk records the versions and floors", detail);
        rows.Check(warnings.Length == 1 && warnings[0].GetProperty("tool").GetString() == "git"
            && (warnings[0].GetProperty("message").GetString() ?? "").StartsWith("WARNING: git 2.42.0 is below the minimum 2.43; lands will fail", StringComparison.Ordinal),
            "C1698 Summary json on disk records the warning", detail);
        rows.ShouldAllPass(2, "C1698 Summary json on disk records the versions and floors", "C1698 Summary json on disk records the warning");
    }
}
