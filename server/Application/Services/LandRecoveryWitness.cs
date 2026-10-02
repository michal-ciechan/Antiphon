using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Only a prior durable adoption intent and its request-owned pins can authorize a fresh reset.</summary>
internal sealed class LandRecoveryWitness(AppDbContext db, ILandingGit git)
{
    internal sealed record Result(Guid? RequestId, string? LocalBeforeSha, string Reason)
    {
        public bool Accepted => RequestId is not null && LocalBeforeSha is not null;
    }

    public async Task<Result> FindAsync(AgentTaskLandRequest current, string common, string sourceFingerprint,
        CancellationToken ct)
    {
        var candidates = await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => r.TaskId == current.TaskId && r.Id != current.Id
                && r.ExpectedSourceSha == current.ExpectedSourceSha)
            .OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var proven = new List<AgentTaskLandRequest>();
        foreach (var row in candidates)
        {
            if (row.RecoveryMode != current.RecoveryMode || row.RecoveryAdoptedAt is not null
                || row.SourceResolutionState != LandSourceResolutionState.AdvanceStarted
                || row.SourceAdvanceChildOperation != "source-adopt-reset"
                || !GitObjectId.IsFull(row.RecoveryLocalBeforeSha)
                || row.RecoverySourceTaskId != current.RecoverySourceTaskId
                || row.RecoverySourceFullRef != current.RecoverySourceFullRef
                || row.RecoverySourceFingerprint != sourceFingerprint
                || row.SourceFullRefSnapshot != current.SourceFullRefSnapshot
                || !SamePath(row.RepositoryPathSnapshot, current.RepositoryPathSnapshot)
                || !SamePath(row.WorktreePathSnapshot, current.WorktreePathSnapshot)
                || row.SourceCommonDirectory is not null && !SamePath(row.SourceCommonDirectory, common)
                || row.SourceWorktreePath is not null
                    && !SamePath(row.SourceWorktreePath, current.WorktreePathSnapshot))
                continue;
            if (!SamePath(await git.CommonDirectoryAsync(row.RepositoryPathSnapshot!, ct), common)
                || !SamePath(await git.CommonDirectoryAsync(row.WorktreePathSnapshot!, ct), common))
                continue;
            if (row.SourceAdvanceChildProcessId is int pid && row.SourceAdvanceChildStartTicks is long ticks)
            {
                var alive = await git.IsProcessAliveAsync(pid, ticks, ct);
                if (alive != false) return new(null, null, "interrupted_process_requires_inspection");
            }
            else if (row.SourceAdvanceChildProcessId is not null || row.SourceAdvanceChildStartTicks is not null)
                return new(null, null, "interrupted_process_requires_inspection");
            if (!await PinEqualsAsync(row, "local-before", row.RecoveryLocalBeforeSha!, ct)
                || !await PinEqualsAsync(row, "source", current.ExpectedSourceSha!, ct))
                continue;
            proven.Add(row);
        }
        if (proven.Count == 0) return new(null, null, "source_dirty");
        if (proven.Select(r => r.RecoveryLocalBeforeSha).Distinct(StringComparer.Ordinal).Skip(1).Any())
            return new(null, null, "adopt_recovery_ambiguous");
        return new(proven[0].Id, proven[0].RecoveryLocalBeforeSha, "recovery_witness_proven");
    }

    private async Task<bool> PinEqualsAsync(AgentTaskLandRequest row, string name, string expected,
        CancellationToken ct)
    {
        var reference = $"refs/antiphon/land/{row.TaskId:N}/{row.Id:N}/adopt/{name}";
        var pin = await git.RunAsync(row.RepositoryPathSnapshot!, ["rev-parse", "--verify", reference], ct);
        return pin.Succeeded && pin.Output.Trim() == expected;
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }
}
