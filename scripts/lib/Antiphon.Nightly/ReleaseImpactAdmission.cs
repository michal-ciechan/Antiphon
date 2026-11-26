#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Antiphon.Nightly;

/// <summary>Diagnostic verification of a proposal against qualification-owned evidence.</summary>
public static partial class ReleaseImpactAdmission
{
    public sealed record Request(string RepositoryId, string Line, string BaselineSha, string CandidateSha,
        string RunId, uint Attempt, uint Revision, string TargetIntentDigest);

    public sealed record Case(string Project, string Assembly, string Class, string Method, string CaseUid);
    public sealed record Chunk(string Id, string Suite, string ExecutionKind, string Project, IReadOnlyList<Case> Cases);
    public sealed record Origin(string Sha, string RunId, uint Attempt, string InventorySeal, string EvidenceId, string EvidenceDigest);
    public sealed record Verified(Request Request, string RootDigest);
    public sealed record SelectionReason(string Kind, string? Path, string? Side, IReadOnlyList<string> Chain);
    public sealed record Row(string Chunk, string Disposition, IReadOnlyList<Case> Cases, Origin? Origin,
        IReadOnlyList<SelectionReason> Reasons);

    public sealed class Decision
    {
        internal Decision(Verified? verified, IEnumerable<Row> rows, bool discoveryOutstanding, string reason)
        {
            VerifiedHandle = verified;
            Rows = Freeze(rows);
            DiscoveryOutstanding = discoveryOutstanding;
            Reason = reason;
        }

        public string EffectiveMode => VerifiedHandle is null ? "Full" : "Scoped";
        public Verified? VerifiedHandle { get; }
        public IReadOnlyList<Row> Rows { get; }
        public IEnumerable<Row> CarryCandidates => Rows.Where(row => row.Disposition == "carry-candidate");
        public IEnumerable<Row> Owed => Rows.Where(row => row.Disposition != "carry-candidate");
        public int CreditedCount => Rows.Count(row => row.Disposition == "carried");
        public bool DiscoveryOutstanding { get; }
        public string Reason { get; }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());

    private static Decision Full(IReadOnlyList<Chunk>? discovery, string reason) =>
        new(null, (discovery ?? []).Select(chunk => new Row(chunk.Id, "execute", Freeze(chunk.Cases), null, [])),
            discovery is null, reason);

    /// <summary>
    /// Use the current trusted snapshot for each admission. Missing evaluation or custody produces a Full decision.
    /// The stream opener, including any access failure, is part of the proposal boundary.
    /// </summary>
    public static Decision Admit(Request request, IReadOnlyList<Chunk>? discovery, Func<Stream> openProposal,
        EvidenceSnapshot? snapshot = null, int envelopeLimit = ReleaseImpactIntegrity.EnvelopeLimit)
    {
        if (snapshot is null || discovery is null) return Full(discovery, "p2-incomplete:independent-evidence");
        if (snapshot.Request != request) return Full(discovery, "p2-context:revision");
        if (!SameDiscovery(discovery, snapshot.Chunks)) return Full(discovery, "p2-selection:roster");
        try
        {
            using var stream = openProposal();
            var proposed = ReleaseImpactIntegrity.Decode(stream, snapshot.FoldedPaths, snapshot.OpenProof, envelopeLimit);
            var mismatch = snapshot.Match(proposed);
            if (mismatch is not null) return Full(discovery, mismatch);
            if (!snapshot.Complete) return Full(discovery, "p2-incomplete:selection");
            return new Decision(new Verified(request, proposed.RootDigest), snapshot.Rows, false, "");
        }
        catch (ReleaseImpactIntegrity.WireException error)
        {
            return Full(discovery, (error.IsAddress ? RoleFamily(error.Component) : "p2-format") + ":" + error.Component);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Full(discovery, "p2-format:read");
        }
    }

