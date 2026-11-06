using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>Stable fingerprint shared by park storage and source publication.</summary>
internal static class ParkFingerprint
{
    internal static string? Digest(string? value) => value is null ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
