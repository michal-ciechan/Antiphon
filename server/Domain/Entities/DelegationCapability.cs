namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A named hashed-at-rest principal on <c>X-Antiphon-Task-Token</c> (CARD-0398). Roots are this
/// principal's own list — independent of <c>Delegation:AllowedRoots</c>. The raw token is stored
/// only as SHA-256; the CLI holds the DPAPI blob.
/// </summary>
public class DelegationCapability
{
    public const int NameMaxLength = 64;
    public const int TokenHashLength = 64;
    public const int RootsJsonMaxLength = 8000;
    public const int MinRoots = 1;
    public const int MaxRoots = 8;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the raw bearer. Never log this value.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>JSON array of absolute directory roots this principal may create into.</summary>
    public string RootsJson { get; set; } = "[]";

    public Guid? BoardId { get; set; }
    public Guid? ProjectId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RotatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Board? Board { get; set; }
    public Project? Project { get; set; }
    public ICollection<DelegationCapabilityEvent> Events { get; set; } = new List<DelegationCapabilityEvent>();
}
