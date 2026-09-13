using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class AgentPinnedInstructionService
{
    public const string RevisionConflict = "pin_revision_conflict";
    public const string SourceConflict = "pin_source_conflict";
    public const string RequestConflict = "pin_request_conflict";

    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IAgentPinnedInstructionReconciler _reconciler;

    public AgentPinnedInstructionService(
        AppDbContext db,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IAgentPinnedInstructionReconciler reconciler)
    {
        _db = db;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _reconciler = reconciler;
    }

    public async Task<PinnedInstructionSetDto> GetAsync(
        Guid agentId,
        PinPrincipal principal,
        bool includeRevoked,
        CancellationToken ct)
    {
        var agent = await RequireNamedAgentAsync(agentId, ct);
        AuthorizeAgentAccess(agent, principal);
        return await LoadSetAsync(agent, principal.SessionId, includeRevoked, ct);
    }

    public async Task<PinnedInstructionMutationResult> CaptureAsync(
        Guid agentId,
        CapturePinnedInstructionRequest request,
        PinPrincipal principal,
        CancellationToken ct)
    {
        var agent = await RequireNamedAgentAsync(agentId, ct);
        AuthorizeAgentAccess(agent, principal);
        RejectForgedCallerFields(request, principal);

        var text = AgentPinnedInstructionText.NormalizeAndValidate(request.Text);
        var sourceNamespace = AgentPinnedInstructionText.NormalizeOptional(
            request.SourceNamespace, "sourceNamespace", AgentPinnedInstruction.MaxSourceNamespaceLength);
        var sourceKey = AgentPinnedInstructionText.NormalizeOptional(
            request.SourceKey, "sourceKey", AgentPinnedInstruction.MaxSourceKeyLength);
        var sourceRef = AgentPinnedInstructionText.NormalizeOptional(
            request.SourceRef, "sourceRef", AgentPinnedInstruction.MaxSourceRefLength);
        if ((sourceNamespace is null) != (sourceKey is null))
            throw new ValidationException("sourceKey", "sourceNamespace and sourceKey must be supplied together.");

        var kind = request.RepinsPinId is not null
            ? PinOperationKind.Repin
            : request.ReplacesPinId is not null
                ? PinOperationKind.Replace
                : PinOperationKind.Capture;
        var fingerprint = Fingerprint(
            kind,
            text,
            sourceNamespace,
            sourceKey,
            sourceRef,
            request.ReplacesPinId,
            request.RepinsPinId);

        if (await TryReplayAsync(agentId, request.RequestId, fingerprint, principal, ct) is { } replay)
            return replay;

        var now = UtcNow();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockAgentAsync(agentId, ct);
        agent = await _db.Agents.SingleAsync(a => a.Id == agentId, ct);

        var state = await GetOrCreateStateForUpdateAsync(agent, now, ct);
        if (state.Revision != request.ExpectedRevision)
            throw new ConflictException("Pinned instruction revision does not match.", RevisionConflict);

        var active = await _db.AgentPinnedInstructions
            .Where(p => p.AgentId == agentId && p.RevokedAt == null)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        AgentPinnedInstruction? superseded = null;
        if (request.ReplacesPinId is { } replaceId)
        {
            superseded = active.SingleOrDefault(p => p.Id == replaceId)
                ?? throw NotFoundPin(replaceId);
            AssertCanMutate(superseded, principal);
            if (sourceNamespace is not null
                && (superseded.SourceNamespace != sourceNamespace || superseded.SourceKey != sourceKey))
            {
                throw new ConflictException(
                    "Replacement must keep the existing source key.",
                    SourceConflict);
            }
        }
        else if (request.RepinsPinId is { } repinId)
        {
            var revoked = await _db.AgentPinnedInstructions
                .SingleOrDefaultAsync(p => p.Id == repinId && p.AgentId == agentId, ct)
                ?? throw NotFoundPin(repinId);
            if (revoked.RevokedAt is null)
                throw new ConflictException("Only a revoked pin can be re-pinned.", SourceConflict);
            AssertCanMutate(revoked, principal);
            superseded = revoked;
            if (active.Count >= AgentPinnedInstruction.MaxActivePerAgent)
                throw new ValidationException("pins", $"At most {AgentPinnedInstruction.MaxActivePerAgent} active pins are allowed.");
        }
        else if (sourceNamespace is not null)
        {
            var sameSource = active.SingleOrDefault(p =>
                p.SourceNamespace == sourceNamespace && p.SourceKey == sourceKey);
            if (sameSource is not null)
            {
                if (sameSource.Text == text)
                {
                    await tx.CommitAsync(ct);
                    return new PinnedInstructionMutationResult(
                        await LoadSetAsync(agent, principal.SessionId, includeRevoked: false, ct),
                        CreatedNewRow: false);
                }

                throw new ConflictException(
                    "An active pin already uses this source key; replace it explicitly.",
                    SourceConflict);
            }

            var revokedSource = await _db.AgentPinnedInstructions
                .Where(p => p.AgentId == agentId
                    && p.SourceNamespace == sourceNamespace
                    && p.SourceKey == sourceKey
                    && p.RevokedAt != null)
                .OrderByDescending(p => p.RevokedAt)
                .FirstOrDefaultAsync(ct);
            if (revokedSource is not null)
            {
                throw new ConflictException(
                    "A revoked source cannot be resurrected except by explicit re-pin.",
                    SourceConflict);
            }
        }

        if (superseded is null && active.Count >= AgentPinnedInstruction.MaxActivePerAgent)
            throw new ValidationException("pins", $"At most {AgentPinnedInstruction.MaxActivePerAgent} active pins are allowed.");

        if (superseded is { RevokedAt: null })
        {
            superseded.RevokedAt = now;
            superseded.RevokedByUserId = principal.UserId;
            superseded.RevokedBySessionId = principal.SessionId;
            active.Remove(superseded);
        }

        var pin = new AgentPinnedInstruction
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Text = text,
            Source = principal.Source,
            SourceNamespace = sourceNamespace ?? superseded?.SourceNamespace,
            SourceKey = sourceKey ?? superseded?.SourceKey,
            SourceRef = sourceRef,
            CreatedAt = now,
            CreatedByUserId = principal.UserId,
            CreatedBySessionId = principal.SessionId,
            SupersedesPinId = superseded?.Id
        };
        _db.AgentPinnedInstructions.Add(pin);
        active.Add(pin);

        var firstUse = state.Revision == 0;
        RotateState(state, active, now);
        await DirtyReconciliationAsync(agent, state, firstUse, now, ct);
        RecordOperation(agentId, request.RequestId, kind, fingerprint, pin.Id, superseded?.Id, state, createdNewRow: true, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await PublishAsync(agentId, state, ct);
        await _reconciler.ReconcileAfterCommitAsync(agentId, state.Revision, ct);
        return new PinnedInstructionMutationResult(
            await LoadSetAsync(agent, principal.SessionId, includeRevoked: false, ct),
            CreatedNewRow: true);
    }

    public async Task<PinnedInstructionSetDto> RevokeAsync(
        Guid agentId,
        Guid pinId,
        RevokePinnedInstructionRequest request,
        PinPrincipal principal,
        CancellationToken ct)
    {
        var agent = await RequireNamedAgentAsync(agentId, ct);
        AuthorizeAgentAccess(agent, principal);
        var fingerprint = Fingerprint(PinOperationKind.Revoke, pinId.ToString("N"), null, null, null, pinId, null);
        if (await TryReplayAsync(agentId, request.RequestId, fingerprint, principal, ct) is { } replay)
            return replay.Set;

        var now = UtcNow();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockAgentAsync(agentId, ct);
        agent = await _db.Agents.SingleAsync(a => a.Id == agentId, ct);

        var pin = await _db.AgentPinnedInstructions
            .SingleOrDefaultAsync(p => p.Id == pinId && p.AgentId == agentId, ct)
            ?? throw NotFoundPin(pinId);
        AssertCanMutate(pin, principal);

        var state = await GetOrCreateStateForUpdateAsync(agent, now, ct);
        if (state.Revision != request.ExpectedRevision)
            throw new ConflictException("Pinned instruction revision does not match.", RevisionConflict);

        if (pin.RevokedAt is not null)
        {
            await tx.CommitAsync(ct);
            return await LoadSetAsync(agent, principal.SessionId, includeRevoked: true, ct);
        }

        pin.RevokedAt = now;
        pin.RevokedByUserId = principal.UserId;
        pin.RevokedBySessionId = principal.SessionId;

        var active = await _db.AgentPinnedInstructions
            .Where(p => p.AgentId == agentId && p.RevokedAt == null && p.Id != pinId)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
        RotateState(state, active, now);
        await DirtyReconciliationAsync(agent, state, firstUse: false, now, ct);
        RecordOperation(agentId, request.RequestId, PinOperationKind.Revoke, fingerprint, pin.Id, pin.Id, state, createdNewRow: false, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await PublishAsync(agentId, state, ct);
        await _reconciler.ReconcileAfterCommitAsync(agentId, state.Revision, ct);
        return await LoadSetAsync(agent, principal.SessionId, includeRevoked: true, ct);
    }

    public async Task<PinnedInstructionSetDto> ReconcileAsync(
        Guid agentId,
        ReconcilePinnedInstructionsRequest request,
        PinPrincipal principal,
        CancellationToken ct)
    {
        if (!principal.IsOperator)
            throw new ForbiddenException("Only the operator can retry pin reconciliation.", "pin_caller_mismatch");

        var agent = await RequireNamedAgentAsync(agentId, ct);
        var state = await _db.AgentPinnedInstructionStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AgentId == agentId, ct);
        var revision = state?.Revision ?? 0;
        if (revision != request.ExpectedRevision)
            throw new ConflictException("Pinned instruction revision does not match.", RevisionConflict);

        if (state is not null)
            await _reconciler.ReconcileAfterCommitAsync(agentId, revision, ct);

        return await LoadSetAsync(agent, principal.SessionId, includeRevoked: false, ct);
    }

    public async Task<AgentPinProjection> EnsureLocationAsync(
        Guid agentId,
        string host,
        string cwd,
        bool configuredConsumer,
        CancellationToken ct)
    {
        var agent = await RequireNamedAgentAsync(agentId, ct);
        var now = UtcNow();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockAgentAsync(agentId, ct);
        var state = await _db.AgentPinnedInstructionStates
            .SingleOrDefaultAsync(s => s.AgentId == agentId, ct)
            ?? throw new ValidationException("pins", "Pins have not been used for this agent.");

        var projection = await EnsureLocationCoreAsync(
            agent,
            AgentPinPaths.CanonicalHost(host),
            AgentPinPaths.CanonicalCwd(cwd),
            configuredConsumer,
            state.Revision,
            now,
            ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return projection;
    }

    public async Task RecordProjectionWriteAsync(
        Guid projectionId,
        int revision,
        string? byteHash,
        CancellationToken ct)
    {
        var projection = await _db.AgentPinProjections
            .SingleOrDefaultAsync(p => p.Id == projectionId, ct)
            ?? throw new NotFoundException(nameof(AgentPinProjection), projectionId);
        projection.ProjectedRevision = revision;
        projection.LastWrittenByteHash = byteHash;
        projection.Status = PinProjectionStatus.Ready;
        projection.Error = null;
        projection.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
    }

    public async Task OnWorkingDirectoryChangedAsync(Agent agent, string previousCwd, CancellationToken ct)
    {
        var state = await _db.AgentPinnedInstructionStates
            .SingleOrDefaultAsync(s => s.AgentId == agent.Id, ct);
        if (state is null)
            return;

        var now = UtcNow();
        await EnsureLocationCoreAsync(
            agent,
            AgentPinPaths.DefaultHost,
            AgentPinPaths.CanonicalCwd(agent.WorkingDirectory),
            configuredConsumer: true,
            state.Revision,
            now,
            ct);
        await _db.SaveChangesAsync(ct);
        await _reconciler.ReconcileAfterCommitAsync(agent.Id, state.Revision, ct);
        _ = previousCwd;
    }

    public async Task PreserveCleanupOnDeleteAsync(Guid agentId, CancellationToken ct)
    {
        var projections = await _db.AgentPinProjections
            .Where(p => p.AgentId == agentId)
            .ToListAsync(ct);
        if (projections.Count == 0)
            return;

        var now = UtcNow();
        foreach (var projection in projections)
        {
            _db.AgentPinCleanupRecords.Add(new AgentPinCleanupRecord
            {
                Id = Guid.NewGuid(),
                OriginalAgentId = projection.AgentId,
                CanonicalHost = projection.CanonicalHost,
                CanonicalCwd = projection.CanonicalCwd,
                TargetRelativePath = projection.TargetRelativePath,
                TargetAbsolutePath = projection.TargetAbsolutePath,
                PathSchemaVersion = projection.PathSchemaVersion,
                Status = PinCleanupStatus.Pending,
                CreatedAt = now
            });
        }
    }

    public static bool IsRuntimeSupported(AgentKind kind) =>
        kind is AgentKind.ClaudeCode or AgentKind.Codex or AgentKind.Grok;

    private async Task<PinnedInstructionMutationResult?> TryReplayAsync(
        Guid agentId,
        Guid requestId,
        string fingerprint,
        PinPrincipal principal,
        CancellationToken ct)
    {
        var existing = await _db.AgentPinOperations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.AgentId == agentId && o.RequestId == requestId, ct);
        if (existing is null)
            return null;
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            throw new ConflictException("RequestId was reused with a different pin operation.", RequestConflict);

        var agent = await _db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId, ct);
        return new PinnedInstructionMutationResult(
            await LoadSetAsync(agent, principal.SessionId, includeRevoked: true, ct),
            CreatedNewRow: false);
    }

    private async Task<PinnedInstructionSetDto> LoadSetAsync(
        Agent agent,
        Guid? sessionId,
        bool includeRevoked,
        CancellationToken ct)
    {
        var query = _db.AgentPinnedInstructions.AsNoTracking().Where(p => p.AgentId == agent.Id);
        if (!includeRevoked)
            query = query.Where(p => p.RevokedAt == null);
        var pins = await query
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
        var state = await _db.AgentPinnedInstructionStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AgentId == agent.Id, ct);
        var reconciliation = await _db.AgentPinReconciliations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.AgentId == agent.Id, ct);
        var projections = await _db.AgentPinProjections.AsNoTracking()
            .Where(p => p.AgentId == agent.Id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
        var pendingCleanup = await _db.AgentPinCleanupRecords.AsNoTracking()
            .CountAsync(c => c.OriginalAgentId == agent.Id && c.Status == PinCleanupStatus.Pending, ct);

        string? ownTarget = null;
        if (sessionId is { } sid)
        {
            var session = await _db.AgentSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == sid, ct);
            if (session is not null)
            {
                ownTarget = session.PinLaunchAbsolutePath
                    ?? AgentPinPaths.AbsolutePath(AgentPinPaths.CanonicalCwd(session.Cwd), agent.Id);
            }
        }

        return new PinnedInstructionSetDto(
            agent.Id,
            state?.Revision ?? 0,
            state?.ContentHash,
            state?.FirstUsedAt,
            IsRuntimeSupported(agent.Kind),
            agent.Kind,
            agent.PinClaudeImportMode,
            pins.Select(ToDto).ToList(),
            projections.Select(ToDto).ToList(),
            reconciliation is null
                ? null
                : new PinReconciliationDto(
                    reconciliation.DesiredRevision,
                    reconciliation.DesiredHash,
                    reconciliation.Status,
                    reconciliation.Error),
            ownTarget,
            pendingCleanup);
    }

    private async Task<Agent> RequireNamedAgentAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _db.Agents.SingleOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new NotFoundException(nameof(Agent), agentId);
        if (agent.IsPoolDelegate)
            throw new ValidationException("agentId", "Pool delegates cannot carry pinned instructions.");
        return agent;
    }

    private static void AuthorizeAgentAccess(Agent agent, PinPrincipal principal)
    {
        if (principal.IsOperator)
            return;
        if (principal.NamedAgentId != agent.Id)
            throw new ForbiddenException("A session token may only access its own agent's pins.", "pin_caller_mismatch");
    }

    private static void RejectForgedCallerFields(CapturePinnedInstructionRequest request, PinPrincipal principal)
    {
        if (request.Source is { } source && source != principal.Source)
            throw new ForbiddenException("Pin source is assigned by the server.", "pin_caller_mismatch");
        if (request.PinClaudeImportMode is not null)
            throw new ForbiddenException("Native import mode cannot be changed on pin capture.", "pin_caller_mismatch");
    }

    private static void AssertCanMutate(AgentPinnedInstruction pin, PinPrincipal principal)
    {
        if (principal.IsOperator)
            return;
        if (pin.Source != PinInstructionSource.Agent)
            throw new ForbiddenException("An agent cannot replace or revoke operator-source pins.", "pin_caller_mismatch");
    }

    private static NotFoundException NotFoundPin(Guid pinId) =>
        new(nameof(AgentPinnedInstruction), pinId);

    private async Task LockAgentAsync(Guid agentId, CancellationToken ct)
    {
        await _db.Agents
            .FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {agentId} FOR UPDATE")
            .AsNoTracking()
            .SingleAsync(ct);
    }

    private async Task<AgentPinnedInstructionState> GetOrCreateStateForUpdateAsync(
        Agent agent,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _db.AgentPinnedInstructionStates
            .FromSqlInterpolated($"SELECT * FROM \"AgentPinnedInstructionStates\" WHERE \"AgentId\" = {agent.Id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (existing is not null)
        {
            await _db.Entry(existing).ReloadAsync(ct);
            return existing;
        }

        var created = new AgentPinnedInstructionState
        {
            AgentId = agent.Id,
            Revision = 0,
            ConcurrencyToken = Guid.NewGuid(),
            ContentHash = AgentPinSnapshotHasher.HashActive([]),
            FirstUsedAt = now,
            UpdatedAt = now
        };
        _db.AgentPinnedInstructionStates.Add(created);
        return created;
    }

    private static void RotateState(
        AgentPinnedInstructionState state,
        IReadOnlyCollection<AgentPinnedInstruction> active,
        DateTime now)
    {
        state.Revision++;
        state.ConcurrencyToken = Guid.NewGuid();
        state.ContentHash = AgentPinSnapshotHasher.HashActive(active);
        state.UpdatedAt = now;
    }

    private async Task DirtyReconciliationAsync(
        Agent agent,
        AgentPinnedInstructionState state,
        bool firstUse,
        DateTime now,
        CancellationToken ct)
    {
        var reconciliation = await _db.AgentPinReconciliations
            .SingleOrDefaultAsync(r => r.AgentId == agent.Id, ct);
        if (reconciliation is null)
        {
            reconciliation = new AgentPinReconciliation { AgentId = agent.Id };
            _db.AgentPinReconciliations.Add(reconciliation);
        }

        reconciliation.DesiredRevision = state.Revision;
        reconciliation.DesiredHash = state.ContentHash;
        reconciliation.Status = PinProjectionStatus.Pending;
        reconciliation.Error = null;
        reconciliation.UpdatedAt = now;

        var locations = await _db.AgentPinProjections
            .Where(p => p.AgentId == agent.Id)
            .ToListAsync(ct);
        if (firstUse && locations.Count == 0)
        {
            await EnsureLocationCoreAsync(
                agent,
                AgentPinPaths.DefaultHost,
                AgentPinPaths.CanonicalCwd(agent.WorkingDirectory),
                configuredConsumer: true,
                state.Revision,
                now,
                ct);
            return;
        }

        foreach (var location in locations)
        {
            location.DesiredRevision = state.Revision;
            if (location.Status != PinProjectionStatus.Retired)
                location.Status = PinProjectionStatus.Pending;
            location.UpdatedAt = now;
        }
    }

    private async Task<AgentPinProjection> EnsureLocationCoreAsync(
        Agent agent,
        string host,
        string cwd,
        bool configuredConsumer,
        int desiredRevision,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _db.AgentPinProjections
            .SingleOrDefaultAsync(
                p => p.AgentId == agent.Id && p.CanonicalHost == host && p.CanonicalCwd == cwd,
                ct);
        if (existing is not null)
        {
            existing.HasConfiguredConsumer |= configuredConsumer;
            existing.DesiredRevision = desiredRevision;
            if (existing.Status == PinProjectionStatus.Retired)
                existing.Status = PinProjectionStatus.Pending;
            existing.UpdatedAt = now;
            return existing;
        }

        var maxGeneration = await _db.AgentPinProjections
            .Where(p => p.AgentId == agent.Id)
            .Select(p => (int?)p.LocationGeneration)
            .MaxAsync(ct) ?? 0;
        var projection = new AgentPinProjection
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            CanonicalHost = host,
            CanonicalCwd = cwd,
            PathSchemaVersion = AgentPinPaths.PathSchemaVersion,
            TargetRelativePath = AgentPinPaths.RelativePath(agent.Id),
            TargetAbsolutePath = AgentPinPaths.AbsolutePath(cwd, agent.Id),
            LocationGeneration = maxGeneration + 1,
            DesiredRevision = desiredRevision,
            MarkerVersion = AgentPinPaths.MarkerVersion,
            Status = PinProjectionStatus.Pending,
            ImportStatus = PinImportStatus.None,
            ImportMode = agent.PinClaudeImportMode,
            HasConfiguredConsumer = configuredConsumer,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AgentPinProjections.Add(projection);
        return projection;
    }

    private void RecordOperation(
        Guid agentId,
        Guid requestId,
        PinOperationKind kind,
        string fingerprint,
        Guid resultPinId,
        Guid? revokedPinId,
        AgentPinnedInstructionState state,
        bool createdNewRow,
        DateTime now)
    {
        _db.AgentPinOperations.Add(new AgentPinOperation
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            RequestId = requestId,
            Kind = kind,
            Fingerprint = fingerprint,
            ResultPinId = resultPinId,
            ResultRevokedPinId = revokedPinId,
            ResultRevision = state.Revision,
            ResultHash = state.ContentHash,
            CreatedNewRow = createdNewRow,
            CreatedAt = now
        });
    }

    private Task PublishAsync(Guid agentId, AgentPinnedInstructionState state, CancellationToken ct) =>
        _eventBus.PublishToAllAsync(
            "AgentPinnedInstructionsChanged",
            new AgentPinnedInstructionsChangedDto(agentId, state.Revision, state.ContentHash),
            ct);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Fingerprint(
        PinOperationKind kind,
        string text,
        string? sourceNamespace,
        string? sourceKey,
        string? sourceRef,
        Guid? replacesPinId,
        Guid? repinsPinId)
    {
        var payload =
            $"{kind}\n{text}\n{sourceNamespace}\n{sourceKey}\n{sourceRef}\n{replacesPinId:N}\n{repinsPinId:N}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static PinnedInstructionDto ToDto(AgentPinnedInstruction pin) => new(
        pin.Id,
        pin.Text,
        pin.Source,
        pin.SourceNamespace,
        pin.SourceKey,
        pin.SourceRef,
        pin.CreatedAt,
        pin.CreatedByUserId,
        pin.CreatedBySessionId,
        pin.RevokedAt,
        pin.RevokedByUserId,
        pin.RevokedBySessionId,
        pin.SupersedesPinId);

    private static PinProjectionDto ToDto(AgentPinProjection projection) => new(
        projection.Id,
        projection.CanonicalHost,
        projection.CanonicalCwd,
        projection.TargetRelativePath,
        projection.TargetAbsolutePath,
        projection.LocationGeneration,
        projection.DesiredRevision,
        projection.ProjectedRevision,
        projection.Status,
        projection.Error,
        projection.ImportStatus,
        projection.ImportMode);
}
