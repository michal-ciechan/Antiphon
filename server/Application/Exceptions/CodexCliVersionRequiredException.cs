using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Exceptions;

public sealed class CodexCliVersionRequiredException : HttpException
{
    public const string TooOld = "codex_cli_version_too_old";
    public const string Unknown = "codex_cli_version_unknown";
    public const string Stale = "codex_cli_version_stale";
    public CodexCliVersionRequiredException(string code, string runner, string model, string floor,
        RunnerCodexCliVersionDto? sample, string reason, int maxAgeMinutes)
        : base(409, $"{code}: runner '{runner}' needs Codex CLI >= {floor} for {model}. "
            + "Upgrade or refresh that runner and retry, or ask the operator for a scoped expiring CLI override.", code,
            new Dictionary<string,object?>
            {
                ["runnerId"] = runner, ["model"] = model, ["requiredVersion"] = floor,
                ["observedVersion"] = CodexCliVersion.Parse(sample?.CodexCliVersion)?.ToString(),
                ["checkedAtUtc"] = sample?.CodexCliVersionCheckedAtUtc,
                ["reason"] = reason, ["maxAgeMinutes"] = maxAgeMinutes,
                ["remedy"] = "Upgrade/refresh the selected runner and retry, or commission a scoped expiring operator override.",
            }) { }
}
