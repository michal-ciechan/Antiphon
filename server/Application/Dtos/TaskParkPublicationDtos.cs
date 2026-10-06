using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

public enum TaskParkPublicationOutcome { Published, NoSourceChanges, Held, Unknown }

/// <summary>Source proof only. Never authority to stop a session. NoSourceChanges has no remote receipt.</summary>
public sealed record TaskParkPublicationEvidence(
    Guid ReceiptId, WorkspaceParkRequest Request, string SourceSha, string? RemoteSha,
    bool Clean, bool DescendsFromBaseline, TaskParkPublicationOutcome Outcome)
{
    public static TaskParkPublicationEvidence From(WorkspaceParkReceipt receipt) => new(
        receipt.ReceiptId, receipt.Request, receipt.SourceSha, receipt.RemoteSha,
        receipt.Clean, receipt.DescendsFromBaseline, TaskParkPublicationOutcome.Published);

    public WorkspaceParkReceipt ToRunnerReceipt() => new(ReceiptId, Request, SourceSha,
        RemoteSha!, null, Clean, DescendsFromBaseline, DateTimeOffset.UnixEpoch);
}

public sealed record TaskParkPublicationResult(TaskParkPublicationOutcome Outcome, string Reason,
    TaskParkPublicationEvidence? Evidence = null);
