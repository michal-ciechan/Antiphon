using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class AttentionSettingsValidator : IValidateOptions<AttentionSettings>
{
    public ValidateOptionsResult Validate(string? name, AttentionSettings options)
    {
        var failures = new List<string>();
        if (options.SeatIdleWarningMinutes <= 0)
            failures.Add("Attention:SeatIdleWarningMinutes must be positive.");
        if (options.SeatIdleErrorMinutes <= options.SeatIdleWarningMinutes)
            failures.Add("Attention:SeatIdleErrorMinutes must be greater than Attention:SeatIdleWarningMinutes.");
        if (options.OccupancySampleIntervalSeconds < 10 || options.OccupancySampleIntervalSeconds > 3600)
            failures.Add("Attention:OccupancySampleIntervalSeconds must be from 10 to 3600.");
        if (options.OccupancySampleRetentionDays < 1 || options.OccupancySampleRetentionDays > 365)
            failures.Add("Attention:OccupancySampleRetentionDays must be from 1 to 365.");
        if (options.InventoryTimeoutMs <= 0)
            failures.Add("Attention:InventoryTimeoutMs must be positive.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
