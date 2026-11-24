using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// scripts/build-slot.ps1, the thin launcher of the build-slot command (unit 34 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): agents, the delegate bundle, other checkouts and the server2
/// harnesses run it by path, so it is production PowerShell and these tests start it, under pwsh and Windows PowerShell 5.1,
/// from a copy beside scripts/lib/antiphon-cli.ps1 under a temporary checkout root. ANTIPHON_SCRIPTS_DLL names the cli-echo
/// mode of the script harness host, so nothing runs msbuild and the argv the CLI would receive is printed. The rows are the
/// launcher's half of the deleted driver's C589 and C800/C845 in-process cases: the raw command line under -File (so -m:2 stays
/// one token), an in-process caller's unsplatted arrays, empty elements and pushed location, the exit code, exit 9, and the
/// holder PID (the launcher's process, as when the body was PowerShell). The command's own rows are BuildSlotScriptTests.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BuildSlotLauncherTests
{
    private static string Echo => Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");

    private sealed record Run(int Exit, int Pid, string Stdout, string Stderr)
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
        var fixture = NightlyFixture.Create("build-slot-launcher");
        Directory.CreateDirectory(fixture.PathOf("scripts", "lib"));
        foreach (var file in new[] { "build-slot.ps1", Path.Combine("lib", "antiphon-cli.ps1") })
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", file), fixture.PathOf("scripts", file));
        return fixture;
    }

    private static bool SamePath(string observed, string expected) =>
        string.Equals(Path.GetFullPath(observed).TrimEnd(Path.DirectorySeparatorChar), expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string[] Shells => OperatingSystem.IsWindows() ? ["pwsh", "powershell.exe"] : ["pwsh"];

    private static async Task<Run> Start(string shell, string script, IEnumerable<string> parameters, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(parameters)) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = Echo;
        start.Environment["CLI_ECHO_COMMAND"] = "build-slot";
        foreach (var pair in environment ?? new Dictionary<string, string>()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException(shell + " did not start");
        var pid = process.Id;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        return new Run(process.ExitCode, pid, await stdout, await stderr);
    }

    // Under -File the launcher reads its raw command line: -m:2 stays one token (the binder would split it), and an empty
    // token, a trailing backslash, quotes and wildcards arrive as given. Its flags become the command's; its PID is the holder.
    [Test]
    public async Task File_form_passes_its_flags_and_the_raw_command_verbatim()
    {
        using var fixture = Checkout();
        foreach (var shell in Shells)
        {
            string[] command = ["dotnet", "build", "-m:2", "", "a b", @"folder\", "*/*/[Category=Unit]", "-Switch:$false", "quote\"d", "--", "-Label"];
            var run = await Start(shell, fixture.PathOf("scripts", "build-slot.ps1"), ["-Label", "mutation shard", "-SlotWaitMinutes", "2", "-NoSlot", "--", .. command], fixture.Root);
            run.Exit.ShouldBe(0, shell + ": " + run.Stderr);
            run.Arguments().ShouldBe(["--label", "mutation shard", "--slot-wait-minutes", "2", "--no-slot", "--holder-pid", run.Pid.ToString(), "--", .. command], shell);
            var options = BuildSlotOptions.Parse(run.Arguments(), fixture.Root);
            (options.Label, options.WaitMinutes, options.NoSlot, options.HolderPid).ShouldBe(("mutation shard", 2.0, true, (int?)run.Pid), shell);
            options.Command.ShouldBe(command, shell + ": the command parses back verbatim");
            SamePath(run.Echo().GetProperty("workingDirectory").GetString()!, fixture.Root).ShouldBeTrue(shell + ": the CLI runs in the caller's directory");
        }
    }

    [Test]
    public async Task Without_a_separator_the_first_other_token_starts_the_command_and_a_blank_label_is_left_out()
    {
        using var fixture = Checkout();
        var run = await Start("pwsh", fixture.PathOf("scripts", "build-slot.ps1"), ["-label", "", "dotnet", "test", "-NoSlot"], fixture.Root);
        run.Exit.ShouldBe(0, run.Stderr);
        run.Arguments().ShouldBe(["--holder-pid", run.Pid.ToString(), "--", "dotnet", "test", "-NoSlot"], "a flag after the command belongs to it");

        // No command: the CLI answers the usage error (exit 2); the launcher adds nothing of its own.
        var usage = await Start("pwsh", fixture.PathOf("scripts", "build-slot.ps1"), ["-Label", "x"], fixture.Root);
        usage.Arguments().ShouldBe(["--label", "x", "--holder-pid", usage.Pid.ToString(), "--"]);
    }

    [Test]
    public async Task The_commands_exit_code_is_the_launchers_and_a_cli_that_cannot_start_is_9()
    {
        using var fixture = Checkout();
        var entry = fixture.PathOf("scripts", "build-slot.ps1");
        foreach (var shell in Shells)
        {
            var seven = await Start(shell, entry, ["-Label", "red", "--", "dotnet", "build"], fixture.Root, new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "7" });
            seven.Exit.ShouldBe(7, shell + ": " + seven.Stderr);
            var four = await Start(shell, entry, ["-Label", "late", "--", "dotnet", "build"], fixture.Root, new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "4" });
            four.Exit.ShouldBe(4, shell + ": the slot timeout passes through");
            var missing = await Start(shell, entry, ["-Label", "x", "--", "dotnet", "build"], fixture.Root,
                new Dictionary<string, string> { ["ANTIPHON_SCRIPTS_DLL"] = fixture.PathOf("missing", "Antiphon.Scripts.dll") });
            missing.Exit.ShouldBe(9, shell + ": " + missing.Stderr);
            missing.Stderr.Trim().ShouldStartWith("antiphon-cli: ANTIPHON_SCRIPTS_DLL names no file");
        }
    }

    // Called from a PowerShell session (dev-aspire.ps1, start-windows-phone-home-runner.ps1 call it with &), the launcher
    // reads $args: an unsplatted array stays one token per element, an empty element included; the CLI runs at the caller's
    // pushed location; the caller's earlier $LASTEXITCODE does not leak; the caller's process holds the lease.
    [Test]
    public async Task An_in_process_caller_keeps_its_arrays_location_and_exit_code()
    {
        using var fixture = Checkout();
        var pushed = fixture.PathOf("pushed folder");
        Directory.CreateDirectory(pushed);
        File.WriteAllText(Path.Combine(pushed, "run.trx"), "");
        var caller = fixture.PathOf("caller.ps1");
        File.WriteAllText(caller, """
            param([string]$Launcher, [string]$Pushed)
            $global:LASTEXITCODE = 37
            Push-Location -LiteralPath $Pushed
            $array = @('*.trx', '', '/*/*/*/*[Category=Unit]')
            & $Launcher -Label inproc -- dotnet build $array x
            $code = $LASTEXITCODE
            Pop-Location
            [Console]::Out.WriteLine('CALLER ' + $PID + ' ' + $code)
            exit $code
            """);
        foreach (var shell in Shells)
        {
            var run = await Start(shell, caller, ["-Launcher", fixture.PathOf("scripts", "build-slot.ps1"), "-Pushed", pushed], fixture.Root,
                new Dictionary<string, string> { ["CLI_ECHO_EXIT"] = "5" });
            run.Exit.ShouldBe(5, shell + ": " + run.Stdout + run.Stderr);
            run.Stdout.ShouldContain("CALLER " + run.Pid + " 5");
            run.Arguments().ShouldBe(["--label", "inproc", "--holder-pid", run.Pid.ToString(), "--", "dotnet", "build", "*.trx", "", "/*/*/*/*[Category=Unit]", "x"], shell);
            SamePath(run.Echo().GetProperty("workingDirectory").GetString()!, pushed).ShouldBeTrue(shell + ": the CLI runs at the pushed location");
        }
    }

    [Test]
    public async Task The_launcher_is_thin_ascii_and_parses_on_both_shells()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "build-slot.ps1");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        text.ShouldStartWith("#requires -Version 5.1");
        text.Split('\n').Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\.\s")).ToArray()
            .ShouldBe([". (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')"]);
        text.ShouldNotContain("[Antiphon.");
        text.ShouldContain("exit (Invoke-AntiphonCli -Command 'build-slot' ");
        foreach (var file in new[] { path, Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "antiphon-cli.ps1") })
            File.ReadAllBytes(file).All(b => b < 128).ShouldBeTrue(file + " is ASCII");
        foreach (var shell in Shells)
        {
            var command = "$e = $null; $t = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''")
                + "', [ref]$t, [ref]$e); if ($e.Count) { $e | ForEach-Object { Write-Output $_.ToString() }; exit 1 }; exit 0";
            var start = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException(shell + " did not start");
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldBe(0, shell + ": " + output);
        }
    }
}
