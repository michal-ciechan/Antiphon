using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0306 S1 — kind-gated <c>--settings</c> overlay, merge, and idempotence.</summary>
[Category("Unit")]
public class ClaudeRemoteControlLaunchArgsTests
{
    [Test]
    public void ClaudeCode_appends_settings_file_with_exactly_the_off_key()
    {
        var args = ClaudeRemoteControlLaunchArgs.ApplyOff(
            AgentKind.ClaudeCode, ["--dangerously-skip-permissions"]);

        var flag = args.ToList().IndexOf(ClaudeRemoteControlLaunchArgs.SettingsFlag);
        flag.ShouldBeGreaterThanOrEqualTo(0);
        args.Count(a => a == ClaudeRemoteControlLaunchArgs.SettingsFlag).ShouldBe(1);
        var path = args[flag + 1];
        File.Exists(path).ShouldBeTrue(path);
        Path.IsPathRooted(path).ShouldBeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["remoteControlAtStartup"]);
        doc.RootElement.GetProperty("remoteControlAtStartup").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Arguments(AgentKind.Grok)]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.Raw)]
    [Arguments(AgentKind.OpenCode)]
    public void Non_claude_kinds_leave_args_unchanged(AgentKind kind)
    {
        string[] original = ["--dangerously-skip-permissions", "--session-id", Guid.NewGuid().ToString("D")];
        var args = ClaudeRemoteControlLaunchArgs.ApplyOff(kind, original);
        args.ShouldBeSameAs(original);
    }

    [Test]
    public void Existing_settings_file_is_merged_and_stays_a_single_flag()
    {
        var prior = Path.Combine(AppContext.BaseDirectory, "card0306-prior-settings.json");
        File.WriteAllText(prior, """{"env":{"FOO":"bar"},"permissionMode":"acceptEdits"}""");

        var args = ClaudeRemoteControlLaunchArgs.ApplyOff(
            AgentKind.ClaudeCode, ["--settings", prior, "--session-id", "x"]);

        args.Count(a => a == ClaudeRemoteControlLaunchArgs.SettingsFlag).ShouldBe(1);
        var path = args[args.ToList().IndexOf(ClaudeRemoteControlLaunchArgs.SettingsFlag) + 1];
        path.ShouldNotBe(prior);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        doc.RootElement.GetProperty("remoteControlAtStartup").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("permissionMode").GetString().ShouldBe("acceptEdits");
        doc.RootElement.GetProperty("env").GetProperty("FOO").GetString().ShouldBe("bar");
        args.ShouldContain("--session-id");
        args.ShouldContain("x");
    }

    [Test]
    public void Existing_inline_json_settings_are_merged_into_a_file()
    {
        var args = ClaudeRemoteControlLaunchArgs.ApplyOff(
            AgentKind.ClaudeCode, ["""--settings={"env":{"FOO":"1"}}"""]);

        args.Count(a => a == ClaudeRemoteControlLaunchArgs.SettingsFlag
                        || a.StartsWith(ClaudeRemoteControlLaunchArgs.SettingsFlag + "=", StringComparison.Ordinal))
            .ShouldBe(1);
        var value = args[0].StartsWith(ClaudeRemoteControlLaunchArgs.SettingsFlag + "=", StringComparison.Ordinal)
            ? args[0][(ClaudeRemoteControlLaunchArgs.SettingsFlag.Length + 1)..]
            : args[1];
        File.Exists(value).ShouldBeTrue(value);
        using var doc = JsonDocument.Parse(File.ReadAllText(value));
        doc.RootElement.GetProperty("remoteControlAtStartup").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("env").GetProperty("FOO").GetString().ShouldBe("1");
    }

    [Test]
    public void ApplyOff_twice_does_not_duplicate_the_flag()
    {
        var once = ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode, ["--name", "pool"]);
        var twice = ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode, once);

        twice.Count(a => a == ClaudeRemoteControlLaunchArgs.SettingsFlag).ShouldBe(1);
        twice.ShouldBe(once);
    }

    [Test]
    public void Off_settings_path_round_trips_through_LaunchArgvGuard()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("CommandLineToArgvW is Windows-only");

        var args = ClaudeRemoteControlLaunchArgs.ApplyOff(
            AgentKind.ClaudeCode, ["--dangerously-skip-permissions"]);
        string[] argv = [.. args];
        const string exe = @"C:\Program Files\Anthropic\claude.exe";
        var commandLine = ModernConPtyConnection.BuildCommandLine(exe, argv, verbatim: false);
        LaunchArgvGuard.VerifyOrThrow(exe, argv, commandLine, "card-0306");
    }
}
