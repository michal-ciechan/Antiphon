using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>Configuration for proactive, server-composed away digests.</summary>
public sealed class DigestSettings
{
    public bool Enabled { get; set; }
    public List<string> SendTimesLocal { get; set; } = ["08:00", "18:00"];
    public string TimeZone { get; set; } = "Europe/London";
    public bool WakeOnBlocked { get; set; } = true;
    public bool WakeOnDecision { get; set; } = true;
    /// <summary>
    /// CARD-0338 S3: Critical incident kinds that page every DigestEnabled channel once.
    /// Empty = do not page on incidents. Default is ChannelReplyLost; ProviderCapacity is
    /// excluded in the notifier (CARD-0281 already notified).
    /// </summary>
    public List<AgentIncidentKind> WakeOnIncidentKinds { get; set; } = [AgentIncidentKind.ChannelReplyLost];
    public int SweepSeconds { get; set; } = 60;
    public int MaxChars { get; set; } = 3500;
    public int RowsPerSection { get; set; } = 5;
    public string? PublicBaseUrl { get; set; }
}

public sealed class DigestSettingsValidator : IValidateOptions<DigestSettings>
{
    public ValidateOptionsResult Validate(string? name, DigestSettings settings)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone); }
        catch (TimeZoneNotFoundException) { return ValidateOptionsResult.Fail($"Digest:TimeZone '{settings.TimeZone}' is invalid."); }
        catch (InvalidTimeZoneException) { return ValidateOptionsResult.Fail($"Digest:TimeZone '{settings.TimeZone}' is invalid."); }
        if (settings.SendTimesLocal.Count == 0 || settings.SendTimesLocal.Any(t => !TimeOnly.TryParse(t, out _)))
            return ValidateOptionsResult.Fail("Digest:SendTimesLocal must contain local HH:mm times.");
        return ValidateOptionsResult.Success;
    }
}
