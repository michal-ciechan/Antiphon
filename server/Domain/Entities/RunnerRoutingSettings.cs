using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0710. One fleet-wide runner-default row. Null <see cref="GlobalRunnerId"/> means the
/// built-in fallback. <c>desktop</c> is a real value here, unlike task storage.
/// </summary>
public class RunnerRoutingSettings
{
    public const string SingletonKey = "fleet";

    public string Id { get; set; } = SingletonKey;
    public long Revision { get; set; } = 1;
    public string? GlobalRunnerId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? LastReason { get; set; }
    public string? LastProvenance { get; set; }
    public Guid? LastCallerTaskId { get; set; }
    public ICollection<RunnerKindDefault> KindDefaults { get; set; } = new List<RunnerKindDefault>();
    public ICollection<RunnerRoutingRevision> Revisions { get; set; } = new List<RunnerRoutingRevision>();
}

public class RunnerKindDefault
{
    public Guid Id { get; set; }
    public string SettingsId { get; set; } = RunnerRoutingSettings.SingletonKey;
    public AgentKind AgentKind { get; set; }
    public string RunnerId { get; set; } = "";
    public RunnerRoutingSettings Settings { get; set; } = null!;
}

public class RunnerRoutingRevision
{
    public Guid Id { get; set; }
    public string SettingsId { get; set; } = RunnerRoutingSettings.SingletonKey;
    public long Revision { get; set; }
    public long? PreviousRevision { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public string Provenance { get; set; } = "Migration";
    public Guid? CallerTaskId { get; set; }
    public RunnerRoutingSettings Settings { get; set; } = null!;
}
