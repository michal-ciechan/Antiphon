// CARD-1820 S1R1: the release-impact evidence envelope (antiphon-release-impact-v1), impacted-classes --release-evidence.
// The 2026-11-22 plan (2026-11-22-remaining-ps1-to-csharp.md, unit 4) ported it from scripts/lib/release-impact.ps1
// (New-ReleaseImpactEnvelope, Get-ReleaseImpactAlgorithmIdentity, ConvertTo-ReleaseImpactJson), which is deleted.
//
// Release evidence is separate from the advisory schema-1 index. No release graph is admitted yet, so the envelope is
// always incomplete with fallback Full: unknown identities stay null, classes stay empty and the target is unverified.
// The endpoints must be exact full lowercase commit ids of the repository. The algorithm identity binds the bytes being
// executed: the running selector's embedded sources (ImpactedClasses.SelectorFiles) must be the source checkout's
// committed blobs, byte for byte (no clean filter, no line-ending normalisation), else the request is refused. Plain C#.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>One release-impact envelope. Only the incomplete/Full contract is producible.</summary>
    public sealed class ReleaseImpactEnvelope
    {
        /// <summary>The schema.</summary>
        public string Schema { get; init; } = ReleaseImpactEvidence.Schema;

        /// <summary>The repository's common git directory, forward slashes.</summary>
        public string Repository { get; init; }

        /// <summary>The baseline commit.</summary>
        public string BaselineSha { get; init; }

        /// <summary>The candidate commit.</summary>
        public string CandidateSha { get; init; }

        /// <summary>The source checkout's commit the algorithm runs from.</summary>
        public string AlgorithmSha { get; init; }

        /// <summary>The digest of the algorithm sources' committed blobs.</summary>
        public string AlgorithmDigest { get; init; }

        /// <summary>linux or windows.</summary>
        public string Platform { get; init; }

        /// <summary>Always false: no release graph.</summary>
        public bool Complete { get; init; }

        /// <summary>Why it is incomplete.</summary>
        public IReadOnlyList<string> IncompleteReasons { get; init; } = new[] { "release-graph-unavailable" };

        /// <summary>Always Full.</summary>
        public string Fallback { get; init; } = "Full";

        /// <summary>The target was verified (never yet).</summary>
        public bool TargetVerified { get; init; }

        /// <summary>The selected classes (none without a graph).</summary>
        public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();

        /// <summary>The graph identities (null: unverified).</summary>
        public string InputManifestDigest { get; init; }

        /// <summary>The baseline graph digest (null: unverified).</summary>
        public string BaselineGraphDigest { get; init; }

        /// <summary>The candidate graph digest (null: unverified).</summary>
        public string CandidateGraphDigest { get; init; }

        /// <summary>The universe digest (null: unverified).</summary>
        public string UniverseDigest { get; init; }
    }

    /// <summary>The release-impact envelope and its algorithm identity.</summary>
    public static class ReleaseImpactEvidence
    {
        internal static string RunningSelectorBinaryDigest()
        {
            using var stream = File.OpenRead(typeof(RcChunkSelection).Assembly.Location);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>The envelope schema.</summary>
        public const string Schema = "antiphon-release-impact-v1";

        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Sha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Invoke-ReleaseImpactGit: git with the arguments in root, without GIT_DIR, GIT_WORK_TREE or GIT_INDEX_FILE, 60 s to
        /// exit and 10 s to drain; any failure throws. Stdout trimmed.
        /// </summary>
        public static string Git(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(key);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var child = Process.Start(start) ?? throw new InvalidOperationException("release-impact Git operation did not start");
            try
            {
                var stdout = child.StandardOutput.ReadToEndAsync();
                var stderr = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(60000)) throw new InvalidOperationException("release-impact Git operation timed out");
                if (!Task.WaitAll(new Task[] { stdout, stderr }, 10000)) throw new InvalidOperationException("release-impact Git output drain timed out");
                if (child.ExitCode != 0) throw new InvalidOperationException("release-impact Git input could not be verified");
                return stdout.Result.Trim();
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    if (!child.WaitForExit(30000)) throw new InvalidOperationException("release-impact Git teardown timed out");
                }
                child.Dispose();
            }
        }

        /// <summary>
        /// Get-ReleaseImpactAlgorithmIdentity: the source checkout's HEAD commit, and the digest of "path NUL blob LF" over
        /// ImpactedClasses.SelectorFiles at that commit. Each committed blob must be the running selector's own bytes
        /// (release-impact algorithm source drift otherwise).
        /// </summary>
        public static (string Sha, string Digest) AlgorithmIdentity(string sourceRoot)
        {
            var sha = Git(sourceRoot, "rev-parse", "--verify", "HEAD^{commit}");
            if (!FullSha.IsMatch(sha)) throw new InvalidOperationException("release-impact algorithm SHA is unsupported");
            var inventory = new StringBuilder();
            foreach (var path in ImpactedClasses.SelectorFiles)
            {
                var blob = Git(sourceRoot, "rev-parse", "--verify", sha + ":" + path);
                if (blob != BlobId(ImpactedClasses.RunningSource(path), blob.Length)) throw new InvalidOperationException("release-impact algorithm source drift");
                inventory.Append(path).Append('\0').Append(blob).Append('\n');
            }
            return (sha, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inventory.ToString()))));
        }

        /// <summary>Git's object id of a blob of these bytes: SHA-1 for a 40-character id, SHA-256 for a 64-character one.</summary>
        public static string BlobId(byte[] bytes, int idLength)
        {
            var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");
            var data = header.Concat(bytes).ToArray();
            return idLength == 64 ? Convert.ToHexStringLower(SHA256.HashData(data)) : Convert.ToHexStringLower(SHA1.HashData(data));
        }

        /// <summary>
        /// New-ReleaseImpactEnvelope: both endpoints exact full lowercase commit ids of the repository, a linux or windows
        /// target, the repository's common git directory and the algorithm identity of sourceRoot. Throws with the reason.
        /// </summary>
        public static ReleaseImpactEnvelope Envelope(string repositoryRoot, string baseline, string candidate, string platform, string sourceRoot)
        {
            foreach (var sha in new[] { baseline, candidate })
            {
                if (sha == null || !FullSha.IsMatch(sha)) throw new InvalidOperationException("release-impact endpoints must be full lowercase 40-hex commit SHAs");
                if (Git(repositoryRoot, "rev-parse", "--verify", sha + "^{commit}") != sha) throw new InvalidOperationException("release-impact endpoint is not the exact commit");
            }
            if (platform != "linux" && platform != "windows") throw new InvalidOperationException("release-impact target must be linux or windows");
            var common = Git(repositoryRoot, "rev-parse", "--git-common-dir");
            if (!Path.IsPathRooted(common)) common = Path.Combine(repositoryRoot, common);
            var algorithm = AlgorithmIdentity(sourceRoot);
            return new ReleaseImpactEnvelope
            {
                Repository = Path.GetFullPath(common).Replace('\\', '/'), BaselineSha = baseline, CandidateSha = candidate,
                AlgorithmSha = algorithm.Sha, AlgorithmDigest = algorithm.Digest, Platform = platform,
            };
        }

        /// <summary>
        /// ConvertTo-ReleaseImpactJson: only the incomplete/Full contract with SHA-bound endpoints, a repository, an
        /// algorithm digest, a linux or windows target and no graph identity is written; anything else throws.
        /// </summary>
        public static string Json(ReleaseImpactEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            // Graph support must bring its own completeness validation before this gate can accept true or any classes.
            if (envelope.Schema != Schema || envelope.Complete || envelope.Fallback != "Full" || envelope.IncompleteReasons == null || envelope.IncompleteReasons.Count == 0
                || (envelope.Classes?.Count ?? 0) != 0 || envelope.TargetVerified)
                throw new InvalidOperationException("release-impact envelope refused: incomplete/Full evidence is required");
            foreach (var sha in new[] { envelope.BaselineSha, envelope.CandidateSha, envelope.AlgorithmSha })
                if (sha == null || !FullSha.IsMatch(sha)) throw new InvalidOperationException("release-impact envelope refused: invalid SHA binding");
            if (string.IsNullOrWhiteSpace(envelope.Repository) || envelope.AlgorithmDigest == null || !Sha256.IsMatch(envelope.AlgorithmDigest)
                || (envelope.Platform != "linux" && envelope.Platform != "windows"))
                throw new InvalidOperationException("release-impact envelope refused: invalid repository, algorithm or target");
            if (envelope.InputManifestDigest != null || envelope.BaselineGraphDigest != null || envelope.CandidateGraphDigest != null || envelope.UniverseDigest != null)
                throw new InvalidOperationException("release-impact envelope refused: graph identity is unverified");
            var target = new ImpactedRecord().Add("platform", envelope.Platform).Add("profile", null).Add("framework", null).Add("rid", null)
                .Add("buildProperties", null).Add("toolchain", null).Add("verified", envelope.TargetVerified);
            var compatibility = new ImpactedRecord().Add("policy", null).Add("catalog", null).Add("selector", null).Add("runtime", null)
                .Add("projections", Array.Empty<string>()).Add("globalIncompatibilityReasons", new[] { "release-graph-unavailable" });
            var record = new ImpactedRecord()
                .Add("schema", envelope.Schema).Add("repository", envelope.Repository).Add("baselineSha", envelope.BaselineSha).Add("candidateSha", envelope.CandidateSha)
                .Add("algorithmSha", envelope.AlgorithmSha).Add("algorithmDigest", envelope.AlgorithmDigest).Add("target", target)
                .Add("inputManifestDigest", envelope.InputManifestDigest).Add("baselineGraphDigest", envelope.BaselineGraphDigest)
                .Add("candidateGraphDigest", envelope.CandidateGraphDigest).Add("universeDigest", envelope.UniverseDigest).Add("classes", envelope.Classes ?? Array.Empty<string>())
                .Add("complete", envelope.Complete).Add("incompleteReasons", envelope.IncompleteReasons).Add("fallback", envelope.Fallback).Add("compatibility", compatibility)
                // Not computed in this slice. Advisory filters never substitute for a universe.
                .Add("legacySelection", null);
            return ImpactedJson.Text(record);
        }
    }
}
