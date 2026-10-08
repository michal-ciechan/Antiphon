using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-1153 D-3: HMAC-SHA256 authentication of direct-HTTP absence-evidence requests and
/// responses. The canonical input is domain-separated, versioned and length-delimited. The
/// response MAC covers the operation, the request nonce, the status and the exact response body
/// bytes, so changing any evidence bit invalidates it. Never logs or returns key material.
/// </summary>
public static class AbsenceEvidenceAuthentication
{
    public const string KeyIdHeader = "X-Antiphon-Absence-Key-Id";
    public const string MacHeader = "X-Antiphon-Absence-Mac";
    public const string PrepareOperation = "prepare";
    public const string CertifyOperation = "certify";
    public const int MinimumKeyBytes = 32;

    private const string RequestDomain = "antiphon/session-absence-evidence/v1/request";
    private const string ResponseDomain = "antiphon/session-absence-evidence/v1/response";
    private const string KeyIdDomain = "antiphon/session-absence-evidence/v1/key-id";

    public static byte[] RequestCanonical(
        string operation, Guid routeSessionId, DateTime acceptedStartedAt, Guid runnerStoreId, string nonce,
        ReadOnlySpan<byte> body) =>
        Canonical(RequestDomain, operation, routeSessionId.ToString("D"),
            SessionGeneration.Normalize(acceptedStartedAt).ToString("O", CultureInfo.InvariantCulture),
            runnerStoreId.ToString("D"), nonce, Convert.ToHexString(SHA256.HashData(body)));

    public static byte[] ResponseCanonical(string operation, string requestNonce, int status, ReadOnlySpan<byte> body) =>
        Canonical(ResponseDomain, operation, requestNonce, status.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(SHA256.HashData(body)));

    public static string Sign(AbsenceEvidenceKey key, byte[] canonical) =>
        Convert.ToBase64String(HMACSHA256.HashData(key.Material, canonical));

    /// <summary>Constant-time comparison of a presented base64 MAC.</summary>
    public static bool Verify(AbsenceEvidenceKey key, byte[] canonical, string? presentedMac)
    {
        if (string.IsNullOrEmpty(presentedMac) || presentedMac.Length > 64)
            return false;
        var presented = new byte[48];
        if (!Convert.TryFromBase64String(presentedMac, presented, out var written) || written != 32)
            return false;
        var expected = HMACSHA256.HashData(key.Material, canonical);
        return CryptographicOperations.FixedTimeEquals(expected, presented.AsSpan(0, written));
    }

    internal static string KeyIdFor(ReadOnlySpan<byte> material)
    {
        var domain = Encoding.UTF8.GetBytes(KeyIdDomain);
        var buffer = new byte[domain.Length + material.Length];
        domain.CopyTo(buffer, 0);
        material.CopyTo(buffer.AsSpan(domain.Length));
        return Convert.ToHexString(SHA256.HashData(buffer), 0, 8).ToLowerInvariant();
    }

    private static byte[] Canonical(params string[] fields)
    {
        using var buffer = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            buffer.Write(length);
            buffer.Write(bytes);
        }

        return buffer.ToArray();
    }
}

/// <summary>
/// A loaded key. The key id is a domain-separated fingerprint, so it can be compared and logged;
/// <see cref="ToString"/> never prints material.
/// </summary>
public sealed class AbsenceEvidenceKey
{
    internal byte[] Material { get; }

    public string KeyId { get; }

    private AbsenceEvidenceKey(byte[] material)
    {
        Material = material;
        KeyId = AbsenceEvidenceAuthentication.KeyIdFor(material);
    }

    public static AbsenceEvidenceKey FromMaterial(byte[] material)
    {
        if (material.Length < AbsenceEvidenceAuthentication.MinimumKeyBytes)
            throw new ArgumentException("Absence evidence key must be at least 32 bytes.", nameof(material));
        return new((byte[])material.Clone());
    }

    /// <summary>
    /// The key file holds base64 of at least 32 random bytes. Null when no path is configured or
    /// the file is missing, unreadable or malformed: the transport then cannot certify.
    /// </summary>
    public static AbsenceEvidenceKey? TryLoad(string? path, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "no key path configured";
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).Trim();
            var material = Convert.FromBase64String(text);
            if (material.Length < AbsenceEvidenceAuthentication.MinimumKeyBytes)
            {
                problem = "key file is shorter than 32 bytes";
                return null;
            }

            return new(material);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            problem = "key file unreadable: " + ex.GetType().Name;
            return null;
        }
    }

    public override string ToString() => $"AbsenceEvidenceKey({KeyId})";
}
