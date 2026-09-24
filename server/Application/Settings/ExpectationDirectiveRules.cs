using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Settings;

public sealed record ExpectationAgentReference(Guid? BoardId, bool IsPoolDelegate);

public sealed record ExpectationReferenceCatalog(
    IReadOnlyDictionary<Guid, Guid> CardBoards,
    IReadOnlyDictionary<Guid, ExpectationAgentReference> Agents,
    IReadOnlyDictionary<Guid, bool> ChannelsEnabled,
    IReadOnlySet<string> ConfiguredRunnerIds);

public sealed record ExpectationConfigurationFault(string Code, string Detail);

/// <summary>Normalized sha256 of a directive. Order and surrounding whitespace are not semantic.</summary>
public static class ExpectationDirectiveDigest
{
    public static string Compute(ExpectationDirectiveSettings directive)
    {
        var builder = new StringBuilder();
        builder.Append("v1\n");
        builder.Append("id=").Append((directive.Id ?? string.Empty).Trim()).Append('\n');
        builder.Append("agent=").Append(directive.AgentId.ToString("D")).Append('\n');
        builder.Append("board=").Append(directive.BoardId.ToString("D")).Append('\n');
        builder.Append("card=").Append(directive.AuditCardId.ToString("D")).Append('\n');
        builder.Append("channel=").Append(directive.OperatorChannelId.ToString("D")).Append('\n');
        builder.Append("enabled=").Append(directive.Enabled ? "true" : "false").Append('\n');
        builder.Append("until=");
        if (directive.ActiveUntilUtc is { } until)
            builder.Append(until.UtcTicks.ToString(CultureInfo.InvariantCulture));
        else
            builder.Append("none");
        builder.Append('\n');

        var targets = (directive.Targets ?? [])
            .Select(target => new
            {
                Runner = string.IsNullOrWhiteSpace(target.RunnerId) ? "local" : target.RunnerId.Trim(),
                target.InFlightTarget,
                Candidates = (target.Candidates ?? [])
                    .Select(candidate =>
                        candidate.AgentKind.ToString()
                        + ":"
                        + candidate.ModelLevel.ToString()
                        + ":"
                        + (candidate.SubscriptionKey ?? string.Empty).Trim())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(target => target.Runner, StringComparer.Ordinal);

        foreach (var target in targets)
        {
            builder.Append("target=").Append(target.Runner)
                .Append(" inflight=").Append(target.InFlightTarget.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
            foreach (var candidate in target.Candidates)
                builder.Append("candidate=").Append(candidate).Append('\n');
        }

        return HashUtf8(builder.ToString());
    }

    public static string HashUtf8(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class ExpectationDirectiveActivity
{
    public static bool HasEffects(
        ExpectationWatchdogSettings settings,
        ExpectationDirectiveSettings directive,
        DateTimeOffset now)
    {
        if (settings is null || directive is null)
            return false;
        if (!settings.Enabled || !directive.Enabled)
            return false;
        if (directive.ActiveUntilUtc is { } until && until.UtcTicks <= now.UtcTicks)
            return false;
        return true;
    }
}

public static class ExpectationDirectiveReferences
{
    public static IReadOnlyList<ExpectationConfigurationFault> Evaluate(
        ExpectationDirectiveSettings directive,
        ExpectationReferenceCatalog catalog)
    {
        var faults = new List<ExpectationConfigurationFault>();
        if (!catalog.Agents.TryGetValue(directive.AgentId, out var agent))
        {
            faults.Add(new ExpectationConfigurationFault("agent_missing", directive.AgentId.ToString("D")));
        }
        else if (agent.IsPoolDelegate)
        {
            faults.Add(new ExpectationConfigurationFault("pool_agent", directive.AgentId.ToString("D")));
        }
        else if (agent.BoardId != directive.BoardId)
        {
            faults.Add(new ExpectationConfigurationFault("agent_wrong_board", directive.AgentId.ToString("D")));
        }

        if (!catalog.CardBoards.TryGetValue(directive.AuditCardId, out var cardBoard))
            faults.Add(new ExpectationConfigurationFault("audit_card_missing", directive.AuditCardId.ToString("D")));
        else if (cardBoard != directive.BoardId)
            faults.Add(new ExpectationConfigurationFault("audit_card_wrong_board", directive.AuditCardId.ToString("D")));

        if (!catalog.ChannelsEnabled.TryGetValue(directive.OperatorChannelId, out var channelEnabled))
            faults.Add(new ExpectationConfigurationFault("channel_missing", directive.OperatorChannelId.ToString("D")));
        else if (!channelEnabled)
            faults.Add(new ExpectationConfigurationFault("channel_disabled", directive.OperatorChannelId.ToString("D")));

        foreach (var target in directive.Targets ?? [])
        {
            if (string.IsNullOrWhiteSpace(target.RunnerId))
                continue;
            var runner = target.RunnerId.Trim();
            if (!catalog.ConfiguredRunnerIds.Contains(runner))
                faults.Add(new ExpectationConfigurationFault("runner_not_configured", runner));
        }

        return faults;
    }
}
