// CARD-1836 S2: the per-chunk selection between a comparison commit B and a candidate A (plan D-3.2..D-3.5). It reads
// git objects only: the NUL name-status diff B..A, the derived impact index blob at B AND at A (bound by blob id and the
// SHA-256 of its bytes), both project graphs through RcProjectSelection.Select (deleted and linked readers are found in
// the B graph), and the declared runtime-reader census. Old-only and new-only path->class edges both select; a simple
// class name selects every chunk that has it; a capped path widens to its project closure or forces Full; a path no
// graph, index edge or reader owns forces Full. A chunk outside every hit carries only from a strictly reusable receipt
// for exactly its contents. Diagnostic only: Full still executes. Plain C#, ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>One planned chunk: its id, owning test project, simple class names and content digest.</summary>
    public sealed class RcPlannedChunk
    {
        public string Id { get; set; } = "";
        public string Project { get; set; } = "";
        public string[] Classes { get; set; } = Array.Empty<string>();
        public string ContentDigest { get; set; } = "";
        /// <summary>The stable case keys of its expanded roster (the admission predicate requires each passed).</summary>
        public string[] Cases { get; set; } = Array.Empty<string>();

        public bool RepositoryInvariant { get; set; }
        public bool RequiresFreshInvariantExecution => RepositoryInvariant || Classes.Any(NightlyTestsPlan.IsInvariantClass);
    }

    /// <summary>A declared runtime reader (directory listing, fixture, assembled file name): its class and the path globs it reads.</summary>
    public sealed class RcReaderCensusEntry
    {
        public string Reader { get; set; } = "";
        public string[] Globs { get; set; } = Array.Empty<string>();
    }

    /// <summary>The index blob bound at one side.</summary>
    public sealed class RcIndexBinding
    {
        public string Side { get; set; } = "";
        public string Commit { get; set; } = "";
        public string Blob { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    /// <summary>One chunk's decision: execute with every reason, or carry with its origin receipt.</summary>
    public sealed class RcChunkDecision
    {
        public string Chunk { get; set; } = "";
        public string Decision { get; set; } = "execute";
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public IDictionary Origin { get; set; }
    }

    public sealed class RcChunkSelectionResult
    {
        public const string Format = "antiphon-rc-chunk-selection-v1";
        public string BaselineSha { get; set; } = "";
        public string CandidateSha { get; set; } = "";
        /// <summary>Scoped or Full.</summary>
        public string Mode { get; set; } = "Full";
        public string[] FallbackReasons { get; set; } = Array.Empty<string>();
        public RcIndexBinding[] Index { get; set; } = Array.Empty<RcIndexBinding>();
        public RcChunkDecision[] Decisions { get; set; } = Array.Empty<RcChunkDecision>();

        public RcChunkDecision Of(string chunk) => Decisions.FirstOrDefault(d => string.Equals(d.Chunk, chunk, StringComparison.Ordinal));
    }

    public static class RcChunkSelection
    {
        public const string IndexPath = "tests/derived-impact-index.json";

        /// <summary>
        /// The selection of every planned chunk between baseline and candidate (full commit ids). admit decides whether a
        /// prior receipt is a reusable clean pass (RcChunkReceipt.Reusable in production).
        /// </summary>
        public static RcChunkSelectionResult Select(string root, string baselineSha, string candidateSha, IReadOnlyList<RcPlannedChunk> chunks,
            IReadOnlyList<IDictionary> priorReceipts, IReadOnlyList<RcReaderCensusEntry> census, Func<IDictionary, RcReuse> admit)
        {
            var result = new RcChunkSelectionResult { BaselineSha = baselineSha ?? "", CandidateSha = candidateSha ?? "" };
            var fallback = new List<string>();
            var hits = chunks.ToDictionary(c => c.Id, c => new List<string>(), StringComparer.Ordinal);

            var project = RcProjectSelection.Select(root, baselineSha, candidateSha);
            var old = ReadIndex(root, baselineSha, "B", fallback);
            var @new = ReadIndex(root, candidateSha, "A", fallback);
            result.Index = new[] { old.Binding, @new.Binding };

            var census0 = census ?? Array.Empty<RcReaderCensusEntry>();
            var changes = Items(project["changedPaths"]).Cast<IDictionary>().Select(r => (Path: PsValue.Str(r["path"]), Status: PsValue.Str(r["status"]))).ToArray();
            var discharged = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (path, status) in changes)
            {
                if (path == IndexPath)
                {
                    // An index regeneration is not an all-chunk dirty bit: its changed edges (old-only and new-only) are the
                    // positive evidence, its declared readers run (the census loop below), and a change to any non-edge field
                    // (cap, allowlist, windowsOnly, linuxOnly, ...) changes what every edge means, so it forces Full.
                    discharged.Add(path);
                    foreach (var cls in ChangedEdgeClasses(old.Edges, @new.Edges)) Hit(chunks, hits, cls, "index-edge-changed:" + cls);
                    if (old.Fields != null && @new.Fields != null && !string.Equals(old.Fields, @new.Fields, StringComparison.Ordinal)) fallback.Add("index-fields-changed");
                }
                else
                    foreach (var (index, side) in new[] { (old, "B"), (@new, "A") })
                    {
                        if (index.Edges.TryGetValue(path, out var classes))
                            foreach (var cls in classes) Hit(chunks, hits, cls, "index-edge:" + side + ":" + path);
                    }
                foreach (var entry in census0)
                    foreach (var glob in entry.Globs)
                        if (RcInputInventory.Pattern(glob).IsMatch(path))
                        {
                            Hit(chunks, hits, entry.Reader, "runtime-reader:" + glob);
                            discharged.Add(path);
                        }
            }

            // Project closures from both graphs; reasons the project planner could not resolve stay Full unless discharged. A
            // reader-census match only discharges a planner fallback: it never narrows a path's own project closure (Review r1 D-4).
            var projectReasons = Items(project["fallbackReasons"]).Select(PsValue.Str).ToArray();
            foreach (var reason in projectReasons) if (!Discharged(reason, discharged)) fallback.Add(reason);
            var graphFull = projectReasons.Any(r => !Discharged(r, discharged));
            // Owners and their downstream closure in the B graph (deleted and modified paths) and the A graph (added and
            // modified paths): a deleted harness and its removed linked reader are found at B.
            var graphs = Items(project["graphs"]).Cast<IDictionary>().ToArray();
            var closure = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (!graphFull)
                foreach (var (path, status) in changes)
                {
                    if (path == IndexPath) continue;
                    var sides = status == "A" ? new[] { 1 } : status == "D" ? new[] { 0 } : new[] { 0, 1 };
                    foreach (var side in sides)
                        foreach (IDictionary p in Items(graphs[side]["projects"]))
                        {
                            if (!Owns(p, path)) continue;
                            foreach (var member in new[] { PsValue.Str(p["path"]) }.Concat(Items(p["downstream"]).Select(PsValue.Str)))
                            {
                                if (!closure.TryGetValue(member, out var why)) closure[member] = why = new List<string>();
                                why.Add("project-closure:" + (side == 0 ? "B" : "A") + ":" + path);
                            }
                        }
                }
            var affected = new HashSet<string>(closure.Keys, StringComparer.Ordinal);

            // Capped paths: widen to the owning project closure, or Full when either side's closure is unknown.
            foreach (var (path, _) in changes)
                foreach (var (index, side) in new[] { (old, "B"), (@new, "A") })
                    if (index.Capped.Contains(path))
                    {
                        if (graphFull || !OwnedByGraph(project, path)) fallback.Add("index-capped:" + path + ":" + side);
                        else foreach (var chunk in chunks) if (affected.Contains(chunk.Project)) hits[chunk.Id].Add("index-capped-closure:" + path + ":" + side);
                    }

            foreach (var chunk in chunks)
                if (closure.TryGetValue(chunk.Project, out var why)) hits[chunk.Id].AddRange(why);

            result.FallbackReasons = fallback.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToArray();
            result.Mode = result.FallbackReasons.Length > 0 ? "Full" : "Scoped";

            var carries = RcChunkReceipt.Match(chunks.Select(c => (c.Id, c.ContentDigest)), priorReceipts ?? Array.Empty<IDictionary>(), admit)
                .ToDictionary(o => o.Chunk, StringComparer.Ordinal);
            var decisions = new List<RcChunkDecision>();
            foreach (var chunk in chunks)
            {
                var reasons = hits[chunk.Id].Distinct(StringComparer.Ordinal).ToList();
                if (result.Mode == "Full") reasons.Insert(0, "full");
                if (chunk.RequiresFreshInvariantExecution) reasons.Add("repository-invariant-fresh-execution");
                var match = carries[chunk.Id];
                if (reasons.Count == 0 && match.Decision == "carry")
                    decisions.Add(new RcChunkDecision { Chunk = chunk.Id, Decision = "carry", Origin = match.Origin });
                else
                {
                    if (match.Decision != "carry") reasons.AddRange(match.Reasons);
                    decisions.Add(new RcChunkDecision { Chunk = chunk.Id, Decision = "execute", Reasons = reasons.ToArray() });
                }
            }
            result.Decisions = decisions.ToArray();
            return result;
        }

        /// <summary>
        /// A planner reason a declared reader or the index itself answers: unknown-path for such a path, and shared-input
        /// for a scripts/ path a census reader covers (v2 replaces the all-suite scripts/** owner with its readers).
        /// </summary>
        static bool Discharged(string reason, HashSet<string> discharged)
        {
            if (reason.StartsWith("unknown-path:", StringComparison.Ordinal)) return discharged.Contains(reason.Substring("unknown-path:".Length));
            if (reason.StartsWith("shared-input:scripts/", StringComparison.Ordinal)) return discharged.Contains(reason.Substring("shared-input:".Length));
            return false;
        }

        sealed class Index
        {
            public RcIndexBinding Binding;
            public Dictionary<string, string[]> Edges = new Dictionary<string, string[]>(StringComparer.Ordinal);
            public HashSet<string> Capped = new HashSet<string>(StringComparer.Ordinal);
            /// <summary>The canonical text of every field other than edges and cappedPaths (null when unbound or malformed).</summary>
            public string Fields;
        }

        static Index ReadIndex(string root, string sha, string side, List<string> fallback)
        {
            var index = new Index { Binding = new RcIndexBinding { Side = side, Commit = sha ?? "" } };
            var blob = TestSelectionGit.Run(root, new[] { "rev-parse", "--verify", "--quiet", sha + ":" + IndexPath }, new[] { 0, 1, 128 });
            var id = blob.Stdout.Trim();
            if (blob.Exit != 0 || id.Length != 40)
            {
                fallback.Add("index-unbound:" + side);
                return index;
            }
            var text = TestSelectionGit.Run(root, new[] { "cat-file", "blob", id }).Stdout;
            index.Binding.Blob = id;
            index.Binding.Sha256 = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text))).ToLowerInvariant();
            try
            {
                using var doc = JsonDocument.Parse(text);
                var body = doc.RootElement;
                // Review r1 D-4: only schema 1 with both arrays is understood; anything else is malformed and forces Full.
                if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number
                    || !schema.TryGetInt32(out var version) || version != 1 || !body.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array
                    || !body.TryGetProperty("cappedPaths", out var capped) || capped.ValueKind != JsonValueKind.Array)
                {
                    fallback.Add("index-malformed:" + side);
                    return new Index { Binding = index.Binding };
                }
                foreach (var edge in edges.EnumerateArray())
                    index.Edges[edge.GetProperty("path").GetString() ?? ""] = edge.GetProperty("classes").EnumerateArray().Select(c => c.GetString() ?? "").ToArray();
                foreach (var row in capped.EnumerateArray())
                    index.Capped.Add(row.ValueKind == JsonValueKind.String ? row.GetString() : row.GetProperty("path").GetString());
                index.Fields = string.Join("\n", body.EnumerateObject().Where(p => p.Name != "edges" && p.Name != "cappedPaths")
                    .OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "=" + p.Value.GetRawText()));
            }
            catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException)
            {
                fallback.Add("index-malformed:" + side);
                return new Index { Binding = index.Binding };
            }
            return index;
        }

        static IEnumerable<string> ChangedEdgeClasses(Dictionary<string, string[]> old, Dictionary<string, string[]> @new)
        {
            var pairsOld = new HashSet<string>(old.SelectMany(e => e.Value.Select(c => e.Key + "\0" + c)), StringComparer.Ordinal);
            var pairsNew = new HashSet<string>(@new.SelectMany(e => e.Value.Select(c => e.Key + "\0" + c)), StringComparer.Ordinal);
            var changed = new HashSet<string>(pairsOld, StringComparer.Ordinal);
            changed.SymmetricExceptWith(pairsNew);
            return changed.Select(p => p.Substring(p.IndexOf('\0') + 1)).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal);
        }

        static void Hit(IReadOnlyList<RcPlannedChunk> chunks, Dictionary<string, List<string>> hits, string cls, string reason)
        {
            foreach (var chunk in chunks)
                if (chunk.Classes.Any(c => string.Equals(Simple(c), Simple(cls), StringComparison.Ordinal))) hits[chunk.Id].Add(reason);
        }

        static string Simple(string name)
        {
            var dot = (name ?? "").LastIndexOf('.');
            return dot < 0 ? name ?? "" : name.Substring(dot + 1);
        }

        static bool OwnedByGraph(IDictionary project, string path)
        {
            foreach (IDictionary graph in Items(project["graphs"]))
                foreach (IDictionary p in Items(graph["projects"]))
                    if (Owns(p, path)) return true;
            return false;
        }

        /// <summary>The project row owns the path: under its directory, or one of its resolved inputs (linked Compile/Content, imports).</summary>
        static bool Owns(IDictionary p, string path)
        {
            var projectPath = PsValue.Str(p["path"]);
            var slash = projectPath.LastIndexOf('/');
            var dir = slash < 0 ? "" : projectPath.Substring(0, slash + 1);
            return path.StartsWith(dir, StringComparison.Ordinal) || Items(p["inputs"]).Select(PsValue.Str).Contains(path, StringComparer.Ordinal);
        }

        static object[] Items(object value) => value is IEnumerable items && !(value is string) ? items.Cast<object>().ToArray() : value == null ? new object[0] : new[] { value };
    }
}
