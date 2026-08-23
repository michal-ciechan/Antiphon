using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0136: warn-and-override launch gate against a fresh subscription-quota reading.
/// Section <c>SubscriptionQuotaGate</c>. Defaults live here; <c>appsettings.json</c> carries
/// only overrides. Enabled-by-default is inert with no samples (D4).
/// </summary>
public sealed class SubscriptionQuotaGateSettings
{
    /// <summary>Whole-gate switch. Default TRUE: with no samples the gate is inert (D4), so
    /// enabling it costs nothing until monitoring is turned on, and turning monitoring on
    /// then activates the gate without a second flag to remember.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A sample older than this is no evidence and passes through (D4).
    /// Default 6× the monitor's 30-minute cadence.</summary>
    public int MaxSampleAgeMinutes { get; set; } = 180;

    /// <summary>When a sample carries a percentage but no parseable reset time, assume this
    /// long until reset. Default one week — all three measured providers expose a WEEKLY
    /// limit, so unknown is treated as worst case, never as "about to reset".</summary>
    public int AssumedMinutesToResetWhenUnknown { get; set; } = 10_080;

    /// <summary>ANY rule tripping refuses. Evaluated in order; the first trip names the verdict.</summary>
    public List<SubscriptionQuotaRule> Rules { get; set; } =
    [
        new() { Name = "low-with-a-day-left", MaxRemainingPercent = 10, MinMinutesToReset = 1440 },
        new() { Name = "critical-with-hours-left", MaxRemainingPercent = 5, MinMinutesToReset = 120 },
    ];
}

public sealed class SubscriptionQuotaRule
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Trips when RemainingPercent is less than or equal to this.</summary>
    public double MaxRemainingPercent { get; set; }

    /// <summary>AND time-to-reset is greater than this, in minutes.</summary>
    public int MinMinutesToReset { get; set; }
}

public sealed class SubscriptionQuotaGateSettingsValidator : IValidateOptions<SubscriptionQuotaGateSettings>
{
    public ValidateOptionsResult Validate(string? name, SubscriptionQuotaGateSettings options)
    {
        var failures = new List<string>();

        if (options.MaxSampleAgeMinutes < 0)
            failures.Add("SubscriptionQuotaGate:MaxSampleAgeMinutes must not be negative.");
        if (options.AssumedMinutesToResetWhenUnknown < 0)
            failures.Add("SubscriptionQuotaGate:AssumedMinutesToResetWhenUnknown must not be negative.");

        foreach (var rule in options.Rules)
        {
            var label = string.IsNullOrWhiteSpace(rule.Name) ? "(unnamed)" : rule.Name;
            if (rule.MaxRemainingPercent is < 0 or > 100)
            {
                failures.Add(
                    $"SubscriptionQuotaGate:Rules[{label}].MaxRemainingPercent must be between 0 and 100.");
            }

            if (rule.MinMinutesToReset < 0)
            {
                failures.Add(
                    $"SubscriptionQuotaGate:Rules[{label}].MinMinutesToReset must not be negative.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
