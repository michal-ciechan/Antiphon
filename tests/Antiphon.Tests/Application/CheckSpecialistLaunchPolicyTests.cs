using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CheckSpecialistLaunchPolicyTests
{
    private static AgentLaunchSpec Launch(SpecialistSpec spec, params string[] extra) => new(
        "synthetic-claude", AgentKind.ClaudeCode, "claude", ["--append-system-prompt", spec.Contract, ..extra],
        new Dictionary<string, string> { ["CLAUDE_CODE_SIMPLE"] = "0", ["CLAUDE_CODE_SAFE_MODE"] = "false" },
        spec.WorkingDirectory, 120, 30);

    [Test]
    public void Card0415_V05_launch_rearms_explicit_hooks_without_claiming_certification()
    {
        using var scratch = new TempWorkspace();
        var spec = CheckInterpreterProvisioner.Spec(new()) with { WorkingDirectory = scratch.Path };
        var launch = Launch(spec, "--resume", "synthetic-native-session");
        var protectedLaunch = CheckSpecialistLaunchPolicy.Apply(launch, spec, SessionBackend.PtyHost);
        protectedLaunch.Args.Take(launch.Args.Count).ShouldBe(launch.Args);
        protectedLaunch.Args[^2].ShouldBe("--settings");
        var path = protectedLaunch.Args[^1];
        Path.GetFullPath(path).ShouldStartWith(Path.GetFullPath(scratch.Path) + Path.DirectorySeparatorChar);
        var settings = JsonNode.Parse(File.ReadAllText(path))!;
        settings["disableAllHooks"]!.GetValue<bool>().ShouldBeFalse();
        settings["remoteControlAtStartup"]!.GetValue<bool>().ShouldBeFalse();
        settings["hooks"]!["PreToolUse"]![0]!["matcher"]!.GetValue<string>().ShouldBe("*");
        settings["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().ShouldContain("exit 2");
        File.WriteAllText(path, "{\"disableAllHooks\":true}");
        CheckSpecialistLaunchPolicy.Apply(launch, spec, SessionBackend.PtyHost);
        JsonNode.Parse(File.ReadAllText(path))!["disableAllHooks"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Test]
    [Arguments("--bare")]
    [Arguments("--safe")]
    [Arguments("--settings=foreign.json")]
    [Arguments("--setting-sources")]
    [Arguments("--system-prompt")]
    [Arguments("--append-system-prompt-file")]
    [Arguments("--system-prompt-snapshot=on")]
    [Arguments("--agent")]
    [Arguments("--mcp-config")]
    [Arguments("--plugin-dir")]
    [Arguments("--tools")]
    public void Card0415_V05_inherited_policy_overrides_refuse_launch(string arg)
    {
        using var scratch = new TempWorkspace();
        var spec = CheckInterpreterProvisioner.Spec(new()) with { WorkingDirectory = scratch.Path };
        Should.Throw<ConflictException>(() => CheckSpecialistLaunchPolicy.Apply(Launch(spec, arg), spec, SessionBackend.PtyHost));
    }

    [Test]
    [Arguments("CLAUDE_CODE_SIMPLE")]
    [Arguments("CLAUDE_CODE_SAFE_MODE")]
    public void Card0415_V05_hook_disabling_environment_refuses_launch(string name)
    {
        using var scratch = new TempWorkspace();
        var spec = CheckInterpreterProvisioner.Spec(new()) with { WorkingDirectory = scratch.Path };
        var launch = Launch(spec) with { Env = new Dictionary<string, string> { [name] = "1" } };
        Should.Throw<ConflictException>(() => CheckSpecialistLaunchPolicy.Apply(launch, spec, SessionBackend.PtyHost));
    }

    [Test]
    [Arguments(AgentKind.Codex, SessionBackend.PtyHost)]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.Herdr)]
    [Arguments(AgentKind.Grok, SessionBackend.PtyHost)]
    public void Card0415_V05_uncertified_provider_or_backend_refuses_launch(AgentKind kind, SessionBackend backend)
    {
        using var scratch = new TempWorkspace();
        var spec = CheckInterpreterProvisioner.Spec(new()) with { WorkingDirectory = scratch.Path };
        Should.Throw<ConflictException>(() => CheckSpecialistLaunchPolicy.Apply(Launch(spec) with { Kind = kind }, spec, backend));
    }

    [Test]
    public void Card0415_V05_missing_current_contract_refuses_launch()
    {
        using var scratch = new TempWorkspace();
        var spec = CheckInterpreterProvisioner.Spec(new()) with { WorkingDirectory = scratch.Path };
        Should.Throw<ConflictException>(() => CheckSpecialistLaunchPolicy.Apply(Launch(spec) with { Args = [] }, spec, SessionBackend.PtyHost));
    }
}
