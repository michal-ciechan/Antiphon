using System.Globalization;

namespace Antiphon.TestSupport;

/// <summary>
/// The last owned-dispatch observation appended to a delivery-wait <see cref="TimeoutException"/>.
/// Rows, status and problem text travel with the message so a native timeout names its cause.
/// </summary>
public static class DispatchWaitDiagnostic
{
    public sealed record Snapshot(
        string Status,
        string? FailureReason,
        string? BaseRef,
        string? BaseSource,
        int IntentCount,
        IReadOnlyList<string> WarningKeys,
        string? IntentDetails,
        bool HookFired,
        string? Events);

    public readonly record struct WarningRow(Guid Id, string? Detail);

    public static string Append(string evidence, Snapshot snapshot) =>
        evidence
        + " | status=" + Flat(snapshot.Status)
        + " failure=" + Flat(snapshot.FailureReason)
        + " baseRef=" + Flat(snapshot.BaseRef)
        + " baseSource=" + Flat(snapshot.BaseSource)
        + " intents=" + snapshot.IntentCount.ToString(CultureInfo.InvariantCulture)
        + " keys=" + string.Join(",", snapshot.WarningKeys.Select(Flat))
        + " details=" + Flat(snapshot.IntentDetails)
        + " hookFired=" + (snapshot.HookFired ? "true" : "false")
        + " events=" + Flat(snapshot.Events);

    /// <summary>
    /// A committed dispatch-base projection: the event id is an unmaterialized intent id,
    /// or the detail is a sibling, stale-base, unresolved-default, or preview warning.
    /// Quota and capacity-consume warnings committed with the claim are not projections.
    /// </summary>
    public static bool IsUnmaterializedProjection(Guid eventId, string? detail, IReadOnlyCollection<Guid> intentIds) =>
        intentIds.Contains(eventId) || IsDispatchBaseDetail(detail);

    public static bool IsDispatchBaseDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
            return false;
        return detail.Contains("[dispatch-base ", StringComparison.Ordinal)
            || detail.Contains("kept branch", StringComparison.Ordinal)
            || detail.Contains("sibling containment was evaluated against", StringComparison.Ordinal)
            || detail.Contains("configured default branch '", StringComparison.Ordinal)
            || detail.Contains("Same-card warning inspection incomplete", StringComparison.Ordinal)
            || detail.Contains("Worktree base changed after create preview", StringComparison.Ordinal)
            || detail.Contains("Worktree base preview could not be compared", StringComparison.Ordinal);
    }

    public static string FormatWarnings(IEnumerable<WarningRow> warnings)
    {
        var text = string.Join("; ", warnings.Select(w => w.Id.ToString("N") + ":" + Flat(w.Detail)));
        return text.Length <= 700 ? text : text[..700];
    }

    private static string Flat(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var flat = value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= 180 ? flat : flat[..180];
    }
}
