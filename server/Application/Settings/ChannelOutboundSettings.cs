using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0418: named outbound conversion profiles. Empty by default — null channel binding is passthrough.</summary>
public sealed class ChannelOutboundSettings
{
    public const string SectionName = "ChannelOutbound";

    public Dictionary<string, ChannelOutboundProfileSettings> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Server-owned durable root for frozen inputs and sealed outputs. Empty = beside the process.</summary>
    public string? StoreRoot { get; set; }

    public int MaxOutputDescriptors { get; set; } = 32;

    public long MaxUncompressedZipExpandedBytes { get; set; } = 64L * 1024 * 1024;

    public int MaxPublishAttempts { get; set; } = 3;

    public int GlobalConversionLimit { get; set; } = 2;

    public int LeaseSeconds { get; set; } = 30;
}

public sealed class ChannelOutboundProfileSettings
{
    public Guid ProjectId { get; set; }
    public Guid AgentId { get; set; }
    public string PromptFile { get; set; } = "";
    public ChannelOutboundTrigger Trigger { get; set; } = ChannelOutboundTrigger.MarkdownSources;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxPending { get; set; } = 8;
}

public sealed class ChannelOutboundSettingsValidator : IValidateOptions<ChannelOutboundSettings>
{
    public ValidateOptionsResult Validate(string? name, ChannelOutboundSettings options)
    {
        var failures = new List<string>();
        if (options.MaxOutputDescriptors is < 1 or > 256)
            failures.Add("ChannelOutbound:MaxOutputDescriptors must be 1..256.");
        if (options.MaxUncompressedZipExpandedBytes < 1024)
            failures.Add("ChannelOutbound:MaxUncompressedZipExpandedBytes must be at least 1 KiB.");
        if (options.MaxPublishAttempts is < 1 or > 10)
            failures.Add("ChannelOutbound:MaxPublishAttempts must be 1..10.");
        if (options.GlobalConversionLimit is < 1 or > 8)
            failures.Add("ChannelOutbound:GlobalConversionLimit must be 1..8.");
        if (options.LeaseSeconds is < 5 or > 300)
            failures.Add("ChannelOutbound:LeaseSeconds must be 5..300.");

        foreach (var (profileName, profile) in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                failures.Add("ChannelOutbound profile names must be non-empty.");
            if (profile.ProjectId == Guid.Empty)
                failures.Add($"ChannelOutbound:Profiles:{profileName}:ProjectId is required.");
            if (profile.AgentId == Guid.Empty)
                failures.Add($"ChannelOutbound:Profiles:{profileName}:AgentId is required.");
            if (string.IsNullOrWhiteSpace(profile.PromptFile))
                failures.Add($"ChannelOutbound:Profiles:{profileName}:PromptFile is required.");
            if (profile.PromptFile.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(profile.PromptFile))
            {
                failures.Add($"ChannelOutbound:Profiles:{profileName}:PromptFile must be a project-local relative path.");
            }

            if (profile.TimeoutSeconds is < 10 or > 300)
                failures.Add($"ChannelOutbound:Profiles:{profileName}:TimeoutSeconds must be 10..300.");
            if (profile.MaxPending is < 1 or > 32)
                failures.Add($"ChannelOutbound:Profiles:{profileName}:MaxPending must be 1..32.");
            if (!Enum.IsDefined(profile.Trigger))
                failures.Add($"ChannelOutbound:Profiles:{profileName}:Trigger is not a known value.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}