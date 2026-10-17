using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1320 S1. Reads the standing-orchestrator population and stores a Human toggle.
/// Shortfall and reminders are later slices; this service does not enqueue or dispatch.
/// </summary>
public sealed class KeepSixService(
    AppDbContext db,
    TimeProvider clock,
    OrchestratorInstructionsService instructions)
{
    public const string HumanProvenance = "Human";

    public async Task<IReadOnlyList<KeepSixOrchestratorDto>> ListAsync(CancellationToken ct)
    {
        var agents = await LoadPopulationAsync(ct).ConfigureAwait(false);
        var stored = await LoadRowsAsync(agents, ct).ConfigureAwait(false);
        return agents
            .OrderBy(agent => agent.Name, StringComparer.Ordinal)
            .ThenBy(agent => agent.Id)
            .Select(agent => ToDto(agent, stored.GetValueOrDefault(agent.Id)))
            .ToArray();
    }

    public async Task<IReadOnlyList<KeepSixInstructionRow>> ReadInstructionRowsAsync(CancellationToken ct)
    {
        var agents = await LoadPopulationAsync(ct).ConfigureAwait(false);
        var stored = await LoadRowsAsync(agents, ct).ConfigureAwait(false);
        return agents
            .OrderBy(agent => agent.Name, StringComparer.Ordinal)
            .ThenBy(agent => agent.Id)
            .Select(agent =>
            {
                stored.TryGetValue(agent.Id, out var row);
                return new KeepSixInstructionRow(
                    agent.Id,
                    agent.Name,
                    row?.Enabled ?? false,
                    row?.Target ?? KeepSixOrchestrator.DefaultTarget,
                    string.IsNullOrEmpty(row?.HoldReason) ? null : row!.HoldReason,
                    FormatInstant(row?.HoldUntil));
            })
            .ToArray();
    }

    public async Task<KeepSixOrchestratorDto> PutAsync(Guid agentId, PutKeepSixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var agents = await LoadPopulationAsync(ct).ConfigureAwait(false);
        var agent = agents.SingleOrDefault(candidate => candidate.Id == agentId);
        if (agent is null)
            throw new NotFoundException("KeepSixOrchestrator", agentId);

        var row = await db.KeepSixOrchestrators
            .SingleOrDefaultAsync(candidate => candidate.AgentId == agentId, ct)
            .ConfigureAwait(false);
        var current = row?.Revision ?? 0;
        if (current != request.ExpectedRevision)
        {
            throw new ConflictException(
                $"Keep-six revision is {current}, not {request.ExpectedRevision}.",
                "keep_six_revision");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var turningOn = request.Enabled && (row is null || !row.Enabled);
        if (row is null)
        {
            row = new KeepSixOrchestrator { AgentId = agentId };
            db.KeepSixOrchestrators.Add(row);
        }

        row.Enabled = request.Enabled;
        row.Target = request.Target;
        row.Provenance = HumanProvenance;
        row.Reason = request.Reason.Trim();
        row.Revision = current + 1;
        row.UpdatedAt = now;
        if (turningOn)
            row.EnabledAt = now;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                $"Keep-six revision is no longer {request.ExpectedRevision}.",
                ex,
                "keep_six_revision");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException(
                $"Keep-six revision is no longer {request.ExpectedRevision}.",
                ex,
                "keep_six_revision");
        }

        instructions.Signal("keep-six " + agent.Name);
        return ToDto(agent, row);
    }

    private async Task<List<Agent>> LoadPopulationAsync(CancellationToken ct)
    {
        var bundleIds = db.AgentBundleAttachments.AsNoTracking()
            .Where(attachment => attachment.BundleKey == InstructionBundles.Orchestrator)
            .Select(attachment => attachment.AgentId);
        return await db.Agents.AsNoTracking()
            .Where(agent => bundleIds.Contains(agent.Id) && !agent.IsPoolDelegate)
            .Where(agent => agent.StandingSpecialistRole == null
                || agent.StandingSpecialistRole != StandingSpecialistSeatPolicy.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, KeepSixOrchestrator>> LoadRowsAsync(
        IReadOnlyList<Agent> agents, CancellationToken ct)
    {
        var ids = agents.Select(agent => agent.Id).ToArray();
        if (ids.Length == 0)
            return [];
        var rows = await db.KeepSixOrchestrators.AsNoTracking()
            .Where(row => ids.Contains(row.AgentId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.AgentId);
    }

    private static void Validate(PutKeepSixRequest request)
    {
        if (!string.Equals(request.Provenance, HumanProvenance, StringComparison.Ordinal))
            throw new BadRequestException("provenance must be Human.");
        if (request.Target < KeepSixOrchestrator.MinTarget || request.Target > KeepSixOrchestrator.MaxTarget)
            throw new BadRequestException(
                $"target must be from {KeepSixOrchestrator.MinTarget} to {KeepSixOrchestrator.MaxTarget}.");
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > KeepSixOrchestrator.ReasonMaxLength)
            throw new BadRequestException(
                $"reason is required and must be at most {KeepSixOrchestrator.ReasonMaxLength} characters.");
        if (request.ExpectedRevision < 0)
            throw new BadRequestException("expectedRevision must be zero or greater.");
    }

    private static KeepSixOrchestratorDto ToDto(Agent agent, KeepSixOrchestrator? row) => new(
        agent.Id,
        agent.Name,
        row?.Enabled ?? false,
        row?.Target ?? KeepSixOrchestrator.DefaultTarget,
        row?.Revision ?? 0,
        row?.Reason,
        row?.Provenance,
        Instant(row?.UpdatedAt),
        Instant(row?.EnabledAt),
        string.IsNullOrEmpty(row?.HoldReason) ? null : row!.HoldReason,
        Instant(row?.HoldUntil),
        Instant(row?.LastReminderEnqueuedAt),
        row?.OutstandingMessageId,
        Instant(row?.LastAcknowledgedAt),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private static DateTimeOffset? Instant(DateTime? value) =>
        value is DateTime instant
            ? new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc))
            : null;

    private static string? FormatInstant(DateTime? value) =>
        value is DateTime instant
            ? DateTime.SpecifyKind(instant, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")
            : null;

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }
}
