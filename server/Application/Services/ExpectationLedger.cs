using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

public sealed record ExpectationEpisodeOpen(
    string DirectiveId,
    string ConfigDigest,
    ExpectationEpisodeKind Kind,
    string SubjectKey,
    string Evidence,
    DateTime? ObservedAt = null,
    DateTime? LastSuccessfulScanAt = null,
    DateTime? NextNudgeAt = null);

public sealed record ExpectationNudgeRequest(
    string DirectiveId,
    string ConfigDigest,
    ExpectationEpisodeKind Kind,
    string SubjectKey,
    string Evidence,
    string Body,
    Guid AuditCardId,
    Guid BoardId,
    IReadOnlyList<Guid> AffectedTaskIds,
    Guid? DestinationSessionId,
    DateTime? DestinationGeneration,
    long? BaselineSequence,
    DateTime? LastSuccessfulScanAt,
    DateTime? NextNudgeAt,
    DateTime? ObservedAt);

public sealed record ExpectationNudgeCommit(
    Guid NudgeId,
    int Ordinal,
    string BodyDigest,
    Guid AuditCommentId,
    IReadOnlyList<Guid> CheckEventIds,
    Guid EpisodeId);

/// <summary>
/// S1 scaffold. The seam compiles and returns without writing a nudge or an audit row.
/// </summary>
public sealed class ExpectationLedger
{
    public const int MaxBodyChars = 8000;
    public const int MaxEvidenceChars = 2000;
    public const int MaxCheckDetailChars = 4000;
    public const string AuditAuthor = "expectation-watchdog";

    public ExpectationLedger(AppDbContext db, TimeProvider time, IEventBus events)
    {
    }

    public Task<Guid> OpenEpisodeAsync(ExpectationEpisodeOpen request, CancellationToken ct) =>
        Task.FromResult(Guid.Empty);

    public Task<ExpectationNudgeCommit> CommitNudgeAsync(ExpectationNudgeRequest request, CancellationToken ct) =>
        Task.FromResult(new ExpectationNudgeCommit(Guid.Empty, 0, string.Empty, Guid.Empty, [], Guid.Empty));
}
