using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// CARD-1698: ToolchainFloor.cs on every floor file shape and version text. From W2-nightly S03 until 2026-11-22 this
/// class held the compiled checks to their PowerShell twin, scripts/lib/toolchain-floor.ps1, on the same inputs; the ps1
/// is deleted (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 1) and the rows and warnings the two agreed on are stated
/// here. One deliberate change: global.json's keys are read as spelled ("sdk", "version"), so a file that spells them
/// "SDK"/"Version" (which ConvertFrom-Json matched) has an unreadable floor.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ToolchainFloorTwinTests
{
    private const string Sdk = "10.0.204 [/fake/sdk]";
    private const string GitOk = "git version 2.47.3";

    private static string Repo => DelegateScriptRunner.RepoRoot;

    private static Func<string, string> Source(string git, string sdks = Sdk) => tool => tool == "git" ? git : tool == "dotnet-sdk" ? sdks : "";

    private static ToolchainFloorRow Row(ToolchainFloorReport report, string tool) => report.Tools.Single(row => row.Tool == tool);

    private static string Fix(string tool, bool windows)
    {
        var entry = ToolchainFloor.Manifest().Single(item => item.Tool == tool);
        return windows ? entry.FixWindows : entry.FixOther;
    }

    private static string Impact(string tool) => ToolchainFloor.Manifest().Single(entry => entry.Tool == tool).Impact;

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Git_versions_against_the_repository_floor(bool windows)
    {
        foreach (var (text, status, found) in new[]
                 {
                     ("git version 2.42.0.windows.1", "below-floor", "2.42.0"), ("git version 2.9.5", "below-floor", "2.9.5"),
                     ("git version 2.43", "ok", "2.43"), ("git version 2.43.0", "ok", "2.43.0"), ("git version 2.56.0.windows.1", "ok", "2.56.0"),
                     ("git version 2.100.1", "ok", "2.100.1"), ("git version 3.0.0", "ok", "3.0.0"), ("  git version 2.47.3\n", "ok", "2.47.3"),
                     ("garbage", "version-unreadable", ""), ("", "version-unreadable", ""), (new string('x', 200), "version-unreadable", ""),
                 })
        {
            var report = ToolchainFloor.Report(Repo, Source(text), windows);
            var git = Row(report, "git");
            (git.Status, git.Found, git.Floor, git.Source, git.Enforced).ShouldBe((status, found, "2.43", "docker/git-minimum-version.sh", true), text);
            Row(report, "dotnet-sdk").Status.ShouldBe("ok", text);
            var warnings = report.Warnings.Where(w => w.Tool == "git").ToArray();
            if (status == "ok")
            {
                warnings.ShouldBeEmpty(text);
                continue;
            }
            var warning = warnings.ShouldHaveSingleItem(text);
            (warning.Found, warning.Floor, warning.Reason, warning.Impact, warning.Fix).ShouldBe((found, "2.43", status, Impact("git"), Fix("git", windows)), text);
            var raw = text.Trim();
            if (raw.Length > 120) raw = raw[..120];
            warning.Message.ShouldBe(status == "below-floor"
                ? "WARNING: git " + found + " is below the minimum 2.43; " + Impact("git") + ". Fix: " + Fix("git", windows)
                : "WARNING: the git version could not be read ('" + raw + "'); the minimum is 2.43; if it is older, " + Impact("git") + ". Fix: " + Fix("git", windows), text);
        }
    }

    [Test]
    public void Sdk_lists_against_the_pin()
    {
        foreach (var (text, status, found) in new[]
                 {
                     ("9.0.300 [/x]\n10.0.100 [/x]", "below-floor", "10.0.100"), ("11.0.100 [/x]", "below-floor", "11.0.100"),
                     ("9.0.300 [/x]\r\n10.0.204 [/x]", "ok", "10.0.204"), ("10.1.100-preview.1 [/x]", "ok", "10.1.100"),
                     ("10.0.300 [/x]\n10.0.204 [/x]\n11.0.100 [/x]", "ok", "10.0.300"), ("  10.0.204 [C:\\Program Files\\dotnet\\sdk]\r\n", "ok", "10.0.204"),
                     ("no sdks", "version-unreadable", ""),
                 })
        {
            var report = ToolchainFloor.Report(Repo, Source(GitOk, text), true);
            var sdk = Row(report, "dotnet-sdk");
            (sdk.Status, sdk.Found, sdk.Floor, sdk.Source).ShouldBe((status, found, "10.0.204", "global.json"), text);
            var warnings = report.Warnings.Where(w => w.Tool == "dotnet-sdk").ToArray();
            warnings.Length.ShouldBe(status == "ok" ? 0 : 1, text);
            if (status == "below-floor")
                warnings[0].Message.ShouldBe("WARNING: dotnet-sdk " + found + " is below the minimum 10.0.204; " + Impact("dotnet-sdk") + ". Fix: winget install Microsoft.DotNet.SDK.10", text);
            Row(report, "git").Status.ShouldBe("ok", text);
        }
    }

    [Test]
    public void Floor_files_that_cannot_be_read_warn()
    {
        using var fixture = NightlyFixture.Create("toolchain-floor-files");
        foreach (var (name, shell, global, git, sdk) in new (string, string?, string?, string, string)[]
                 {
                     ("missing", null, null, "floor-unreadable", "floor-unreadable"),
                     ("broken", "GIT_MINIMUM=2.43\n", """{"sdk":{"version":"10.0"}}""", "floor-unreadable", "floor-unreadable"),
                     ("crlf", "#!/bin/sh\r\nGIT_MINIMUM_VERSION=2.43\r\nexit 0\r\n", "{ // the pin\n  \"sdk\": { \"version\": \"10.0.204\", },\n}", "ok", "ok"),
                     ("invalid", "GIT_MINIMUM_VERSION=2.43\n", "{ not json", "ok", "floor-unreadable"),
                     // One canonical key spelling: the reader matches "sdk" and "version" exactly.
                     ("casing", "GIT_MINIMUM_VERSION=2.43\n", """{"SDK":{"Version":"10.0.204"}}""", "ok", "floor-unreadable"),
                     ("number", "GIT_MINIMUM_VERSION=2.43\n", """{"sdk":{"version":10}}""", "ok", "floor-unreadable"),
                     ("array", "GIT_MINIMUM_VERSION=2.43\n", """[{"sdk":{"version":"10.0.204"}}]""", "ok", "floor-unreadable"),
                 })
        {
            var root = fixture.PathOf(name);
            Directory.CreateDirectory(Path.Combine(root, "docker"));
            if (shell is not null) File.WriteAllText(Path.Combine(root, "docker", "git-minimum-version.sh"), shell);
            if (global is not null) File.WriteAllText(Path.Combine(root, "global.json"), global);
            foreach (var windows in new[] { true, false })
            {
                var report = ToolchainFloor.Report(root, Source(GitOk), windows);
                Row(report, "git").Status.ShouldBe(git, name);
                Row(report, "dotnet-sdk").Status.ShouldBe(sdk, name);
                // The version is still read when the floor is not, and the warning names the file it could not read.
                Row(report, "git").Found.ShouldBe("2.47.3", name);
                Row(report, "dotnet-sdk").Found.ShouldBe("10.0.204", name);
                foreach (var (tool, source, status) in new[] { ("git", "docker/git-minimum-version.sh", git), ("dotnet-sdk", "global.json", sdk) })
                {
                    var warnings = report.Warnings.Where(w => w.Tool == tool).ToArray();
                    if (status == "ok") { warnings.ShouldBeEmpty(name + " " + tool); continue; }
                    var warning = warnings.ShouldHaveSingleItem(name + " " + tool);
                    (warning.Reason, warning.Floor).ShouldBe(("floor-unreadable", ""), name + " " + tool);
                    warning.Message.ShouldBe("WARNING: the " + tool + " floor could not be read from " + source + "; " + tool + " was not checked.", name);
                }
            }
        }
    }

    [Test]
    public void A_source_that_throws_leaves_both_versions_unread()
    {
        foreach (var windows in new[] { true, false })
        {
            var report = ToolchainFloor.Report(Repo, _ => throw new InvalidOperationException("git: command not found"), windows);
            report.Tools.Where(row => row.Enforced).Select(row => row.Tool + ":" + row.Status + ":" + row.Found).ToArray()
                .ShouldBe(new[] { "git:version-unreadable:", "dotnet-sdk:version-unreadable:" });
            report.Warnings.Select(w => w.Message).ToArray().ShouldBe(new[]
            {
                "WARNING: the git version could not be read ('git: command not found'); the minimum is 2.43; if it is older, " + Impact("git") + ". Fix: " + Fix("git", windows),
                "WARNING: the dotnet-sdk version could not be read ('git: command not found'); the minimum is 10.0.204; if it is older, " + Impact("dotnet-sdk")
                    + ". Fix: " + Fix("dotnet-sdk", windows),
            });
        }
    }

    [Test]
    public void The_manifest_lists_five_tools_and_enforces_two()
    {
        ToolchainFloor.Manifest().Select(entry => entry.Tool + ":" + entry.Source + ":" + entry.Enforced).ToArray()
            .ShouldBe(new[] { "git:docker/git-minimum-version.sh:True", "dotnet-sdk:global.json:True", "pwsh::False", "node::False", "docker::False" });
        ToolchainFloor.Manifest()[0].FixWindows.ShouldBe("winget upgrade Git.Git");
        ToolchainFloor.Manifest()[1].FixWindows.ShouldBe("winget install Microsoft.DotNet.SDK.10");
        foreach (var entry in ToolchainFloor.Manifest().Where(entry => !entry.Enforced))
            (entry.Impact, entry.FixWindows, entry.FixOther).ShouldBe(("", "", ""), entry.Tool);
        var report = ToolchainFloor.Report(Repo, Source(GitOk), true);
        report.Tools.Where(row => !row.Enforced).Select(row => row.Tool + "=" + row.Floor + "/" + row.Status + "/" + row.Found).ToArray()
            .ShouldBe(new[] { "pwsh=to decide/not-enforced/", "node=to decide/not-enforced/", "docker=to decide/not-enforced/" });
        report.Warnings.ShouldBeEmpty();
    }

    [Test]
    public void The_real_probes_read_this_host()
    {
        // git and dotnet --list-sdks: each stdout line ends with a newline; a tool without a reader answers empty.
        var git = ToolchainFloor.VersionText("git");
        git.ShouldStartWith("git version");
        git.ShouldEndWith(Environment.NewLine);
        ToolchainFloor.VersionText("dotnet-sdk").ShouldContain(" [");
        ToolchainFloor.VersionText("node").ShouldBe("");
        var report = ToolchainFloor.Report(Repo);
        Row(report, "git").Found.ShouldNotBeNullOrEmpty();
        Row(report, "git").Status.ShouldBeOneOf("ok", "below-floor");
        Row(report, "dotnet-sdk").Status.ShouldBeOneOf("ok", "below-floor");
    }

    [Test]
    public void Warnings_never_throw_and_name_the_floor_source()
    {
        using var fixture = NightlyFixture.Create("toolchain-floor-twin");
        var warnings = ToolchainFloor.Warnings(fixture.Root, _ => "git version 2.47.3");
        warnings.Select(item => item.Tool + ":" + item.Reason).ShouldBe(new[] { "git:floor-unreadable", "dotnet-sdk:floor-unreadable" });
        warnings[0].Message.ShouldBe("WARNING: the git floor could not be read from docker/git-minimum-version.sh; git was not checked.");
        ToolchainFloor.Warnings(DelegateScriptRunner.RepoRoot, _ => throw new InvalidOperationException("boom"))
            .Select(item => item.Reason).ShouldBe(new[] { "version-unreadable", "version-unreadable" });
        ToolchainFloor.Warnings("", _ => "").Select(item => item.Reason).ShouldBe(new[] { "floor-unreadable", "floor-unreadable" });
    }
}
