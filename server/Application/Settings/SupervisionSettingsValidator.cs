using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0334 S1: PolicyRefresh cadence and instruction-file list.</summary>
public sealed class SupervisionSettingsValidator : IValidateOptions<SupervisionSettings>
{
    public ValidateOptionsResult Validate(string? name, SupervisionSettings options)
    {
        var failures = new List<string>();
        if (options.HerdrFailureLimit is < 1 or > 10)
            failures.Add("Supervision:HerdrFailureLimit must be between 1 and 10.");
        var refresh = options.PolicyRefresh ?? new PolicyRefreshSettings();

        if (refresh.IdleMinutes < 1)
            failures.Add("Supervision:PolicyRefresh:IdleMinutes must be at least 1.");
        if (refresh.CooldownMinutes < 5)
            failures.Add("Supervision:PolicyRefresh:CooldownMinutes must be at least 5.");

        var files = refresh.InstructionFiles ?? [];
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            if (string.IsNullOrWhiteSpace(file))
            {
                failures.Add($"Supervision:PolicyRefresh:InstructionFiles[{i}] must not be empty.");
                continue;
            }

            if (Path.IsPathRooted(file) || file.Replace('\\', '/').StartsWith('/'))
            {
                failures.Add(
                    $"Supervision:PolicyRefresh:InstructionFiles[{i}] must be a relative path, not '{file}'.");
                continue;
            }

            foreach (var segment in file.Replace('\\', '/').Split('/'))
            {
                if (segment is "" or "..")
                {
                    failures.Add(
                        $"Supervision:PolicyRefresh:InstructionFiles[{i}] must not contain '..' or empty segments ('{file}').");
                    break;
                }
            }
        }

        var recovery = options.CapacityRecovery ?? new CapacityRecoverySettings();
        if (recovery.AdmissionIntervalSeconds is < 1 or > 3600)
            failures.Add("Supervision:CapacityRecovery:AdmissionIntervalSeconds must be between 1 and 3600.");
        if (recovery.JitterSeconds is < 0 or > 300)
            failures.Add("Supervision:CapacityRecovery:JitterSeconds must be between 0 and 300.");
        if (recovery.MaxEpisodeAttempts is int attempts && attempts is < 1 or > 10)
            failures.Add("Supervision:CapacityRecovery:MaxEpisodeAttempts must be between 1 and 10.");
        if (recovery.ReconciliationBatchSize is < 1 or > 1000)
            failures.Add("Supervision:CapacityRecovery:ReconciliationBatchSize must be between 1 and 1000.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
