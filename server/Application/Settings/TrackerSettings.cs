namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0347: per-card tracker write settings. Bound from the <c>Tracker</c> section.</summary>
public sealed class TrackerSettings
{
    public const string SectionName = "Tracker";

    /// <summary>
    /// When true, a card close or reopen pushes that one issue's state synchronously after the
    /// local write commits. The scheduled bidirectional run remains the retry.
    /// </summary>
    public bool PushStateOnCardTransition { get; set; } = true;

    /// <summary>
    /// Bound on the per-card GitHub push, in seconds. A GitHub outage delays a close by at most
    /// this rather than HttpClient's 100 s default.
    /// </summary>
    public int CardStatePushTimeoutSeconds { get; set; } = 15;
}
