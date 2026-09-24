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

/// <summary>
/// S3 aggregate nudge. <see cref="ExpectedLatestNudgeId"/> is the latest nudge the caller's
/// cooldown decision saw; a different latest nudge under the lock means another sweep won.
/// </summary>
public sealed record ExpectationAggregateNudgeRequest(
    Guid NudgeId,
    string DirectiveId,
    string ConfigDigest,
    Guid BoardId,
    Guid AuditCardId,
    IReadOnlyList<Guid> EpisodeIds,
    IReadOnlyList<string> SubjectKeys,
    IReadOnlyList<Guid> AffectedTaskIds,
    string Evidence,
    string Body,
    Guid? ExpectedLatestNudgeId,
    DateTime ObservedAt,
    DateTime NextNudgeAt);

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
    /// One aggregate nudge for several open episodes, with a Check event on every affected task
    /// and the audit card comment, in one transaction. Under the directive row lock the latest
    /// nudge is re-read: if another sweep committed since this decision was made, nothing is
    /// written and null is returned. A resolved episode is dropped; none left means no nudge.
    /// </summary>
    public async Task<ExpectationNudgeCommit?> CommitAggregateNudgeAsync(
        ExpectationAggregateNudgeRequest request,
        CancellationToken ct)
    {
        ValidateIdentity(request.DirectiveId, request.ConfigDigest, "aggregate", request.Evidence);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxBodyChars)
        {
            throw new ValidationException(
                nameof(request.Body),
                "Nudge body is required and must fit the audit ceiling.");
        }

        if (request.EpisodeIds is not { Count: > 0 })
            throw new ValidationException(nameof(request.EpisodeIds), "At least one episode is required.");

        ExpectationNudgeCommit? committed = null;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                var now = AsUtc(request.ObservedAt);
                var directiveId = request.DirectiveId.Trim();
                await LockOrCreateStateAsync(directiveId, request.ConfigDigest.Trim(), null, null, now, ct);
                var latest = await _db.ExpectationNudges.AsNoTracking()
                    .Where(nudge => nudge.DirectiveId == directiveId)
                    .OrderByDescending(nudge => nudge.Ordinal)
                    .Select(nudge => (Guid?)nudge.Id)
                    .FirstOrDefaultAsync(ct);
                if (latest != request.ExpectedLatestNudgeId)
                {
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    return null;
                }

                var wanted = request.EpisodeIds.Distinct().ToList();
                var open = await _db.ExpectationEpisodes.AsNoTracking()
                    .Where(row => wanted.Contains(row.Id) && row.DirectiveId == directiveId && row.ResolvedAt == null)
                    .Select(row => row.Id)
                    .ToListAsync(ct);
                if (open.Count == 0)
                {
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    return null;
                }

                var state = await _db.ExpectationWatchStates
                    .SingleAsync(row => row.DirectiveId == directiveId, ct);
                state.NextNudgeAt = AsUtc(request.NextNudgeAt);
                state.UpdatedAt = now;
                state.ConcurrencyToken = Guid.NewGuid();

                var ordinal = await NextOrdinalAsync(directiveId, ct);
                var commentId = Guid.NewGuid();
                var checkIds = new List<Guid>();
                foreach (var taskId in request.AffectedTaskIds.Distinct())
                {
                    var checkId = Guid.NewGuid();
                    checkIds.Add(checkId);
                    var detail = $"[expectation-nudge:{request.NudgeId:D}] {request.Evidence.Trim()}";
                    _db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = checkId,
                        AgentTaskId = taskId,
                        Type = AgentTaskEventType.Check,
                        At = now,
                        Detail = detail.Length <= MaxCheckDetailChars ? detail : detail[..MaxCheckDetailChars],
                    });
                }

                _db.CardComments.Add(new CardComment
                {
                    Id = commentId,
                    CardId = request.AuditCardId,
                    Body = AuditBody(request.NudgeId, request.Body)
                        + "\nSubjects: " + string.Join(", ", request.SubjectKeys)
                        + "\nTasks: " + (request.AffectedTaskIds.Count == 0
                            ? "none"
                            : string.Join(", ", request.AffectedTaskIds.Distinct().Select(id => id.ToString("D")))),
                    Author = AuditAuthor,
                    Origin = CardCommentOrigin.Antiphon,
                    CreatedAt = now,
                });
                var digest = ExpectationDirectiveDigest.HashUtf8(request.Body);
                _db.ExpectationNudges.Add(new ExpectationNudge
                {
                    Id = request.NudgeId,
                    DirectiveId = directiveId,
                    Ordinal = ordinal,
                    EpisodeIdsJson = JsonSerializer.Serialize(open),
                    EvidenceSnapshot = request.Evidence.Trim(),
                    Body = request.Body,
                    BodyDigest = digest,
                    AttemptState = ExpectationAttemptState.None,
                    OperatorOutboxState = ExpectationOperatorOutboxState.None,
                    AuditCommentId = commentId,
                    CheckEventIdsJson = JsonSerializer.Serialize(checkIds),
                    CreatedAt = now,
                    ConcurrencyToken = Guid.NewGuid(),
                });
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                committed = new ExpectationNudgeCommit(request.NudgeId, ordinal, digest, commentId, checkIds, open[0]);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // A concurrent sweep took this ordinal first: its nudge stands, this one is not written.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return null;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        _db.ChangeTracker.Clear();
        await _events.PublishToAllAsync("BoardChanged", new { boardId = request.BoardId }, ct);
        return committed;
    }

    /// <summary>
    /// Resolve open episodes of the current digest that this successful scan did not observe and
    /// that were not observed by the previous successful scan either (two clear scans at least
    /// <see cref="ExpectationWindows.ClearGap"/> apart). Observed subjects stay open. A subject whose
    /// evidence was unknown this scan (or every subject, when the scan preserves all episodes) has
    /// its LastObservedAt moved to this scan: an unknown scan is never counted as a clear one.
    /// </summary>
    public async Task<int> ResolveClearedAsync(
        string directiveId,
        string configDigest,
        IReadOnlyCollection<string> observedSubjects,
        IReadOnlyCollection<string> unknownSubjects,
        bool preserveAll,
        DateTime? previousSuccessfulScanAt,
        DateTime observedAt,
        CancellationToken ct)
    {
        ValidateIdentity(directiveId, configDigest, "scan", "scan");
        var now = AsUtc(observedAt);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var id = directiveId.Trim();
            var digest = configDigest.Trim();
            var rows = await _db.ExpectationEpisodes
                .FromSqlInterpolated($"""
                    SELECT * FROM "ExpectationEpisodes"
                    WHERE "DirectiveId" = {id}
                      AND "ResolvedAt" IS NULL
                      AND "ConfigDigest" = {digest}
                    FOR UPDATE
                    """)
                .AsTracking()
                .ToListAsync(ct);
            var observed = observedSubjects.ToHashSet(StringComparer.Ordinal);
            var unknown = unknownSubjects.ToHashSet(StringComparer.Ordinal);
            var previous = AsUtc(previousSuccessfulScanAt);
            var canResolve = !preserveAll && previous is not null && now - previous.Value >= ExpectationWindows.ClearGap;
            var resolved = 0;
            foreach (var row in rows)
            {
                if (observed.Contains(row.SubjectKey))
                    continue;
                if (preserveAll || unknown.Contains(row.SubjectKey))
                {
                    if (row.LastObservedAt < now)
                    {
                        row.LastObservedAt = now;
                        row.ConcurrencyToken = Guid.NewGuid();
                    }

                    continue;
                }

                if (!canResolve || row.LastObservedAt >= previous!.Value)
                    continue;
                row.ResolvedAt = now;
                row.ConcurrencyToken = Guid.NewGuid();
                resolved++;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _db.ChangeTracker.Clear();
            return resolved;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            throw;
        }
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
