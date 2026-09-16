using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Common serialization for manual placement and automatic following. Caller owns transaction.</summary>
internal static class HerdrPlacementLock
{
    internal static async Task<Agent?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var snapshot = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (snapshot is null) return null;
        if (StandingSpecialistSeatPolicy.IsCheck(snapshot))
        {
            var ownerId = snapshot.StandingSpecialistOwnerId ?? snapshot.Id;
            if (ownerId != id)
                await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                    .AsNoTracking().SingleOrDefaultAsync(ct);
        }
        var agent = await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        // A tracked object can predate the lock, including a same-spelling manual reaffirmation.
        if (agent is not null) await db.Entry(agent).ReloadAsync(ct);
        return agent;
    }
}
