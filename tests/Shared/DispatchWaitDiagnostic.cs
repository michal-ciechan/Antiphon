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

    private static string Flat(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var flat = value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= 180 ? flat : flat[..180];
    }
}
