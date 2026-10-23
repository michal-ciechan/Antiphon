namespace Antiphon.TimeoutAudit;

public sealed record AuditOptions(string Repository, string[] ReceiptRoots, string? Exceptions = null);
public sealed record Declaration(string Id, string Path, int Line, string Kind, string Expression,
    double? Seconds, string? Test = null, string? Filter = null);
public sealed record Observation(string Subject, string Context, double Seconds, string Evidence,
    string SourceSha, string Outcome, double? SlotWaitSeconds = null);
public sealed record Budget(string DeclarationId, string Subject, string Context, int Samples,
    double? P50, double? P95, double? Max, double? RecommendedSeconds, string Status,
    string[] Evidence, string CollectionCard = "CARD-1563",
    string CollectionPlan = "Collect 20 ordinary successful comparable runs within 14 days; include shared-host load and slot waits; re-audit before tightening.");
public sealed record Finding(string DeclarationId, string Reason);
public sealed class AuditReport
{
    public string SchemaVersion { get; init; } = "antiphon-timeout-audit-v1";
    public List<Declaration> Inventory { get; init; } = [];
    public List<Observation> Observations { get; init; } = [];
    public List<Budget> Budgets { get; init; } = [];
    public List<Finding> Violations { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
public static class Audit
{
    public static AuditReport Run(AuditOptions options) => new();
}
