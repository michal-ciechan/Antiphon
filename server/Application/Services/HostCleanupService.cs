using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>Protected receipt persistence only. Does not dispatch filesystem work or agents.</summary>
public sealed class HostCleanupService(AppDbContext db, IEventBus events,
    TimeProvider? clock = null, IOptions<HostCleanupSettings>? options = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly HostCleanupSettings _settings = options?.Value ?? new();

    public async Task<Guid?> IngestAsync(HostCleanupReceiptDto receipt, CancellationToken cancellationToken)
    {
        // IReadOnlyList may wrap a caller-owned mutable List. Hash, validation and persistence
        // must use one snapshot even when a database await lets that caller change its list.
        if (receipt.Candidates is null || receipt.Candidates.Count > _settings.MaxCandidates)
            throw new ValidationException("candidates", "Invalid cleanup receipt metadata.");
        receipt = receipt with { Candidates = receipt.Candidates.ToArray() };
        Validate(receipt);
        var receivedAt = _clock.GetUtcNow().UtcDateTime;
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(receipt)));
        var existing = await db.HostCleanupRuns.AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == receipt.RunId, cancellationToken);
        if (existing is not null)
        {
            RequireSameReceipt(existing, digest);
            if (existing.InvalidationPending) await PublishOneAsync(existing.Id, cancellationToken);
            return existing.Id;
        }

        var run = new HostCleanupRun
        {
            Id = receipt.RunId, BoardId = receipt.BoardId, HostId = receipt.HostId,
            StorageId = receipt.StorageId, RunnerStoreId = receipt.RunnerStoreId,
            ProcessBootId = receipt.ProcessBootId, SourceSha = receipt.SourceSha,
            ConfigDigest = receipt.ConfigDigest, PlanDigest = receipt.PlanDigest, ReceiptDigest = digest,
            LocalDate = receipt.LocalDate, Daily = receipt.Daily, Execute = receipt.Execute,
            // An old outbox upload preserves its observation and digest, but cannot become
            // today's complete inventory or clear an already-open backlog episode.
            Complete = receipt.Complete && receipt.FinishedAt <= receivedAt &&
                receivedAt - receipt.FinishedAt <= TimeSpan.FromDays(1) &&
                receipt.Candidates.All(candidate => candidate.ScanComplete &&
                candidate.Outcome is not ("partial" or "deferred" or "unknown")),
            PlannedAt = receipt.PlannedAt, FinishedAt = receipt.FinishedAt,
            SampledAt = receipt.SampledAt,
            SampleComplete = receipt.SampleComplete && receipt.SampledAt is { } sampled &&
                sampled <= receivedAt &&
                receivedAt - sampled <= TimeSpan.FromMinutes(_settings.SampleFreshMinutes),
            NamespaceAllocatedBytes = receipt.NamespaceAllocatedBytes, DiskCapacityBytes = receipt.DiskCapacityBytes,
            FreeBytesBefore = receipt.FreeBytesBefore, FreeBytesAfter = receipt.FreeBytesAfter,
            AttemptLimit = receipt.AttemptLimit, ByteLimit = receipt.ByteLimit,
            Attempts = receipt.Attempts, ReservedBytes = receipt.ReservedBytes, InvalidationPending = true,
        };
        run.Status = run.Complete ? "complete" : "incomplete";
        for (var ordinal = 0; ordinal < receipt.Candidates.Count; ordinal++)
        {
            var candidate = receipt.Candidates[ordinal];
            run.Candidates.Add(new HostCleanupCandidate
            {
                Id = Guid.NewGuid(), RunId = run.Id, Ordinal = ordinal,
                CanonicalPath = candidate.CanonicalPath, StorageId = candidate.StorageId,
                FileId = candidate.FileId, OwnerGeneration = candidate.OwnerGeneration,
                TaskId = candidate.TaskId, SessionId = candidate.SessionId, Family = candidate.Family,
                Worktree = candidate.Worktree, Disposition = candidate.Disposition,
                ReasonCode = candidate.ReasonCode, ContentClass = candidate.ContentClass,
                ExistingOwner = candidate.ExistingOwner, OwnerRefusalCode = candidate.OwnerRefusalCode,
                Branch = candidate.Branch, SourceSha = candidate.SourceSha, PushedSha = candidate.PushedSha,
                TargetSha = candidate.TargetSha, NewestWriteAt = candidate.NewestWriteAt,
                ScanComplete = candidate.ScanComplete, LogicalBytes = candidate.LogicalBytes,
                AllocatedBytes = candidate.AllocatedBytes, ReservedBytes = candidate.ReservedBytes,
                Outcome = candidate.Outcome, ReclaimedBytes = candidate.ReclaimedBytes,
            });
        }
        run.ReclaimedBytes = CheckedSum(receipt.Candidates.Select(candidate => candidate.ReclaimedBytes));
        run.EligibleWorktreeBytes = CheckedSum(receipt.Candidates
            .Where(candidate => candidate.Worktree && candidate.Disposition == "would-remove")
            .Select(candidate => candidate.AllocatedBytes!.Value));
        db.HostCleanupRuns.Add(run);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Another upload can win the exact run ID or the storage/day key. Read the durable
            // winner; a conflict never modifies it or submits a second filesystem command.
            db.ChangeTracker.Clear();
            var winner = await db.HostCleanupRuns.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == receipt.RunId, cancellationToken);
            if (winner is null)
                throw new ConflictException("The daily storage namespace already has a run.",
                    "host_cleanup_daily_run_conflict");
            RequireSameReceipt(winner, digest);
        }
        await PublishOneAsync(run.Id, cancellationToken);
        return run.Id;
    }

    public async Task<HostCleanupReportPage?> ReadAsync(Guid boardId, Guid runId, int offset, int take,
        CancellationToken cancellationToken)
    {
        if (boardId == Guid.Empty || offset < 0 || take is <= 0 or > 200)
            throw new ValidationException("page", "A board and a bounded candidate page are required.");
        var run = await db.HostCleanupRuns.AsNoTracking().SingleOrDefaultAsync(
            row => row.BoardId == boardId && row.Id == runId, cancellationToken);
        if (run is null) return null;
        var query = db.HostCleanupCandidates.AsNoTracking().Where(candidate => candidate.RunId == run.Id);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(candidate => candidate.Ordinal).Skip(offset).Take(take)
            .ToListAsync(cancellationToken);
        return new(run.Id, run.BoardId, run.HostId, run.StorageId, run.ReceiptDigest ?? "",
            run.Complete, run.ReclaimedBytes, run.EligibleWorktreeBytes, count,
            rows.Select(ToDto).ToArray(), (long)offset + rows.Count < count ? offset + rows.Count : null,
            new(run.SampledAt, run.SampleComplete, run.NamespaceAllocatedBytes, run.DiskCapacityBytes,
                run.FreeBytesBefore, run.FreeBytesAfter));
    }

    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await db.HostCleanupRuns.AsNoTracking().Where(run => run.InvalidationPending)
            .OrderBy(run => run.PlannedAt).ThenBy(run => run.Id).Select(run => run.Id)
            .Take(200).ToListAsync(cancellationToken);
        var published = 0;
        foreach (var id in pending)
            if (await PublishOneAsync(id, cancellationToken)) published++;
        return published;
    }

    private async Task<bool> PublishOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.HostCleanupRuns.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == id && row.InvalidationPending, cancellationToken);
        if (run is null) return false;
        try
        {
            // This event already invalidates attention on the client. A crash after publishing
            // can repeat the invalidation, while the immutable receipt still exists only once.
            await events.PublishToAllAsync("ScheduleChanged",
                new { hostCleanupRunId = id, boardId = run.BoardId }, cancellationToken);
            await db.HostCleanupRuns.Where(row => row.Id == id && row.InvalidationPending)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.InvalidationPending, false), cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not discard the pending notification or copy arbitrary exception payloads into
            // attention. Recovery uses the persisted run and never re-executes cleanup.
            return false;
        }
    }

    private static void RequireSameReceipt(HostCleanupRun existing, string digest)
    {
        if (existing.ReceiptDigest != digest)
            throw new ConflictException("The run ID already names a different receipt.", "host_cleanup_receipt_conflict");
    }

    private void Validate(HostCleanupReceiptDto receipt)
    {
        void Require(bool condition, string field)
        {
            if (!condition) throw new ValidationException(field, "Invalid cleanup receipt metadata.");
        }
        Require(receipt.RunId != Guid.Empty && receipt.BoardId != Guid.Empty, "identity");
        Require(Code(receipt.HostId, 64) && Code(receipt.StorageId, 200) &&
            Code(receipt.RunnerStoreId, 200) && Code(receipt.ProcessBootId, 200), "storageIdentity");
        Require(Hex(receipt.SourceSha, 40, 64) && Hex(receipt.ConfigDigest, 64, 64) &&
            Hex(receipt.PlanDigest, 64, 64), "digest");
        Require(receipt.PlannedAt.Kind == DateTimeKind.Utc && receipt.FinishedAt.Kind == DateTimeKind.Utc &&
            receipt.FinishedAt >= receipt.PlannedAt, "timestamps");
        Require(receipt.SampledAt is null || receipt.SampledAt.Value.Kind == DateTimeKind.Utc, "sampledAt");
        Require(receipt.AttemptLimit > 0 && receipt.AttemptLimit <= _settings.MaxAttempts &&
            receipt.ByteLimit > 0 && receipt.ByteLimit <= _settings.MaxBytes && receipt.Attempts >= 0 &&
            receipt.Attempts <= receipt.AttemptLimit && receipt.ReservedBytes >= 0 &&
            receipt.ReservedBytes <= receipt.ByteLimit, "budget");
        Require(receipt.Candidates is not null && receipt.Candidates.Count <= _settings.MaxCandidates, "candidates");
        Require(receipt.NamespaceAllocatedBytes is null or >= 0 && receipt.DiskCapacityBytes is null or > 0 &&
            receipt.FreeBytesBefore is null or >= 0 && receipt.FreeBytesAfter is null or >= 0, "sample");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_settings.TimeZoneId);
        Require(receipt.LocalDate == DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(receipt.PlannedAt, zone)), "localDate");
        foreach (var candidate in receipt.Candidates!)
        {
            Require(candidate.StorageId == receipt.StorageId && Text(candidate.CanonicalPath, 4000) &&
                Code(candidate.Family, 64) && Code(candidate.ReasonCode, 64) && Code(candidate.ContentClass, 32) &&
                Code(candidate.ExistingOwner, 64) && (candidate.OwnerRefusalCode is null || Code(candidate.OwnerRefusalCode, 64)), "candidate");
            Require(candidate.Disposition is "eligible" or "would-remove" or "keep" or "unknown" &&
                candidate.Outcome is "inventory_only" or "removed" or "partial" or "kept" or "already_absent" or "deferred" or "unknown", "outcome");
            Require(candidate.FileId is null || Text(candidate.FileId, 200), "fileId");
            Require(candidate.OwnerGeneration is null || Text(candidate.OwnerGeneration, 200), "ownerGeneration");
            Require(candidate.Branch is null || Text(candidate.Branch, 300), "branch");
            Require(new[] { candidate.SourceSha, candidate.PushedSha, candidate.TargetSha }
                .All(sha => sha is null || Hex(sha, 40, 64)), "candidateSha");
            Require(candidate.NewestWriteAt is null || candidate.NewestWriteAt.Value.Kind == DateTimeKind.Utc, "newestWriteAt");
            Require(candidate.LogicalBytes is null or >= 0 && candidate.AllocatedBytes is null or >= 0 &&
                candidate.ReservedBytes is null or >= 0 && candidate.ReclaimedBytes >= 0, "candidateBytes");
            if (candidate.Worktree)
            {
                Require(candidate.ReservedBytes is null && candidate.ReclaimedBytes == 0 &&
                    candidate.Outcome == "inventory_only", "worktreeInventoryOnly");
                Require(candidate.Disposition != "would-remove" || candidate.AllocatedBytes is not null, "worktreeBytes");
            }
            else if (candidate.Outcome is "removed" or "partial")
            {
                Require(candidate.ReservedBytes is not null && candidate.FileId is not null &&
                    candidate.OwnerGeneration is not null && candidate.ReclaimedBytes <= candidate.ReservedBytes,
                    "scratchCustody");
            }
            else Require(candidate.ReclaimedBytes == 0, "unremovedBytes");
        }
        Require(CheckedSum(receipt.Candidates!.Where(candidate => !candidate.Worktree)
            .Select(candidate => candidate.ReservedBytes ?? 0)) <= receipt.ReservedBytes, "reservationTotal");
        Require(receipt.Candidates.Count(candidate => !candidate.Worktree && candidate.Outcome is "removed" or "partial")
            <= receipt.Attempts, "attemptTotal");
    }

    private static long CheckedSum(IEnumerable<long> values)
    {
        try { return values.Aggregate(0L, (sum, value) => checked(sum + value)); }
        catch (OverflowException) { throw new ValidationException("bytes", "Cleanup byte totals overflow."); }
    }

    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);
    private static bool Code(string value, int max) => Text(value, max) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
    private static bool Hex(string value, int firstLength, int secondLength) =>
        value is not null && (value.Length == firstLength || value.Length == secondLength) &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static HostCleanupReportedCandidate ToDto(HostCleanupCandidate candidate) => new(
        candidate.CanonicalPath, candidate.StorageId, candidate.FileId, candidate.OwnerGeneration,
        candidate.Family, candidate.Worktree, candidate.Disposition, candidate.ReasonCode,
        candidate.ContentClass, candidate.ExistingOwner, candidate.OwnerRefusalCode, candidate.NewestWriteAt,
        candidate.ScanComplete, candidate.LogicalBytes, candidate.AllocatedBytes, candidate.ReservedBytes,
        candidate.Outcome ?? "unknown", candidate.ReclaimedBytes, candidate.TaskId, candidate.SessionId,
        candidate.Branch, candidate.SourceSha, candidate.PushedSha, candidate.TargetSha);
}
