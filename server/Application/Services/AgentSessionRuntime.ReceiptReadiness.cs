namespace Antiphon.Server.Application.Services;

public enum ReceiptReadinessStatus { Unknown, Behind, Ready }

public sealed record ReceiptReadinessObservation(
    ReceiptReadinessStatus Status, string Reason, long? BaselineSequence = null,
    string? BindingId = null, long? ParsedThrough = null, long? ObservedLength = null);

public sealed partial class AgentSessionRuntime
{
    public Task<ReceiptReadinessObservation> ObserveReceiptReadinessAsync(
        Guid sessionId, DateTime expectedGeneration, Guid standingAgentId, CancellationToken ct) =>
        Task.FromResult(new ReceiptReadinessObservation(ReceiptReadinessStatus.Unknown, "not_implemented"));
}
