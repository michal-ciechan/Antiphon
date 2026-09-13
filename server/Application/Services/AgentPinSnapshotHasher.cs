using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public static class AgentPinSnapshotHasher
{
    public static string HashActive(IEnumerable<AgentPinnedInstruction> pins)
    {
        var ordered = pins
            .Where(p => p.RevokedAt is null)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);
        var builder = new StringBuilder();
        foreach (var pin in ordered)
        {
            builder.Append(pin.Id.ToString("N"));
            builder.Append('\n');
            builder.Append(pin.Text);
            builder.Append('\n');
        }

        return Sha256Hex(builder.ToString());
    }

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
