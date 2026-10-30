namespace Antiphon.SessionRunner.Contracts;

/// <summary>Bounded diagnostic shape. A serialized claim alone never establishes a signal issuer.</summary>
public record ProcessStopIntentDto
{
    public Guid RequestId { get; init; }
    public Guid SessionId { get; init; }
    public DateTime AcceptedStartedAt { get; init; }
    public Guid IssuerBootId { get; init; }
    public Guid HostInstanceId { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public int? TargetPid { get; init; }
    public DateTime? TargetStartedAtUtc { get; init; }
    public string? CustodyNamespace { get; init; }
    public string? CustodyContainer { get; init; }
    public ProcessStopTarget Target { get; init; }
    public int? AttemptedSignal { get; init; }
    public ProcessStopPath Path { get; init; }
    public ProcessStopOutcome Outcome { get; init; }
    public ProcessStopProvenance Provenance { get; init; }
    public bool Durable { get; init; }
}

public enum ProcessStopTarget { Unknown, Child, Host, Custody, Helper }
public enum ProcessStopPath { Unknown, ProcessTree, PortaFallback, Disposal, OwnerShutdown, CustodyRelease, Recovery, LaunchCleanup }
public enum ProcessStopOutcome { Requested, Issued, Failed }
public enum ProcessStopProvenance { RecoveredUnverified, LiveIssuer }
