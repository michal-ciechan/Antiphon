using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Checkpoints;

public sealed class TimingEvent
{
    public string SchemaVersion { get; set; } = TimingJournal.SchemaVersion;
    public string EventId { get; set; } = "";
    public string EventKind { get; set; } = "";
    public string RunId { get; set; } = "";
    public int Attempt { get; set; } = 1;
    public string? ParentRunId { get; set; }
    public string Producer { get; set; } = "";
    public string ProducerVersion { get; set; } = "1";
    public string? RepositoryIdentity { get; set; }
    public string? CandidateSha { get; set; }
    public string? BaselineSha { get; set; }
    public string? PolicyHash { get; set; }
    public string? CatalogHash { get; set; }
    public string? SelectionHash { get; set; }
    public string? Scope { get; set; }
    public string? Profile { get; set; }
    public string SetKind { get; set; } = "";
    public string SetId { get; set; } = "";
    public string? Suite { get; set; }
    public string? Project { get; set; }
    public string? ClassName { get; set; }
    public string? Category { get; set; }
    public string? MembershipDigest { get; set; }
    public string? ProducingBuildId { get; set; }
    public string? FilterDigest { get; set; }
    public string Purpose { get; set; } = "ordinary";
    public string? HostId { get; set; }
    public string? Os { get; set; }
    public string? Architecture { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? TunitVersion { get; set; }
    public int? Concurrency { get; set; }
    public int? SlotLimit { get; set; }
    public double? CpuObserved { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double? WallSeconds { get; set; }
    public string? MeasurementKind { get; set; }
    public double? BuildSeconds { get; set; }
    public double? SlotWaitSeconds { get; set; }
    public double? StartupSeconds { get; set; }
    public double? TeardownSeconds { get; set; }
    public double? CaseWindowSeconds { get; set; }
    public double? CaseDurationSumSeconds { get; set; }
    public string? UnavailableReason { get; set; }
    public int? SelectedCount { get; set; }
    public int? ObservedCount { get; set; }
    public int? PassedCount { get; set; }
    public int? FailedCount { get; set; }
    public int? SkippedCount { get; set; }
    public int? UnknownCount { get; set; }
    public string? Outcome { get; set; }
    public int? ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool Canceled { get; set; }
    public string? CoverageStatus { get; set; }
    public string TimingPersistence { get; set; } = "complete";
    public string? FailurePhase { get; set; }
    public string? SourceClean { get; set; }
    public string? BuildProvenance { get; set; }
    public string? DiscoveryHash { get; set; }
    public string? TrxHash { get; set; }
    public string? ReceiptDigest { get; set; }
    public int Sequence { get; set; }
    public string DurabilityClass { get; set; } = "local";

    [JsonIgnore]
    public string PayloadIdentity => TimingJournal.PayloadIdentity(this);
}

public enum TimingFault
{
    None,
    BeforeSpool,
    AfterSpoolBeforeAppend,
    AfterFlushBeforeAck,
}

public sealed class TimingAppendResult
{
    public bool Acknowledged { get; init; }
    public bool Idempotent { get; init; }
    public bool SuppressedProducer { get; init; }
    public int Sequence { get; init; }
    public string TimingPersistence { get; init; } = "complete";
}

public sealed class TimingReadResult
{
    public List<TimingEvent> Events { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> CorruptLines { get; init; } = [];
    public int TornTails { get; init; }
}

public sealed class TimingRootException : InvalidOperationException
{
    public TimingRootException(string message) : base(message) { }
}

public sealed class TimingIntegrityException : InvalidOperationException
{
    public TimingIntegrityException(string message) : base(message) { }
}

public sealed class TimingPersistenceException : IOException
{
    public TimingPersistenceException(string message) : base(message) { }
}
