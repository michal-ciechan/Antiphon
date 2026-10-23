using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable identity of the housekeeping command before an unrelated warm dispatch.
/// This excludes a positively identified non-brief; it never proves brief delivery.
/// Untagged legacy rows and inconsistent purpose/body/ownership remain uncertain.
/// </summary>
internal static class DispatchRefocusIdentity
{
    public static string Key(DispatchBriefEnsureRequest request, AgentKind kind) =>
        FormattableString.Invariant($"refocus-compact:v1:{request.TaskId:D}:{request.Attempt}:{request.SessionId:D}:{SessionGeneration.Normalize(request.DispatchedAt).Ticks}:{SessionGeneration.Normalize(request.SessionStartedAt).Ticks}:{kind}");

    public static string Body(string command, string goal)
    {
        var focus = goal.ReplaceLineEndings(" ").Trim();
        if (focus.Length > 300) focus = focus[..300];
        return $"{command} This session is being handed NEW, unrelated work. Keep only context useful for: {focus}".TrimEnd();
    }

    public static bool Matches(DispatchBriefRowEvidence row, DispatchBriefEnsureRequest request,
        AgentKind? sessionKind, string? goal)
    {
        if (sessionKind is not AgentKind kind || !Enum.IsDefined(kind) || goal is null
            || row.AgentSessionId != request.SessionId || row.ExecutionTaskId != request.TaskId
            || row.Origin != QueuedMessageOrigin.Delegation || row.SourceTaskId is not null
            || row.SourceLandNotificationId is not null || row.RulesCoveredByMessageId is not null
            || !string.IsNullOrEmpty(row.RemoteSpillBody) || !string.IsNullOrEmpty(row.RemoteSpillRelativePath)
            || string.IsNullOrEmpty(row.Body) || row.Body.Contains("[antiphon-task:", StringComparison.Ordinal)
            || SessionGeneration.Normalize(row.CreatedAt) < SessionGeneration.Normalize(request.DispatchedAt)
            || !string.Equals(row.ConversationKey, Key(request, kind), StringComparison.Ordinal))
            return false;

        var compact = ProviderContractCatalog.For(kind).RefocusCompact;
        return compact is { State: AgentTuiCapabilityState.Supported, Command: not null }
            && string.Equals(row.Body, Body(compact.Command, goal), StringComparison.Ordinal);
    }
}
