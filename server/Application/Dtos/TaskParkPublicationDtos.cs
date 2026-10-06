using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

public enum TaskParkPublicationOutcome { Published, NoSourceChanges, Held, Unknown }

public enum TaskParkSourceIdentityOutcome { Captured, Held, Unknown }

public sealed record TaskParkSourceIdentityResult(TaskParkSourceIdentityOutcome Outcome, string Reason)
{
    public bool Captured => Outcome == TaskParkSourceIdentityOutcome.Captured;
}

/// <summary>Source proof only. Never authority to stop a session. NoSourceChanges has no remote receipt.</summary>
public sealed record TaskParkPublicationEvidence(
    Guid ReceiptId, WorkspaceParkRequest Request, string SourceSha, string? RemoteSha,
    bool Clean, bool DescendsFromBaseline, TaskParkPublicationOutcome Outcome)
{
    public static TaskParkPublicationEvidence From(WorkspaceParkReceipt receipt) => receipt.HasConsistentSourceMode
        ? new(receipt.ReceiptId, receipt.Request, receipt.SourceSha, receipt.RemoteSha,
            receipt.Clean, receipt.DescendsFromBaseline, receipt.SourceMode == WorkspaceParkSourceMode.NoSourceChanges
                ? TaskParkPublicationOutcome.NoSourceChanges : TaskParkPublicationOutcome.Published)
        : throw new InvalidOperationException("Inconsistent park source mode.");

    public WorkspaceParkReceipt ToRunnerReceipt() => new(ReceiptId, Request, SourceSha,
        RemoteSha, null, Clean, DescendsFromBaseline, DateTimeOffset.UnixEpoch, Outcome switch
        {
            TaskParkPublicationOutcome.Published when RemoteSha is not null => WorkspaceParkSourceMode.Published,
            TaskParkPublicationOutcome.NoSourceChanges when RemoteSha is null => WorkspaceParkSourceMode.NoSourceChanges,
            _ => throw new InvalidOperationException("Inconsistent park source evidence.")
        });

    public TerminalSeatReleaseRequest ToRunnerReleaseRequest(TerminalSeatObservationRequest observation, string token) =>
        new(Request.Binding.ActionId, observation, token, ToRunnerReceipt(), ParkVersion: 2);
}

public sealed record TaskParkPublicationResult(TaskParkPublicationOutcome Outcome, string Reason,
    TaskParkPublicationEvidence? Evidence = null);
