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
            var identity = Digest(Encoding.UTF8.GetBytes(string.Join("\n", new[]
            {
                "antiphon-rc-type-proof-v1", "proven-subtraction=true", algorithmDigest, policyDigest,
                baseline.Source, baseline.Digest, baseline.Manifest, candidate.Source, candidate.Digest, candidate.Manifest,
            })));
            return new RcTypeProofComparison(baseline, candidate, identity);
        }

        internal bool Matches(string baseline, string candidate) =>
            _baseline.Source == baseline && _candidate.Source == candidate;

        internal bool DropIndexHit(string side, string path, string legacyName, IReadOnlyList<RcPlannedChunk> chunks)
        {
            var endpoint = side == "B" ? _baseline : _candidate;
            var name = Simple(legacyName);
            if (!endpoint.Members.TryGetValue(name, out var identities) || identities.Length != 1)
                return false;
            var id = identities[0];
            if (!endpoint.Before.TryGetValue((path, id), out var original) ||
                original.Length != 1 || original[0] != "type" || endpoint.After.ContainsKey((path, id)))
                return false;

            var origins = endpoint.Origins[(path, id)];
            if (!origins.Any() || origins.Any(origin => !origin.Proven || origin.Retained.Contains(path, StringComparer.Ordinal)))
                return false;

            var matching = chunks.Where(chunk => chunk.Classes.Any(cls => Simple(cls) == name)).ToArray();
            return matching.Length > 0 && matching.All(chunk => chunk.Project == id.Project
                && chunk.Classes.Any(cls => cls == id.Type) && chunk.Cases.Length > 0);
        }

        private sealed record Member(string Project, string Type);
        private sealed record Site(Member Consumer, string[] Candidates, string[] Retained, bool Proven);
        private sealed record Endpoint(string Source, string Digest, string Manifest, Dictionary<string, Member[]> Members,
            Dictionary<(string Path, Member Id), string[]> Before, Dictionary<(string Path, Member Id), string[]> After,
            ILookup<(string Path, Member Id), Site> Origins);

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
                    !Hex(Text(root, "manifestDigest"), 64))
                    throw new InvalidDataException("type-proof-endpoint-unbound");
                var before = root.GetProperty("conservative");
                var after = root.GetProperty("graph");
                var members = before.GetProperty("universe").EnumerateArray().Select(Identity)
                    .GroupBy(member => Simple(member.Type), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Distinct().ToArray(), StringComparer.Ordinal);
                var sites = root.GetProperty("typeSites").EnumerateArray().Select(site =>
                {
                    var origin = site.GetProperty("origin");
                    return new Site(Identity(origin.GetProperty("consumer")), Strings(origin, "candidates"),
                        Strings(site, "retained"), Text(site, "rule") == "unique-source-type"
                            && Hex(Text(site, "sourceSha256"), 64) && Hex(Text(site, "contextDigest"), 64));
                }).ToLookup(site => site.Consumer);
                return new Endpoint(sha, expectedDigest, Text(root, "manifestDigest"), members, Edges(before), Edges(after),
                    sites.SelectMany(group => group.SelectMany(site => site.Candidates.Select(candidate => (candidate, site))))
                        .ToLookup(row => (row.candidate, row.site.Consumer), row => row.site));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidDataException("type-proof-artifact-malformed", ex);
            }
        }

        private static Dictionary<(string Path, Member Id), string[]> Edges(JsonElement graph) =>
            graph.GetProperty("edges").EnumerateArray().SelectMany(edge => edge.GetProperty("classes").EnumerateArray()
                .Select(consumer => (Path: Text(edge, "path"), Id: Identity(consumer), Kinds: Strings(consumer, "kinds"))))
                .ToDictionary(row => (row.Path, row.Id), row => row.Kinds);

        private static Member Identity(JsonElement row) => new Member(Text(row, "project"), Text(row, "fullName"));

        private static string Simple(string name) => name.Substring(name.LastIndexOf('.') + 1);
        private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString() ?? "";
        private static string[] Strings(JsonElement row, string key) =>
            row.GetProperty(key).EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        private static bool Hex(string value, int length) =>
            value != null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
