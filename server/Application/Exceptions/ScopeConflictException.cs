namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0515. <c>boardId</c> names a known board whose project is not <c>projectId</c>.
/// Maps to HTTP 422 <c>scope_conflict</c> rather than a silent intersection.
/// </summary>
public sealed class ScopeConflictException : HttpException
{
    public ScopeConflictException()
        : base(422, "boardId and projectId refer to different projects.", "scope_conflict")
    {
    }
}
