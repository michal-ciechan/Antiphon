namespace Antiphon.Agents.Pty;

/// <summary>
/// Durable host I/O at native launch boundaries. Each method must finish its durable write
/// before returning; an exception prevents the dependent native action. This is not exit proof.
/// </summary>
public interface IPtyCustodyJournal
{
    void RecordStartIntent();
    void RecordTracking(int processId);
    void RecordSeal();
}

/// <summary>An observation of the original job; the host must still persist its bound receipt.</summary>
public sealed record PtyCustodyObservation(uint ActiveProcesses, bool OutputDrained);
