#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public sealed record RcMissDiff(string Status, string? OldPath, string? NewPath);
    public sealed record RcMissCase(string Kind, string? Assembly, string? Class, string? Method, string? CaseKey, string? Outcome);
    public sealed record RcSelectionMissObservation(string ObservedBy, string ObservedAtUtc, string Repository, string Line, string Suite,
        string Platform, string Architecture, string Profile, string BaselineSha, string CandidateSha, string Run, string Attempt,
        string Chunk, string ContentDigest, RcMissCase Case, string Actual, string Predicted,
        string? Trx, string? TrxSha256, string PredictionDigest, string ResolvedDigest, string? OriginDigest, string SelectorIdentity,
        string PolicyDigest, string? BaselineGraph, string? CandidateGraph, RcRemovedIndexHit[] Removed, RcMissDiff[] Diff, string Attribution);
    public sealed record RcSelectionMissRecord(string MissId, string Path, string Sha256);
    public sealed record RcSelectionMissDisposition(string Attribution, string? Fixture, string? CauseCard,
        string? WhyMissed, string? ChangedRule, string? FixSha, string? RerunDigest);

    public static class RcSelectionMiss
    {
        private const string Schema = "antiphon-rc-selection-miss-v1";
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        };

        public static RcSelectionMissRecord Record(string outputRoot, RcSelectionMissObservation observation)
        {
            if (observation.Predicted != "carry" || observation.Actual == "clean")
                throw new InvalidDataException("miss-outcome-not-applicable");
            var caseKey = observation.Case;
            var parts = new[]
            {
                Schema, observation.Repository, observation.Line, observation.Platform, observation.Architecture,
                observation.Profile, observation.BaselineSha, observation.CandidateSha, observation.Run, observation.Attempt,
                observation.Suite, observation.Chunk, observation.ContentDigest, caseKey.Kind, caseKey.Assembly ?? "",
                caseKey.Class ?? "", caseKey.Method ?? "", caseKey.CaseKey ?? "",
            };
            if (parts.Any(part => part.Contains('\0'))) throw new InvalidDataException("miss-identity-invalid");
            var id = Digest(Encoding.UTF8.GetBytes(string.Join('\0', parts)));
            var relative = "misses/" + id + ".json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = Schema, missId = id, observation }, Json);
            Persist(Path.Combine(outputRoot, "misses", id + ".json"), bytes);
            return new RcSelectionMissRecord(id, relative, Digest(bytes));
        }

        public static RcSelectionMissRecord AppendDisposition(string outputRoot, string missId, string observationSha,
            RcSelectionMissDisposition disposition)
        {
            if (!RcChunkReceipt.IsDigest(missId) || !RcChunkReceipt.IsDigest(observationSha)
                || disposition.Attribution is not ("unresolved" or "absent-reader" or "wrongly-dropped"))
                throw new InvalidDataException("miss-disposition-identity-invalid");
            var original = Path.Combine(outputRoot, "misses", missId + ".json");
            if (!File.Exists(original) || File.GetAttributes(original).HasFlag(FileAttributes.ReparsePoint)
                || Digest(File.ReadAllBytes(original)) != observationSha)
                throw new InvalidDataException("miss-observation-binding-mismatch");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "antiphon-rc-selection-miss-disposition-v1", missId, observationSha, status = "open", disposition,
            }, Json);
            var digest = Digest(bytes);
            var relative = "misses/dispositions/" + missId + "/" + digest + ".json";
            Persist(Path.Combine(outputRoot, relative), bytes);
            return new RcSelectionMissRecord(missId, relative, digest);
        }

        private static void Persist(string destination, byte[] bytes)
        {
            var parent = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parent);
            if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("miss-output-linked");
            var pending = Path.Combine(parent, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                try { File.Move(pending, destination, overwrite: false); }
                catch (IOException) when (File.Exists(destination))
                {
                    if (File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)
                        || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
                        throw new InvalidDataException("miss-id-content-conflict");
                }
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
        }

        internal static RcMissDiff[] ReadDiff(string repository, string baseline, string candidate)
        {
            var fields = TestSelectionGit.Run(repository, new[] { "diff", "--no-ext-diff", "--name-status", "-z",
                "--find-renames", baseline, candidate, "--" }).Stdout.Split('\0');
            var rows = new List<RcMissDiff>();
            for (var i = 0; i < fields.Length && fields[i].Length > 0;)
            {
                var status = fields[i++];
                var path = Next();
                if (status.StartsWith("R", StringComparison.Ordinal) || status.StartsWith("C", StringComparison.Ordinal))
                    rows.Add(new(status, path, Next()));
                else
                    rows.Add(new(status, status == "A" ? null : path, status == "D" ? null : path));

                string Next() => i < fields.Length && fields[i].Length > 0 ? fields[i++]
                    : throw new InvalidDataException("miss-diff-incomplete");
            }
            return rows.ToArray();
        }

        private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
