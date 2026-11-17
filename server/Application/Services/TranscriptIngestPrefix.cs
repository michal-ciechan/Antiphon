using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1888: the session's verified runner-prefix memo. After a clean commit a batch of at least
/// <see cref="MinEntries"/> entries records how many leading entries are now stored, with a SHA-256
/// digest of their identities. A later snapshot whose first <see cref="Count"/> entries digest the
/// same sends only its tail UUIDs to the identity probe. Process-local on
/// <see cref="SessionStateStore"/>'s entry; never persisted, never part of the snapshot.
/// </summary>
internal sealed record TranscriptIngestPrefix(int Count, byte[] Digest)
{
    /// <summary>A smaller batch is at most this many seeks today; it neither records nor skips (D-10).</summary>
    internal const int MinEntries = 64;

    internal bool Covers(Digests digests) =>
        digests.Prefix is { } prefix && CryptographicOperations.FixedTimeEquals(prefix, Digest);

    /// <param name="Prefix">The digest of the first <c>prefixCount</c> entries, or null when the batch is shorter.</param>
    /// <param name="Full">The digest of every entry.</param>
    internal sealed record Digests(byte[]? Prefix, byte[] Full);

    /// <summary>
    /// One pass over the sanitized batch in source order. Each identity is
    /// <c>(Uuid, Kind, Uuid is null ? Sequence : 0)</c>, every field length-prefixed with a UUID
    /// presence byte, so two different identity sequences never encode to the same bytes.
    /// </summary>
    internal static Digests Compute(IReadOnlyList<SessionRunnerTranscriptEvent> entries, int prefixCount)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[8];
        byte[]? prefix = null;
        for (var i = 0; i < entries.Count; i++)
        {
            if (i == prefixCount) prefix = hash.GetCurrentHash();
            var e = entries[i];
            number[0] = e.Uuid is null ? (byte)0 : (byte)1;
            hash.AppendData(number[..1]);
            Field(hash, e.Uuid ?? "", number);
            Field(hash, e.Kind, number);
            BinaryPrimitives.WriteInt64LittleEndian(number, e.Uuid is null ? e.Sequence : 0);
            hash.AppendData(number);
        }
        var full = hash.GetHashAndReset();
        return new Digests(prefixCount == entries.Count ? full : prefix, full);
    }

    private static void Field(IncrementalHash hash, string value, Span<byte> number)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(number, bytes.Length);
        hash.AppendData(number[..4]);
        hash.AppendData(bytes);
    }
}
