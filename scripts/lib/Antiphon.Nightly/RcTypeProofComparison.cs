#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public sealed record RcRemovedIndexHit(string Side, string Path, string Class);
    public sealed record RcChunkProofTrace(string Identity, string BaselineGraphDigest, string CandidateGraphDigest, RcRemovedIndexHit[] Removed);

    public sealed class RcTypeProofComparison
    {
        private const string GraphSchema = "antiphon-release-graph-artifact-v2";
        private readonly Endpoint _baseline;
        private readonly Endpoint _candidate;

        private RcTypeProofComparison(Endpoint baseline, Endpoint candidate, string identity)
        {
            _baseline = baseline;
            _candidate = candidate;
            Identity = identity;
        }

        public string Identity { get; }
        public string BaselineGraphDigest => _baseline.Digest;
        public string CandidateGraphDigest => _candidate.Digest;

        /// <summary>Read the externally pinned diagnostic graph pair for the same target. Qualification owns these pins.</summary>
        public static RcTypeProofComparison Read(string baselineArtifact, string baselineDigest, string baselineSha,
            string candidateArtifact, string candidateDigest, string candidateSha, string algorithmDigest, string policyDigest)
        {
            if (!Hex(algorithmDigest, 64) || !Hex(policyDigest, 64))
                throw new InvalidDataException("type-proof-algorithm-or-policy-unbound");
            var baseline = ReadEndpoint(baselineArtifact, baselineDigest, baselineSha);
            var candidate = ReadEndpoint(candidateArtifact, candidateDigest, candidateSha);
            if (baseline.Producer != candidate.Producer || !baseline.Targets.SequenceEqual(candidate.Targets))
                throw new InvalidDataException("type-proof-endpoint-target-or-producer-mismatch");
            var identity = Digest(Encoding.UTF8.GetBytes(string.Join("\n", new[]
            {
                "antiphon-rc-type-proof-v1", "proven-subtraction=true", algorithmDigest, policyDigest,
                ReleaseImpactEvidence.RunningSelectorBinaryDigest(), baseline.Producer,
                baseline.Source, baseline.Digest, baseline.Manifest, candidate.Source, candidate.Digest, candidate.Manifest,
            })));
            return new RcTypeProofComparison(baseline, candidate, identity);
        }

        internal bool Matches(string baseline, string candidate) =>
            _baseline.Source == baseline && _candidate.Source == candidate;

        internal bool RosterBound(IReadOnlyList<RcPlannedChunk> chunks)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in chunks)
            {
                if (chunk.CaseRoster.Length == 0 || chunk.Cases.Length != chunk.CaseRoster.Length ||
                    !chunk.Cases.ToHashSet(StringComparer.Ordinal).SetEquals(chunk.CaseRoster.Select(member => member.CaseKey)) ||
                    !chunk.Classes.ToHashSet(StringComparer.Ordinal).SetEquals(chunk.CaseRoster.Select(member => member.Class)))
                    return false;
                foreach (var member in chunk.CaseRoster)
                    if (string.IsNullOrWhiteSpace(member.Assembly) || string.IsNullOrWhiteSpace(member.Method) ||
                        string.IsNullOrWhiteSpace(member.Class) || string.IsNullOrWhiteSpace(member.CaseKey) || !keys.Add(member.CaseKey))
                        return false;
            }
            return chunks.Count > 0;
        }

        internal bool DropIndexHit(string side, string path, string legacyName, IReadOnlyList<RcPlannedChunk> chunks)
        {
            var endpoint = side == "B" ? _baseline : _candidate;
            var name = Simple(legacyName);
            if (!endpoint.Members.TryGetValue(name, out var identities) || identities.Length != 1)
                return false;
            var id = identities[0];
            if (endpoint.LegacyReaders.Contains((path, name))) return false;
            if (!endpoint.Before.TryGetValue((path, id), out var original) ||
                original.Length != 1 || original[0] != "type" || endpoint.After.ContainsKey((path, id)))
                return false;

            var origins = endpoint.Origins[(path, id)];
            if (!origins.Any() || origins.Any(origin => !origin.Proven || origin.Retained.Contains(path, StringComparer.Ordinal)))
                return false;

            var matching = chunks.Where(chunk => chunk.Classes.Any(cls => Simple(cls) == name)).ToArray();
            return matching.Length > 0 && matching.All(chunk => chunk.Project == id.Project
                && chunk.Classes.Any(cls => cls == id.Type)
                && endpoint.Assemblies.TryGetValue(id.Project, out var assemblies) && assemblies.Length == 1
                && chunk.CaseRoster.Where(member => member.Class == id.Type).All(member => member.Assembly == assemblies[0]));
        }

        internal IEnumerable<(string Name, string Reason)> Independent(string side, string path)
        {
            var endpoint = side == "B" ? _baseline : _candidate;
            foreach (var row in endpoint.ReadersByPath[path])
                yield return (row.Class, row.Kind + ":" + side + ":" + path);
            foreach (var row in endpoint.RetainedByPath[path])
                foreach (var kind in row.Value)
                    yield return (row.Key.Id.Type, "release-relation-" + kind + ":" + side + ":" + path);
        }

        private sealed record Member(string Project, string Type);
        private sealed record Site(Member Consumer, string[] Candidates, string[] Retained, bool Proven);
        private sealed record LegacyReader(string Path, string Class, string Kind);
        private sealed record Endpoint(string Source, string Digest, string Manifest, string Producer, string[] Targets,
            Dictionary<string, Member[]> Members, Dictionary<string, string[]> Assemblies, HashSet<(string Path, string Class)> LegacyReaders,
            Dictionary<(string Path, Member Id), string[]> Before, Dictionary<(string Path, Member Id), string[]> After,
            ILookup<(string Path, Member Id), Site> Origins, ILookup<string, LegacyReader> ReadersByPath,
            ILookup<string, KeyValuePair<(string Path, Member Id), string[]>> RetainedByPath);

        private static Endpoint ReadEndpoint(string path, string expectedDigest, string sha)
        {
            if (!Hex(expectedDigest, 64) || !Hex(sha, 40))
                throw new InvalidDataException("type-proof-authority-unbound");
            using var stream = File.OpenRead(path);
            if (stream.Length <= 0 || stream.Length > 128L * 1024 * 1024)
                throw new InvalidDataException("type-proof-size-unbound");
            var raw = new byte[(int)stream.Length];
            stream.ReadExactly(raw);
            if (stream.ReadByte() != -1 || Digest(raw) != expectedDigest)
                throw new InvalidDataException("type-proof-artifact-digest-mismatch");

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (Text(root, "schema") != GraphSchema || Text(root, "sourceSha") != sha ||
                    !root.GetProperty("provenSubtraction").GetBoolean() || !root.GetProperty("proofEligible").GetBoolean() ||
                    !Hex(Text(root, "manifestDigest"), 64) || !Hex(Text(root, "producerBinarySha256"), 64))
                    throw new InvalidDataException("type-proof-endpoint-unbound");
                var before = root.GetProperty("conservative");
                var after = root.GetProperty("graph");
                var contexts = root.GetProperty("compilerContexts").EnumerateArray().ToArray();
                var targets = contexts.Select(context => string.Join("\0",
                    Text(context, "operatingSystem"), Text(context, "architecture"), Text(context, "framework"),
                    Text(context, "runtimeIdentifier"), Text(context, "configuration"), Text(context, "platform")))
                    .Distinct(StringComparer.Ordinal).OrderBy(target => target, StringComparer.Ordinal).ToArray();
                if (targets.Length == 0) throw new InvalidDataException("type-proof-target-unbound");
                var assemblies = contexts.GroupBy(context => Text(context, "project"), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Select(context => Text(context, "assembly"))
                        .Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
                var readers = root.GetProperty("legacyReaders").EnumerateArray()
                    .Select(row => new LegacyReader(Text(row, "path"), Simple(Text(row, "class")), Text(row, "kind"))).ToArray();
                var kept = Edges(after);
                var members = before.GetProperty("universe").EnumerateArray().Select(ReadMember)
                    .GroupBy(member => Simple(member.Type), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Distinct().ToArray(), StringComparer.Ordinal);
                var sites = root.GetProperty("typeSites").EnumerateArray().Select(site =>
                {
                    var origin = site.GetProperty("origin");
                    return new Site(ReadMember(origin.GetProperty("consumer")), Strings(origin, "candidates"),
                        Strings(site, "retained"), Text(site, "rule") == "unique-source-type"
                            && Hex(Text(site, "sourceSha256"), 64) && Hex(Text(site, "contextDigest"), 64));
                }).ToLookup(site => site.Consumer);
                return new Endpoint(sha, expectedDigest, Text(root, "manifestDigest"), Text(root, "producerBinarySha256"), targets,
                    members, assemblies, readers.Select(reader => (reader.Path, reader.Class)).ToHashSet(), Edges(before), kept,
                    sites.SelectMany(group => group.SelectMany(site => site.Candidates.Select(candidate => (candidate, site))))
                        .ToLookup(row => (row.candidate, row.site.Consumer), row => row.site),
                    readers.ToLookup(reader => reader.Path, StringComparer.Ordinal), kept.ToLookup(row => row.Key.Path, StringComparer.Ordinal));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidDataException("type-proof-artifact-malformed", ex);
            }
        }

        private static Dictionary<(string Path, Member Id), string[]> Edges(JsonElement graph) =>
            graph.GetProperty("edges").EnumerateArray().SelectMany(edge => edge.GetProperty("classes").EnumerateArray()
                .Select(consumer => (Path: Text(edge, "path"), Id: ReadMember(consumer), Kinds: Strings(consumer, "kinds"))))
                .ToDictionary(row => (row.Path, row.Id), row => row.Kinds);

        private static Member ReadMember(JsonElement row) => new Member(Text(row, "project"), Text(row, "fullName"));

        private static string Simple(string name) => name.Substring(name.LastIndexOf('.') + 1);
        private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString() ?? "";
        private static string[] Strings(JsonElement row, string key) =>
            row.GetProperty(key).EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        private static bool Hex(string value, int length) =>
            value != null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
