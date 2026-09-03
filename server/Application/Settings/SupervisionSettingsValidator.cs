using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0334 S1: PolicyRefresh cadence and instruction-file list.</summary>
public sealed class SupervisionSettingsValidator : IValidateOptions<SupervisionSettings>
{
    public ValidateOptionsResult Validate(string? name, SupervisionSettings options)
    {
        var failures = new List<string>();
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
