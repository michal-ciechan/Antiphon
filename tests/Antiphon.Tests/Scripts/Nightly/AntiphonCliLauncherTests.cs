using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: scripts/lib/antiphon-cli.ps1, the launcher the Windows PowerShell 5.1 entries run antiphon-scripts
/// through. It is production PowerShell, so these tests start it: powershell.exe (5.1) on Windows, pwsh elsewhere, from
/// a copy under a temporary checkout root (its bin-launcher stays there). ANTIPHON_SCRIPTS_DLL names the staged
/// Antiphon.Scripts build (antiphon-scripts/) or the cli-echo mode of the script harness host, so nothing runs msbuild.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AntiphonCliLauncherTests
{
    private const string Driver = """
        param([string]$Request)
        $ErrorActionPreference = 'Stop'
        $r = [IO.File]::ReadAllText($Request) | ConvertFrom-Json
        . (Join-Path $PSScriptRoot 'scripts/lib/antiphon-cli.ps1')
        $arguments = New-Object System.Collections.Generic.List[string]
        foreach ($a in @($r.arguments)) { $arguments.Add([string]$a) }
        $code = Invoke-AntiphonCli -Command ([string]$r.command) -Arguments $arguments.ToArray() -ResultFile ([string]$r.resultFile)
        exit $code
        """;

    private static string StagedCli => Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll");

    private static string StagedEcho => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    private sealed record LauncherRun(int Exit, string Stdout, string Stderr)
    {
        public JsonElement Echo()
        {
            var line = Stdout.Split('\n').Select(text => text.Trim()).First(text => text.StartsWith('{'));
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    /// <summary>A temporary checkout root holding scripts/lib/antiphon-cli.ps1 and the driver.</summary>
    private static NightlyFixture Checkout()
    {
        var fixture = NightlyFixture.Create("antiphon-cli");
        Directory.CreateDirectory(fixture.PathOf("scripts", "lib"));
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "antiphon-cli.ps1"), fixture.PathOf("scripts", "lib", "antiphon-cli.ps1"));
        File.WriteAllText(fixture.PathOf("drive.ps1"), Driver);
        return fixture;
    }

    private static Process Start(NightlyFixture fixture, string dll, string command, string[] arguments, string resultFile = "",
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var request = fixture.PathOf("request-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(request, JsonSerializer.Serialize(new { command, arguments, resultFile }));
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = fixture.Root,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", fixture.PathOf("drive.ps1"), "-Request", request })
            start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = dll;
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        return Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start");
    }

    private static async Task<LauncherRun> Finish(Process process)
    {
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            return new LauncherRun(process.ExitCode, await stdout, await stderr);
        }
    }

    private static Task<LauncherRun> Run(NightlyFixture fixture, string dll, string command, string[] arguments, string resultFile = "",
        IReadOnlyDictionary<string, string>? environment = null) =>
        Finish(Start(fixture, dll, command, arguments, resultFile, environment));

    private static string[] Arguments(JsonElement echo) =>
        echo.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray();

    [Test]
    public async Task Exit_codes_and_streams_pass_through()
    {
        using var fixture = Checkout();

        var status = await Run(fixture, StagedCli, "rc-ledger-status", []);
        status.Exit.ShouldBe(2, status.Stderr);
        status.Stderr.ShouldContain("usage: antiphon-scripts rc-ledger-import");
        status.Stdout.Trim().ShouldBeEmpty();

        var unknown = await Run(fixture, StagedCli, "no-such-command", []);
        unknown.Exit.ShouldBe(2, unknown.Stderr);
        unknown.Stderr.ShouldStartWith("usage: antiphon-scripts select-release");

        var echoed = await Run(fixture, StagedEcho, "cli-echo", ["--flag", "value"], environment: new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "37" });
        echoed.Exit.ShouldBe(37, echoed.Stderr);
        Arguments(echoed.Echo()).ShouldBe(new[] { "--flag", "value" });
        echoed.Echo().GetProperty("workingDirectory").GetString()!.TrimEnd('\\', '/').ShouldBe(fixture.Root.TrimEnd('\\', '/'), StringCompareShould.IgnoreCase);
    }

    [Test]
    public async Task A_missing_dll_exits_9_with_one_stderr_line()
    {
        using var fixture = Checkout();

        var run = await Run(fixture, fixture.PathOf("missing", "Antiphon.Scripts.dll"), "rc-ledger-status", []);

        run.Exit.ShouldBe(9);
        run.Stdout.Trim().ShouldBeEmpty();
        var lines = run.Stderr.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        lines.Length.ShouldBe(1, run.Stderr);
        lines[0].ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
    }

    [Test]
    public async Task An_empty_value_is_dropped_with_its_flag()
    {
        using var fixture = Checkout();

        var run = await Run(fixture, StagedEcho, "cli-echo", ["--a", "", "--b", "x", "--c", "   ", "--what-if", "--d", "y", ""]);

        run.Exit.ShouldBe(0, run.Stderr);
        Arguments(run.Echo()).ShouldBe(new[] { "--b", "x", "--what-if", "--d", "y" });
    }

    [Test]
    public async Task A_trailing_backslash_is_trimmed_and_quoting_survives()
    {
        using var fixture = Checkout();
        var spaced = fixture.PathOf("with space");

        var run = await Run(fixture, StagedEcho, "cli-echo",
            ["--root", spaced + "\\", "--next", "v", "--drive", @"C:\", "--quote", "say \"hi\"", "--many", @"x\\", "--inner", @"a\b c"]);

        run.Exit.ShouldBe(0, run.Stderr);
        Arguments(run.Echo()).ShouldBe(new[] { "--root", spaced, "--next", "v", "--drive", @"C:\", "--quote", "say \"hi\"", "--many", "x", "--inner", @"a\b c" });
    }

    [Test]
    public async Task The_result_file_is_passed_after_a_stale_one_is_removed()
    {
        using var fixture = Checkout();
        var result = fixture.PathOf("out dir", "result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.WriteAllText(result, "stale");

        var run = await Run(fixture, StagedEcho, "cli-echo", ["--x", "1"], result);

        run.Exit.ShouldBe(0, run.Stderr);
        var echo = run.Echo();
        Arguments(echo).ShouldBe(new[] { "--x", "1", "--result-file", result });
        echo.GetProperty("resultFileExisted").GetBoolean().ShouldBeFalse();
        using var written = JsonDocument.Parse(File.ReadAllText(result));
        Arguments(written.RootElement).ShouldBe(new[] { "--x", "1", "--result-file", result });
    }

    [Test]
    public async Task The_cli_runs_from_a_copy_and_the_source_dll_stays_unlocked()
    {
        using var fixture = Checkout();
        var source = fixture.PathOf("source");
        Directory.CreateDirectory(source);
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(StagedEcho)!))
            File.Copy(file, Path.Combine(source, Path.GetFileName(file)));
        var dll = Path.Combine(source, Path.GetFileName(StagedEcho));
        var release = fixture.PathOf("release");
        var launcherRoot = fixture.PathOf("bin-launcher");

        var process = Start(fixture, dll, "cli-echo", [], environment: new Dictionary<string, string> { ["CLI_ECHO_HOLD"] = release });
        try
        {
            var first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120));
            first.ShouldNotBeNull("the held CLI printed nothing");
            using var echo = JsonDocument.Parse(first);
            var running = echo.RootElement.GetProperty("baseDirectory").GetString()!;
            running.ShouldStartWith(Path.Combine(launcherRoot, "run-"), Case.Insensitive);
            if (OperatingSystem.IsWindows())
            {
                // While the CLI runs, the source can be rewritten (a build) but the loaded copy cannot.
                using (new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Should.Throw<IOException>(() => new FileStream(Path.Combine(running, Path.GetFileName(dll)), FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
            }
        }
        finally
        {
            File.WriteAllText(release, "go");
        }
        var held = await Finish(process);
        held.Exit.ShouldBe(0, held.Stderr);

        // The same output reuses its copy; a changed output gets a new one.
        (await Run(fixture, dll, "cli-echo", [])).Exit.ShouldBe(0);
        Directory.GetDirectories(launcherRoot, "run-*").Length.ShouldBe(1);
        File.WriteAllText(Path.Combine(source, "extra.txt"), "changed");
        var changed = await Run(fixture, dll, "cli-echo", []);
        changed.Exit.ShouldBe(0, changed.Stderr);
        Directory.GetDirectories(launcherRoot, "run-*").Length.ShouldBe(2);
        File.Exists(Path.Combine(changed.Echo().GetProperty("baseDirectory").GetString()!, "extra.txt")).ShouldBeTrue();
    }
}
