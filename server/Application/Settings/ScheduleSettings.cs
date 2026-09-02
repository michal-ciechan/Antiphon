using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0057. Bound from the <c>Schedules</c> configuration section.</summary>
public sealed class ScheduleSettings
{
    public const string SectionName = "Schedules";

    /// <summary>Off and the sweep selects nothing; existing rows keep NextFireAt.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Claim cadence. Floor 1 s in the hosted service.</summary>
    public int SweepSeconds { get; set; } = 5;

    /// <summary>
    /// Default zone when create omits TimeZoneId. Null falls through to Digest:TimeZone, then
    /// <see cref="TimeZoneInfo.Local"/>.
    /// </summary>
    public string? DefaultTimeZone { get; set; }

    /// <summary>Fire history kept per schedule; the sweep prunes the rest.</summary>
    public int FireHistoryKeep { get; set; } = 50;

    /// <summary>Create-time ceiling on PromptText.</summary>
    public int MaxPromptLength { get; set; } = 16_000;
}

public sealed class ScheduleSettingsValidator : IValidateOptions<ScheduleSettings>
{
    public ValidateOptionsResult Validate(string? name, ScheduleSettings settings)
    {
        if (settings.SweepSeconds < 1)
            return ValidateOptionsResult.Fail("Schedules:SweepSeconds must be at least 1.");
        if (settings.FireHistoryKeep < 1)
            return ValidateOptionsResult.Fail("Schedules:FireHistoryKeep must be at least 1.");
        if (settings.MaxPromptLength < 1)
            return ValidateOptionsResult.Fail("Schedules:MaxPromptLength must be at least 1.");
        if (!string.IsNullOrWhiteSpace(settings.DefaultTimeZone)
            && !ScheduleTimeZone.TryResolve(settings.DefaultTimeZone, out _))
        {
            return ValidateOptionsResult.Fail(
                $"Schedules:DefaultTimeZone '{settings.DefaultTimeZone}' is invalid.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>Tiny wrapper so the settings validator does not take a dependency on the recurrence type.</summary>
internal static class ScheduleTimeZone
{
    public static bool TryResolve(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
