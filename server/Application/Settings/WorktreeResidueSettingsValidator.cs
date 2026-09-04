using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class WorktreeResidueSettingsValidator : IValidateOptions<WorktreeResidueSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeResidueSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RecurringJobId))
            failures.Add("WorktreeResidue:RecurringJobId must not be empty.");

        if (string.IsNullOrWhiteSpace(options.Cron))
        {
            failures.Add("WorktreeResidue:Cron must not be empty.");
        }
        else
        {
            var parts = options.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 5)
                failures.Add("WorktreeResidue:Cron must be a five-field expression (minute hour day month weekday).");
        }

        if (string.IsNullOrWhiteSpace(options.TimeZoneId))
        {
            failures.Add("WorktreeResidue:TimeZoneId must not be empty.");
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                failures.Add($"WorktreeResidue:TimeZoneId '{options.TimeZoneId}' is not a resolvable timezone.");
            }
        }

        if (options.MinSettledMinutes <= 0)
            failures.Add("WorktreeResidue:MinSettledMinutes must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
