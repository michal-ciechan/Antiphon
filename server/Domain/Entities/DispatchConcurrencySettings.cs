namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0505. One row per scope: <see cref="GlobalScopeKey"/> or a project id in "D" form.
/// Clearing overrides keeps the row so revision cannot return to zero.
/// </summary>
public class DispatchConcurrencySettings
{
    public const string GlobalScopeKey = "global";
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = GlobalScopeKey;
    public Guid? ProjectId { get; set; }
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string OverridesJson { get; set; } = "{}";
    /// <summary>Immutable imported configuration. Only the global row stores it.</summary>
    public string? SeedJson { get; set; }
    public long Revision { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string LastReason { get; set; } = "";
    public string LastProvenance { get; set; } = "Migration";
    public Guid? LastCallerTaskId { get; set; }
    public ICollection<DispatchConcurrencyRevision> Revisions { get; set; } = new List<DispatchConcurrencyRevision>();
}

/// <summary>Complete scope snapshot at one revision. History survives a clear.</summary>
public class DispatchConcurrencyRevision
{
    public Guid Id { get; set; }
    public Guid SettingsId { get; set; }
    public long Revision { get; set; }
    public long? PreviousRevision { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public string Provenance { get; set; } = "Migration";
    public Guid? CallerTaskId { get; set; }
    public DispatchConcurrencySettings Settings { get; set; } = null!;
}
