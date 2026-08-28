using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTuiRunnerCatalog
{
    public const string RemoteControlCapability = "remoteControl";

    /// <summary>
    /// CARD-0212. True only when the kind's catalog row declares remoteControl Supported.
    /// Unknown and Degraded read as not-supported for enabling machinery (ProviderContract rule 2).
    /// </summary>
    public bool SupportsRemoteControl(AgentKind kind) =>
        Enum.IsDefined(kind)
        && Get(kind).Capabilities.Any(c =>
            string.Equals(c.Name, RemoteControlCapability, StringComparison.Ordinal)
            && c.State == AgentTuiCapabilityState.Supported);

    public IReadOnlyList<AgentTuiRunnerTypeDto> List() =>
    [
        Get(AgentKind.ClaudeCode),
        Get(AgentKind.Codex),
        Get(AgentKind.OpenCode),
        Get(AgentKind.Grok),
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
        AgentKind.Grok => Runner(
            kind,
            "Grok",
            "xAI Grok Build TUI terminal client.",
            ["grok-4.6", "grok-4.5"],
            GrokCapabilities(profileArguments),
            "Use --always-approve for permission bypass and --no-alt-screen so Antiphon can capture the PTY. Structured activity follows the tailed ACP turn_completed stream (CARD-0080 S2)."),
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
        AgentKind.Grok => ModelLevelAliases.ForGrok(level),
        // Delegated, not duplicated (CARD-0099 S3): this table predated ModelLevelAliases.ForCodex
        // and said the same three slugs. Two copies of a ladder is how the profile picker and the
        // launch path come to disagree about which model a tier means.
        AgentKind.Codex => ModelLevelAliases.ForCodex(level),
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
        StructuredActivity(AgentKind.ClaudeCode),
        Supported("sessionResume", "Claude sessions can be resumed by conversation identity."),
        Supported(RemoteControlCapability, "Claude supports Antiphon's remote-control launch behaviour."),
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
        StructuredActivity(AgentKind.Codex),
        Unknown("sessionResume", "Installed-client resume support has not been probed."),
        Unsupported(RemoteControlCapability, "Claude-style remote control is not available."),
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
        StructuredActivity(AgentKind.OpenCode),
        Unknown("sessionResume", "Installed OpenCode session-resume support has not been established."),
        Unsupported(RemoteControlCapability, "Claude-style remote control is not supported."),
        Unsupported("systemPromptAppend", "Claude-style system-prompt append is not supported."),
        ContainsArgument(arguments, "--auto")
            ? Supported("permissionBypass", "The profile explicitly includes --auto.")
            : Unsupported("permissionBypass", "Permission bypass requires --auto in the profile arguments.")
    ];

    private static IReadOnlyList<AgentTuiCapabilityDto> GrokCapabilities(
        IReadOnlyList<string>? arguments) =>
    [
        Supported("modelArgument", "Grok accepts an exact model through --model."),
        Supported("modelDiscovery", "Grok exposes the models command."),
        StructuredActivity(AgentKind.Grok),
        Supported("sessionResume", "Grok sessions can be resumed by conversation identity."),
        Unsupported(RemoteControlCapability, "Claude-style remote control is not available."),
        Supported("systemPromptAppend", "Grok accepts extra standing instructions through --rules."),
        ContainsArgument(arguments, "--always-approve")
            || ContainsArgument(arguments, "bypassPermissions")
            ? Supported("permissionBypass", "The profile explicitly requests Grok permission bypass.")
            : Unsupported("permissionBypass", "Permission bypass requires --always-approve or --permission-mode bypassPermissions.")
    ];

    private static IReadOnlyList<AgentTuiCapabilityDto> RawCapabilities() =>
    [
        Unknown("modelArgument", "Raw commands have no runner-owned model contract."),
        Unsupported("modelDiscovery", "Raw commands have no model discovery contract."),
        StructuredActivity(AgentKind.Raw),
        Unknown("sessionResume", "Raw commands have no declared resume contract."),
        Unsupported(RemoteControlCapability, "Raw commands have no declared remote-control contract."),
        Unsupported("systemPromptAppend", "Raw commands have no declared system-prompt contract."),
        Unknown("permissionBypass", "Raw command permission semantics are not known.")
    ];

    // D2: structuredActivity is derived from ProviderContract.TurnCompletion so the TUI
    // row and the machinery cannot drift (the Grok Degraded reason was stale since CARD-0080 S2).
    private static AgentTuiCapabilityDto StructuredActivity(AgentKind kind)
    {
        var turn = ProviderContractCatalog.For(kind).TurnCompletion;
        return new AgentTuiCapabilityDto("structuredActivity", turn.State, turn.Reason);
    }

    private static AgentTuiCapabilityDto Supported(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Supported, reason);

    private static AgentTuiCapabilityDto Unsupported(string name, string reason) =>
        new(name, AgentTuiCapabilityState.Unsupported, reason);

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
