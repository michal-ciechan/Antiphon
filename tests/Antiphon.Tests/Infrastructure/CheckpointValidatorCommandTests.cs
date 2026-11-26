using System.Text.Json;
using Antiphon.Server.Infrastructure.Verification;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// N2 (unit 59 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the process the server starts to validate a
/// receipt, from CheckpointReceipts:ValidatorScript (CheckpointValidatorCommand). A built Antiphon.Checkpoints.dll runs with
/// dotnet exec (30 s); a checkout's scripts/antiphon, scripts/antiphon.cmd or the older scripts/validate-checkpoint-receipt.ps1
/// runs the CLI's checkpoints validate verb with no PowerShell (5 minutes): /bin/sh scripts/antiphon off Windows, the
/// published CLI with dotnet exec on Windows (bootstrapped first when nothing is published). The process rows run the
/// real launch against a checkout whose published CLI is the script harness host's cli-echo, so nothing builds.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointValidatorCommandTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Published = "0123456789abcdef01234567";

    private static string Harness => Path.Combine(AppContext.BaseDirectory, "script-harness-host");

    // A checkout with the two launchers and, unless bare, a published "CLI" that is cli-echo under the CLI's name.
    private static NightlyFixture Checkout(bool publish = true)
    {
        var fixture = NightlyFixture.Create("validator-command");
        Directory.CreateDirectory(fixture.PathOf("scripts"));
        foreach (var name in new[] { "antiphon", "antiphon.cmd" })
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name), fixture.PathOf("scripts", name));
        if (!publish) return fixture;
        var folder = fixture.PathOf("bin-launcher", "antiphon-scripts", Published);
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Harness, "Antiphon.ScriptHarnessHost.dll"), Path.Combine(folder, "Antiphon.Scripts.dll"));
        File.Copy(Path.Combine(Harness, "Antiphon.ScriptHarnessHost.runtimeconfig.json"), Path.Combine(folder, "Antiphon.Scripts.runtimeconfig.json"));
        File.WriteAllText(fixture.PathOf("bin-launcher", "antiphon-scripts", "current"), Published);
        return fixture;
    }

    private static readonly Dictionary<string, string?> Echo = new()
    {
        ["CLI_ECHO_COMMAND"] = "checkpoints", ["CLI_ECHO_ENV"] = "ANTIPHON_CLI_CHECK",
    };

    [Test]
    public void A_dll_keeps_thirty_seconds_and_the_cli_gets_time_to_build()
    {
        CheckpointValidatorCommand.Budget(@"C:\tools\Antiphon.Checkpoints.DLL").ShouldBe(TimeSpan.FromSeconds(30));
        foreach (var entry in new[] { "antiphon", "antiphon.cmd", "validate-checkpoint-receipt.ps1" })
            CheckpointValidatorCommand.Budget(Path.Combine("C:", "src", "Antiphon", "scripts", entry)).ShouldBe(TimeSpan.FromMinutes(5), entry);
    }

    [Test]
    public void The_setting_names_a_checkout_by_its_scripts_entry()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "validator checkout"));
        foreach (var entry in new[] { "antiphon", "antiphon.cmd", "validate-checkpoint-receipt.ps1", "ANTIPHON.CMD" })
            CheckpointValidatorCommand.Checkout(Path.Combine(root, "scripts", entry)).ShouldBe(root, entry);
        foreach (var other in new[]
        {
            "", "  ", Path.Combine(root, "scripts", "other.ps1"), Path.Combine(root, "tools", "antiphon"), Path.Combine(root, "scripts"),
            Path.Combine(root, "bin", "Antiphon.Checkpoints.dll"),
        })
            CheckpointValidatorCommand.Checkout(other).ShouldBeNull(other);
    }

    [Test]
    public void A_dll_runs_the_validate_verb_with_dotnet_exec()
    {
        const string dll = @"C:\tools\Antiphon.Checkpoints.DLL";
        var verb = CheckpointValidatorCommand.VerbArguments("r.json", "abc", ["CP-1", "CP-2"]);
        verb.ShouldBe(["validate", "--evidence", "r.json", "--expected-source-sha", "abc", "--rows", "CP-1,CP-2"]);
        foreach (var windows in new[] { true, false })
        {
            var step = CheckpointValidatorCommand.Command(dll, verb, windows).ShouldNotBeNull();
            step.FileName.ShouldBe("dotnet");
            step.Arguments.ShouldBe(["exec", dll, .. verb]);
            step.WorkingDirectory.ShouldBeNull();
            step.Environment.ShouldBeEmpty();
        }
    }

    [Test]
    public void Off_windows_the_sh_launcher_runs_the_verb()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "validator checkout"));
        var verb = CheckpointValidatorCommand.VerbArguments("/r/report.json", Sha, ["CP-1"]);
        foreach (var entry in new[] { "antiphon", "validate-checkpoint-receipt.ps1" })
        {
            var step = CheckpointValidatorCommand.Command(Path.Combine(root, "scripts", entry), verb, windows: false).ShouldNotBeNull();
            step.FileName.ShouldBe("/bin/sh", "run by sh, so a checkout without the exec bit still works");
            step.Arguments.ShouldBe([Path.Combine(root, "scripts", "antiphon"), "checkpoints", .. verb]);
            step.WorkingDirectory.ShouldBe(root);
            step.Environment.ShouldBeEmpty("the launcher sets ANTIPHON_CLI_CHECK itself");
        }
    }

    [Test]
    public void On_windows_the_published_cli_runs_with_the_launch_check()
    {
        using var fixture = Checkout();
        var verb = CheckpointValidatorCommand.VerbArguments(@"C:\r\report.json", Sha, ["CP-1"]);
        var cli = fixture.PathOf("bin-launcher", "antiphon-scripts", Published, "Antiphon.Scripts.dll");
        CheckpointValidatorCommand.PublishedCli(fixture.Root).ShouldBe(cli);
        var step = CheckpointValidatorCommand.Command(fixture.PathOf("scripts", "antiphon.cmd"), verb, windows: true).ShouldNotBeNull();
        step.FileName.ShouldBe("dotnet");
        step.Arguments.ShouldBe(["exec", cli, "checkpoints", .. verb]);
        step.WorkingDirectory.ShouldBe(fixture.Root);
        step.Environment.ShouldBe(new Dictionary<string, string?> { ["ANTIPHON_CLI_CHECK"] = fixture.Root });

        File.WriteAllText(fixture.PathOf("bin-launcher", "antiphon-scripts", "current"), "not-a-build");
        CheckpointValidatorCommand.PublishedCli(fixture.Root).ShouldBeNull();
        CheckpointValidatorCommand.Command(fixture.PathOf("scripts", "antiphon"), verb, windows: true).ShouldBeNull("nothing published: bootstrap first");
        File.WriteAllText(fixture.PathOf("bin-launcher", "antiphon-scripts", "current"), "abcdefabcdefabcdefabcdef");
        CheckpointValidatorCommand.PublishedCli(fixture.Root).ShouldBeNull("the pointer names no published folder");
    }

    // The server's first run is scripts\antiphon.cmd's: the same build line and the same publish.
    [Test]
    public void The_bootstrap_is_the_launchers_first_run()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "validator checkout"));
        var boot = Path.Combine(root, "bin-launcher", "antiphon-scripts", "boot-x");
        var steps = CheckpointValidatorCommand.BootstrapSteps(root, boot, ["PATH", "MSBuildExtensionsPath", "MSBUILD_EXE_PATH"]);
        steps.Length.ShouldBe(2);
        steps[0].FileName.ShouldBe("dotnet");
        steps[0].Arguments.ShouldBe(["build", Path.Combine(root, "scripts", "lib", "Antiphon.CliLauncher", "Antiphon.CliLauncher.csproj"), "-c", "Release",
            "-o", Path.Combine(boot, "out"), "--artifacts-path", Path.Combine(boot, "artifacts"), "-nologo", "-v", "q", "-nodeReuse:false",
            "-p:UseSharedCompilation=false"]);
        steps[1].Arguments.ShouldBe(["exec", Path.Combine(boot, "out", "Antiphon.CliLauncher.dll"), "publish", root]);
        foreach (var step in steps)
        {
            step.WorkingDirectory.ShouldBe(root);
            step.Environment.ShouldBe(new Dictionary<string, string?> { ["MSBuildExtensionsPath"] = null, ["MSBUILD_EXE_PATH"] = null });
        }
        var cmd = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "antiphon.cmd"));
        cmd.ShouldContain(@"dotnet build ""%root%\scripts\lib\Antiphon.CliLauncher\Antiphon.CliLauncher.csproj"" -c Release -o ""%boot%\out"" --artifacts-path ""%boot%\artifacts"" -nologo -v q -nodeReuse:false -p:UseSharedCompilation=false");
        cmd.ShouldContain(@"dotnet exec ""%boot%\out\Antiphon.CliLauncher.dll"" publish ""%root%""");
    }

    [Test]
    public async Task A_setting_that_names_no_validator_validates_nothing()
    {
        foreach (var setting in new[] { "", Path.Combine(Path.GetTempPath(), "scripts", "other.ps1") })
        {
            var (exit, output) = await CheckpointValidatorCommand.RunAsync(setting, CheckpointValidatorCommand.VerbArguments("r.json", Sha, ["CP-1"]),
                CancellationToken.None);
            exit.ShouldBe(CheckpointValidatorCommand.Unconfigured);
            output.ShouldStartWith("validator_unconfigured: ");
        }
    }

    // The receipt path holds every character cmd.exe would parse; it reaches the CLI as one argument, in the checkout.
    [Test]
    [Arguments("validate-checkpoint-receipt.ps1")]
    [Arguments("antiphon")]
    [Arguments("antiphon.cmd")]
    public async Task The_cli_receives_the_receipt_path_as_given(string entry)
    {
        using var fixture = Checkout();
        var evidence = fixture.PathOf("retained", "a & b | c ^ %PATH% (d)! e", "report.json");
        var verb = CheckpointValidatorCommand.VerbArguments(evidence, Sha, ["CP-1", "CP-2"]);
        var (exit, output) = await CheckpointValidatorCommand.RunAsync(fixture.PathOf("scripts", entry), verb, CancellationToken.None, Echo);
        exit.ShouldBe(0, output);
        var line = output.Split('\n').Select(text => text.Trim()).Single(text => text.StartsWith("{\"arguments\"", StringComparison.Ordinal));
        using var echo = JsonDocument.Parse(line);
        echo.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).ShouldBe(verb);
        Path.GetFullPath(echo.RootElement.GetProperty("workingDirectory").GetString()!).TrimEnd(Path.DirectorySeparatorChar)
            .ShouldBe(fixture.Root.TrimEnd(Path.DirectorySeparatorChar), "the CLI runs in the checkout");
        var check = echo.RootElement.GetProperty("environment").GetProperty("ANTIPHON_CLI_CHECK").GetString().ShouldNotBeNull();
        Path.GetFullPath(check).TrimEnd(Path.DirectorySeparatorChar).ShouldBe(fixture.Root.TrimEnd(Path.DirectorySeparatorChar),
            "the CLI checks its identity against the checkout first, as from a launcher");
    }

    // Nothing published and no launcher project: the first run cannot build, exit 9 with one line.
    [Test]
    public async Task A_checkout_whose_cli_cannot_be_built_is_exit_9()
    {
        using var fixture = Checkout(publish: false);
        var (exit, output) = await CheckpointValidatorCommand.RunAsync(fixture.PathOf("scripts", "validate-checkpoint-receipt.ps1"),
            CheckpointValidatorCommand.VerbArguments(fixture.PathOf("report.json"), Sha, ["CP-1"]), CancellationToken.None, Echo);
        exit.ShouldBe(CheckpointValidatorCommand.Unavailable, output);
        output.ShouldContain("antiphon-cli: the launcher did not build (exit ");
    }
}
