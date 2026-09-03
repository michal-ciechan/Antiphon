using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Card
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public Guid BoardColumnId { get; set; }
    public Guid? OwnerSessionId { get; set; }
    public Guid? CurrentWorktreeId { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public int? AgentQueuePosition { get; set; }
    public Guid? ActiveWorkflowRunId { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CardImportance Importance { get; set; } = CardImportance.Normal;

    /// <summary>
    /// Whether <see cref="Importance"/> was produced by an automatic writer or an explicit
    /// create/edit (CARD-0327). Auto is the default; the tracker sync writes importance only
    /// while this is Auto.
    /// </summary>
    public CardImportanceProvenance ImportanceProvenance { get; set; } = CardImportanceProvenance.Auto;

    public CardUrgency Urgency { get; set; } = CardUrgency.Normal;
    public DateTime? DueAt { get; set; }

    /// <summary>
    /// Set when <see cref="Urgency"/> rises above <see cref="CardUrgency.Normal"/>; cleared when
    /// it returns. Surfaced as staleness ("rated Now 12d ago"), never used to decay the rating.
    /// </summary>
    public DateTime? UrgentSince { get; set; }

    /// <summary>
    /// Dense 1..n order inside the card's (column, rank cell). Null means never placed and sorts
    /// after every placed card in the cell, then dueAt, then <see cref="CreatedAt"/> (CARD-0098).
    /// Cleared by a column or axis change; a dueAt change does not clear it.
    /// </summary>
    public int? Position { get; set; }
    public string LabelsJson { get; set; } = "[]";
    public CardStatus Status { get; set; } = CardStatus.Backlog;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TerminalReason { get; set; }

    /// <summary>
    /// The last decision question that was sent to digest channels. It is deliberately separate
    /// from card history: notification delivery is operational state, not a user-facing edit.
    /// A later parking has a newer attention <c>SinceUtc</c> and therefore wakes the human again.
    /// </summary>
    public DateTime? DecisionNotifiedAt { get; set; }

    /// <summary>
    /// Set when the card is archived. Archive is what "delete" means here — the row stays, so a
    /// card that is cited in commit messages, docs and other cards' terminal reasons never turns
    /// into a dangling reference, and <c>NextIdentifierAsync</c> keeps seeing its identifier
    /// (a hard delete of the highest card would hand its number out again — CARD-0005;
    /// <c>CardIdentifierAllocator</c> counts archived rows).
    /// </summary>
    public DateTime? ArchivedAt { get; set; }
    public string? ArchivedReason { get; set; }

    /// <summary>Self-reported archiver. Free text; the server has no principals.</summary>
    public string? ArchivedBy { get; set; }

    /// <summary>
    /// Set when a card is placed in an active column and work was declined. Auto-dispatch skips
    /// any card where this is set. Cleared by an explicit spawn and by a move off an active
    /// column. Null is the fail-closed default: seeded and tracker-synced cards stay in the tick
    /// queue.
    /// </summary>
    public DateTime? AutoDispatchHeldAt { get; set; }

    /// <summary>
    /// How many revisions this card has, and the allocator for the next
    /// <see cref="CardRevision.RevisionNumber"/>. Stored on the card rather than counted so that
    /// (a) the board GET can surface an "edited" affordance without a second query or a windowed
    /// subquery, and (b) any code path holding a tracked card can append a revision without a
    /// database round-trip. The card's concurrency token guards the allocation.
    /// </summary>
    public int RevisionCount { get; set; }

    public Board Board { get; set; } = null!;
    public BoardColumn BoardColumn { get; set; } = null!;
    public AgentSession? OwnerSession { get; set; }
    public Worktree? CurrentWorktree { get; set; }
    public Agent? AssignedAgent { get; set; }
    public CardWorkflowRun? ActiveWorkflowRun { get; set; }
    public ExternalIssueRef? ExternalIssueRef { get; set; }
    public RetrySchedule? RetrySchedule { get; set; }
    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
    public ICollection<Worktree> Worktrees { get; set; } = new List<Worktree>();
    public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();
    public ICollection<CardRevision> Revisions { get; set; } = new List<CardRevision>();
    public ICollection<CardComment> Comments { get; set; } = new List<CardComment>();
}
