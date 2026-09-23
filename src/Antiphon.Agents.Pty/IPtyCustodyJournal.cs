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
}

/// <summary>An observation of the original job; the host must still persist its bound receipt.</summary>
public sealed record PtyCustodyObservation(uint ActiveProcesses, bool OutputDrained);
