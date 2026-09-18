namespace Antiphon.Server.Application.Exceptions;

/// <summary>The Git mutation succeeded, but its receipt is not yet available.</summary>
public sealed class CommitInspectionPendingException : HttpException
{
    /// <summary>The gated operation whose receipt is pending; the caller recovers it by this id.</summary>
    public Guid OperationId { get; }

    public CommitInspectionPendingException(Guid operationId, string? sha = null)
        : base(503, "The commit succeeded; metadata inspection is pending. Recover this operation before committing again.",
            "commit_inspection_pending", new Dictionary<string, object?>
            {
                ["committed"] = true,
                ["operationId"] = operationId,
                ["sha"] = sha,
            })
    {
        OperationId = operationId;
    }
}
