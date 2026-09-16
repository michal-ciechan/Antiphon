using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Pull replayable runner snapshots; only this transaction may follow existing pins.</summary>
public sealed class HerdrLabelFollowService(AppDbContext db, ISessionRunnerClient runner,
    IEventBus events, TimeProvider clock, IOptions<HerdrLabelFollowSettings> options, ILogger<HerdrLabelFollowService> logger)
{
    internal Func<string, CancellationToken, Task>? Boundary { get; set; }
    internal int ConditionalWrites { get; private set; }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        options.Value.Validate();
        if (!options.Value.Enabled) return 0;
        var ids = await db.Agents.AsNoTracking().Where(a => !a.IsPoolDelegate && a.SessionBackend == SessionBackend.Herdr
            && a.PersistentSessionId != null && (a.HerdrTabLabel != null || a.HerdrWorkspaceLabel != null)
            && db.AgentSessions.Any(s => s.Id.ToString() == a.PersistentSessionId && s.StandingAgentId == a.Id
                && s.CardId == null && s.SessionBackend == SessionBackend.Herdr && s.EndedAt == null
                && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running)))
            .OrderBy(a => a.Id).Select(a => a.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await FollowAsync(id, ct)) changed++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogDebug("Herdr label sweep skipped {AgentId}: {Failure}", id, ex.GetType().Name); }
        }
        return changed;
    }

    public async Task<bool> FollowAsync(Guid agentId, CancellationToken ct)
    {
        var pointer = await db.Agents.AsNoTracking().Where(a => a.Id == agentId).Select(a => a.PersistentSessionId).SingleOrDefaultAsync(ct);
        if (!Guid.TryParse(pointer, out var sessionId)) return false;
        var dto = await runner.GetAsync(sessionId, ct); // No transaction or row lock during network I/O.
        if (Boundary is not null) await Boundary("after-response", ct);
        return await ApplyAsync(agentId, dto, ct);
    }

    internal async Task<bool> ApplyAsync(Guid agentId, SessionRunnerSessionDto dto, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (dto.LabelObservation is not { Version: 1, Intent.Version: 1, Sequence: > 0, PositivelyVerified: true } o
            || dto.SessionId != o.SessionId || !SessionGeneration.Equal(dto.AcceptedStartedAt, o.AcceptedStartedAt)
            || dto.HerdrOrigin != HerdrPaneOrigins.Launched || o.Origin != HerdrPaneOrigins.Launched
            || dto.Backend != SessionBackends.Herdr || dto.Status != "Running" || dto.Pending is not null
            || o.CompletedAtUtc > now.AddSeconds(30) || o.ExpiresAtUtc <= now
            || o.CompletedAtUtc >= o.ExpiresAtUtc || string.IsNullOrWhiteSpace(o.ResultCode)
            || o.ResultCode == "in-progress" || string.IsNullOrWhiteSpace(o.PaneId)
            || string.IsNullOrWhiteSpace(o.TabId) || string.IsNullOrWhiteSpace(o.WorkspaceId)) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (Boundary is not null) await Boundary("before-lock", ct);
        var agent = await HerdrPlacementLock.LoadAsync(db, agentId, ct);
        if (Boundary is not null) await Boundary("after-reload", ct);
        if (agent is null || agent.IsPoolDelegate || agent.SessionBackend != SessionBackend.Herdr
            || !Guid.TryParse(agent.PersistentSessionId, out var currentId) || currentId != o.SessionId
            || o.Intent.StandingAgentId != agent.Id || o.Intent.PlacementEditToken != agent.HerdrPlacementEditToken) return false;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == o.SessionId, ct);
        if (session is null || session.StandingAgentId != agent.Id || session.CardId is not null
            || session.SessionBackend != SessionBackend.Herdr || session.EndedAt is not null
            || session.Status is not (SessionStatus.Starting or SessionStatus.Running)
            || !SessionGeneration.Equal(session.StartedAt, o.AcceptedStartedAt)) return false;
        if (agent.HerdrLabelFollowSessionId == o.SessionId && SessionGeneration.Equal(agent.HerdrLabelFollowStartedAt, o.AcceptedStartedAt)
            && o.Sequence <= agent.HerdrLabelFollowSequence) return false;

        var tab = agent.HerdrTabLabel;
        var workspace = agent.HerdrWorkspaceLabel;
        if (o.ResultCode == "validated")
        {
            if (tab is not null && o.Intent.TabLabel is not null && ValidLabel(o.TabLabel)) tab = o.TabLabel;
            if (workspace is not null && o.Intent.WorkspaceLabel is not null && ValidLabel(o.WorkspaceLabel)) workspace = o.WorkspaceLabel;
        }
        var changed = tab != agent.HerdrTabLabel || workspace != agent.HerdrWorkspaceLabel;
        var updatedAt = changed ? now : agent.UpdatedAt;
        var generation = SessionGeneration.Normalize(o.AcceptedStartedAt);
        var pointer = agent.PersistentSessionId;
        if (Boundary is not null) await Boundary("before-write", ct);
        ConditionalWrites++;
        var rows = await db.Agents.Where(a => a.Id == agentId && a.PersistentSessionId == pointer
            && a.HerdrPlacementEditToken == o.Intent.PlacementEditToken
            && db.AgentSessions.Any(s => s.Id == o.SessionId && s.StartedAt == generation && s.StandingAgentId == agentId
                && s.CardId == null && s.SessionBackend == SessionBackend.Herdr && s.EndedAt == null
                && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running)))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.HerdrTabLabel, tab)
                .SetProperty(a => a.HerdrWorkspaceLabel, workspace).SetProperty(a => a.UpdatedAt, updatedAt)
                .SetProperty(a => a.HerdrLabelFollowSessionId, o.SessionId)
                .SetProperty(a => a.HerdrLabelFollowStartedAt, generation)
                .SetProperty(a => a.HerdrLabelFollowSequence, o.Sequence), ct);
        if (rows != 1) return false;
        if (Boundary is not null) await Boundary("before-commit", ct);
        await transaction.CommitAsync(ct);
        await db.Entry(agent).ReloadAsync(ct);
        if (changed)
        {
            if (Boundary is not null) await Boundary("after-commit", ct);
            await events.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
        }
        logger.LogDebug("Herdr label follow {AgentId} {SessionId} {Generation} {Sequence}: {Code}",
            agentId, o.SessionId, generation, o.Sequence, o.ResultCode);
        return changed;
    }

    private static bool ValidLabel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256
        && !value.Any(char.IsControl) && value == value.Trim();
}
