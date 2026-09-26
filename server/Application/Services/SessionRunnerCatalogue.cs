using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0710 D-7. Desktop plus every configured runner. One entry's failure does not drop the others.
/// </summary>
public static class SessionRunnerCatalogue
{
    public static async Task<IReadOnlyList<SessionRunnerCatalogueEntryDto>> ListAsync(
        PhoneHomeRunnerDirectory directory,
        AppDbContext? db,
        DelegationSettings delegation,
        RemoteWorkspacePreparer? prep,
        CancellationToken ct)
    {
        var rows = new List<SessionRunnerCatalogueEntryDto>();
        var desktop = await directory.DescribeAsync(null, ct);
        rows.Add(await DesktopAsync(desktop, db, delegation, ct));
        foreach (var id in directory.KnownRunnerIds.Where(id => !RunnerRequestIntent.IsDesktopAlias(id)))
        {
            try
            {
                rows.Add(await RemoteAsync(directory, id, db, prep, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                rows.Add(new SessionRunnerCatalogueEntryDto(
                    id, id, null, null, false, false, "catalogue_read_failed",
                    null, null, "sessions", null, true, [], false, false));
            }
        }

        return rows;
    }

    private static async Task<SessionRunnerCatalogueEntryDto> DesktopAsync(
        RunnerDescriptor? described, AppDbContext? db, DelegationSettings delegation, CancellationToken ct)
    {
        int? occupied = null;
        if (db is not null)
        {
            occupied = await db.AgentTasks.AsNoTracking()
                .Where(AgentTaskRoles.NotSpecialist)
                .CountAsync(
                    t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                        && !t.CapacityWaitRetained
                        && (t.RunnerId == null || t.RunnerId == ""),
                    ct);
        }

        return Row(described, RunnerPlatformWire.DesktopId, "Desktop", "delegatedTasks",
            delegation.MaxConcurrentTasks, occupied, described is not null, draining: false, retired: false);
    }

    private static async Task<SessionRunnerCatalogueEntryDto> RemoteAsync(
        PhoneHomeRunnerDirectory directory,
        string id,
        AppDbContext? db,
        RemoteWorkspacePreparer? prep,
        CancellationToken ct)
    {
        var described = await directory.DescribeAsync(id, ct);
        int? capacity = null;
        int? occupied = null;
        var observed = described is { DispatchEligible: true };
        if (observed && db is not null)
        {
            capacity = directory.DeclaredCapacity(id);
            var sessions = await db.AgentSessions.AsNoTracking().CountAsync(
                s => s.RunnerId == id
                    && (s.Status == SessionStatus.Created
                        || s.Status == SessionStatus.Starting
                        || s.Status == SessionStatus.Running
                        || s.Status == SessionStatus.Stopping),
                ct);
            var pending = await db.AgentTasks.AsNoTracking().CountAsync(
                t => t.RunnerId == id
                    && t.Status == AgentTaskStatus.Queued
                    && t.AgentSessionId == null
                    && t.RemoteWorktreePath != null,
                ct);
            occupied = sessions + pending + (prep?.InFlightCount(id, Guid.Empty) ?? 0);
        }

        var name = described?.DisplayName ?? id;
        var state = directory.DrainState(id);
        return Row(described, id, name, "sessions", capacity, occupied, observed,
            state is { Draining: true }, state?.RetiredAt is not null);
    }

    private static SessionRunnerCatalogueEntryDto Row(
        RunnerDescriptor? described,
        string id,
        string name,
        string capacityKind,
        int? capacity,
        int? occupied,
        bool current,
        bool draining,
        bool retired)
    {
        var available = described?.Available == true;
        var eligible = described?.DispatchEligible == true;
        string? reason = eligible && draining
            ? "draining"
            : eligible ? null : described?.Stale == true ? "stale" : "unavailable";
        return new SessionRunnerCatalogueEntryDto(
            id,
            name,
            described?.Platform,
            described?.PlatformObservedAt,
            available,
            eligible,
            reason,
            current ? capacity : null,
            current ? occupied : null,
            capacityKind,
            current ? DateTimeOffset.UtcNow : null,
            !current || described?.Stale == true,
            described?.Capabilities?.Features ?? [],
            draining,
            eligible && !draining && !retired);
    }
}
