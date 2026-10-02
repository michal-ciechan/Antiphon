using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal sealed record LandRecoveryWitness(Guid RequestId, string LocalBeforeSha, string? Reason = null);

internal static class LandRecoveryWitnessFinder
{
    public static async Task<LandRecoveryWitness?> FindAsync(AppDbContext db, ILandingGit git,
        AgentTaskLandRequest current, LandSourceCoordinates coordinates, string common,
        string expected, string fingerprint, CancellationToken ct)
    {
        var candidates = await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => r.TaskId == current.TaskId && r.Id != current.Id && r.ExpectedSourceSha == expected)
            .ToListAsync(ct);
        var valid = new List<LandRecoveryWitness>();
        foreach (var old in candidates)
        {
            if (old.RecoveryMode != current.RecoveryMode || old.RecoveryMode == LandRecoveryMode.None
                || old.RecoverySourceTaskId != current.RecoverySourceTaskId
                || old.RecoverySourceFullRef != current.RecoverySourceFullRef
                || old.RecoverySourceFingerprint != fingerprint
                || old.SourceFullRefSnapshot != coordinates.SourceFullRef
                || old.WorktreePathSnapshot is null || old.RepositoryPathSnapshot is null
                || old.SourceResolutionState != LandSourceResolutionState.AdvanceStarted
                || old.SourceAdvanceChildOperation != "source-adopt-reset"
                || old.RecoveryAdoptedAt is not null || !GitObjectId.IsFull(old.RecoveryLocalBeforeSha))
                continue;
            if (!SamePath(old.WorktreePathSnapshot, coordinates.WorktreePath)
                || !SamePath(await git.CommonDirectoryAsync(old.RepositoryPathSnapshot, ct), common))
                continue;
            if (old.SourceAdvanceChildProcessId is int pid)
            {
                if (old.SourceAdvanceChildStartTicks is not long ticks
                    || await git.IsProcessAliveAsync(pid, ticks, ct) != false)
                    return new(old.Id, old.RecoveryLocalBeforeSha!, "interrupted_process_requires_inspection");
            }
            var prefix = $"refs/antiphon/land/{old.TaskId:N}/{old.Id:N}/adopt";
            if (!await PinEqualsAsync(git, coordinates.RepositoryPath, $"{prefix}/local-before", old.RecoveryLocalBeforeSha!, ct)
                || !await PinEqualsAsync(git, coordinates.RepositoryPath, $"{prefix}/source", expected, ct))
                continue;
            valid.Add(new(old.Id, old.RecoveryLocalBeforeSha!));
        }
        if (valid.Count == 0) return null;
        if (valid.Select(x => x.LocalBeforeSha).Distinct(StringComparer.Ordinal).Skip(1).Any())
            return new(Guid.Empty, "", "adopt_recovery_ambiguous");
        return valid.OrderBy(x => x.RequestId).First();
    }

    private static async Task<bool> PinEqualsAsync(ILandingGit git, string repository, string pin,
        string expected, CancellationToken ct)
    {
        var value = await git.RunAsync(repository, ["show-ref", "--verify", "--hash", pin], ct);
        return value.Succeeded && value.Output.Trim() == expected;
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
