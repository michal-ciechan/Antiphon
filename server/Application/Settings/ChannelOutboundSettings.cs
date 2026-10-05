using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>Named, explicit policies for per-conversation outbound preparation.</summary>
public sealed class ChannelOutboundSettings
{
    public const string SectionName = "ChannelOutbound";
    /// <summary>Opt-in activation of CARD-0519 capture. Keep false until the recovery slices are deployed together.</summary>
    public bool UnifiedRecoveryEnabled { get; set; }
    public Dictionary<string, ChannelOutboundProfile> Profiles { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ChannelOutboundProfile
{
    public Guid ProjectId { get; set; }
    public Guid AgentId { get; set; }
    public string PromptFile { get; set; } = string.Empty;
    public ChannelOutboundTrigger Trigger { get; set; } = ChannelOutboundTrigger.MarkdownSources;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxPending { get; set; } = 8;
}

public enum ChannelOutboundTrigger
{
    MarkdownSources,
    EveryAgentReply,
}

public sealed class ChannelOutboundSettingsValidator : IValidateOptions<ChannelOutboundSettings>
{
    public ValidateOptionsResult Validate(string? name, ChannelOutboundSettings settings)
    {
        if (settings.Profiles is null)
            return ValidateOptionsResult.Fail("ChannelOutbound:Profiles is required.");

        foreach (var (key, profile) in settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || profile is null
                || profile.ProjectId == Guid.Empty || profile.AgentId == Guid.Empty
                || string.IsNullOrWhiteSpace(profile.PromptFile)
                || Path.IsPathRooted(profile.PromptFile)
                || profile.PromptFile.Split('/', '\\').Any(part => part is ".." or "")
                || !Enum.IsDefined(profile.Trigger)
                || profile.TimeoutSeconds is < 10 or > 300
                || profile.MaxPending is < 1 or > 32)
                return ValidateOptionsResult.Fail($"ChannelOutbound:Profiles:{key} has invalid identity, prompt, trigger or limits.");
        }

        return ValidateOptionsResult.Success;
    }
}
