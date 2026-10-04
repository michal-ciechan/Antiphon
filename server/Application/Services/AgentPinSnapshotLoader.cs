using System.Collections.Immutable;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Copied pin content, without EF identity, provenance or mutable history.</summary>
public sealed record AgentPinValue(Guid Id, string Text)
{
    public override string ToString() => $"AgentPinValue {Id:N}";
}

/// <summary>
/// One immutable complete set shared by rendering and future launch/file consumers. Location and
/// revision are separate from the content hash. A never-used store is distinct from a last revoke.
/// </summary>
public sealed class AgentPinSnapshot
{
    private AgentPinSnapshot(Guid agentId, int revision, bool hasHistory, ImmutableArray<AgentPinValue> pins)
    {
        AgentId = agentId;
        Revision = revision;
        HasHistory = hasHistory;
        Pins = pins;
        ContentHash = AgentPinSnapshotHasher.HashOrdered(pins);
    }

    public Guid AgentId { get; }
    public int Revision { get; }
    public bool HasHistory { get; }
    public string ContentHash { get; }
    public ImmutableArray<AgentPinValue> Pins { get; }

    public static AgentPinSnapshot Create(Guid agentId, int revision, bool hasHistory,
        IEnumerable<AgentPinnedInstruction> pins)
    {
        if (agentId == Guid.Empty || revision < 0 || hasHistory != (revision > 0))
            throw new InvalidOperationException("Invalid pin snapshot identity or revision.");
        var rows = pins.ToArray();
        if (rows.Any(p => p.AgentId != agentId))
            throw new InvalidOperationException("A pin snapshot cannot contain another owner's pins.");
        var active = rows.Where(p => p.RevokedAt is null).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new AgentPinValue(p.Id, p.Text)).ToImmutableArray();
        if ((!hasHistory && active.Length != 0) || active.Select(p => p.Id).Distinct().Count() != active.Length)
            throw new InvalidOperationException("Invalid pin snapshot contents.");
        return new(agentId, revision, hasHistory, active);
    }

    public override string ToString() => $"AgentPinSnapshot {AgentId:N} revision={Revision} hash={ContentHash}";
}

/// <summary>
/// Dormant S2 core: no production caller or DI registration until the final-location launch gate
/// exists. One SQL statement reads metadata and active rows together, so concurrent mutation cannot
/// pair an old revision with new text. Batch preview has the same query count as detail and performs
/// no projection/host I/O. The existing store's content hash remains authoritative and is checked.
/// </summary>
public sealed class AgentPinSnapshotLoader(AppDbContext db)
{
    public async Task<AgentPinSnapshot> LoadAsync(Guid agentId, CancellationToken ct) =>
        (await LoadManyAsync([agentId], ct)).GetValueOrDefault(agentId)
        ?? throw new NotFoundException("Named agent", agentId);

    public async Task<IReadOnlyDictionary<Guid, AgentPinSnapshot>> LoadManyAsync(
        IReadOnlyCollection<Guid> agentIds, CancellationToken ct)
    {
        if (agentIds.Count == 0) return ImmutableDictionary<Guid, AgentPinSnapshot>.Empty;
        var rows = await (
            from agent in db.Agents.AsNoTracking()
            where agentIds.Contains(agent.Id) && !agent.IsPoolDelegate
            join state in db.AgentPinnedInstructionStates.AsNoTracking() on agent.Id equals state.AgentId into states
            from state in states.DefaultIfEmpty()
            join pin in db.AgentPinnedInstructions.AsNoTracking().Where(p => p.RevokedAt == null)
                on agent.Id equals pin.AgentId into pins
            from pin in pins.DefaultIfEmpty()
            select new { agent.Id, Revision = (int?)state.Revision, Hash = state.ContentHash, Pin = pin })
            .ToListAsync(ct);
        var result = ImmutableDictionary.CreateBuilder<Guid, AgentPinSnapshot>();
        foreach (var group in rows.GroupBy(r => r.Id))
        {
            var first = group.First();
            var snapshot = AgentPinSnapshot.Create(group.Key, first.Revision ?? 0, first.Revision.HasValue,
                group.Where(r => r.Pin is not null).Select(r => r.Pin));
            if (snapshot.HasHistory && !string.Equals(snapshot.ContentHash, first.Hash, StringComparison.Ordinal))
                throw new InvalidOperationException($"Pin snapshot state does not match active content for agent {group.Key:N}.");
            result.Add(group.Key, snapshot);
        }
        return result.ToImmutable();
    }
}
