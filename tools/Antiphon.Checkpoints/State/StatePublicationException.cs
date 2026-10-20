namespace Antiphon.Checkpoints;

public sealed class StatePublicationException(Exception inner)
    : IOException(ReceiptFailureReason, inner)
{
    public const string ReceiptFailureReason = "required state.json receipt-write-failed";
    public SchedulerResult? PartialResult { get; internal set; }
}
