using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>Named, explicit policies for per-conversation outbound preparation.</summary>
public sealed class ChannelOutboundSettings
{
    public const string SectionName = "ChannelOutbound";
    /// <summary>Opt-in activation of CARD-0519 capture. Keep false until the recovery slices are deployed together.</summary>
    public bool UnifiedRecoveryEnabled { get; set; }
    public int ScanIntervalSeconds { get; set; } = 5;
    public int PageSize { get; set; } = 32;
    public int MaximumPages { get; set; } = 10;
    public int RetryDelaySeconds { get; set; } = 30;
    public int SendTimeoutSeconds { get; set; } = 30;
    public int LeaseSeconds { get; set; } = 300;
    public int PreparationAttemptLimit { get; set; } = 3;
    public int PublicationAttemptLimit { get; set; } = 3;
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
        var errors = new List<string>();
        if (settings.ScanIntervalSeconds is < 1 or > 60) errors.Add("ChannelOutbound:ScanIntervalSeconds must be 1..60.");
        if (settings.PageSize is < 1 or > 32) errors.Add("ChannelOutbound:PageSize must be 1..32.");
        if (settings.MaximumPages is < 1 or > 10) errors.Add("ChannelOutbound:MaximumPages must be 1..10.");
        if (settings.RetryDelaySeconds is < 1 or > 300) errors.Add("ChannelOutbound:RetryDelaySeconds must be 1..300.");
        if (settings.SendTimeoutSeconds is < 1 or > 300) errors.Add("ChannelOutbound:SendTimeoutSeconds must be 1..300.");
        if (settings.LeaseSeconds is < 2 or > 900) errors.Add("ChannelOutbound:LeaseSeconds must be 2..900.");
        if (settings.PreparationAttemptLimit is < 1 or > 3) errors.Add("ChannelOutbound:PreparationAttemptLimit must be 1..3.");
        if (settings.PublicationAttemptLimit is < 1 or > 3) errors.Add("ChannelOutbound:PublicationAttemptLimit must be 1..3.");
        if (settings.SendTimeoutSeconds >= settings.LeaseSeconds)
            errors.Add("ChannelOutbound:SendTimeoutSeconds must be less than LeaseSeconds.");
        if (errors.Count > 0) return ValidateOptionsResult.Fail(errors);
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
