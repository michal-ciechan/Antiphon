using System.Text.Json;

namespace Antiphon.E2E.Fixtures;

/// <summary>Operator-authored decision record. Opt-in flags never manufacture approval.</summary>
internal sealed record DistillerCanaryApproval(
    string ApprovalReference, DateTimeOffset ApprovedUtc, DateTimeOffset ExpiresUtc,
    string ExpectedSourceRole, string ExpectedDistillerKind, string ExpectedDistillerModelAlias,
    DateTimeOffset AvailabilityCheckedUtc, string AvailabilityVerdict)
{
    public static async Task<DistillerCanaryApproval> ReadAsync(string? path, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Operator approval file must be an absolute path.");
        var record = JsonSerializer.Deserialize<DistillerCanaryApproval>(await File.ReadAllTextAsync(path, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Missing operator approval.");
        record.Validate(now);
        return record;
    }
    public void Validate(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ApprovalReference) || ApprovalReference.Contains("EXAMPLE", StringComparison.OrdinalIgnoreCase)
            || ApprovedUtc > now || now >= ExpiresUtc || ApprovedUtc >= ExpiresUtc)
            throw new InvalidOperationException("No current scoped operator approval.");
        if (ExpectedSourceRole != "Review" || ExpectedDistillerKind != "ClaudeCode"
            || string.IsNullOrWhiteSpace(ExpectedDistillerModelAlias))
            throw new InvalidOperationException("Approval must name Review, ClaudeCode and the approved Low model alias.");
        if (AvailabilityVerdict != "allowed" || AvailabilityCheckedUtc > now
            || now - AvailabilityCheckedUtc > TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("Operator availability verdict is refused or stale.");
    }
    public void ValidateModel(string kind, string alias)
    {
        if (kind != ExpectedDistillerKind || alias != ExpectedDistillerModelAlias)
            throw new InvalidOperationException("Resolved specialist differs from operator approval.");
    }
}

internal static class DistillerCanaryGuard
{
    public static void ValidateOptIns(string? headed, string? canary)
    {
        if (headed != "1" || canary != "1") throw new InvalidOperationException("Both dedicated canary opt-ins are required.");
    }
    public static void ValidateResources(string actualDb, string ownedDb, string runner, string ownedRunner,
        string manifests, string ownedManifests, string broker)
    {
        if (actualDb != ownedDb || string.IsNullOrWhiteSpace(ownedDb)
            || runner != ownedRunner || !Uri.TryCreate(runner, UriKind.Absolute, out var uri)
            || !uri.IsLoopback || uri.Port is 17204 or 17202 or 17203 or 17205 or <= 1
            || !Path.IsPathFullyQualified(manifests) || manifests != ownedManifests
            || broker != "127.0.0.1:1") throw new InvalidOperationException("Canary resources are not isolated and owned.");
    }
}

internal sealed record DistillerCanaryEvidence(
    Guid SourceId, Guid ParentId, Guid NoteId, Guid LedgerSourceId, Guid LedgerNoteId, Guid TranscriptParentId,
    string Raw, string RawDigest, string FilePath, string FileRaw, string Summary, string Header,
    string Mode, string Outcome, DateTimeOffset Deadline, DateTimeOffset Decision,
    string TranscriptKind, long Sequence, DateTimeOffset PromptAt, string Prompt, string DeliveryVerdict,
    string Kind, string ModelAlias, string ExpectedModelAlias, string ParentReadHash, int ParentReadLength,
    bool ParentToolEvidence, bool FileSurvived, int CompletionsBefore, int CompletionsAfter, bool Teardown)
{
    public void Validate(string distinctiveMiddle, IReadOnlyList<string> requiredMarkers)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Raw)));
        if (SourceId == Guid.Empty || ParentId == Guid.Empty || NoteId == Guid.Empty
            || LedgerSourceId != SourceId || LedgerNoteId != NoteId || TranscriptParentId != ParentId
            || RawDigest != Antiphon.Server.Application.Services.DelegationNoteDigest.Compute(Raw)
            || !Path.IsPathFullyQualified(FilePath) || FileRaw != Raw || Raw.Length is < 4000 or > 14400
            || string.IsNullOrWhiteSpace(Summary) || Mode != "Apply" || Outcome != "Applied" || Decision >= Deadline
            || TranscriptKind != "UserPrompt" || Sequence <= 0 || PromptAt == default
            || DeliveryVerdict is not ("Delivered" or "LateConfirmed")
            || !Prompt.StartsWith(Header, StringComparison.Ordinal) || !Prompt.Contains(Summary, StringComparison.Ordinal)
            || !Prompt.Contains("Full report: " + FilePath, StringComparison.Ordinal)
            || !Header.Contains(Antiphon.Server.Application.Services.DelegationReportFormatter.Short(SourceId), StringComparison.Ordinal)
            || Prompt.Contains(distinctiveMiddle, StringComparison.Ordinal) || Prompt.Contains(Raw, StringComparison.Ordinal)
            || requiredMarkers.Any(m => !Prompt.Contains(m, StringComparison.Ordinal))
            || Kind != "ClaudeCode" || ModelAlias != ExpectedModelAlias
            || !ParentToolEvidence || !string.Equals(ParentReadHash, hash, StringComparison.OrdinalIgnoreCase) || ParentReadLength != Raw.Length
            || !FileSurvived || CompletionsBefore != 1 || CompletionsAfter != 1 || !Teardown)
            throw new InvalidOperationException("Incomplete or contradictory live Apply evidence.");
    }
}
