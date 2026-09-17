namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0544 D-7. Deployment readiness gate for Interim verification rounds. Disabled by default:
/// with <see cref="Enabled"/> false no Interim task can be admitted or launched, whatever a card's
/// policy or a caller's request says. It is an additional operator gate, never a global opt-in for
/// cards, and nothing in a task request can supply or override any of these values.
///
/// <para>Enabling it is CARD-0544 S6's job and requires accepted CARD-0545 qualification first.</para>
/// </summary>
public sealed class InterimVerificationSettings
{
    public const string SectionName = "InterimVerification";

    /// <summary>Master switch. False (the shipped value) denies every Interim readiness check.</summary>
    public bool Enabled { get; set; }

    /// <summary>The single qualified canonical repository. Other repositories stay FullOnly.</summary>
    public string? CanonicalRepositoryPath { get; set; }

    /// <summary>The qualified project. Compared exactly: null matches only a task with no project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Trusted nightly state root holding the qualification receipt and <c>last-monitor.json</c>.</summary>
    public string? StateRoot { get; set; }

    /// <summary>A saved monitor is fresh while its RecordedAt is no more than this old (future is invalid).</summary>
    public int MonitorFreshMinutes { get; set; } = 60;

    /// <summary>Bounded read: a larger readiness file is treated as unreadable.</summary>
    public int MaxFileBytes { get; set; } = 64 * 1024;
}
