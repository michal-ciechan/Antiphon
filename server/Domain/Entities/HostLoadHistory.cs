namespace Antiphon.Server.Domain.Entities;

public sealed class HostLoadSampleRecord
{
    public long Id { get; set; }
    public long CreatedTransactionId { get; set; }
    public Guid RunnerStoreId { get; set; }
    public Guid BootId { get; set; }
    public long Sequence { get; set; }
    public string RunnerId { get; set; } = "";
    public string? PhysicalHostKey { get; set; }
    public Guid? SourceEpoch { get; set; }
    public DateTimeOffset SampledAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public DateTimeOffset? DerivedAt { get; set; }
    public bool IsPrimary { get; set; }
    public string PayloadJson { get; set; } = "";
    public string? CountersJson { get; set; }
}

/// <summary>Payload fingerprints outlive raw retention, so replay cannot count an expired K twice.</summary>
public sealed class HostLoadSampleIdentity
{
    public Guid RunnerStoreId { get; set; }
    public Guid BootId { get; set; }
    public long Sequence { get; set; }
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset IngestedAt { get; set; }
}
public sealed class HostLoadHourlyRollup
{
    public long Id { get; set; }
    public long CreatedTransactionId { get; set; }
    public string PhysicalHostKey { get; set; } = "";
    public Guid SourceEpoch { get; set; }
    public DateTimeOffset HourUtc { get; set; }
    public string Metric { get; set; } = "";
    public string HistogramJson { get; set; } = "";
    public string StatisticsJson { get; set; } = "";
}
public sealed class HostLoadIngestCursor
{
    public string RunnerId { get; set; } = "";
    public Guid RunnerStoreId { get; set; }
    public string? Cursor { get; set; }
    public string GapsJson { get; set; } = "[]";
    public bool Complete { get; set; }
    public DateTimeOffset CommittedAt { get; set; }
}
public sealed class AgentTaskLoadWindow
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public string PhysicalHostKey { get; set; } = "";
    public Guid RunnerBootId { get; set; }
    public int WindowOrdinal { get; set; }
    public Guid StartEventId { get; set; }
    public Guid? EndEventId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string DetailsJson { get; set; } = "";
    public bool Complete { get; set; }
}
public sealed class HostLoadTaskMetricBucket
{
    public Guid WindowId { get; set; }
    public DateTimeOffset HourUtc { get; set; }
    public string Metric { get; set; } = "";
    public string HistogramJson { get; set; } = "";
}
public sealed class HostLoadObservedEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}

/// <summary>One row. A restart continues a backlog instead of waiting out an in-memory hour.</summary>
public sealed class HostLoadRetentionState
{
    public int Id { get; set; }
    public bool Backlog { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
