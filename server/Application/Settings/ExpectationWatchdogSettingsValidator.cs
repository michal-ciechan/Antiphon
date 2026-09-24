using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Syntax, duplicate and range checks. Reference checks (board, agent, channel, runner catalog)
/// are <see cref="ExpectationDirectiveReferences"/> and produce a configuration fault instead of
/// failing options binding.
/// </summary>
public sealed class ExpectationWatchdogSettingsValidator : IValidateOptions<ExpectationWatchdogSettings>
{
    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, ExpectationWatchdogSettings options)
    {
        if (options is null)
            return ValidateOptionsResult.Fail("ExpectationWatchdog settings are required.");

        var failures = new List<string>();
        var directives = options.Directives ?? [];
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var enabledBoards = new HashSet<Guid>();

        for (var index = 0; index < directives.Count; index++)
        {
            var directive = directives[index];
            var path = $"ExpectationWatchdog:Directives[{index}]";
            var id = (directive.Id ?? string.Empty).Trim();
            if (!IdPattern.IsMatch(id))
            {
                failures.Add($"{path}:Id must be a stable identifier.");
            }
            else if (!seenIds.Add(id))
            {
                failures.Add($"{path}:Id duplicate directive id '{id}'.");
            }

            if (directive.AgentId == Guid.Empty)
                failures.Add($"{path}:AgentId is required.");
            if (directive.BoardId == Guid.Empty)
                failures.Add($"{path}:BoardId is required.");
            if (directive.AuditCardId == Guid.Empty)
                failures.Add($"{path}:AuditCardId is required.");
            if (directive.OperatorChannelId == Guid.Empty)
                failures.Add($"{path}:OperatorChannelId is required.");
            if (directive.ActiveUntilUtc is { Offset: var offset } && offset != TimeSpan.Zero)
                failures.Add($"{path}:ActiveUntilUtc must be UTC.");
            if (directive.Enabled && directive.BoardId != Guid.Empty && !enabledBoards.Add(directive.BoardId))
                failures.Add($"{path}:BoardId duplicate enabled board '{directive.BoardId:D}'.");

            var targets = directive.Targets ?? [];
            if (targets.Count == 0)
                failures.Add($"{path}:Targets must include at least one target.");

            var runners = new HashSet<string>(StringComparer.Ordinal);
            for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                var target = targets[targetIndex];
                var targetPath = $"{path}:Targets[{targetIndex}]";
                string runnerKey;
                if (target.RunnerId is null)
                {
                    runnerKey = "local";
                }
                else if (!IdPattern.IsMatch(target.RunnerId.Trim()) || target.RunnerId != target.RunnerId.Trim())
                {
                    failures.Add($"{targetPath}:RunnerId must be a stable identifier or null for local.");
                    runnerKey = target.RunnerId;
                }
                else
                {
                    runnerKey = target.RunnerId;
                }

                if (!runners.Add(runnerKey))
                    failures.Add($"{targetPath}:RunnerId duplicate runner '{runnerKey}'.");
                if (target.InFlightTarget <= 0)
                    failures.Add($"{targetPath}:InFlightTarget must be positive.");

                var candidates = target.Candidates ?? [];
                if (candidates.Count == 0)
                    failures.Add($"{targetPath}:Candidates must include at least one candidate.");

                var seenCandidates = new HashSet<string>(StringComparer.Ordinal);
                for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    var candidate = candidates[candidateIndex];
                    var candidatePath = $"{targetPath}:Candidates[{candidateIndex}]";
                    if (!Enum.IsDefined(candidate.AgentKind))
                        failures.Add($"{candidatePath}:AgentKind is unknown.");
                    if (!Enum.IsDefined(candidate.ModelLevel))
                        failures.Add($"{candidatePath}:ModelLevel is unknown.");
                    var key = (candidate.SubscriptionKey ?? string.Empty).Trim();
                    if (key.Length == 0 || key.Length > 100 || key.Any(char.IsWhiteSpace) || key != candidate.SubscriptionKey)
                        failures.Add($"{candidatePath}:SubscriptionKey must be a trimmed identifier.");
                    else if (!seenCandidates.Add($"{candidate.AgentKind}:{candidate.ModelLevel}:{key}"))
                        failures.Add($"{candidatePath}:SubscriptionKey duplicate candidate.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
