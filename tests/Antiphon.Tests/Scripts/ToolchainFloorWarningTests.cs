using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1698 slice 1, in-process. The Test-C1698_* cases of the deleted scripts/test-toolchain-floor.ps1 (plan
/// 2026-11-22-remaining-ps1-to-csharp.md, unit 1) call ToolchainFloor.Warnings with an injected version source and
/// ToolchainFloor.Run (the toolchain-floor command) on a fake host; restart-server.ps1's call is read as source text,
/// because no test launches a .ps1, and the runner restart (the restart-session-runner command since unit 30) runs the
/// warnings in process. The nightly run and tests cases below it were already in-process.
/// Not ported: the cases that ran restart-server.ps1 and restart-session-runner.ps1 with a fake git and dotnet
/// (launching a .ps1; what they proved, the warning text and an unchanged exit and call sequence, is the command's output,
/// the call block's shape and the in-process restart below), the library-load-failure cases (the library is gone; a CLI
/// that cannot be built or started exits 9 with one stderr line, AntiphonCliLauncherTests, and the block only warns on a
/// non-zero exit), and the pwsh-7-only syntax scan (PowerShell mechanics:
/// AntiphonCliLauncherTests.Kept_launchers_and_restart_entries_parse_and_are_ascii parses the launcher and
/// restart-server.ps1 with powershell.exe 5.1 and pwsh).
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ToolchainFloorWarningTests
{
    private static string Repo => NightlyPlanCases.Repo;

    private static Func<string, string> Source(string git, string sdks = "10.0.204 [/fake/sdk]") =>
        tool => tool == "git" ? git : tool == "dotnet-sdk" ? sdks : "";

    private static ToolchainFloorEntry Entry(string tool) => ToolchainFloor.Manifest().Single(entry => entry.Tool == tool);

    private static string HostFix(string tool) => ToolchainFloor.IsWindowsHost() ? Entry(tool).FixWindows : Entry(tool).FixOther;

    [Test]
    public void C1698_GitBelowFloor()
    {
        var w = ToolchainFloor.Warnings(Repo, Source("git version 2.42.0.windows.1"));
        var x = w.ShouldHaveSingleItem("C1698 Below one warning");
        x.Tool.ShouldBe("git", "C1698 Below names the tool");
        x.Found.ShouldBe("2.42.0", "C1698 Below names the found version");
        x.Floor.ShouldBe("2.43", "C1698 Below names the floor");
        x.Impact.ShouldContain("lands will fail", customMessage: "C1698 Below names what stops working");
        x.Impact.ShouldContain("show-ref --exists", customMessage: "C1698 Below names what stops working");
        if (ToolchainFloor.IsWindowsHost()) x.Fix.ShouldBe("winget upgrade Git.Git", "C1698 Below names the fix command");
        else x.Fix.ShouldContain("install Git", customMessage: "C1698 Below names the fix command");
        x.Message.ShouldStartWith("WARNING: git 2.42.0 is below the minimum 2.43; lands will fail", customMessage: "C1698 Below message carries all five fields");
        x.Message.ShouldContain("Fix: " + x.Fix, customMessage: "C1698 Below message carries all five fields");
        var below = ToolchainFloor.Warnings(Repo, Source("git version 2.9.5"));
        below.ShouldHaveSingleItem("C1698 Below compares by version not string (2.9.5)").Reason.ShouldBe("below-floor");
    }

    [Test]
    public void C1698_GitAtOrAboveFloor()
    {
        foreach (var v in new[] { "git version 2.43", "git version 2.43.0", "git version 2.56.0.windows.1", "git version 2.100.1", "git version 3.0.0" })
            ToolchainFloor.Warnings(Repo, Source(v)).Select(w => w.Message).ShouldBeEmpty("C1698 AtOrAbove no warning " + v);
    }

    [Test]
    public void C1698_UnreadableVersion()
    {
        var git = Entry("git");
        foreach (var (name, source, raw) in new (string, Func<string, string>, string)[]
                 {
                     ("garbage", Source("garbage"), "garbage"), ("empty", Source(""), ""),
                     ("throwing source", _ => throw new InvalidOperationException("git: command not found"), "git: command not found"),
                 })
        {
            var w = ToolchainFloor.Warnings(Repo, source).Where(item => item.Tool == "git").ToArray();
            var x = w.ShouldHaveSingleItem("C1698 Unreadable warns, never throws: " + name);
            (x.Reason, x.Floor).ShouldBe(("version-unreadable", "2.43"), "C1698 Unreadable warns, never throws: " + name);
            // The whole message: the floor, what stops working and the host fix, as the below-floor message carries them.
            x.Message.ShouldBe("WARNING: the git version could not be read ('" + raw + "'); the minimum is 2.43; if it is older, " + git.Impact + ". Fix: " + HostFix("git"),
                "C1698 Unreadable message carries floor, impact and fix: " + name);
        }
    }

    [Test]
    public void C1698_SdkFloor()
    {
        foreach (var (sdks, warn, found) in new[]
                 {
                     ("9.0.300 [/x]\n10.0.100 [/x]", true, "10.0.100"), ("11.0.100 [/x]", true, "11.0.100"),
                     ("9.0.300 [/x]\r\n10.0.204 [/x]", false, "10.0.204"), ("10.1.100-preview.1 [/x]", false, "10.1.100"),
                 })
        {
            var report = ToolchainFloor.Report(Repo, Source("git version 2.47.3", sdks));
            var sdk = report.Tools.Single(row => row.Tool == "dotnet-sdk");
            var name = "C1698 Sdk " + Regex.Replace(sdks, @"\s+", " ");
            (report.Warnings.Count(w => w.Tool == "dotnet-sdk") == 1).ShouldBe(warn, name);
            (sdk.Found, sdk.Floor).ShouldBe((found, "10.0.204"), name);
        }
    }

    [Test]
    public void C1698_ManifestSources()
    {
        var report = ToolchainFloor.Report(Repo, Source("git version 2.47.3"));
        var declared = Regex.Match(File.ReadAllText(Path.Combine(Repo, "docker", "git-minimum-version.sh")), @"(?m)^GIT_MINIMUM_VERSION=(\d+\.\d+)\r?$").Groups[1].Value;
        var git = report.Tools.Single(row => row.Tool == "git");
        declared.ShouldNotBeEmpty();
        (git.Floor, git.Source).ShouldBe((declared, "docker/git-minimum-version.sh"), "C1698 Manifest git floor is the image guard declaration");
        using var global = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "global.json")));
        var pin = global.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        var sdk = report.Tools.Single(row => row.Tool == "dotnet-sdk");
        (sdk.Floor, sdk.Source).ShouldBe((pin, "global.json"), "C1698 Manifest SDK floor is global.json");
        string.Join(",", report.Tools.Where(row => !row.Enforced).Select(row => row.Tool + "=" + row.Floor + "/" + row.Status))
            .ShouldBe("pwsh=to decide/not-enforced,node=to decide/not-enforced,docker=to decide/not-enforced", "C1698 Manifest pwsh node docker are listed, not enforced");
        report.Warnings.ShouldBeEmpty("C1698 Manifest no warning at the pins");
        using var empty = NightlyFixture.Create("c1698-no-floors");
        string.Join(",", ToolchainFloor.Warnings(empty.Root, Source("git version 2.47.3")).Select(w => w.Tool + ":" + w.Reason))
            .ShouldBe("git:floor-unreadable,dotnet-sdk:floor-unreadable", "C1698 Manifest missing floor sources warn, not throw");
    }

    /// <summary>
    /// The toolchain-floor command (ToolchainFloor.Run, what the restart entry points run): the warning lines on stdout and
    /// nothing else, none at the floor, the fix of the host the probe names, and the usage refusals of its one flag.
    /// </summary>
    [Test]
    public void C1698_CommandPrintsTheWarnings()
    {
        string[] Lines(StringWriter writer) => writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = ToolchainFloorOptions.Parse(["--repo-root", Repo]);
        options.RepoRoot.ShouldBe(Repo);

        using var below = NightlyFixture.Create("c1698-command");
        var probe = new FakeHostProbe();
        probe.Versions["git"] = "git version 2.42.0";
        var written = ToolchainFloor.Run(options, below.Host(probe: probe));
        written.Select(w => w.Reason).ShouldBe(new[] { "below-floor" });
        Lines(below.Out).ShouldBe(new[] { "WARNING: git 2.42.0 is below the minimum 2.43; " + Entry("git").Impact + ". Fix: winget upgrade Git.Git" },
            "C1698 Command prints the warning");
        below.Error.ToString().ShouldBeEmpty();

        using var at = NightlyFixture.Create("c1698-command");
        probe.Versions["git"] = "git version 2.43.0";
        ToolchainFloor.Run(options, at.Host(probe: probe)).ShouldBeEmpty();
        at.Out.ToString().ShouldBeEmpty("C1698 Command prints none at the floor");

        using var linux = NightlyFixture.Create("c1698-command");
        var other = new FakeHostProbe { PlatformName = "linux" };
        other.Versions["dotnet-sdk"] = "no sdks";
        ToolchainFloor.Run(options, linux.Host(probe: other));
        Lines(linux.Out).ShouldBe(new[]
        {
            "WARNING: git 2.42.0 is below the minimum 2.43; " + Entry("git").Impact + ". Fix: " + Entry("git").FixOther,
            "WARNING: the dotnet-sdk version could not be read ('no sdks'); the minimum is 10.0.204; if it is older, " + Entry("dotnet-sdk").Impact + ". Fix: "
                + Entry("dotnet-sdk").FixOther,
        }, "C1698 Command names the fix of the host the probe names");

        Should.Throw<NightlyUsageException>(() => ToolchainFloorOptions.Parse([])).Message.ShouldBe("missing --repo-root");
        Should.Throw<NightlyUsageException>(() => ToolchainFloorOptions.Parse(["--repo-root", ""])).Message.ShouldBe("missing --repo-root");
        Should.Throw<NightlyUsageException>(() => ToolchainFloorOptions.Parse(["--repo-root", Repo, "--json"])).Message.ShouldBe("unknown flag --json");
    }

    /// <summary>
    /// restart-server.ps1 runs the command through Invoke-AntiphonCli inside a try whose catch only warns, and a non-zero exit
    /// (9: the CLI could not be built or started) is one more WARNING line, never an exit: the restart goes on as when the
    /// library only warned. The server restart warns before it stops the server. (session-runner-restart-health.ps1 had the
    /// same block; the runner restart is the restart-session-runner command since unit 30, C1698_RunnerRestartWarnsInProcess.)
    /// </summary>
    [Test]
    public void C1698_RestartEntriesRunTheCommandAndOnlyWarn()
    {
        foreach (var (path, root, quote) in new[] { ("restart-server.ps1", "$RepoRoot", "\"") })
        {
            var text = File.ReadAllText(Path.Combine(Repo, path)).Replace("\r\n", "\n");
            text.ShouldNotContain("toolchain-floor.ps1", customMessage: path);
            text.ShouldNotContain("Write-ToolchainFloorWarnings", customMessage: path);
            var call = "$floorExit = Invoke-AntiphonCli -Command 'toolchain-floor' -Arguments @('--repo-root', " + root + ")";
            var load = ". (Join-Path " + root + " (Join-Path " + quote + "scripts" + quote + " (Join-Path " + quote + "lib" + quote + " " + quote + "antiphon-cli.ps1" + quote + ")))";
            var at = text.IndexOf(call, StringComparison.Ordinal);
            at.ShouldBeGreaterThan(0, path + " runs the command");
            var open = text.LastIndexOf("try {\n", at, StringComparison.Ordinal);
            var close = text.IndexOf("} catch {\n", at, StringComparison.Ordinal);
            open.ShouldBeGreaterThan(0, path);
            close.ShouldBeGreaterThan(at, path);
            var block = text[open..close];
            block.ShouldContain(load, customMessage: path + " loads the launcher from the checkout root");
            block.IndexOf(load, StringComparison.Ordinal).ShouldBeLessThan(block.IndexOf(call, StringComparison.Ordinal), path);
            block.ShouldContain("if ($floorExit -ne 0) { Write-Host (" + quote + "WARNING: the toolchain floor check failed: antiphon-scripts toolchain-floor exited " + quote
                + " + $floorExit) -ForegroundColor Yellow }", customMessage: path + " warns on a non-zero exit");
            var leaves = new Regex(@"(?<![\w$-])(exit|throw|return)(?!\w)");
            leaves.IsMatch(block).ShouldBeFalse(path + ": the block never exits, throws or returns");
            var handler = text[close..text.IndexOf("}\n", close + "} catch {\n".Length, StringComparison.Ordinal)];
            handler.ShouldContain("Write-Host (" + quote + "WARNING: the toolchain floor check failed: " + quote + " + $_.Exception.Message) -ForegroundColor Yellow",
                customMessage: path + " catches a launcher that throws and warns");
            leaves.IsMatch(handler).ShouldBeFalse(path + ": the handler only warns");
            if (path == "restart-server.ps1") at.ShouldBeLessThan(text.IndexOf("Write-Host \"Stopping server...\"", StringComparison.Ordinal), "the warning precedes the stop");
        }
        File.Exists(Path.Combine(Repo, "scripts", "lib", "toolchain-floor.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Repo, "scripts", "test-toolchain-floor.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(Repo, "scripts", "session-runner-restart-health.ps1")).ShouldBeFalse();
    }

    /// <summary>
    /// Unit 30: the restart-session-runner command (RunnerRestart.Run) prints the floor warnings during its read-only setup,
    /// before any control operation, and restarts anyway: a tool below its floor never refuses the restart.
    /// </summary>
    [Test]
    public void C1698_RunnerRestartWarnsInProcess()
    {
        using var world = new RunnerRestartWorld();
        world.Tools.Versions["git"] = "git version 2.42.0";
        var run = world.Run();
        run.Outcome.ShouldBe("healthy");
        run.Mutations.ShouldBe(["write-state", "stop-service"]);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0].ShouldStartWith("WARNING: git 2.42.0 is below the minimum 2.43; lands will fail");
        lines.Count(line => line.StartsWith("WARNING: git ", StringComparison.Ordinal)).ShouldBe(1);
    }

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