    public static Decision AdmitPrepared(Request request, IReadOnlyList<Chunk>? discovery, Func<Stream> openProposal,
        Preparation preparation, int envelopeLimit = ReleaseImpactIntegrity.EnvelopeLimit) =>
        preparation.Evidence is null ? Full(discovery, preparation.Reason)
            : Admit(request, discovery, openProposal, preparation.Evidence, envelopeLimit);

    private static bool SameDiscovery(IReadOnlyList<Chunk> left, IReadOnlyList<Chunk> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Id == pair.Second.Id
            && pair.First.Suite == pair.Second.Suite && pair.First.ExecutionKind == pair.Second.ExecutionKind
            && pair.First.Project == pair.Second.Project && pair.First.Cases.SequenceEqual(pair.Second.Cases));

    private static string RoleFamily(string role) => role switch
    {
        "target" => "p2-context",
        "inventory" or "receipts" or "obligations" => "p2-receipt",
        "universe" or "roster" or "selection" => "p2-selection",
        _ => "p2-input",
    };

    // A snapshot is derived once from trusted source inputs. It is read-only and usable for only one observed revision.
    // Its constructor is private to the verifier; only Prepare below may create it.
    public sealed class EvidenceSnapshot
    {
        private readonly IReadOnlyDictionary<string, JsonElement> _projections;
        private readonly IReadOnlyDictionary<string, JsonElement> _context;
        private readonly IReadOnlyDictionary<string, byte[]> _canonical;

        internal EvidenceSnapshot(Request request, IReadOnlyList<Chunk> chunks, IReadOnlyList<Row> rows,
            JsonElement context, IReadOnlyDictionary<string, JsonElement> projections, bool foldedPaths,
            Func<string, Stream>? openProof)
        {
            Request = request;
            Chunks = Freeze(chunks.Select(chunk => chunk with { Cases = Freeze(chunk.Cases) }));
            Rows = Freeze(rows.Select(row => row with
            {
                Cases = Freeze(row.Cases),
                Reasons = Freeze(row.Reasons.Select(reason => reason with { Chain = Freeze(reason.Chain) })),
            }));
            FoldedPaths = foldedPaths;
            OpenProof = openProof;
            _context = context.EnumerateObject().ToDictionary(member => member.Name, member => member.Value.Clone(), StringComparer.Ordinal);
            _projections = projections.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal);
            _canonical = _projections.ToDictionary(item => item.Key,
                item => ReleaseImpactIntegrity.ProjectionBytes(item.Key, item.Value, foldedPaths), StringComparer.Ordinal);
        }

        internal Request Request { get; }
        internal IReadOnlyList<Chunk> Chunks { get; }
        internal IReadOnlyList<Row> Rows { get; }
        internal bool FoldedPaths { get; }
        internal bool Complete => _projections["selection"].GetProperty("complete").GetBoolean()
            && _projections["selection"].GetProperty("globalReasons").GetArrayLength() == 0;
        internal Func<string, Stream>? OpenProof { get; }

        /// <summary>Writes a diagnostic proposal from the derivation. Admission still checks the incoming bytes.</summary>
        public byte[] WriteProposal() => ReleaseImpactIntegrity.Write(JsonSerializer.SerializeToElement(_context), _projections, FoldedPaths);

        internal string? Match(ReleaseImpactIntegrity.Decoded proposed)
        {
            foreach (var field in _context)
                if (!JsonElement.DeepEquals(field.Value, proposed.Root[field.Key]))
                    return (field.Key == "algorithmSha" ? "p2-input:" : "p2-context:") + field.Key;
            foreach (var role in ReleaseImpactIntegrity.StructuredRoles)
                if (!_canonical[role].AsSpan().SequenceEqual(ReleaseImpactIntegrity.ProjectionBytes(role, proposed.Projections[role], FoldedPaths)))
                    return RoleFamily(role) + ":" + role;
            return null;
        }
    }
}
