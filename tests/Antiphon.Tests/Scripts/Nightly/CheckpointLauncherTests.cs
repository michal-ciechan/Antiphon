using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// scripts/run-checkpoint.ps1 and scripts/validate-checkpoint-receipt.ps1, the thin launchers of the checkpoint tool's row
/// and validate verbs (unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): agents, the delegate
/// bundle and the server2 nested lane start the first by path, the server's CheckpointReceipts:ValidatorScript the second,
/// so they are production PowerShell and these tests start them (pwsh; both require PowerShell 7) from a copy beside
/// scripts/lib/antiphon-cli.ps1 under a temporary checkout root. ANTIPHON_SCRIPTS_DLL names the cli-echo mode of the script
/// harness host (CLI_ECHO_COMMAND=checkpoints), so nothing builds and the argv the CLI would receive is printed. The rows
/// are the launchers' half of the deleted test-run-checkpoint.ps1: every parameter's flag, the relative paths resolved
/// at the caller's location, values passed as given, the exit code and exit 9. The verbs' own rows are RowVerbTests and
/// CheckpointReceiptQualificationTests.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointLauncherTests
{
    private static string Echo => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    private sealed record Run(int Exit, string Stdout, string Stderr)
    {
        public JsonElement Echo()
        {
            var line = Stdout.Split('\n').Select(text => text.Trim()).Last(text => text.StartsWith("{\"arguments\"", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        public string[] Arguments() => Echo().GetProperty("arguments").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
    }

    private static NightlyFixture Checkout()
    {
        var fixture = NightlyFixture.Create("checkpoint-launcher");
        Directory.CreateDirectory(fixture.PathOf("scripts", "lib"));
        foreach (var file in new[] { "run-checkpoint.ps1", "validate-checkpoint-receipt.ps1", Path.Combine("lib", "antiphon-cli.ps1") })
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", file), fixture.PathOf("scripts", file));
        return fixture;
    }

    private static string Full(string path) => Path.GetFullPath(path);

    private static async Task<Run> Start(string script, IEnumerable<string> parameters, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(parameters)) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = Echo;
        start.Environment["CLI_ECHO_COMMAND"] = "checkpoints";
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        return new Run(process.ExitCode, await stdout, await stderr);
    }

    // Every parameter is the row verb's flag; the defaults go over too, so the verb never guesses the script's.
    [Test]
    public async Task Run_checkpoint_maps_every_parameter_onto_the_row_verb()
    {
        using var fixture = Checkout();
        var caller = fixture.PathOf("caller dir");
        Directory.CreateDirectory(caller);
        var run = await Start(fixture.PathOf("scripts", "run-checkpoint.ps1"),
        [
            "-Name", "CP-7", "-Project", "tests/Antiphon.Tests", "-OutputPath", @"bin-x\", "-Filter", "/*/*/(A*)|(B*)/*",
            "-ResultsRoot", ".antiphon/rows", "-NoBuild", "-Repeat", "3", "-MinExecuted", "2", "-Expect", "'A','B'",
            "-MsBuildProperty", "UseAppHost=false,Foo=bar", "-ExpectedSourceSha", new string('a', 40), "-DotnetShim", "shim.ps1",
            "-SlotWaitMinutes", "0.5", "-NoSlot", "-TimingRoot", "timing",
        ], caller);
        run.Exit.ShouldBe(0, run.Stderr);
        run.Arguments().ShouldBe(new[]
        {
            "row", "--name", "CP-7", "--project", "tests/Antiphon.Tests", "--output-path", @"bin-x\", "--filter", "/*/*/(A*)|(B*)/*",
            "--results-root", Path.Combine(caller, ".antiphon", "rows"), "--repeat", "3", "--min-executed", "2", "--slot-wait-minutes", "0.5",
            "--no-build", "--no-slot", "--expect", "'A','B'", "--msbuild-property", "UseAppHost=false,Foo=bar",
            "--expected-source-sha", new string('a', 40), "--dotnet", "shim.ps1", "--timing-root", "timing",
        });
        string.Equals(Full(run.Echo().GetProperty("workingDirectory").GetString()!).TrimEnd(Path.DirectorySeparatorChar), caller,
            StringComparison.OrdinalIgnoreCase).ShouldBeTrue("the CLI runs in the caller's directory");
    }

    [Test]
    public async Task Run_checkpoint_sends_only_the_defaults_when_nothing_optional_is_given()
    {
        using var fixture = Checkout();
        var run = await Start(fixture.PathOf("scripts", "run-checkpoint.ps1"),
            ["-Name", "CP-1", "-Project", "p", "-OutputPath", "bin-a/", "-Filter", "/*/*/C/*", "-ExpectedSourceSha", "", "-DotnetShim", ""], fixture.Root);
        run.Exit.ShouldBe(0, run.Stderr);
        run.Arguments().ShouldBe(new[]
        {
            "row", "--name", "CP-1", "--project", "p", "--output-path", "bin-a/", "--filter", "/*/*/C/*",
            "--results-root", Path.Combine(fixture.Root, ".antiphon", "checkpoints"), "--repeat", "1", "--min-executed", "1",
            "--slot-wait-minutes", "45",
        });
    }

    [Test]
    public async Task The_verbs_exit_code_is_the_launchers_and_a_cli_that_cannot_start_is_9()
    {
        using var fixture = Checkout();
        string[] row = ["-Name", "CP-1", "-Project", "p", "-OutputPath", "bin-a/", "-Filter", "f"];
        foreach (var code in new[] { 1, 2, 3, 4 })
        {
            var run = await Start(fixture.PathOf("scripts", "run-checkpoint.ps1"), row, fixture.Root,
                new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = code.ToString() });
            run.Exit.ShouldBe(code, run.Stderr);
        }
        var validate = await Start(fixture.PathOf("scripts", "validate-checkpoint-receipt.ps1"),
            ["-Evidence", "report.json", "-ExpectedSourceSha", new string('a', 40)], fixture.Root, new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "2" });
        validate.Exit.ShouldBe(2, validate.Stderr);
        foreach (var script in new[] { "run-checkpoint.ps1", "validate-checkpoint-receipt.ps1" })
        {
            var missing = await Start(fixture.PathOf("scripts", script),
                script == "run-checkpoint.ps1" ? row : ["-Evidence", "r.json", "-ExpectedSourceSha", "x"], fixture.Root,
                new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
            missing.Exit.ShouldBe(9, script + ": " + missing.Stderr);
            missing.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
        }
    }

    // The server starts this file with -Evidence, -ExpectedSourceSha and -Rows CP-1,CP-2 (CheckpointReceiptValidator).
    [Test]
    public async Task Validate_checkpoint_receipt_maps_onto_the_validate_verb()
    {
        using var fixture = Checkout();
        var run = await Start(fixture.PathOf("scripts", "validate-checkpoint-receipt.ps1"),
            ["-Evidence", "run/report.json", "-ExpectedSourceSha", new string('b', 64), "-Rows", "CP-1,CP-2", "-ExpectedRepeat", "3"], fixture.Root);
        run.Exit.ShouldBe(0, run.Stderr);
        run.Arguments().ShouldBe(new[]
        {
            "validate", "--evidence", Path.Combine(fixture.Root, "run", "report.json"), "--expected-source-sha", new string('b', 64),
            "--expected-repeat", "3", "--rows", "CP-1,CP-2",
        });
        var plain = await Start(fixture.PathOf("scripts", "validate-checkpoint-receipt.ps1"),
            ["-Evidence", fixture.PathOf("source.json"), "-ExpectedSourceSha", new string('c', 40)], fixture.Root);
        plain.Arguments().ShouldBe(new[]
        {
            "validate", "--evidence", fixture.PathOf("source.json"), "--expected-source-sha", new string('c', 40), "--expected-repeat", "1",
        });
    }

    [Test]
    public async Task The_launchers_are_thin_ascii_and_parse()
    {
        foreach (var name in new[] { "run-checkpoint.ps1", "validate-checkpoint-receipt.ps1" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            text.ShouldStartWith("#requires -Version 7.0");
            text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray()
                .ShouldBe(new[] { ". (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')" }, name);
            text.ShouldNotContain("[Antiphon.");
            text.ShouldContain("exit (Invoke-AntiphonCli -Command 'checkpoints' -Verbatim ");
            text.Split('\n').Count(line => line.Length > 0 && !line.StartsWith('#')).ShouldBeLessThan(80, name + " stays a launcher");
            File.ReadAllBytes(path).All(b => b < 128).ShouldBeTrue(name + " is ASCII");
            var command = "$e = $null; $t = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''")
                + "', [ref]$t, [ref]$e); if ($e.Count) { $e | ForEach-Object { Write-Output $_.ToString() }; exit 1 }; exit 0";
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldBe(0, name + ": " + output);
        }
    }
}
