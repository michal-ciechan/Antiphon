using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0147 S3: one uncleared (or recently cleared) mismatch between git's
/// <c>feat/card-task-*</c> registrations and <see cref="AgentTask"/> rows.
/// Upserted by the health sweep; create-time concurrency 409 reads uncleared rows
/// for occupant <c>stuck:</c> labels. Never auto-healed.
/// </summary>
public class WorktreeHealthFinding
{
    public const int RepoPathMaxLength = 1000;
    public const int BranchMaxLength = 300;
    public const int PathMaxLength = 1000;
    public const int DetailMaxLength = 1000;

    public Guid Id { get; set; }
    public string RepoPath { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public WorktreeHealthShape Shape { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? ClearedAt { get; set; }
}
