namespace Antiphon.Server.Domain.Entities;

public enum RunnerSeatReleaseState { Observing, Reserved, Confirmed, Unresolved }

/// <summary>
/// Durable debt for one accepted runner generation. Historical identities deliberately have no
/// foreign keys: deleting a task, agent or server session must not erase runner custody.
/// Contains only opaque evidence and bounded codes, never transcript or launch payloads.
/// </summary>
public sealed class RunnerSeatRelease
{
    public Guid Id { get; set; }
    public string RunnerId { get; set; } = "";
    public Guid RunnerStoreId { get; set; }
    public Guid SessionId { get; set; }
    public DateTime AcceptedStartedAt { get; set; }
    public Guid? TaskId { get; set; }
    public int? Attempt { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? SettlementEventId { get; set; }
    public Guid? SettlementRevision { get; set; }
    public DateTime? SettledAt { get; set; }
    public RunnerSeatReleaseState State { get; set; }
    public long Revision { get; set; }
    public Guid? ActionId { get; set; }
    public string ReasonCode { get; set; } = "Observing";
    public string? OutcomeCode { get; set; }
    public string? ObservationToken { get; set; }
    public string? BindingIdentity { get; set; }
    public string? FileRevision { get; set; }
    public long? TranscriptRevision { get; set; }
    public DateTime? FirstStableObservedAt { get; set; }
    public DateTime? LastObservedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
