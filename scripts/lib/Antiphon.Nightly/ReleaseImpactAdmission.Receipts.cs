#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactAdmission
{
    private sealed class Custody
    {
        private readonly Dictionary<string, (ulong ByteCount, byte[] Bytes)> _verified = new(StringComparer.Ordinal);
        private ulong _total;

        internal byte[] Read(Artifact artifact)
        {
            Require(RcChunkReceipt.IsDigest(artifact.Digest), "p2-receipt:address");
            if (_verified.TryGetValue(artifact.Digest, out var verified))
            {
                Require(verified.ByteCount == artifact.ByteCount, "p2-receipt:length");
                return verified.Bytes;
            }
            Require(artifact.ByteCount <= ReleaseImpactIntegrity.ProofLimit &&
                artifact.ByteCount <= ReleaseImpactIntegrity.AggregateProofLimit - _total, "p2-receipt:proof-size");
            try
            {
                using var stream = artifact.Open();
                var bytes = ReleaseImpactIntegrity.ReadBounded(stream, (int)artifact.ByteCount, "proof-size");
                Require((ulong)bytes.Length == artifact.ByteCount &&
                    ReleaseImpactIntegrity.RawSha256(bytes) == artifact.Digest, "p2-receipt:proof-bytes");
                _verified.Add(artifact.Digest, (artifact.ByteCount, bytes));
                _total += artifact.ByteCount;
                return bytes;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ReleaseImpactIntegrity.WireException)
            {
                throw new Refusal("p2-receipt:proof-read");
            }
        }

        internal RcReceiptInventory Inventory(InventoryPin pin)
        {
            var bytes = Read(pin.Artifact);
            using var document = ReleaseImpactIntegrity.Parse(bytes);
            var revision = document.RootElement.GetProperty("attemptRevision");
            Require(revision.TryGetUInt32(out var value) && value == pin.Revision, "p2-receipt:inventory-revision");
            var result = RcChunkReceipt.ReadBytes(bytes, pin.Artifact.Digest);
            Require(result.Refusal.Length == 0 && result.Sha == pin.Sha, "p2-receipt:inventory");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in result.Rows)
                Require(Text(row, "chunk") is { Length: > 0 } key && keys.Add(key), "p2-receipt:inventory-chunk");
            return result;
        }
    }

    private sealed record Receipt(string Chunk, string Disposition, Origin? Origin, string Platform, string Architecture, string Profile,
        string? ContentDigest, string? InputDigest, string Result, uint? ExitCode, bool Skipped, bool TimedOut, bool Tolerated,
        IReadOnlyList<Case> ExpectedCases, uint ObservedCount, IReadOnlyList<RcTrxCase> RawCases, string? EvidenceKind, string? Reason)
    {
        internal object Projection => new
        {
            Chunk, Disposition, Origin, Platform, Architecture, Profile, ContentDigest, InputDigest, Result, ExitCode,
            Skipped, TimedOut, Tolerated, ExpectedCases, ObservedCount,
            observedCases = RawCases.Select(observed => new
            {
                caseUid = observed.CaseKey, observed.Class, observed.Method, observed.Outcome,
            }),
            EvidenceKind, Reason,
        };
    }

    private static string Text(IDictionary value, string field) => PsValue.Str(value[field]);

    private static string ContentDigest(Chunk chunk) =>
        RcChunkReceipt.ContentDigest(chunk.Suite, chunk.ExecutionKind, chunk.Cases.Select(c =>
            new RcCaseIdentity(c.Assembly, c.Class, c.Method, c.CaseUid)))
        ?? throw new Refusal("p2-incomplete:case-identity");

    private static Receipt ReadReceipt(Custody custody, InventoryPin inventory, IDictionary? row, ExecutionPin? pin,
        Chunk chunk, string expectedInput, Target target, GitSnapshot git, bool owned = false)
    {
        if (row is null || Text(row, "disposition") == "missing")
        {
            Require(pin is null, "p2-receipt:unattached-origin");
            return new Receipt(chunk.Id, "missing", null, "", "", "", null, null, "MISSING", null,
                false, false, false, chunk.Cases, 0, [], null, "missing-row");
        }

        var disposition = Text(row, "disposition");
        var actualContent = Text(row, "contentDigest");
        var result = Text(row, "result");
        // The wire binds the process exit as a 32-bit value. Keep its signed interpretation in Reusable.
        var exit = row.Contains("exitCode") ? unchecked((uint)PsOps.ToInt(row["exitCode"])) : (uint?)null;
        var skipped = PsValue.IsTrue(row["skipped"]);
        var timedOut = PsValue.IsTrue(row["timedOut"]);
        var tolerated = PsValue.IsTrue(row["processExitTolerated"]);
        Require(disposition is "executed" or "carried" && RcChunkReceipt.IsDigest(actualContent), "p2-receipt:row");
        var scope = new RcReceiptScope { Platform = target.Os, Architecture = target.Architecture, Profile = target.Profile };

        Origin? origin = null;
        RcTrxCase[] observed = [];
        string? absence = "origin-unavailable";
        if (pin is not null)
        {
            Require(pin.Chunk == chunk.Id && pin.Sha == Text(row, "sha") &&
                (!row.Contains("runId") || pin.RunId == Text(row, "runId")) &&
                (!row.Contains("attempt") || pin.Attempt == PsOps.ToInt(row["attempt"])) &&
                RcChunkReceipt.IsDigest(pin.InventorySeal), "p2-receipt:origin");
            Require(git.Commit(pin.Sha) && git.Ancestor(pin.Sha, inventory.Sha), "p2-receipt:origin-ancestry");
            IDictionary execution = row;
            if (disposition == "executed")
                Require(pin.Sha == inventory.Sha && pin.InventorySeal == inventory.Artifact.Digest &&
                    pin.RunId == inventory.RunId && pin.Attempt == inventory.Attempt, "p2-receipt:original-inventory");
            else
            {
                var original = pin.OriginalInventory ?? throw new Refusal("p2-receipt:original-inventory");
                Require(pin.InventorySeal == original.Artifact.Digest && pin.InventorySeal == Text(row, "originReceiptDigest") &&
                    pin.Sha == original.Sha && pin.RunId == original.RunId && pin.Attempt == original.Attempt, "p2-receipt:carried-origin");
                execution = custody.Inventory(original).Rows.SingleOrDefault(candidate => Text(candidate, "chunk") == chunk.Id)
                    ?? throw new Refusal("p2-receipt:original-row");
                Require(Text(execution, "disposition") == "executed" && Text(execution, "sha") == pin.Sha &&
                    Text(execution, "contentDigest") == actualContent, "p2-receipt:original-row");
            }

            // Resolve only a qualification-owned execution pin. Verify and parse the same buffered stream bytes.
            using var evidence = new MemoryStream(custody.Read(pin.Evidence), writable: false);
            observed = RcChunkReceipt.ReadTrx(evidence, strict: true) ?? throw new Refusal("p2-receipt:trx");
            var candidate = RcChunkReceipt.Reusable(execution, observed, chunk.Cases.Select(c => c.CaseUid), scope);
            var exact = observed.Select(c => (c.Assembly, c.CaseKey, c.Class, c.Method))
                .OrderBy(key => key, CaseTupleComparer.Instance)
                .SequenceEqual(chunk.Cases.Select(c => (c.Assembly, c.CaseUid, c.Class, c.Method))
                    .OrderBy(key => key, CaseTupleComparer.Instance));

            if (!candidate.Reusable) absence = "row-not-reusable";
            else if (!exact || actualContent != ContentDigest(chunk)) absence = "case-evidence-mismatch";
            else if (pin.InputDigest != expectedInput) absence = "origin-inputs-changed";
            else if (owned && disposition != "executed") absence = "owned-run-not-executed";
            else absence = null;
            origin = new(pin.Sha, pin.RunId, pin.Attempt, pin.InventorySeal, pin.Evidence.Id, pin.Evidence.Digest);
        }

        return new Receipt(chunk.Id, disposition, origin, Text(row, "platform"), Text(row, "architecture"), Text(row, "profile"),
            actualContent, pin?.InputDigest, result, exit, skipped, timedOut, tolerated, chunk.Cases,
            (uint)observed.Length, observed, pin is null ? null : "trx", absence);
    }

    private sealed class CaseTupleComparer : IComparer<(string Assembly, string Uid, string Class, string Method)>
    {
        internal static readonly CaseTupleComparer Instance = new();
        public int Compare((string Assembly, string Uid, string Class, string Method) first, (string Assembly, string Uid, string Class, string Method) second)
        {
            var assembly = string.CompareOrdinal(first.Assembly, second.Assembly);
            if (assembly != 0) return assembly;
            var uid = string.CompareOrdinal(first.Uid, second.Uid);
            if (uid != 0) return uid;
            var @class = string.CompareOrdinal(first.Class, second.Class);
            return @class != 0 ? @class : string.CompareOrdinal(first.Method, second.Method);
        }
    }
}
