using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable seat reconciliation only. Provider work stays in the existing launch/qualification owners.</summary>
public sealed class StandingSpecialistSeatService(
    AppDbContext db, IOptions<DelegationSettings> options, TimeProvider time,
    AgentSessionRuntime runtime, AgentSessionService sessions)
{
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var owners = await db.Agents.AsNoTracking()
            .Where(StandingSpecialistSeatPolicy.OwnerOrSlug(settings)).ToListAsync(ct);
        foreach (var owner in owners)
            await ReconcileOwnerAsync(owner.Id, ct);

        // Removal/deletion stops admission immediately via the typed start guard. Drain first:
        // a busy or task-owned process remains owned, even when its logical owner was removed.
        var retained = await db.Agents.AsNoTracking()
            .Where(StandingSpecialistSeatPolicy.Alternate).ToListAsync(ct);
        foreach (var seat in retained)
        {
            if (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, seat, settings, true, ct) is null)
                continue;
            // Capability/hold/quarantine refusal must never kill a seat. Only explicit lifecycle
            // removal/disable (including missing owner) retires an otherwise idle process.
            var owner = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == seat.StandingSpecialistOwnerId, ct);
            var enabled = settings.Enabled && settings.CheckEnabled && settings.CheckInterpreterEnabled
                && owner is not null && owner.AlwaysOn
                && await db.StandingSpecialistRoutings.AnyAsync(r => r.AgentId == owner.Id && r.Enabled, ct)
                && await db.StandingSpecialistCandidateStates.AnyAsync(c => c.PhysicalAgentId == seat.Id && c.Enabled, ct);
            if (enabled) continue;
            await CancelUnclaimedAsync(seat.Id, ct);
            if (!Guid.TryParse(seat.PersistentSessionId, out var sessionId)) continue;
            if (await HasOwnedWorkAsync(seat.Id, ct)) continue;
            await runtime.CatchUpTranscriptAsync(sessionId, ct);
            if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)) continue;
            if (await db.AgentSessions.AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct))
                await sessions.KillAsync(sessionId, SessionTerminationSource.SystemRequest, ct);
        }
    }

    public async Task ReconcileOwnerAsync(Guid ownerId, CancellationToken ct)
    {
        var directories = new List<string>();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var owner = await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (owner is null || !StandingSpecialistSeatPolicy.IsCheck(owner, options.Value)) return;
            owner.StandingSpecialistOwnerId = owner.Id;
            owner.StandingSpecialistRole = StandingSpecialistSeatPolicy.Role;
            var now = time.GetUtcNow().UtcDateTime;
            var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == ownerId, ct);
            var pairs = routing is null ? new[] { new RoutingCandidate(owner.Kind, owner.ModelLevel) }
                : RoutingCandidate.Parse(routing.CandidatesJson).ToArray();
            var states = await db.StandingSpecialistCandidateStates.Where(c => c.AgentId == ownerId).ToListAsync(ct);
            // Do not guess a replacement head after an identity edit. The declaration owner
            // requires an explicit matching head; retained evidence describes the old pair.
            var validHead = pairs.Length > 0 && pairs[0] == new RoutingCandidate(owner.Kind, owner.ModelLevel);
            var enabled = options.Value.Enabled && options.Value.CheckEnabled && options.Value.CheckInterpreterEnabled
                && owner.AlwaysOn && !owner.IsPoolDelegate && routing?.Enabled != false && validHead;
            foreach (var state in states)
            {
                if (!enabled || !pairs.Contains(new(state.AgentKind, state.ModelLevel)))
                {
                    state.Enabled = false;
                    state.Status = StandingSpecialistCandidateStatus.Disabled;
                    state.Reason = validHead ? "Check service or candidate is disabled." : "The declared head no longer matches the primary; update routing.";
                    state.QualifiedAt = null;
                }
            }
            await db.SaveChangesAsync(ct);
            for (var index = 0; index < pairs.Length && validHead; index++)
            {
                var pair = pairs[index];
                var state = states.SingleOrDefault(c => c.AgentKind == pair.AgentKind && c.ModelLevel == pair.ModelLevel);
                if (state is null)
                {
                    state = new StandingSpecialistCandidateState
                    {
                        Id = Guid.NewGuid(), AgentId = ownerId, AgentKind = pair.AgentKind!.Value,
                        ModelLevel = pair.ModelLevel!.Value, Enabled = enabled, DeclaredAt = now,
                        ModelAlias = DispatchModelAlias.Resolve(pair.AgentKind.Value, pair.ModelLevel.Value, index == 0 ? owner.ModelId : null),
                        Status = enabled ? StandingSpecialistCandidateStatus.Unqualified : StandingSpecialistCandidateStatus.Disabled,
                        QualificationAuthorization = Guid.NewGuid(), UpdatedAt = now,
                    };
                    db.StandingSpecialistCandidateStates.Add(state);
                    states.Add(state);
                }
                if (!enabled) continue;
                if (!state.Enabled)
                {
                    state.Enabled = true;
                    state.QualifiedAt = null;
                    state.Status = StandingSpecialistCandidateStatus.Unqualified;
                    state.QualificationAuthorization = Guid.NewGuid();
                }
                if (pair.AgentKind == AgentKind.Codex)
                {
                    state.Status = StandingSpecialistCandidateStatus.PendingDependency;
                    state.Reason = "Pending CARD-0167 agent-path injection and CARD-0415 capability certification.";
                    continue;
                }
                if (index == 0) state.PhysicalAgentId = owner.Id;
                else if (state.PhysicalAgentId is null)
                {
                    var id = Guid.NewGuid();
                    var cwd = Path.Combine(Path.GetTempPath(), "antiphon", "standing-specialists", ownerId.ToString("N"), state.Id.ToString("N"));
                    db.Agents.Add(new Agent
                    {
                        Id = id, Name = $"Check {pair.AgentKind}/{pair.ModelLevel}", Slug = "check-seat-" + state.Id.ToString("N"),
                        WorkingDirectory = cwd, Kind = pair.AgentKind!.Value, ModelLevel = pair.ModelLevel!.Value,
                        StandingSpecialistOwnerId = ownerId, StandingSpecialistRole = StandingSpecialistSeatPolicy.Role,
                        AlwaysOn = true, IsPoolDelegate = false, RemoteControlEnabled = false, AutoCompactEnabled = false,
                        SessionBackend = SessionBackend.PtyHost, SystemPromptAppend = CheckInterpretation.Contract,
                        Details = "Managed Check interpreter seat. Facts arrive inline; no tools.",
                        CreatedAt = now, UpdatedAt = now,
                    });
                    state.PhysicalAgentId = id;
                    state.UnprovisionedAt ??= now;
                    state.Status = StandingSpecialistCandidateStatus.DeclaredButUnprovisioned;
                    state.Reason = "Awaiting certified capability and standing-start admission; next eligibility unknown.";
                    directories.Add(cwd);
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        // No filesystem or process operation under the owner lock.
        foreach (var directory in directories) Directory.CreateDirectory(directory);
    }

    private Task<bool> HasOwnedWorkAsync(Guid seatId, CancellationToken ct) => db.AgentTasks.AnyAsync(t => t.AgentId == seatId
        && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct);

    private async Task CancelUnclaimedAsync(Guid seatId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.AgentTasks.Where(StandingSpecialistSeatPolicy.SeatWork)
            .Where(t => t.AgentId == seatId && t.Status == AgentTaskStatus.Queued)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                .SetProperty(t => t.CompletedAt, now).SetProperty(t => t.FailureReason, "Specialist candidate admission was disabled."), ct);
    }
}
