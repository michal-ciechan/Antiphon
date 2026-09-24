using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
/// Atomic episode, nudge and audit writes. Direct delivery is S4. This type never enqueues a
/// session message, never calls delivery-failure recovery, and never touches CARD-0648 recovery.
/// BoardChanged is published only after the audit transaction commits.
/// </summary>
public sealed class ExpectationLedger
{
    public const int MaxBodyChars = 8000;
    public const int MaxEvidenceChars = 2000;
    public const int MaxCheckDetailChars = 4000;
    public const string AuditAuthor = "expectation-watchdog";

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly IEventBus _events;

    public ExpectationLedger(AppDbContext db, TimeProvider time, IEventBus events)
    {
        _db = db;
        _time = time;
        _events = events;
    }

    public async Task<Guid> OpenEpisodeAsync(ExpectationEpisodeOpen request, CancellationToken ct)
    {
        ValidateIdentity(request.DirectiveId, request.ConfigDigest, request.SubjectKey, request.Evidence);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var now = AsUtc(request.ObservedAt ?? _time.GetUtcNow().UtcDateTime);
                await LockOrCreateStateAsync(
                    request.DirectiveId.Trim(),
                    request.ConfigDigest.Trim(),
                    AsUtc(request.LastSuccessfulScanAt),
                    AsUtc(request.NextNudgeAt),
                    now,
                    ct);
                var episodeId = await UpsertEpisodeAsync(
                    request.DirectiveId.Trim(),
                    request.ConfigDigest.Trim(),
                    request.Kind,
                    request.SubjectKey.Trim(),
                    request.Evidence.Trim(),
                    now,
                    ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return episodeId;
            }
            catch (DbUpdateException ex) when (attempt == 1 && IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        throw new InvalidOperationException("Expectation episode open did not settle.");
    }

    public async Task<ExpectationNudgeCommit> CommitNudgeAsync(ExpectationNudgeRequest request, CancellationToken ct)
    {
        ValidateIdentity(request.DirectiveId, request.ConfigDigest, request.SubjectKey, request.Evidence);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxBodyChars)
        {
            throw new ValidationException(
                nameof(request.Body),
                "Nudge body is required and must fit the audit ceiling.");
        }

        if (request.AffectedTaskIds is null)
            throw new ValidationException(nameof(request.AffectedTaskIds), "Affected tasks are required.");

        ExpectationNudgeCommit? committed = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var now = AsUtc(request.ObservedAt ?? _time.GetUtcNow().UtcDateTime);
                var directiveId = request.DirectiveId.Trim();
                await LockOrCreateStateAsync(
                    directiveId,
                    request.ConfigDigest.Trim(),
                    AsUtc(request.LastSuccessfulScanAt),
                    AsUtc(request.NextNudgeAt),
                    now,
                    ct);
                var episodeId = await UpsertEpisodeAsync(
                    directiveId,
                    request.ConfigDigest.Trim(),
                    request.Kind,
                    request.SubjectKey.Trim(),
                    request.Evidence.Trim(),
                    now,
                    ct);
                var ordinal = await NextOrdinalAsync(directiveId, ct);
                var nudgeId = Guid.NewGuid();
                var commentId = Guid.NewGuid();
                var checkIds = new List<Guid>();
                foreach (var taskId in request.AffectedTaskIds.Distinct())
                {
                    var checkId = Guid.NewGuid();
                    checkIds.Add(checkId);
                    _db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = checkId,
                        AgentTaskId = taskId,
                        Type = AgentTaskEventType.Check,
                        At = now,
                        Detail = CheckDetail(nudgeId, request),
                    });
                }

                _db.CardComments.Add(new CardComment
                {
                    Id = commentId,
                    CardId = request.AuditCardId,
                    Body = AuditBody(nudgeId, request.Body),
                    Author = AuditAuthor,
                    Origin = CardCommentOrigin.Antiphon,
                    CreatedAt = now,
                });
                _db.ExpectationNudges.Add(new ExpectationNudge
                {
                    Id = nudgeId,
                    DirectiveId = directiveId,
                    Ordinal = ordinal,
                    EpisodeIdsJson = JsonSerializer.Serialize(new[] { episodeId }),
                    EvidenceSnapshot = request.Evidence.Trim(),
                    Body = request.Body,
                    BodyDigest = ExpectationDirectiveDigest.HashUtf8(request.Body),
                    DestinationSessionId = request.DestinationSessionId,
                    DestinationGeneration = AsUtc(request.DestinationGeneration),
                    BaselineSequence = request.BaselineSequence,
                    AttemptState = ExpectationAttemptState.None,
                    OperatorOutboxState = ExpectationOperatorOutboxState.None,
                    AuditCommentId = commentId,
                    CheckEventIdsJson = JsonSerializer.Serialize(checkIds),
                    CreatedAt = now,
                    ConcurrencyToken = Guid.NewGuid(),
                });
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                committed = new ExpectationNudgeCommit(
                    nudgeId,
                    ordinal,
                    ExpectationDirectiveDigest.HashUtf8(request.Body),
                    commentId,
                    checkIds,
                    episodeId);
                break;
            }
            catch (DbUpdateException ex) when (attempt == 1 && IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        if (committed is null)
            throw new InvalidOperationException("Expectation nudge commit did not settle.");

        await _events.PublishToAllAsync("BoardChanged", new { boardId = request.BoardId }, ct);
        return committed;
    }

    /// <summary>
    /// Stamp the last scan or its error. A failed observation does not move the success clock
    /// or retire a digest. This writes no nudge and no session message.
    /// </summary>
    public async Task RecordObservationAsync(
        string directiveId,
        string configDigest,
        DateTime observedAt,
        bool successful,
        string? error,
        CancellationToken ct)
    {
        ValidateIdentity(directiveId, configDigest, "scan", "scan");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = AsUtc(observedAt);
            var id = directiveId.Trim();
            var digest = configDigest.Trim();
            var state = await LockStateAsync(id, ct);
            if (state is null)
            {
                state = new ExpectationWatchState
                {
                    Id = Guid.NewGuid(),
                    DirectiveId = id,
                    ConfigDigest = digest,
                    UpdatedAt = now,
                    ConcurrencyToken = Guid.NewGuid(),
                };
                _db.ExpectationWatchStates.Add(state);
            }

            if (successful)
            {
                state.ConfigDigest = digest;
                state.LastSuccessfulScanAt = now;
                state.LastObservationError = null;
            }
            else
            {
                state.LastObservationError = ClipError(error);
            }

            state.UpdatedAt = now;
            state.ConcurrencyToken = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>Retire open episodes whose digest is no longer the directive's. History stays.</summary>
    public async Task ResolveDigestMismatchesAsync(
        string directiveId,
        string configDigest,
        DateTime observedAt,
        CancellationToken ct)
    {
        ValidateIdentity(directiveId, configDigest, "scan", "scan");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = AsUtc(observedAt);
            var id = directiveId.Trim();
            var digest = configDigest.Trim();
            var rows = await _db.ExpectationEpisodes
                .FromSqlInterpolated($"""
                    SELECT * FROM "ExpectationEpisodes"
                    WHERE "DirectiveId" = {id}
                      AND "ResolvedAt" IS NULL
                      AND "ConfigDigest" <> {digest}
                    FOR UPDATE
                    """)
                .AsTracking()
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                row.ResolvedAt = now;
                row.ConcurrencyToken = Guid.NewGuid();
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ExpectationWatchState?> LockStateAsync(string directiveId, CancellationToken ct) =>
        await _db.ExpectationWatchStates
            .FromSqlInterpolated($"SELECT * FROM \"ExpectationWatchStates\" WHERE \"DirectiveId\" = {directiveId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(ct);

    private static string? ClipError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "observation unknown";
        var trimmed = error.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private async Task<ExpectationWatchState> LockOrCreateStateAsync(
        string directiveId,
        string digest,
        DateTime? lastSuccessfulScanAt,
        DateTime? nextNudgeAt,
        DateTime now,
        CancellationToken ct)
    {
        var locked = await _db.ExpectationWatchStates
            .FromSqlInterpolated($"SELECT * FROM \"ExpectationWatchStates\" WHERE \"DirectiveId\" = {directiveId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(ct);
        if (locked is not null)
        {
            locked.ConfigDigest = digest;
            if (lastSuccessfulScanAt is not null)
                locked.LastSuccessfulScanAt = lastSuccessfulScanAt;
            if (nextNudgeAt is not null)
                locked.NextNudgeAt = nextNudgeAt;
            locked.UpdatedAt = now;
            locked.ConcurrencyToken = Guid.NewGuid();
            return locked;
        }

        var created = new ExpectationWatchState
        {
            Id = Guid.NewGuid(),
            DirectiveId = directiveId,
            ConfigDigest = digest,
            LastSuccessfulScanAt = lastSuccessfulScanAt,
            NextNudgeAt = nextNudgeAt,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        };
        _db.ExpectationWatchStates.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<Guid> UpsertEpisodeAsync(
        string directiveId,
        string digest,
        ExpectationEpisodeKind kind,
        string subjectKey,
        string evidence,
        DateTime now,
        CancellationToken ct)
    {
        var kindValue = (int)kind;
        var existing = await _db.ExpectationEpisodes
            .FromSqlInterpolated($"""
                SELECT * FROM "ExpectationEpisodes"
                WHERE "DirectiveId" = {directiveId}
                  AND "Kind" = {kindValue}
                  AND "SubjectKey" = {subjectKey}
                  AND "ResolvedAt" IS NULL
                FOR UPDATE
                """)
            .AsTracking()
            .SingleOrDefaultAsync(ct);
        if (existing is not null)
        {
            existing.LastObservedAt = now;
            existing.Evidence = evidence;
            existing.ConfigDigest = digest;
            existing.ConcurrencyToken = Guid.NewGuid();
            return existing.Id;
        }

        var created = new ExpectationEpisode
        {
            Id = Guid.NewGuid(),
            DirectiveId = directiveId,
            Kind = kind,
            SubjectKey = subjectKey,
            FirstObservedAt = now,
            LastObservedAt = now,
            Evidence = evidence,
            ConfigDigest = digest,
            ConcurrencyToken = Guid.NewGuid(),
        };
        _db.ExpectationEpisodes.Add(created);
        return created.Id;
    }

    private async Task<int> NextOrdinalAsync(string directiveId, CancellationToken ct)
    {
        var max = await _db.ExpectationNudges
            .Where(nudge => nudge.DirectiveId == directiveId)
            .MaxAsync(nudge => (int?)nudge.Ordinal, ct);
        return (max ?? 0) + 1;
    }

    private static void ValidateIdentity(string directiveId, string digest, string subjectKey, string evidence)
    {
        if (string.IsNullOrWhiteSpace(directiveId) || directiveId.Trim().Length > 100)
            throw new ValidationException(nameof(directiveId), "Directive id is required.");
        if (string.IsNullOrWhiteSpace(digest) || digest.Trim().Length != 64)
            throw new ValidationException(nameof(digest), "Config digest must be a 64-character sha256.");
        if (string.IsNullOrWhiteSpace(subjectKey) || subjectKey.Trim().Length > 400)
            throw new ValidationException(nameof(subjectKey), "Subject key is required.");
        if (evidence is null || evidence.Trim().Length > MaxEvidenceChars)
            throw new ValidationException(nameof(evidence), "Evidence exceeds the sanitized ceiling.");
    }

    private static string AuditBody(Guid nudgeId, string body) =>
        $"[expectation-nudge:{nudgeId:D}]\n{body}";

    private static string CheckDetail(Guid nudgeId, ExpectationNudgeRequest request)
    {
        var detail = $"[expectation-nudge:{nudgeId:D}] {request.Kind} {request.SubjectKey.Trim()} {request.Evidence.Trim()}";
        return detail.Length <= MaxCheckDetailChars ? detail : detail[..MaxCheckDetailChars];
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : AsUtc(value.Value);

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }
}
