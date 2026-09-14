using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0514 D-6: one open episode per <c>(SessionId, AcceptedStartedAt)</c>.
/// First observation time is immutable and is never a fabricated paint timestamp.
/// Screens, prompt bodies, URLs, state-file contents and credentials must not be stored here.
/// </summary>
public class RemoteControlModalEpisode
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public DateTime AcceptedStartedAt { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
    public int? ChildPid { get; set; }
    public long? BeforeOutputSequence { get; set; }
    public long? AfterOutputSequence { get; set; }
    public long? TranscriptCatchUpWatermark { get; set; }
    public bool? TranscriptWorking { get; set; }
    public Guid? RelatedMaintenanceQueueId { get; set; }
    public DateTime? DismissalIntentAt { get; set; }
    public DateTime? DismissalSentAt { get; set; }
    public DateTime? DismissalVerifiedAt { get; set; }
    public RemoteControlDismissalResult? DismissalResult { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public RemoteControlEpisodeResolution? Resolution { get; set; }
    public string? LastTransition { get; set; }
    public DateTime? LastIncidentAt { get; set; }
    public bool ChannelBound { get; set; }
    public long? ObservedEnqueueSequence { get; set; }
    public long? ObservedDrainSequence { get; set; }
    public long? ObservedPromptSequence { get; set; }
    public AgentSession? AgentSession { get; set; }
}
