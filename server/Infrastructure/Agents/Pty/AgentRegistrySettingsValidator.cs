using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

/// <summary>
/// Fail-fast validator for <see cref="AgentRegistrySettings"/>. Wired with
/// <c>AddOptions&lt;AgentRegistrySettings&gt;().ValidateOnStart()</c> so bad config
/// kills host startup rather than waiting for first agent launch.
/// </summary>
public sealed class AgentRegistrySettingsValidator : IValidateOptions<AgentRegistrySettings>
{
    public ValidateOptionsResult Validate(string? name, AgentRegistrySettings options)
    {
        var failures = new List<string>();

        if (options.Definitions.Count == 0)
        {
            failures.Add("Agents:Definitions must contain at least one entry.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (defName, def) in options.Definitions)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                failures.Add("Agents:Definitions contains an entry with an empty key.");
                continue;
            }

            if (!seenNames.Add(defName))
            {
                failures.Add($"Agents:Definitions contains duplicate definition name '{defName}' (case-insensitive).");
            }

            if (string.IsNullOrWhiteSpace(def.Exe))
            {
                failures.Add($"Agents:Definitions:{defName}:Exe must not be empty.");
            }

            if (!Enum.TryParse<AgentKind>(def.Kind, ignoreCase: true, out _))
            {
                failures.Add($"Agents:Definitions:{defName}:Kind '{def.Kind}' is not a known AgentKind. Valid values: {string.Join(", ", Enum.GetNames<AgentKind>())}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultDefinition)
            && !options.Definitions.ContainsKey(options.DefaultDefinition))
        {
            failures.Add($"Agents:DefaultDefinition '{options.DefaultDefinition}' does not match any entry in Agents:Definitions.");
        }

        if (options.ClaudeReadyQuietPeriodMs <= 0)
            failures.Add("Agents:ClaudeReadyQuietPeriodMs must be positive.");
        if (options.ClaudeReadyMaxWaitMs <= 0)
            failures.Add("Agents:ClaudeReadyMaxWaitMs must be positive.");
        if (options.ClaudeDoneMaxWaitMs <= 0)
            failures.Add("Agents:ClaudeDoneMaxWaitMs must be positive.");
        if (options.CodexReadyQuietPeriodMs <= 0)
            failures.Add("Agents:CodexReadyQuietPeriodMs must be positive.");
        if (options.CodexReadyMaxWaitMs <= 0)
            failures.Add("Agents:CodexReadyMaxWaitMs must be positive.");
        if (options.CodexDoneQuietPeriodMs <= 0)
            failures.Add("Agents:CodexDoneQuietPeriodMs must be positive.");
        if (options.CodexDoneMaxWaitMs <= 0)
            failures.Add("Agents:CodexDoneMaxWaitMs must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
