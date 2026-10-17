using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1320. Reads the standing-orchestrator population and stores the Human toggle, hold, and ack.
/// Turning the toggle off cancels a still-pending keep-six note, including one that has
/// already failed a typing attempt. This service does not dispatch.
/// </summary>
public sealed class KeepSixService(
    AppDbContext db,
    TimeProvider clock,
    OrchestratorInstructionsService instructions,
    KeepSixShortfall shortfall,
    SessionMessageQueueService messages)
{
    public const string HumanProvenance = "Human";

    public async Task<IReadOnlyList<KeepSixOrchestratorDto>> ListAsync(CancellationToken ct)
    {
        var agents = await LoadPopulationAsync(ct).ConfigureAwait(false);
        var stored = await LoadRowsAsync(agents, ct).ConfigureAwait(false);
        var counts = await shortfall.ReadAsync(agents, stored, ct).ConfigureAwait(false);
        return agents
            .OrderBy(agent => agent.Name, StringComparer.Ordinal)
            .ThenBy(agent => agent.Id)
            .Select(agent => ToDto(agent, stored.GetValueOrDefault(agent.Id), counts[agent.Id]))
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

        if (!request.Enabled)
            await CancelPendingReminderAsync(agent, ct).ConfigureAwait(false);

        instructions.Signal("keep-six " + agent.Name);
        var counts = await shortfall.ReadAsync([agent], new Dictionary<Guid, KeepSixOrchestrator>
        {
            [agent.Id] = row,
        }, ct).ConfigureAwait(false);
        return ToDto(agent, row, counts[agent.Id]);
    }

    public async Task<IReadOnlyList<Agent>> ListStandingOrchestratorsAsync(CancellationToken ct) =>
        await LoadPopulationAsync(ct).ConfigureAwait(false);

    public async Task<KeepSixOrchestratorDto> HoldAsync(
        Guid agentId, HoldKeepSixRequest request, string? token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length is < 1 or > KeepSixOrchestrator.HoldReasonMaxLength)
            throw new BadRequestException(
                $"reason is required and must be at most {KeepSixOrchestrator.HoldReasonMaxLength} characters.");
        var now = clock.GetUtcNow();
        var until = request.Until.ToUniversalTime();
        if (until <= now || until > now.AddDays(KeepSixOrchestrator.HoldMaxDays))
            throw new BadRequestException("until must be in the future and at most 7 days ahead.");

        var agent = await RequireAgentAsync(agentId, ct).ConfigureAwait(false);
        await EnsureWriterAsync(agent, token, ct).ConfigureAwait(false);
        var row = await RequireRowAsync(agentId, ct).ConfigureAwait(false);
        row.HoldReason = reason;
        row.HoldUntil = until.UtcDateTime;
        row.Revision += 1;
        row.UpdatedAt = now.UtcDateTime;
        await SaveRowAsync(ct).ConfigureAwait(false);
        instructions.Signal("keep-six hold " + agent.Name);
        return await ReadBackAsync(agent, row, ct).ConfigureAwait(false);
    }

    public async Task<KeepSixOrchestratorDto> ClearHoldAsync(Guid agentId, string? token, CancellationToken ct)
    {
        var agent = await RequireAgentAsync(agentId, ct).ConfigureAwait(false);
        await EnsureWriterAsync(agent, token, ct).ConfigureAwait(false);
        var row = await RequireRowAsync(agentId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        row.HoldReason = null;
        row.HoldUntil = null;
        row.Revision += 1;
        row.UpdatedAt = now;
        await SaveRowAsync(ct).ConfigureAwait(false);
        instructions.Signal("keep-six hold cleared " + agent.Name);
        return await ReadBackAsync(agent, row, ct).ConfigureAwait(false);
    }

    public async Task<KeepSixOrchestratorDto> AckAsync(Guid agentId, string? token, CancellationToken ct)
    {
        var agent = await RequireAgentAsync(agentId, ct).ConfigureAwait(false);
        await EnsureWriterAsync(agent, token, ct).ConfigureAwait(false);
        var row = await RequireRowAsync(agentId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        row.LastAcknowledgedAt = now;
        row.Revision += 1;
        row.UpdatedAt = now;
        await SaveRowAsync(ct).ConfigureAwait(false);
        return await ReadBackAsync(agent, row, ct).ConfigureAwait(false);
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

    private async Task<Agent> RequireAgentAsync(Guid agentId, CancellationToken ct)
    {
        var agents = await LoadPopulationAsync(ct).ConfigureAwait(false);
        return agents.SingleOrDefault(candidate => candidate.Id == agentId)
            ?? throw new NotFoundException("KeepSixOrchestrator", agentId);
    }

    private async Task<KeepSixOrchestrator> RequireRowAsync(Guid agentId, CancellationToken ct) =>
        await db.KeepSixOrchestrators
            .SingleOrDefaultAsync(candidate => candidate.AgentId == agentId, ct)
            .ConfigureAwait(false)
        ?? throw new NotFoundException("KeepSixOrchestrator", agentId);

    private async Task EnsureWriterAsync(Agent agent, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var hash = AgentTaskService.HashToken(token.Trim());
        var taskMatch = await db.AgentTasks.AsNoTracking()
            .AnyAsync(task => task.AgentId == agent.Id && task.TokenHash == hash, ct)
            .ConfigureAwait(false);
        if (taskMatch)
            return;

        var standingMatch = await db.AgentSessions.AsNoTracking()
            .AnyAsync(session => session.DelegationTokenHash == hash && session.StandingAgentId == agent.Id, ct)
            .ConfigureAwait(false);
        if (standingMatch)
            return;
        if (Guid.TryParse(agent.PersistentSessionId, out var persistentId))
        {
            var persistentMatch = await db.AgentSessions.AsNoTracking()
                .AnyAsync(session => session.DelegationTokenHash == hash && session.Id == persistentId, ct)
                .ConfigureAwait(false);
            if (persistentMatch)
                return;
        }

        throw new ForbiddenException("This token is not this orchestrator's.", "token_agent");
    }

    private async Task SaveRowAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("Keep-six revision changed during the write.", ex, "keep_six_revision");
        }
    }

    private async Task<KeepSixOrchestratorDto> ReadBackAsync(
        Agent agent, KeepSixOrchestrator row, CancellationToken ct)
    {
        var counts = await shortfall.ReadAsync([agent], new Dictionary<Guid, KeepSixOrchestrator>
        {
            [agent.Id] = row,
        }, ct).ConfigureAwait(false);
        return ToDto(agent, row, counts[agent.Id]);
    }

    private async Task CancelPendingReminderAsync(Agent agent, CancellationToken ct)
    {
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return;
        var pending = await db.SessionQueuedMessages.AsNoTracking()
            .Where(message => message.AgentSessionId == sessionId
                && message.NoteHeader == KeepSixReminderText.NoteHeader
                && message.Status == QueuedMessageStatus.Pending)
            .Select(message => message.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var messageId in pending)
        {
            try
            {
                // Pending covers a never-typed row and a row that failed a typing attempt
                // and was reverted. Untyped-only cancel leaves the second one in the queue.
                await messages.CancelAsync(sessionId, messageId, ct).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                // The row left the queue between the read and the locked cancel.
            }
        }
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

    private static KeepSixOrchestratorDto ToDto(
        Agent agent, KeepSixOrchestrator? row, KeepSixShortfall.Counts? counts = null) => new(
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
        counts?.CountedOpen,
        counts?.EffectiveTarget,
        counts?.DispatchableBacklog,
        counts?.Shortfall,
        counts?.TopCards,
        counts?.LastDispatchAt,
        counts?.LastCompletionAt,
        counts?.Badge);

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
