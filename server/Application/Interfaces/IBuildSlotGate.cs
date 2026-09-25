namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0589 S4: the server-side counterpart of <c>scripts/lib/build-slot.ps1</c>, with the same
/// wait, timeout and unreachable rules (D-6). Every <c>BUILD SLOT ...</c> line goes to
/// <paramref name="report"/> so the landing evidence shows a budgeted run from an unbudgeted one.
/// </summary>
public interface IBuildSlotGate
{
    Task<BuildSlotHold> AcquireAsync(string label, Action<string> report, CancellationToken ct);

    /// <summary>Releases a granted lease; a no-op for any other outcome. Never throws for a runner fault.</summary>
    Task ReleaseAsync(BuildSlotHold hold, Action<string> report, CancellationToken ct);
}

public enum BuildSlotHoldOutcome
{
    Granted,
    Unlimited,
    Unleased,
    Timeout,
}

/// <summary><see cref="MaxCpuCount"/> is the <c>-maxcpucount</c> to build with (0 on a timeout).</summary>
public sealed record BuildSlotHold(
    BuildSlotHoldOutcome Outcome,
    Guid? LeaseId,
    int MaxCpuCount,
    TimeSpan Waited,
    int QueuePosition = 0,
    long GrantedTimestamp = 0);
