// CARD-1836 S2 (moved from rc-boundary.ps1): pure boundary metadata and identities. No launch or selection caller.
// The rule order and glob grammar stay aligned with LayeringGuardEngine.ParseMap. A chunk id is the canonical
// SHA-256 of length-delimited UTF-8 membership; a 16-hex prefix shared by two different full digests is rebuilt as
// unassigned, keyed by the full digest, and stays dirty. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A boundary refusal: boundary-map: ... or boundary-chunk: ...</summary>
    public sealed class RcBoundaryRefusal : Exception
    {
        public RcBoundaryRefusal(string message) : base(message) { }
    }

    /// <summary>Layer edges, ordered path rules and namespace prefixes (longest first).</summary>
    public sealed class RcBoundaryMap
    {
        public Dictionary<string, string[]> Edges { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public RcBoundaryRule[] Rules { get; internal set; }
        public RcBoundaryPrefix[] Prefixes { get; internal set; }
    }

    public sealed class RcBoundaryRule
    {
        public string Boundary { get; internal set; }
        public Regex[] Globs { get; internal set; }
    }

    public sealed class RcBoundaryPrefix
    {
        public string Prefix { get; internal set; }
        public string Boundary { get; internal set; }
    }

    /// <summary>One member of a chunk: class, method and case key (empty for a method-level member).</summary>
    public sealed class RcBoundaryIdentity
    {
        public string Class { get; set; }
        public string Method { get; set; }
        public string CaseKey { get; set; }
    }

    /// <summary>A chunk row: suite, boundary, class/method identity key and an optional case key (null when absent).</summary>
    public sealed class RcBoundaryRow
    {
        public string Suite { get; set; }
        public string Boundary { get; set; }
        public string IdentityKey { get; set; }
        public string CaseKey { get; set; }
    }

    public sealed class RcBoundaryMember
    {
        public string IdentityKey { get; set; }
        public string CaseKey { get; set; }
    }

    public sealed class RcBoundaryMembership
    {
        public string Suite { get; set; }
        public string Boundary { get; set; }
        public RcBoundaryMember[] Identities { get; set; }
    }

    public sealed class RcBoundaryChunk
    {
        public string ChunkId { get; set; }
        public string Suite { get; set; }
        public string Boundary { get; set; }
        public string[] IdentityKeys { get; set; }
        public string[] CaseKeys { get; set; }
        public string FullDigest { get; set; }
        public bool Collision { get; set; }
        public bool AlwaysDirty { get; set; }
    }

    public static class RcBoundary
    {
        private static readonly string[] Layers = { "host", "core", "persistence", "git", "runners", "integrations", "application" };
        private static readonly string[] Boundaries =
        {
            "host", "core", "persistence", "git", "runners", "integrations", "application", "scripts", "checkpoints", "tui", "e2e", "client",
            "messaging", "session-runner", "pty-host", "agents-pty", "observability", "server-git", "markdown-pdf", "unassigned",
        };
        private static readonly string[] LaneSuites =
        {
            "scripts", "checkpoints", "tui", "e2e", "client", "messaging", "session-runner", "pty-host", "agents-pty", "observability", "server-git", "markdown-pdf",
        };

        private static Exception Map(string what) => new RcBoundaryRefusal("boundary-map: " + what);
        private static Exception Chunk(string what) => new RcBoundaryRefusal("boundary-chunk: " + what);

        // ---- the map ----------------------------------------------------------------------------------------------

        private static readonly Regex GlobCharacters = new Regex(@"[^A-Za-z0-9_./*\-]");

        // Top-level comma alternatives, then one brace group at a time.
        private static void ExpandGlob(string pattern, List<string> output)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '{') depth++;
                if (pattern[i] == '}') depth--;
                if (depth < 0 || depth > 1) throw Map("unbalanced glob");
                if (pattern[i] == ',' && depth == 0)
                {
                    ExpandGlob(pattern.Substring(start, i - start).Trim(), output);
                    start = i + 1;
                }
            }
            if (depth != 0) throw Map("unbalanced glob");
            var last = pattern.Substring(start).Trim();
            var open = last.IndexOf('{');
            if (open >= 0)
            {
                var close = last.IndexOf('}', open);
                foreach (var alternative in last.Substring(open + 1, close - open - 1).Split(','))
                    ExpandGlob(last.Substring(0, open) + alternative + last.Substring(close + 1), output);
                return;
            }
            if (last.Length == 0 || last.StartsWith("/", StringComparison.Ordinal) || last.Contains('\\') || last.Split('/').Contains("..") || GlobCharacters.IsMatch(last))
                throw Map("invalid glob");
            output.Add(last);
        }

        private static readonly Regex EdgeLine = new Regex(@"^([A-Za-z]+)\s*->\s*(.*)$");
        private static readonly Regex RuleLine = new Regex(@"^(.+?)\s+([A-Za-z]+)$");
        private static readonly Regex PrefixShape = new Regex(@"^Antiphon\.Tests(?:\.[A-Za-z_][A-Za-z0-9_]*)+$");

        /// <summary>
        /// The boundary map: tests/Antiphon.Tests/Layering/layers.txt ([edges], [rules]) and
        /// scripts/lib/rc-boundary-prefixes.json under the repository root. Refuses a malformed, unknown or missing entry.
        /// </summary>
        public static RcBoundaryMap ReadMap(string repoRoot)
        {
            var map = new RcBoundaryMap();
            var rules = new List<RcBoundaryRule>();
            var section = "";
            foreach (var raw in File.ReadAllLines(Path.Combine(repoRoot, "tests", "Antiphon.Tests", "Layering", "layers.txt")))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                if (line == "[edges]" || line == "[rules]") { section = line; continue; }
                if (section == "[edges]")
                {
                    var m = EdgeLine.Match(line);
                    if (!m.Success) throw Map("malformed edge");
                    var from = m.Groups[1].Value.ToLowerInvariant();
                    if (Array.IndexOf(Layers, from) < 0 || map.Edges.ContainsKey(from)) throw Map("unknown/duplicate edge");
                    var destinations = m.Groups[2].Value.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToArray();
                    if (destinations.Any(to => Array.IndexOf(Layers, to) < 0)) throw Map("unknown destination");
                    map.Edges[from] = destinations;
                }
                else if (section == "[rules]")
                {
                    var m = RuleLine.Match(line);
                    if (!m.Success) throw Map("malformed rule");
                    var boundary = m.Groups[2].Value.ToLowerInvariant();
                    if (Array.IndexOf(Layers, boundary) < 0) throw Map("unknown layer");
                    var globs = new List<string>();
                    ExpandGlob(m.Groups[1].Value, globs);
                    rules.Add(new RcBoundaryRule
                    {
                        Boundary = boundary,
                        Globs = globs.Select(glob => new Regex("^" + Regex.Escape(glob).Replace("\\*\\*", "\u0001").Replace("\\*", "[^/]*").Replace("\u0001", ".*") + "$",
                            RegexOptions.CultureInvariant)).ToArray(),
                    });
                }
                else throw Map("unknown section");
            }
            foreach (var layer in Layers) if (!map.Edges.ContainsKey(layer)) throw Map("missing edges " + layer);
            if (rules.Count == 0) throw Map("no rules");
            map.Rules = rules.ToArray();
            var prefixes = new List<RcBoundaryPrefix>();
            try
            {
                using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "scripts", "lib", "rc-boundary-prefixes.json")));
                var root = table.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
                    || format.GetString() != "antiphon-rc-boundary-prefixes-v1") throw Map("invalid prefixes format");
                if (root.TryGetProperty("prefixes", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    foreach (var entry in entries.EnumerateArray())
                    {
                        var boundary = Text(entry, "boundary");
                        var prefix = Text(entry, "prefix");
                        if (Array.IndexOf(Layers, boundary) < 0 || !PrefixShape.IsMatch(prefix) || prefix == "Antiphon.Tests.Infrastructure") throw Map("invalid namespace prefix");
                        prefixes.Add(new RcBoundaryPrefix { Prefix = prefix, Boundary = boundary });
                    }
            }
            catch (JsonException) { throw Map("invalid prefixes format"); }
            map.Prefixes = prefixes.OrderByDescending(p => p.Prefix.Length).ToArray();
            return map;
        }

        private static string Text(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : "";

        private static readonly Regex ServerLiteral = new Regex("\"(server/[^\"\\r\\n]+)\"");
        private static readonly Regex Namespace = new Regex(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]");

        /// <summary>
        /// A test class's boundary: a lane path or lane suite first; otherwise (suite antiphon only) the first rule
        /// matching each literal server/ path in the source, unassigned when two paths disagree; then the longest
        /// namespace prefix; otherwise unassigned. Computed references stay unknown.
        /// </summary>
        public static string ClassBoundary(RcBoundaryMap map, string suite, string testPath, string source)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            suite ??= "";
            source ??= "";
            var path = (testPath ?? "").Replace('\\', '/');
            foreach (var (folder, lane) in new[] { ("Scripts", "scripts"), ("Checkpoints", "checkpoints"), ("AgentTui", "tui") })
                if (path.StartsWith("tests/Antiphon.Tests/" + folder + "/", StringComparison.Ordinal)) return lane;
            if (path.StartsWith("tests/Antiphon.E2E/", StringComparison.Ordinal)) return "e2e";
            if (path.StartsWith("client/", StringComparison.Ordinal)) return "client";
            if (Array.IndexOf(LaneSuites, suite) >= 0) return suite;
            if (suite != "antiphon") return "unassigned";
            string chosen = null;
            foreach (Match literal in ServerLiteral.Matches(source))
            {
                var text = literal.Groups[1].Value;
                if (text.Split('/').Contains("..")) continue;
                var rule = map.Rules.FirstOrDefault(r => r.Globs.Any(glob => glob.IsMatch(text)));
                if (rule == null) continue;
                if (chosen == null) chosen = rule.Boundary;
                else if (chosen != rule.Boundary) return "unassigned";
            }
            if (chosen != null) return chosen;
            var ns = Namespace.Match(source);
            if (ns.Success)
            {
                var name = ns.Groups[1].Value;
                foreach (var entry in map.Prefixes)
                    if (name == entry.Prefix || name.StartsWith(entry.Prefix + ".", StringComparison.Ordinal)) return entry.Boundary;
            }
            return "unassigned";
        }

        // ---- canonical bytes --------------------------------------------------------------------------------------

        /// <summary>The value as four big-endian bytes.</summary>
        public static void AddU32(MemoryStream buffer, uint value)
        {
            buffer.WriteByte((byte)(value >> 24));
            buffer.WriteByte((byte)(value >> 16));
            buffer.WriteByte((byte)(value >> 8));
            buffer.WriteByte((byte)value);
        }

        /// <summary>The encoder's bytes, prefixed by their big-endian u32 length.</summary>
        public static void AddString(MemoryStream buffer, string value, UTF8Encoding encoder)
        {
            var bytes = encoder.GetBytes(value ?? "");
            AddU32(buffer, (uint)bytes.Length);
            buffer.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// The order of rows by ordinal (class, method, caseKey): index i of the result is the position of the i-th row
        /// in that order. NUL is escaped as 0,1 and fields are separated by 0,0, so a field's end sorts before any
        /// continuation, even an embedded NUL.
        /// </summary>
        public static int[] SortOrder(string[] classes, string[] methods, string[] caseKeys)
        {
            var keys = new string[classes.Length];
            for (var i = 0; i < keys.Length; i++)
                keys[i] = Escape(classes[i]) + "\0\0" + Escape(methods[i]) + "\0\0" + Escape(caseKeys[i]);
            var order = Enumerable.Range(0, keys.Length).ToArray();
            Array.Sort(keys, order, StringComparer.Ordinal);
            return order;
        }

        private static string Escape(string field) => (field ?? "").Replace("\0", "\0\u0001");

        /// <summary>The identities in ordinal (class, method, caseKey) order.</summary>
        public static RcBoundaryIdentity[] SortIdentities(RcBoundaryIdentity[] identities) =>
            SortOrder(identities.Select(i => i.Class).ToArray(), identities.Select(i => i.Method).ToArray(), identities.Select(i => i.CaseKey).ToArray())
                .Select(i => identities[i]).ToArray();

        /// <summary>
        /// SHA-256 (lower hex) of length-delimited UTF-8, no BOM: suite, boundary, u32 count, then each identity's class,
        /// method and caseKey in the given order. Text UTF-8 cannot encode (a lone surrogate) is a boundary-chunk refusal.
        /// </summary>
        public static string CanonicalDigest(string suite, string boundary, RcBoundaryIdentity[] identities)
        {
            var encoder = new UTF8Encoding(false, true);
            using var buffer = new MemoryStream();
            try
            {
                AddString(buffer, suite, encoder);
                AddString(buffer, boundary, encoder);
                AddU32(buffer, (uint)identities.Length);
                foreach (var identity in identities)
                {
                    AddString(buffer, identity.Class, encoder);
                    AddString(buffer, identity.Method, encoder);
                    AddString(buffer, identity.CaseKey, encoder);
                }
            }
            catch (EncoderFallbackException e) { throw Chunk("encoding " + e.Message); }
            return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        }

        // ---- chunks -----------------------------------------------------------------------------------------------

        private static readonly Regex SuiteShape = new Regex(@"\A[a-z][a-z0-9-]*\z");
        private static readonly Regex KeyShape = new Regex(@"\A[^/\r\n]+/[^\r\n]+\z");

        /// <summary>The chunk of a membership whose case keys are all empty.</summary>
        public static RcBoundaryChunk ChunkData(string suite, string boundary, string[] identityKeys) => ChunkData(suite, boundary, identityKeys, null, false, CanonicalDigest);

        /// <summary>
        /// The chunk of a non-empty membership of distinct class/method keys (with one case key each), canonically
        /// ordered. Null case keys, or a count other than the identity keys', are refused.
        /// </summary>
        public static RcBoundaryChunk ChunkData(string suite, string boundary, string[] identityKeys, string[] caseKeys) =>
            ChunkData(suite, boundary, identityKeys, caseKeys, true, CanonicalDigest);

        private static RcBoundaryChunk ChunkData(string suite, string boundary, string[] identityKeys, string[] caseKeys, bool hasCaseKeys,
            Func<string, string, RcBoundaryIdentity[], string> digest)
        {
            suite ??= "";
            boundary ??= "";
            if (!SuiteShape.IsMatch(suite) || Array.IndexOf(Boundaries, boundary) < 0) throw Chunk("invalid suite/boundary");
            if (identityKeys == null || identityKeys.Length == 0 || (identityKeys.Length == 1 && string.IsNullOrEmpty(identityKeys[0]))) throw Chunk("empty membership");
            if (hasCaseKeys && (caseKeys == null || caseKeys.Length != identityKeys.Length)) throw Chunk("case key count");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var parsed = new RcBoundaryIdentity[identityKeys.Length];
            for (var i = 0; i < identityKeys.Length; i++)
            {
                var key = identityKeys[i] ?? "";
                var caseKey = hasCaseKeys ? caseKeys[i] ?? "" : "";
                if (string.IsNullOrWhiteSpace(key) || !KeyShape.IsMatch(key) || caseKey.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw Chunk("invalid/duplicate identity");
                // Suite is constant here. The key length delimits identityKey; the remainder is caseKey.
                if (!seen.Add(key.Length.ToString(CultureInfo.InvariantCulture) + ":" + key + caseKey)) throw Chunk("invalid/duplicate identity");
                var slash = key.IndexOf('/');
                parsed[i] = new RcBoundaryIdentity { Class = key.Substring(0, slash), Method = key.Substring(slash + 1), CaseKey = caseKey };
            }
            var sorted = SortIdentities(parsed);
            var hex = digest(suite, boundary, sorted);
            return new RcBoundaryChunk
            {
                ChunkId = "b-" + boundary + "-" + hex.Substring(0, 16), Suite = suite, Boundary = boundary,
                IdentityKeys = sorted.Select(s => s.Class + "/" + s.Method).ToArray(), CaseKeys = sorted.Select(s => s.CaseKey).ToArray(),
                FullDigest = hex, Collision = false, AlwaysDirty = boundary == "unassigned",
            };
        }

        /// <summary>The chunk id of a membership whose case keys are all empty.</summary>
        public static string ChunkId(string suite, string boundary, string[] identityKeys) => ChunkData(suite, boundary, identityKeys).ChunkId;

        /// <summary>The chunk id of the membership.</summary>
        public static string ChunkId(string suite, string boundary, string[] identityKeys, string[] caseKeys) => ChunkData(suite, boundary, identityKeys, caseKeys).ChunkId;

        private static readonly Regex CollisionId = new Regex("^b-unassigned-[0-9a-f]{64}$");
        private static readonly Regex OrdinaryId = new Regex("^b-([a-z][a-z0-9-]*)-([0-9a-f]{16})$");

        /// <summary>b-&lt;boundary&gt;-&lt;16 hex&gt;, or the 77-character collision id b-unassigned-&lt;64 hex&gt;.</summary>
        public static bool IsChunkId(string chunkId)
        {
            if (string.IsNullOrEmpty(chunkId)) return false;
            if (CollisionId.IsMatch(chunkId)) return chunkId.Length == 77;
            var m = OrdinaryId.Match(chunkId);
            return m.Success && chunkId.Length == m.Length && Array.IndexOf(Boundaries, m.Groups[1].Value) >= 0;
        }

        /// <summary>The chunk of rows sharing one suite and boundary.</summary>
        public static RcBoundaryChunk NewChunk(RcBoundaryRow[] rows) => NewChunk(rows, CanonicalDigest);

        private static RcBoundaryChunk NewChunk(RcBoundaryRow[] rows, Func<string, string, RcBoundaryIdentity[], string> digest)
        {
            if (rows == null || rows.Length == 0 || rows.Any(r => r == null)) throw Chunk("empty membership");
            var suite = rows[0].Suite ?? "";
            var boundary = rows[0].Boundary ?? "";
            if (rows.Any(r => (r.Suite ?? "") != suite || (r.Boundary ?? "") != boundary)) throw Chunk("mixed suite/boundary");
            return ChunkData(suite, boundary, rows.Select(r => r.IdentityKey ?? "").ToArray(), rows.Select(r => r.CaseKey ?? "").ToArray(), true, digest);
        }

        /// <summary>
        /// One chunk per membership; an identity may occur once per suite whatever its boundary. Chunks whose 16-hex
        /// prefix is shared by different full digests are rebuilt as unassigned, collision and always dirty, with
        /// chunk id b-unassigned-&lt;full digest&gt;.
        /// </summary>
        public static RcBoundaryChunk[] ResolveChunks(RcBoundaryMembership[] memberships) => ResolveChunks(memberships, CanonicalDigest);

        /// <summary>ResolveChunks with the canonical digest replaced (a test forces a prefix collision this way).</summary>
        public static RcBoundaryChunk[] ResolveChunks(RcBoundaryMembership[] memberships, Func<string, string, RcBoundaryIdentity[], string> digest)
        {
            var built = new List<RcBoundaryChunk>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var membership in memberships ?? Array.Empty<RcBoundaryMembership>())
            {
                var suite = membership?.Suite ?? "";
                var rows = new List<RcBoundaryRow>();
                foreach (var member in membership?.Identities ?? new RcBoundaryMember[] { null })
                {
                    var identityKey = member?.IdentityKey ?? "";
                    var caseKey = member?.CaseKey ?? "";
                    var key = suite.Length.ToString(CultureInfo.InvariantCulture) + ":" + suite + identityKey.Length.ToString(CultureInfo.InvariantCulture) + ":" + identityKey
                        + caseKey.Length.ToString(CultureInfo.InvariantCulture) + ":" + caseKey;
                    if (!seen.Add(key)) throw Chunk("duplicate membership across chunks");
                    rows.Add(new RcBoundaryRow { Suite = suite, Boundary = membership?.Boundary ?? "", IdentityKey = identityKey, CaseKey = caseKey });
                }
                built.Add(NewChunk(rows.ToArray(), digest));
            }
            var fullByPrefix = built.GroupBy(c => c.FullDigest.Substring(0, 16), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.FullDigest).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
            var result = built.Select(chunk =>
            {
                if (fullByPrefix[chunk.FullDigest.Substring(0, 16)] <= 1) return chunk;
                var rows = chunk.IdentityKeys.Select((key, i) => new RcBoundaryRow { Suite = chunk.Suite, Boundary = "unassigned", IdentityKey = key, CaseKey = chunk.CaseKeys[i] }).ToArray();
                var rebuilt = NewChunk(rows, digest);
                rebuilt.Collision = true;
                rebuilt.ChunkId = "b-unassigned-" + rebuilt.FullDigest;
                return rebuilt;
            }).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in result) if (!ids.Add(chunk.ChunkId)) throw Chunk("duplicate resolved chunk id");
            return result;
        }
    }
}
