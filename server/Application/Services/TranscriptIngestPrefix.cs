using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1888: the session's verified runner-prefix memo. After a clean commit a batch of at least
/// <see cref="MinEntries"/> entries records how many leading entries are now stored, with a SHA-256
/// digest of their identities. A later snapshot whose first <see cref="Count"/> entries digest the
/// same sends only its tail UUIDs to the identity probe, but only while the stored prefix is
/// unchanged (<see cref="StillStored"/>). Process-local on <see cref="SessionStateStore"/>'s entry;
/// never persisted, never part of the snapshot.
/// </summary>
/// <param name="FirstRowId">The session's lowest-sequence row when the memo was recorded.</param>
/// <param name="PrefixStoredMaxSequence">S: the session's stored max once the batch committed.</param>
/// <param name="TopRowId">The row stored at S.</param>
/// <param name="PrefixStoredCount">C: the session's rows with a sequence at or below S.</param>
internal sealed record TranscriptIngestPrefix(int Count, byte[] Digest, Guid FirstRowId,
    long PrefixStoredMaxSequence, Guid TopRowId, long PrefixStoredCount)
{
    /// <summary>A smaller batch is at most this many seeks today; it neither records nor skips (D-10).</summary>
    internal const int MinEntries = 64;

    internal bool Covers(Digests digests) =>
        digests.Prefix is { } prefix && CryptographicOperations.FixedTimeEquals(prefix, Digest);

    /// <summary>
    /// Rule 2, read in the prelude's snapshot. Product inserts go above the stored max and no product
    /// path updates a row, so while the row recorded at S is still there nothing was inserted at or
    /// below S; an equal count then means no backing row was deleted. A whole-session delete changes
    /// all three, a middle delete the count, a top-row delete and refill the row at S. Any mismatch,
    /// or a missing fact, is false and the caller probes the prefix as before (fail closed).
    /// </summary>
    internal bool StillStored(TranscriptIngestPrelude.Result prelude) =>
        prelude.FirstRowId == FirstRowId && prelude.PrefixRowId == TopRowId && prelude.PrefixCount == PrefixStoredCount;

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
