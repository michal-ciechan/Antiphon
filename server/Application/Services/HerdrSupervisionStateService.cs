using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Consumes durable attempt evidence under the agent row lock. No process operations.</summary>
public sealed class HerdrSupervisionStateService(
    AppDbContext db, IOptions<SupervisionSettings> settings, TimeProvider clock,
    AgentSessionLaunchQueue queue, IEventBus? eventBus = null)
{
    public const string HeldCode = "herdr_supervision_held";

    public async Task<AgentSupervisionState> ObserveAsync(
        Guid agentId, bool reset, bool observeRunning, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var agent = await db.Agents.FromSqlInterpolated(
                $"""SELECT * FROM "Agents" WHERE "Id" = {agentId} FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);
        var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId };
            db.AgentSupervisionStates.Add(state);
        }
        else
            await db.Entry(state).ReloadAsync(ct);

        // AsNoTracking is essential: both same-row resume and timestamp precision must be
        // compared using the persisted generation, never a caller's cached entity.
        var session = Guid.TryParse(agent.PersistentSessionId, out var sessionId)
            ? await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct) : null;
        var now = clock.GetUtcNow().UtcDateTime;
        var wasHeld = state.HerdrFailureHeldAt is not null;
        var eligible = agent.AlwaysOn && !agent.IsPoolDelegate && session is { CardId: null, SessionBackend: SessionBackend.Herdr };
        var live = session?.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping;
        if (eligible && (!wasHeld || reset) && !queue.Owns(session!.Id))
        {
            if (session.Status is SessionStatus.Failed or SessionStatus.Stopped
                && session.HerdrSupervisionFailureKind is { } evidence
                && (state.LastHerdrObservedSessionId != session.Id || state.LastHerdrObservedStartedAt != session.StartedAt))
            {
                state.LastHerdrObservedSessionId = session.Id;
                state.LastHerdrObservedStartedAt = session.StartedAt;
                state.LastHerdrFailureKind = evidence;
                state.HerdrConsecutiveFailures = evidence == HerdrSupervisionFailureKind.NonQualifying
                    ? 0 : state.HerdrConsecutiveFailures + 1;
                state.HerdrHealthySince = null;
                if (!reset && state.HerdrConsecutiveFailures >= settings.Value.HerdrFailureLimit)
                {
                    state.HerdrFailureHeldAt = now;
                    AddIncident(AgentIncidentKind.HerdrSupervisionHeld, AlertSeverity.Error,
                        $"{Headline(agent.Name, state, settings.Value.HerdrFailureLimit)}. Agent {agent.Id}; "
                        + $"attempt {session.Id}/{session.StartedAt:O}; POST start with resetHerdrFailureHold:true.",
                        session.Id.ToString("D"));
                }
            }

            if (observeRunning && session.Status == SessionStatus.Running)
            {
                state.HerdrHealthySince ??= now;
                if (now - state.HerdrHealthySince >= TimeSpan.FromMinutes(settings.Value.HealthyUptimeResetMinutes))
                    state.HerdrConsecutiveFailures = 0;
            }
            else if (session.Status != SessionStatus.Running)
                state.HerdrHealthySince = null;
        }

        // A live idempotent Start is not an acknowledgement, even with the flag.
        if (reset && !live)
        {
            if (wasHeld)
                AddIncident(AgentIncidentKind.HerdrSupervisionRetried, AlertSeverity.Info,
                    $"Herdr retries acknowledged for {agent.Name}; normal launch guards still apply.", null);
            state.HerdrFailureHeldAt = null;
            state.HerdrConsecutiveFailures = 0;
            state.HerdrHealthySince = null;
        }
        if (state.HerdrFailureHeldAt is not null)
        {
            state.NextRestartAt = null;
            state.HerdrHealthySince = null;
        }
        state.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            if (wasHeld != (state.HerdrFailureHeldAt is not null) && eventBus is not null)
                await eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
        }
        return state;

        void AddIncident(AgentIncidentKind kind, AlertSeverity severity, string message, string? reason) =>
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(), AgentId = agentId, SessionId = state.LastHerdrObservedSessionId,
                Kind = kind, Severity = severity, CreatedAt = now,
                Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength), FailureReason = reason,
            });
    }

    public static string Headline(string name, AgentSupervisionState state, int limit) =>
        $"Herdr retries paused for {name}: {state.HerdrConsecutiveFailures} of {limit} attempts failed "
        + $"({state.LastHerdrFailureKind}); inspect, fix, then Retry and resume";

    public string HeldMessage(string name, AgentSupervisionState state) =>
        Headline(name, state, settings.Value.HerdrFailureLimit) + "; explicitly send resetHerdrFailureHold:true.";
}
