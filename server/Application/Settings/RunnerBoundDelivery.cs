using Antiphon.Agents.Pty;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1224. Ceilings for a runner-bound session from its cached pty-backend report.
/// </summary>
public static class RunnerBoundDelivery
{
    /// <summary>
    /// Resolves ceilings for <paramref name="reportedPtyBackend"/>. This form ignores the
    /// report and returns the inbox arm for every value, including <c>UnixPty</c>.
    /// </summary>
    public static PtyDeliveryCeilings ForRunner(DelegationSettings settings, string? reportedPtyBackend)
    {
        _ = reportedPtyBackend;
        return settings.CeilingsFor(PtyBackend.InboxConhost, "runner-bound input");
    }
}
