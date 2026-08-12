using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTuiRunnerCatalog
{
    private const string OpenCodeQuietFallbackReason =
        "PTY quiet-time fallback; ACP/event integration not active";

    public IReadOnlyList<AgentTuiRunnerTypeDto> List() =>
    [
        Get(AgentKind.ClaudeCode),
        Get(AgentKind.Codex),
        Get(AgentKind.OpenCode),
        Get(AgentKind.Raw)
    ];

    public AgentTuiRunnerTypeDto Get(
        AgentKind kind,
        IReadOnlyList<string>? profileArguments = null) => kind switch
    {
        AgentKind.ClaudeCode => Runner(
            kind,
            "Claude Code",
            "Anthropic Claude Code terminal client.",
            ["fable", "opus", "sonnet", "haiku"],
            ClaudeCapabilities(profileArguments),
            "Use a direct Claude executable or an existing authentication wrapper."),
        AgentKind.Codex => Runner(
            kind,
            "Codex",
            "OpenAI Codex terminal client.",
            ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"],
            CodexCapabilities(profileArguments),
            "Use a direct Codex executable or a wrapper that owns authentication."),
        AgentKind.OpenCode => Runner(
            kind,
            "OpenCode",
            "OpenCode terminal client with provider/model identifiers.",
            ["llmgateway/grok-4-5"],
            OpenCodeCapabilities(profileArguments),
            "Use --auto for explicit permission bypass; structured activity remains degraded until ACP/events are active."),
        AgentKind.Raw => new AgentTuiRunnerTypeDto(
            kind,
            "Raw process",
            "A generic terminal command without runner-specific guarantees.",
            null,
            [AgentTuiAuthenticationMode.WrapperManaged, AgentTuiAuthenticationMode.ManagedEnvironment],
            [],
            RawCapabilities(),
            "Raw profiles do not advertise runner-specific session capabilities."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown agent runner kind.")
    };

    public string? MapLegacyModel(AgentKind kind, AgentModelLevel level) => kind switch
    {
        AgentKind.ClaudeCode => ModelLevelAliases.ForClaude(level),
        AgentKind.Codex => level switch
        {
            AgentModelLevel.Frontier => "gpt-5.6-sol",
            AgentModelLevel.High => "gpt-5.6-terra",
            AgentModelLevel.Medium or AgentModelLevel.Low => "gpt-5.6-luna",
            _ => "gpt-5.6-terra"
        },
        _ => null
    };

    private static AgentTuiRunnerTypeDto Runner(
        AgentKind kind,
        string displayName,
        string description,
        IReadOnlyList<string> modelIdentifiers,
        IReadOnlyList<AgentTuiCapabilityDto> capabilities,
        string guidance) => new(
            kind,
            displayName,
            description,
            "--model",
            [AgentTuiAuthenticationMode.WrapperManaged, AgentTuiAuthenticationMode.ManagedEnvironment],
            modelIdentifiers.Select((identifier, index) => new AgentTuiModelDto(
                    identifier,
                    identifier,
                    Family(identifier),
                    AgentTuiModelSource.Curated,
                    AgentTuiModelAvailability.Unverified,
                    null,
                    null,
                    index == 0))
                .ToArray(),
            capabilities,
            guidance);

    private static IReadOnlyList<AgentTuiCapabilityDto> ClaudeCapabilities(
        IReadOnlyList<string>? arguments) =>
    [
        Supported("modelArgument", "Claude accepts an exact model through --model."),
        Unsupported("modelDiscovery", "No stable machine-readable model-list command is assumed."),
        Supported("structuredActivity", "Claude transcript events provide structured turn activity."),
        Supported("sessionResume", "Claude sessions can be resumed by conversation identity."),
        Supported("remoteControl", "Claude supports Antiphon's remote-control launch behaviour."),
        Supported("systemPromptAppend", "Claude supports --append-system-prompt."),
        ContainsArgument(arguments, "--dangerously-skip-permissions")
            ? Supported("permissionBypass", "The profile explicitly includes --dangerously-skip-permissions.")
            : Unsupported("permissionBypass", "The profile does not request permission bypass.")
    ];

    private static IReadOnlyList<AgentTuiCapabilityDto> CodexCapabilities(
        IReadOnlyList<string>? arguments) =>
    [
        Supported("modelArgument", "Codex accepts an exact model argument."),
        Unknown("modelDiscovery", "Installed-client model discovery has not been probed."),
        Degraded("structuredActivity", "PTY quiet-time detection is not structured turn activity."),
        Unknown("sessionResume", "Installed-client resume support has not been probed."),
        Unsupported("remoteControl", "Claude-style remote control is not available."),
        Unsupported("systemPromptAppend", "Claude-style system-prompt append is not available."),
        ContainsArgument(arguments, "--dangerously-bypass-approvals-and-sandbox")
            ? Supported("permissionBypass", "The profile explicitly requests Codex permission bypass.")
            : Unsupported("permissionBypass", "The profile does not request permission bypass.")
    ];

    private static IReadOnlyList<AgentTuiCapabilityDto> OpenCodeCapabilities(
        IReadOnlyList<string>? arguments) =>
    [
        Supported("modelArgument", "OpenCode accepts an exact provider/model identifier through --model."),
        Supported("modelDiscovery", "OpenCode exposes the models operation."),
        Degraded("structuredActivity", OpenCodeQuietFallbackReason),
        Unknown("sessionResume", "Installed OpenCode session-resume support has not been established."),
        Unsupported("remoteControl", "Claude-style remote control is not supported."),
        Unsupported("systemPromptAppend", "Claude-style system-prompt append is not supported."),
        ContainsArgument(arguments, "--auto")
            ? Supported("permissionBypass", "The profile explicitly includes --auto.")
            : Unsupported("permissionBypass", "Permission bypass requires --auto in the profile arguments.")
    ];

    private static IReadOnlyList<AgentTuiCapabilityDto> RawCapabilities() =>
    [
        Unknown("modelArgument", "Raw commands have no runner-owned model contract."),
        Unsupported("modelDiscovery", "Raw commands have no model discovery contract."),
        Degraded("structuredActivity", "PTY quiet-time detection is not structured turn activity."),
        Unknown("sessionResume", "Raw commands have no declared resume contract."),
        Unsupported("remoteControl", "Raw commands have no declared remote-control contract."),
        Unsupported("systemPromptAppend", "Raw commands have no declared system-prompt contract."),
        Unknown("permissionBypass", "Raw command permission semantics are not known.")
    ];

    private static AgentTuiCapabilityDto Supported(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Supported, reason);

    private static AgentTuiCapabilityDto Unsupported(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Unsupported, reason);

    private static AgentTuiCapabilityDto Degraded(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Degraded, reason);

    private static AgentTuiCapabilityDto Unknown(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Unknown, reason);

    private static bool ContainsArgument(IReadOnlyList<string>? arguments, string expected) =>
        arguments?.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase)) == true;

    private static string? Family(string identifier)
    {
        var slash = identifier.IndexOf('/');
        if (slash > 0)
            return identifier[..slash];

        var dash = identifier.LastIndexOf('-');
        return dash > 0 ? identifier[..dash] : identifier;
    }
}
