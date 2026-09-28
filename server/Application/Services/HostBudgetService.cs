using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Live, persisted admission budgets for the desktop and configured remote runners.</summary>
public sealed class HostBudgetService(
    AppDbContext db, ISessionRunnerDirectory runners, IOptions<DelegationSettings> settings,
    TimeProvider clock)
{
    public IReadOnlyList<string> KnownHostIds =>
        new[] { "local" }.Concat(runners.KnownRunnerIds.Where(id => id != "local"))
            .Distinct(StringComparer.Ordinal).ToArray();

    public async Task<IReadOnlyList<HostLimit>> ListAsync(CancellationToken ct)
    {
        var rows = await db.HostBudgets.AsNoTracking().ToDictionaryAsync(b => b.HostId, ct);
        return KnownHostIds.Select(id => Limit(id, rows.GetValueOrDefault(id))).ToArray();
    }

    public async Task<HostLimit> EffectiveAsync(string hostId, CancellationToken ct)
    {
        RequireKnown(hostId);
        var row = await db.HostBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.HostId == hostId, ct);
        return Limit(hostId, row);
    }

    public async Task<HostLimit> UpsertAsync(string hostId, int? maxInFlight, string? reason, CancellationToken ct)
    {
        RequireKnown(hostId);
        if (maxInFlight is < 0 or > 512)
            throw new ValidationException("maxInFlight", "Must be between 0 and 512, or null to clear.");
        var why = reason?.Trim() ?? "";
        if (why.Length is < 1 or > 400)
            throw new ValidationException("reason", "A reason of 1 to 400 characters is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('antiphon-host-budget'))", cancellationToken: ct);
        var row = await db.HostBudgets.FirstOrDefaultAsync(b => b.HostId == hostId, ct);
        var old = row?.MaxInFlight;
        if (row is null)
        {
            row = new HostBudget { HostId = hostId };
            db.HostBudgets.Add(row);
        }
        row.MaxInFlight = maxInFlight;
        row.Reason = why;
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        row.Revision++;
        var result = Limit(hostId, row);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(), Kind = AgentIncidentKind.HostBudgetChanged,
            Severity = AlertSeverity.Info, CreatedAt = row.UpdatedAt,
            Message = $"Host '{hostId}' budget {Describe(old)} -> {Describe(maxInFlight)} " +
                $"(effective {Describe(result.Effective)}, runner declares {Describe(result.Declared)}): {why}"
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private HostLimit Limit(string id, HostBudget? row)
    {
        var declared = id == "local" ? null : runners.DeclaredCapacity(id);
        var configured = row?.MaxInFlight;
        var effective = id == "local"
            ? configured ?? settings.Value.MaxConcurrentTasks
            : configured is null ? declared : declared is null ? configured : Math.Min(configured.Value, declared.Value);
        return new HostLimit(id, configured, declared, effective,
            configured is not null ? "budget" : id == "local" ? "config" : "runner",
            row?.Reason, row?.UpdatedAt, row?.Revision ?? 0);
    }

    private void RequireKnown(string hostId)
    {
        if (!KnownHostIds.Contains(hostId, StringComparer.Ordinal))
            throw new NotFoundException("Host", hostId);
    }

    private static string Describe(int? value) => value?.ToString() ?? "default";
}
