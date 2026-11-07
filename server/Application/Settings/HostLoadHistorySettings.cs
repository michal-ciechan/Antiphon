namespace Antiphon.Server.Application.Settings;

public sealed class HostLoadHistorySettings
{
    public const string SectionName = "HostLoadHistory";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 5000;
    public int IngestQueueCapacityPerRunner { get; set; } = 2;
    public int RawRetentionDays { get; set; } = 7;
    public int AggregateRetentionDays { get; set; } = 90;
    public int TaskRetentionDays { get; set; } = 90;
    public List<HostLoadBinding> Bindings { get; set; } = [];
    public void Validate()
    {
        if (PollIntervalMs != 5000 || IngestQueueCapacityPerRunner is < 1 or > 2 || RawRetentionDays is < 1 or > 7
            || AggregateRetentionDays is < 1 or > 90 || TaskRetentionDays is < 1 or > 90)
            throw new InvalidOperationException("HostLoadHistory bounds are invalid.");
        if (Bindings.Any(b => string.IsNullOrWhiteSpace(b.RunnerId) || string.IsNullOrWhiteSpace(b.PhysicalHostKey)
            || b.RunnerId.Length > 64 || b.PhysicalHostKey.Length > 128 || b.SourceEpoch == Guid.Empty || b.Revision < 1
            || (b.To is not null && b.To <= b.From)))
            throw new InvalidOperationException("HostLoadHistory binding is invalid.");
    }
}
public sealed class HostLoadBinding
{
    public string RunnerId { get; set; } = "";
    public string PhysicalHostKey { get; set; } = "";
    public Guid SourceEpoch { get; set; }
    public long Revision { get; set; } = 1;
    public bool Primary { get; set; } = true;
    public DateTimeOffset From { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset? To { get; set; }
}
