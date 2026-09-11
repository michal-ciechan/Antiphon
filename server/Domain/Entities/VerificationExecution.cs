namespace Antiphon.Server.Domain.Entities;

/// <summary>Append-only accepted generation. Missing runner evidence never implies no start.</summary>
public sealed class VerificationExecution
{
    public Guid Id { get; init; }
    public Guid TaskId { get; init; }
    public Guid SourceLandingOperationId { get; init; }
    public Guid SessionId { get; init; }
    public DateTime AcceptedStartedAt { get; init; }
    public string BindingJson { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? RunnerCallIntentAt { get; set; }
    public string? HostIdentityJson { get; set; }
    public byte[]? ReceiptBytes { get; set; }
    public string? ReceiptDigest { get; set; }
    public DateTime? ReceiptImportedAt { get; set; }
    public string? CustodyReason { get; set; }
}
