namespace Antiphon.Agents.Pty;

/// <summary>
/// Durable host I/O at native launch boundaries. Each method must finish its durable write
/// before returning; an exception prevents the dependent native action. This is not exit proof.
/// </summary>
public interface IPtyCustodyJournal
{
    /// <summary>
    /// CARD-0604 D-17: the container the tracked tree lives in, chosen by the host before the
    /// child exists and carried unchanged onto the receipt. On Linux it is also the cgroup's
    /// directory name, so the placement shim and the receipt name the same thing.
    /// </summary>
    Guid ContainerId { get; }

    void RecordStartIntent();
    void RecordTracking(int processId);
    void RecordSeal();

    /// <summary>
    /// CARD-1931. The start time <see cref="RecordTracking"/> read for the tracked root, the
    /// generation a placement observation must match. Null when nothing is tracked; Windows and
    /// test journals keep the null default, so they never receive a placement.
    /// </summary>
    DateTime? TrackedStartTimeUtc => null;

    /// <summary>
    /// CARD-1614 / CARD-1931. The live tracked root was observed as the sole member of its Linux
    /// tree, with the observed start time. Windows and test journals keep the empty default; a
    /// missing observation is not placement.
    /// </summary>
    void RecordLinuxPlacement(PtyRootPlacement observed) { }
}

/// <summary>CARD-1931. One observation of the tracked root inside its own tree: pid and start time.</summary>
public readonly record struct PtyRootPlacement(int Pid, DateTime StartTimeUtc);

/// <summary>An observation of the original job; the host must still persist its bound receipt.</summary>
public sealed record PtyCustodyObservation(uint ActiveProcesses, bool OutputDrained);
