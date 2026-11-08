using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class WorkspaceDiskAlarmSettingsValidator : IValidateOptions<WorkspaceDiskAlarmSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkspaceDiskAlarmSettings options)
    {
        var failures = new List<string>();
        if (options.UsedPercent is < WorkspaceDiskAlarmSettings.MinUsedPercent or > WorkspaceDiskAlarmSettings.MaxUsedPercent)
            failures.Add($"WorkspaceDiskAlarm:UsedPercent must be between {WorkspaceDiskAlarmSettings.MinUsedPercent} and {WorkspaceDiskAlarmSettings.MaxUsedPercent}.");
        if (options.SampleFreshMinutes is < 1 or > WorkspaceDiskAlarmSettings.MaxSampleFreshMinutes)
            failures.Add($"WorkspaceDiskAlarm:SampleFreshMinutes must be between 1 and {WorkspaceDiskAlarmSettings.MaxSampleFreshMinutes}.");
        if (options.MinFreeGiB is < 0 or > WorkspaceDiskAlarmSettings.MaxMinFreeGiB)
            failures.Add($"WorkspaceDiskAlarm:MinFreeGiB must be between 0 and {WorkspaceDiskAlarmSettings.MaxMinFreeGiB}.");
        foreach (var (runnerId, volume) in options.Runners ?? [])
        {
            if (string.IsNullOrWhiteSpace(runnerId))
                failures.Add("WorkspaceDiskAlarm:Runners has a blank runner id.");
            // D-11: the desktop's disks are not alarmed; listing it is a mistake, not a choice.
            else if (string.Equals(runnerId, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase))
                failures.Add($"WorkspaceDiskAlarm:Runners must not list the desktop runner ('{runnerId}').");
            if (string.IsNullOrWhiteSpace(volume) || !volume.StartsWith('/'))
                failures.Add($"WorkspaceDiskAlarm:Runners:{runnerId} must be an absolute Linux volume path.");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
