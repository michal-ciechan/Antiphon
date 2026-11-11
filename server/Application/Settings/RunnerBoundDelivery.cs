using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1224. Ceilings for a runner-bound session from its cached pty-backend report.
/// </summary>
public static class RunnerBoundDelivery
{
    /// <summary>
    /// <c>UnixPty</c> selects that arm. Null, empty, <c>InboxConhost</c>, <c>ModernConPty</c>,
    /// and any other report stay on the inbox arm.
    /// </summary>
    public static PtyDeliveryCeilings ForRunner(DelegationSettings settings, string? reportedPtyBackend)
    {
        var backend = string.Equals(reportedPtyBackend, nameof(PtyBackend.UnixPty), StringComparison.Ordinal)
            ? PtyBackend.UnixPty
            : PtyBackend.InboxConhost;
        return settings.CeilingsFor(backend, "runner-bound input");
    }

    /// <summary>
    /// Cached phone-home report for <paramref name="runnerId"/>. No id and no directory stay null.
    /// <see cref="ISessionRunnerDirectory.DescribeAsync"/> reads the slot cache and does not query
    /// the database.
    /// </summary>
    public static async Task<string?> ReportedBackendAsync(
        ISessionRunnerDirectory? directory, string? runnerId, CancellationToken ct)
    {
        if (directory is null || string.IsNullOrWhiteSpace(runnerId))
            return null;
        var described = await directory.DescribeAsync(runnerId, ct);
        return described?.Capabilities?.PtyBackend;
    }
}
