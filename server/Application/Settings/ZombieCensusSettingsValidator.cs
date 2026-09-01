using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class ZombieCensusSettingsValidator : IValidateOptions<ZombieCensusSettings>
{
    public ValidateOptionsResult Validate(string? name, ZombieCensusSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RecurringJobId))
            failures.Add("ZombieCensus:RecurringJobId must not be empty.");

        if (string.IsNullOrWhiteSpace(options.Cron))
        {
            failures.Add("ZombieCensus:Cron must not be empty.");
        }
        else
        {
            var parts = options.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 5)
                failures.Add("ZombieCensus:Cron must be a five-field expression (minute hour day month weekday).");
        }

        if (string.IsNullOrWhiteSpace(options.TimeZoneId))
        {
            failures.Add("ZombieCensus:TimeZoneId must not be empty.");
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                failures.Add($"ZombieCensus:TimeZoneId '{options.TimeZoneId}' is not a resolvable timezone.");
            }
        }

        if (options.MinDoneMinutes <= 0)
            failures.Add("ZombieCensus:MinDoneMinutes must be positive.");
        if (options.QuietHours <= 0)
            failures.Add("ZombieCensus:QuietHours must be positive.");
        if (options.PidReuseToleranceSeconds <= 0)
            failures.Add("ZombieCensus:PidReuseToleranceSeconds must be positive.");
        if (string.IsNullOrWhiteSpace(options.SessionLogPath))
            failures.Add("ZombieCensus:SessionLogPath must not be empty.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
