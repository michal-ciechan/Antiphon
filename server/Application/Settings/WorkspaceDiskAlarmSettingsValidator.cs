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
        ValidateRunners(nameof(options.Runners), options.Runners, failures);
        ValidateRunners(nameof(options.AdmissionRunners), options.AdmissionRunners, failures);
        foreach (var (runnerId, filesystem) in options.AdmissionFilesystems)
        {
            if (!options.AdmissionRunners.ContainsKey(runnerId) && !options.Runners.ContainsKey(runnerId))
                failures.Add($"WorkspaceDiskAlarm:AdmissionFilesystems:{runnerId} must name a configured admission runner.");
            if (string.IsNullOrWhiteSpace(filesystem))
                failures.Add($"WorkspaceDiskAlarm:AdmissionFilesystems:{runnerId} must name a backing filesystem.");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRunners(string key, Dictionary<string, string>? runners, List<string> failures)
    {
        foreach (var (runnerId, volume) in runners ?? [])
        {
            if (string.IsNullOrWhiteSpace(runnerId))
                failures.Add($"WorkspaceDiskAlarm:{key} has a blank runner id.");
            // D-11: the desktop's disks are not alarmed; listing it is a mistake, not a choice.
            else if (string.Equals(runnerId, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase))
                failures.Add($"WorkspaceDiskAlarm:{key} must not list the desktop runner ('{runnerId}').");
            if (string.IsNullOrWhiteSpace(volume) || !volume.StartsWith('/'))
                failures.Add($"WorkspaceDiskAlarm:{key}:{runnerId} must be an absolute Linux volume path.");
        }
    }
}
