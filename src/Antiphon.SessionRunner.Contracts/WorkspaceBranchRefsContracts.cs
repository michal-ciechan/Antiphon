namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-0824 S2. Outcomes and refusal codes of <see cref="PhoneHomeOperation.WorkspaceBranchRefsDelete"/>.</summary>
public static class WorkspaceBranchRefsCodes
{
    /// <summary>Every named ref is now absent (deleted here or already gone).</summary>
    public const string Deleted = "deleted";
    public const string Refused = "refused";

    public const string NotImplemented = "not_implemented";
    public const string ProtectedBranch = "protected_branch";
    public const string InvalidRequest = "invalid_request";
    public const string ExpectedObjectMissing = "expected_object_missing";
    public const string BranchCheckedOut = "branch_checked_out";
    public const string UnpublishedLocalRef = "unpublished_local_ref";
    public const string RefUnreadable = "ref_unreadable";
    public const string RefMoved = "ref_moved";
    public const string RepositoryUnavailable = "repository_unavailable";
}

/// <summary>
/// CARD-0824 S2. <paramref name="Branch"/> is the short delegate branch name; <paramref name="ExpectedSha"/> is the
/// source SHA the server will lease the origin delete on. A local ref at that SHA or behind it is deleted.
/// </summary>
public sealed record PhoneHomeWorkspaceBranchRefsDeleteRequest(string Branch, string ExpectedSha, int Version = 1);

public sealed record PhoneHomeWorkspaceBranchRefsDeleteResponse(
    string Outcome, string? Code, IReadOnlyList<string> Deleted, IReadOnlyList<string> Absent, string? Detail = null)
{
    public bool Succeeded => Outcome == WorkspaceBranchRefsCodes.Deleted;
}
