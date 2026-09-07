using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Append-only ledger of output-distiller work (CARD-0330 D7). Table <c>OutputDistillations</c>.
/// Named away from the static <c>OutputDistillation</c> contract helpers in Application.Services
/// so the two can coexist; the table name is the public one.
/// </summary>
public class OutputDistillationRecord
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }

    /// <summary>The seat's Distill-role row, when one was created.</summary>
    public Guid? DistillTaskId { get; set; }

    public Guid? QueuedMessageId { get; set; }

    /// <summary>Catalog stamp at request time, e.g. <c>output-distiller v1a2b3c4d</c>.</summary>
    public string? BundleStamp { get; set; }

    public OutputDistillerMode Mode { get; set; }
    public int RawChars { get; set; }
    public int DistilledChars { get; set; }
    public int WaitMs { get; set; }
    public decimal CostUsd { get; set; }
    public DistillationOutcome Outcome { get; set; }

    /// <summary>JSON array of missing-anchor labels when <see cref="Outcome"/> is a rejection.</summary>
    public string? MissingAnchors { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? DeadlineAt { get; set; }
    public DateTime? DequeuedAt { get; set; }
    public DateTime? RunCreatedAt { get; set; }
    public DateTime? DecisionAt { get; set; }
    public int? QueueWaitMs { get; set; }
    public int? SpecialistWaitMs { get; set; }
    public int? CleanupMs { get; set; }
    public string? Reason { get; set; }
    public string? ExpiryPhase { get; set; }
    public string? AvailabilityAlias { get; set; }
    public AgentKind? AvailabilityKind { get; set; }
    public DateTime? AvailabilityObservedAt { get; set; }

    public DistillationFeedback Feedback { get; set; } = DistillationFeedback.None;
    public string? FeedbackNote { get; set; }
    public string? FeedbackBy { get; set; }
    public DateTime? FeedbackAt { get; set; }

    /// <summary>
    /// When the parent session polled the full report after an Applied note was sent. Null until
    /// then; a high rate is the cheapest "the summary was not enough" signal.
    /// </summary>
    public DateTime? FullReadAt { get; set; }
}
