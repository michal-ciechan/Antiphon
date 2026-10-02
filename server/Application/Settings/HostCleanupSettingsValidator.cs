using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class HostCleanupSettingsValidator : IValidateOptions<HostCleanupSettings>
{
    public ValidateOptionsResult Validate(string? name, HostCleanupSettings options)
    {
        var failures = new List<string>();
        if (options.MaxAttempts <= 0 || options.MaxBytes <= 0 || options.MaxCandidates <= 0 ||
            options.MaxCandidates > 10_000 || options.MaxDescendants <= 0 || options.PassSeconds <= 0)
            failures.Add("HostCleanup scan and deletion limits must be positive; MaxCandidates must be at most 10000.");
        if (options.NamespaceBudgetBytes <= 0 || options.MinimumFreeBytes <= 0 ||
            options.BacklogBytes <= 0 || options.BacklogDays <= 0 || options.SampleFreshMinutes <= 0 ||
            options.ReportRetentionDays <= 0 || options.PageSize is <= 0 or > 200)
            failures.Add("HostCleanup reporting limits must be positive; PageSize must be at most 200.");
        if (!double.IsFinite(options.WarningFraction) || !double.IsFinite(options.CriticalFraction) ||
            options.WarningFraction <= 0 || options.CriticalFraction > 1 ||
            options.WarningFraction >= options.CriticalFraction ||
            !double.IsFinite(options.MinimumFreeFraction) || options.MinimumFreeFraction <= 0 ||
            options.MinimumFreeFraction >= 1)
            failures.Add("HostCleanup pressure fractions must be finite and ordered within (0,1].");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { failures.Add("HostCleanup:TimeZoneId must resolve to a timezone."); }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
