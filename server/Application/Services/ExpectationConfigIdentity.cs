using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Includes the current channel address in a directive's immutable work identity.</summary>
public static class ExpectationConfigIdentity
{
    public static async Task<string> ResolveAsync(AppDbContext db, ExpectationDirectiveSettings directive,
        ExpectationTimingSettings timing, CancellationToken ct)
    {
        var address = await db.ChatChannels.AsNoTracking()
            .Where(channel => channel.Id == directive.OperatorChannelId)
            .Select(channel => new { channel.Provider, channel.ExternalId })
            .SingleOrDefaultAsync(ct);
        return ExpectationDirectiveDigest.Compute(directive, timing,
            address is null ? null : address.Provider + "\u001f" + address.ExternalId);
    }
}
