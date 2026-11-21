using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S03: scripts/lib/toolchain-floor.ps1 stays for the restart chain, and ToolchainFloor.cs is its compiled twin
/// for the nightly run and tests. One pwsh process runs the ps1 (Get-ToolchainFloorReport, Get-ToolchainFloorVersionText)
/// and, through the nightly scripts' seam glue (a scriptblock source as a Func), the twin on the same floor files and
/// version texts; this process runs the twin with C# sources. All three must give the same rows and warnings.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ToolchainFloorTwinTests
{
    private const string Sdk = "10.0.204 [/fake/sdk]";

    private sealed record FloorCase(string Name, string Root, string Git, string Sdks, bool Windows, bool Throws = false, bool Real = false);

    [Test]
    [Timeout(300_000)]
    public async Task Ps1_and_twin_give_the_same_rows_and_warnings(CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("toolchain-floor-twin");
        var repo = DelegateScriptRunner.RepoRoot;
        var roots = new Dictionary<string, string>
        {
            ["repo"] = repo,
            ["missing"] = Root(fixture, "missing"),
            ["broken"] = Root(fixture, "broken", "GIT_MINIMUM=2.43\n", """{"sdk":{"version":"10.0"}}"""),
            ["crlf"] = Root(fixture, "crlf", "#!/bin/sh\r\nGIT_MINIMUM_VERSION=2.43\r\nexit 0\r\n", "{ // the pin\n  \"sdk\": { \"version\": \"10.0.204\", },\n}"),
            ["invalid"] = Root(fixture, "invalid", "GIT_MINIMUM_VERSION=2.43\n", "{ not json"),
            ["casing"] = Root(fixture, "casing", "GIT_MINIMUM_VERSION=2.43\n", """{"SDK":{"Version":"10.0.204"}}"""),
            ["number"] = Root(fixture, "number", "GIT_MINIMUM_VERSION=2.43\n", """{"sdk":{"version":10}}"""),
        };
        var cases = new List<FloorCase>();
        foreach (var windows in new[] { true, false })
        {
            var host = windows ? "windows" : "other";
            foreach (var git in new[]
                     {
                         "git version 2.42.0.windows.1", "git version 2.9.5", "git version 2.43", "git version 2.43.0",
                         "git version 2.56.0.windows.1", "git version 2.100.1", "git version 3.0.0", "garbage", "",
                         "  git version 2.47.3\n", new string('x', 200),
                     })
                cases.Add(new FloorCase(host + " git " + git, "repo", git, Sdk, windows));
            foreach (var sdks in new[]
                     {
                         "9.0.300 [/x]\n10.0.100 [/x]", "11.0.100 [/x]", "9.0.300 [/x]\r\n10.0.204 [/x]", "10.1.100-preview.1 [/x]",
                         "10.0.300 [/x]\n10.0.204 [/x]\n11.0.100 [/x]", "  10.0.204 [C:\\Program Files\\dotnet\\sdk]\r\n", "no sdks",
                     })
                cases.Add(new FloorCase(host + " sdk " + sdks, "repo", "git version 2.47.3", sdks, windows));
            cases.Add(new FloorCase(host + " throwing source", "repo", "", "", windows, Throws: true));
            foreach (var root in roots.Keys.Where(key => key != "repo"))
                cases.Add(new FloorCase(host + " floors " + root, root, "git version 2.47.3", Sdk, windows));
        }
        cases.Add(new FloorCase("real probes", "repo", "", "", ToolchainFloor.IsWindowsHost(), Real: true));

        var input = fixture.PathOf("cases.json");
        var output = fixture.PathOf("observed.json");
        File.WriteAllText(input, JsonSerializer.Serialize(cases.Select(item => new
        {
            name = item.Name, root = roots[item.Root], git = item.Git, sdks = item.Sdks, windows = item.Windows, throws = item.Throws, real = item.Real,
        })), new UTF8Encoding(false));
        var script = fixture.PathOf("twin.ps1");
        File.WriteAllText(script, Script, new UTF8Encoding(false));
        await RunPwshAsync(script, [repo, input, output], cancellationToken);

        using var observed = JsonDocument.Parse(File.ReadAllText(output));
        var rows = observed.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        rows.Length.ShouldBe(cases.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            var item = cases[i];
            rows[i].GetProperty("name").GetString().ShouldBe(item.Name);
            Func<string, string>? source = item.Real ? null
                : item.Throws ? _ => throw new InvalidOperationException("git: command not found")
                : tool => tool == "git" ? item.Git : tool == "dotnet-sdk" ? item.Sdks : "";
            var twin = Shape(ToolchainFloor.Report(roots[item.Root], source, item.Windows));
            var ps1 = rows[i].GetProperty("ps1").GetRawText();
            var glue = rows[i].GetProperty("glue").GetRawText();
            Canonical(glue).ShouldBe(Canonical(ps1), "ps1 against the twin through the seam glue: " + item.Name);
            twin.ShouldBe(Canonical(ps1), "ps1 against the twin with a C# source: " + item.Name);
        }

        // Get-ToolchainFloorVersionText and VersionText give the same raw text (Out-String of stdout).
        var texts = observed.RootElement.GetProperty("versionText");
        foreach (var tool in new[] { "git", "dotnet-sdk", "node" })
            ToolchainFloor.VersionText(tool).ShouldBe(texts.GetProperty(tool).GetString(), "version text " + tool);
        ToolchainFloor.VersionText("git").ShouldStartWith("git version");

        // The manifest rows are the ps1's, field for field.
        Canonical(observed.RootElement.GetProperty("manifest").GetRawText()).ShouldBe(Canonical(JsonSerializer.Serialize(
            ToolchainFloor.Manifest().Select(entry => new
            {
                tool = entry.Tool, source = entry.Source, enforced = entry.Enforced, impact = entry.Impact, fixWindows = entry.FixWindows, fixOther = entry.FixOther,
            }))));
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

    private static string Root(NightlyFixture fixture, string name, string? shell = null, string? global = null)
    {
        var root = fixture.PathOf(name);
        Directory.CreateDirectory(Path.Combine(root, "docker"));
        if (shell is not null) File.WriteAllText(Path.Combine(root, "docker", "git-minimum-version.sh"), shell);
        if (global is not null) File.WriteAllText(Path.Combine(root, "global.json"), global);
        return root;
    }

    private static string Shape(ToolchainFloorReport report) => Canonical(JsonSerializer.Serialize(new
    {
        tools = report.Tools.Select(row => new { tool = row.Tool, found = row.Found, floor = row.Floor, source = row.Source, enforced = row.Enforced, status = row.Status }),
        warnings = report.Warnings.Select(row => new
        {
            tool = row.Tool, found = row.Found, floor = row.Floor, impact = row.Impact, fix = row.Fix, reason = row.Reason, message = row.Message,
        }),
    }));

    // One text per JSON value, independent of the writer's whitespace and escaping.
    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    internal static async Task RunPwshAsync(string script, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var text = await stdout + await stderr;
            process.ExitCode.ShouldBe(0, text);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
    }

    private const string Script = """
        param([string]$Repo, [string]$CasesPath, [string]$OutPath)
        $ErrorActionPreference = 'Stop'
        . (Join-Path $Repo 'scripts/lib/nightly-core.ps1'); Assert-NightlyCoreAvailable 'toolchain-floor-twin'
        . (Join-Path $Repo 'scripts/lib/toolchain-floor.ps1')
        function ConvertTo-TwinShape($Report) {
            return [ordered]@{
                tools = @(foreach ($t in @($Report.tools)) { [ordered]@{ tool = [string]$t.tool; found = [string]$t.found; floor = [string]$t.floor; source = [string]$t.source; enforced = [bool]$t.enforced; status = [string]$t.status } })
                warnings = @(foreach ($w in @($Report.warnings)) { [ordered]@{ tool = [string]$w.tool; found = [string]$w.found; floor = [string]$w.floor; impact = [string]$w.impact; fix = [string]$w.fix; reason = [string]$w.reason; message = [string]$w.message } })
            }
        }
        $cases = @([IO.File]::ReadAllText($CasesPath) | ConvertFrom-Json)
        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($c in $cases) {
            $git = [string]$c.git; $sdks = [string]$c.sdks
            if ([bool]$c.real) { $source = $null }
            elseif ([bool]$c.throws) { $source = { param($tool) throw 'git: command not found' } }
            else { $source = { param($tool) if ($tool -eq 'git') { $git } elseif ($tool -eq 'dotnet-sdk') { $sdks } else { '' } }.GetNewClosure() }
            if ([bool]$c.windows) { function Test-ToolchainFloorWindowsHost { return $true } } else { function Test-ToolchainFloorWindowsHost { return $false } }
            $ps1 = Get-ToolchainFloorReport -RepoRoot ([string]$c.root) -VersionSource $source
            # The glue of nightly-run-impl.ps1 and nightly-tests-impl.ps1: the seam scriptblock as a Func.
            $func = $null
            if ($null -ne $source) {
                $seam = $source
                $func = [Func[string, string]]({ param([string]$Tool) [string](& $seam $Tool) }.GetNewClosure())
            }
            $twin = [Antiphon.Nightly.ToolchainFloor]::Report([string]$c.root, $func, [bool]$c.windows)
            $rows.Add([ordered]@{ name = [string]$c.name; ps1 = (ConvertTo-TwinShape $ps1); glue = (ConvertTo-TwinShape $twin) })
        }
        $texts = [ordered]@{}
        foreach ($tool in @('git', 'dotnet-sdk', 'node')) { $texts[$tool] = [string](Get-ToolchainFloorVersionText $tool) }
        $manifest = @(foreach ($e in (Get-ToolchainFloorManifest)) { [ordered]@{ tool = $e.tool; source = $e.source; enforced = [bool]$e.enforced; impact = $e.impact; fixWindows = $e.fixWindows; fixOther = $e.fixOther } })
        $out = [ordered]@{ cases = $rows.ToArray(); versionText = $texts; manifest = $manifest }
        [IO.File]::WriteAllText($OutPath, ($out | ConvertTo-Json -Depth 8))
        """;
}
