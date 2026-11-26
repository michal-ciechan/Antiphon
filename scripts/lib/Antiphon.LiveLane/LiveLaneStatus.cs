// CARD-1545 S2a-1/S2a-2/S2b: the live-lane status, ported from scripts/live-lane-status.ps1 and the status functions of
// live-lane-fingerprint.ps1 and live-lane-receipt.ps1 (W2 slice S22; the scripts are deleted), as the CLI command
// live-lane-status. Shadow-only: no state carries gate credit.
//
// For each lane (--lane, or every lane the candidate's tests/live-lane-inputs.json declares) it prints the state document
// the script printed (antiphon-live-lane-status-v1: without --history every lane is DUE; with --history and --context
// the receipts decide, D-6 under --reuse-policy) plus one key the script did not have, "claude": the Claude versions the
// lane's receipts ran with for this code digest (the fingerprint without the attested environment), the newest receipt of
// each version deciding its outcome, and the versions --require-claude-version names. Status is keyed by (code digest,
// Claude version). --json prints one canonical document per lane per line (the script's text plus "claude"); otherwise a
// short table. Exit 0; 2 when a lane is INVALID (the document says why, as the script's did); 1 when a required Claude
// version has no passing receipt. With no requirement the rule is today's: one passing receipt is enough.
//
// The requirement lives on the command line, not in the lane declaration: tests/live-lane-inputs.json is a lane contract
// file, so a field there would move the fingerprint and stale every receipt whenever the set of versions changed, which is
// the opposite of checking one unchanged digest against more versions. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>The Claude versions of one lane's receipts for one code digest, and the requirement against them.</summary>
    public sealed class LiveLaneClaudeVersions
    {
        public string CodeDigest { get; set; }
        /// <summary>One entry per version: the newest receipt of that version decides (GREEN passes).</summary>
        public IReadOnlyList<LiveLaneMap> Versions { get; set; } = Array.Empty<LiveLaneMap>();
        public IReadOnlyList<string> Passing { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Failing { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Required { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Missing { get; set; } = Array.Empty<string>();
        /// <summary>Every required version passes; with none required, at least one version passes (one receipt is enough).</summary>
        public bool Satisfied => Required.Count > 0 ? Missing.Count == 0 : Passing.Count > 0;

        public LiveLaneMap ToJson() => new LiveLaneMap(("codeDigest", CodeDigest), ("versions", Versions.Cast<object>().ToList()),
            ("passing", Passing.Cast<object>().ToList()), ("failing", Failing.Cast<object>().ToList()), ("required", Required.Cast<object>().ToList()), ("missing", Missing.Cast<object>().ToList()),
            ("satisfied", Satisfied));
    }

    /// <summary>One lane's status: the document, the exit it asks for, and the parts the table shows.</summary>
    public sealed class LiveLaneStatusResult
    {
        public string LaneId { get; set; }
        /// <summary>The document (the script's, plus "claude" when the lane was not refused).</summary>
        public LiveLaneMap Document { get; set; }
        public int ExitCode { get; set; }
        public LiveLaneClaudeVersions Claude { get; set; }
    }

    /// <summary>live-lane-status's flags.</summary>
    public sealed class LiveLaneStatusOptions
    {
        public const string Usage = "live-lane-status [--lane <id>] [--candidate-sha <sha>] [--repository-root <dir>] [--attestation <file>] [--history <dir> --context <file>"
            + " [--reuse-policy AlwaysRerun|Bounded24h] [--use-record <file>]] [--require-claude-version <v>[,<v>...]] [--json]";

        private static readonly string[] Valued = { "--lane", "--candidate-sha", "--repository-root", "--attestation", "--history", "--context", "--reuse-policy", "--use-record", "--require-claude-version" };

        /// <summary>null: every declared lane; "" is the script's -Lane '' (lane-required).</summary>
        public string Lane { get; private set; }
        /// <summary>null: the repository's HEAD.</summary>
        public string CandidateSha { get; private set; }
        public string RepositoryRoot { get; private set; }
        public string Attestation { get; private set; }
        public string History { get; private set; }
        public string Context { get; private set; }
        public string ReusePolicy { get; private set; } = LiveLaneAttestation.AlwaysRerun;
        public string UseRecord { get; private set; }
        public IReadOnlyList<string> RequiredClaudeVersions { get; private set; } = Array.Empty<string>();
        public bool Json { get; private set; }

        public static LiveLaneStatusOptions Parse(IReadOnlyList<string> arguments, string defaultRoot)
        {
            var values = LiveLaneArgs.Parse(arguments, Valued, new[] { "--json" });
            var required = new List<string>();
            var requirement = values.Value("--require-claude-version");
            if (requirement != null)
                foreach (var part in requirement.Split(','))
                {
                    var token = part.Trim();
                    if (token.Length == 0 || token == LiveLaneClaudeVersion.UnknownToken || !LiveLaneClaudeVersion.IsToken(token))
                        throw new LiveLaneUsageException("--require-claude-version takes major.minor.patch versions, comma-separated: '" + part + "'");
                    if (!required.Contains(token, StringComparer.Ordinal)) required.Add(token);
                }
            var options = new LiveLaneStatusOptions
            {
                Lane = values.Value("--lane"), CandidateSha = values.Value("--candidate-sha"),
                RepositoryRoot = values.Value("--repository-root") ?? defaultRoot ?? Environment.CurrentDirectory,
                Attestation = values.Value("--attestation") ?? "", History = values.Value("--history") ?? "", Context = values.Value("--context") ?? "",
                ReusePolicy = values.Value("--reuse-policy") ?? LiveLaneAttestation.AlwaysRerun, UseRecord = values.Value("--use-record") ?? "",
                RequiredClaudeVersions = required, Json = values.Switch("--json")
            };
            if (options.UseRecord.Length > 0 && options.Lane == null) throw new LiveLaneUsageException("--use-record needs --lane (one lane writes one use record)");
            return options;
        }

        /// <summary>The same options for one lane and candidate (what the script's parameters were).</summary>
        public LiveLaneStatusOptions For(string lane, string candidateSha)
        {
            var copy = (LiveLaneStatusOptions)MemberwiseClone();
            copy.Lane = lane;
            copy.CandidateSha = candidateSha;
            return copy;
        }

        /// <summary>The options of a direct call (tests, the parity cases): the script's parameters.</summary>
        public static LiveLaneStatusOptions Of(string root, string lane, string candidateSha, string attestation = "", string history = "", string context = "",
            string reusePolicy = LiveLaneAttestation.AlwaysRerun, string useRecord = "", IReadOnlyList<string> required = null) =>
            new LiveLaneStatusOptions
            {
                RepositoryRoot = root, Lane = lane, CandidateSha = candidateSha, Attestation = attestation ?? "", History = history ?? "", Context = context ?? "",
                ReusePolicy = reusePolicy, UseRecord = useRecord ?? "", RequiredClaudeVersions = required ?? Array.Empty<string>()
            };
    }

    /// <summary>The status functions and the live-lane-status command.</summary>
    public static class LiveLaneStatus
    {
        public const string Name = "live-lane-status";
        public const string Format = "antiphon-live-lane-status-v1";

        private static readonly Regex Reason = new Regex("^[a-z][a-z0-9-]*(:[^\r\n]{0,200})?$");
        private const string ContextKeys = "account,host,nonce,nowUtc,probes,ref,repository,revocations";

        private static LiveLaneMap FingerprintJson(LiveLaneFingerprintResult fingerprint) => new LiveLaneMap(
            ("status", fingerprint.Status), ("digest", fingerprint.Digest), ("components", fingerprint.Components),
            ("unknownReasons", fingerprint.UnknownReasons.Cast<object>().ToList()), ("observations", fingerprint.Observations.Cast<object>().ToList()), ("environment", fingerprint.EnvironmentJson()));

        /// <summary>
        /// Get-LiveLaneStatus: without receipt history every lane is DUE. An attestation is validated before any Git read, so
        /// a schema fault refuses on its own.
        /// </summary>
        public static LiveLaneMap Plain(LiveLaneSources sources, string root, string lane, string candidateSha, string attestation, out LiveLaneFingerprintResult fingerprint)
        {
            if (!string.IsNullOrEmpty(attestation)) LiveLaneAttestation.Environment(attestation, lane, null);
            fingerprint = LiveLaneFingerprint.Compute(sources, root, candidateSha, lane, attestation);
            return new LiveLaneMap(("format", Format), ("laneId", lane), ("candidateSha", candidateSha), ("state", "DUE"),
                ("reasons", new List<object> { fingerprint.Complete ? "no-history" : "unknown-input" }), ("effectiveMode", "Full"), ("activation", "shadow-only"),
                ("gateCredit", false), ("executionEvidence", false), ("fingerprint", FingerprintJson(fingerprint)));
        }

        /// <summary>
        /// Get-LiveLaneHistoryStatus: the candidate fingerprint (with its attestation), the lane roster at the candidate and
        /// the binding context (repository, rc ref, host, account, nonce, clock, probes, revocations). A use record is written
        /// only for a SATISFIED state and only on request.
        /// </summary>
        public static LiveLaneMap WithHistory(LiveLaneSources sources, string root, string lane, string candidateSha, string attestation, string history,
            string context, string reusePolicy, string useRecord, out LiveLaneFingerprintResult fingerprint, out IReadOnlyList<string> roster)
        {
            if (!LiveLaneAttestation.Policies.Contains(reusePolicy ?? "", StringComparer.Ordinal)) throw new LiveLaneRefusal("policy-invalid");
            if (string.IsNullOrEmpty(context)) throw new LiveLaneRefusal("context-required");
            var binding = LiveLaneAttestation.ReadDocument(context, "context-invalid").Value as LiveLaneMap;
            var keys = binding == null ? "" : string.Join(",", LiveLaneData.Sorted(binding.Keys));
            if (keys != ContextKeys || !(binding["probes"] is IList<object> probes) || !(binding["revocations"] is IList<object> revocations)
                || LiveLaneReceipt.ToUtc(binding["nowUtc"]) == null) throw new LiveLaneRefusal("context-invalid");
            if (!Directory.Exists(history) || File.GetAttributes(history).HasFlag(FileAttributes.ReparsePoint)) throw new LiveLaneRefusal("history-invalid");
            var paths = LiveLaneData.Sorted(Directory.GetFiles(history, "*.json"));
            if (!string.IsNullOrEmpty(attestation)) LiveLaneAttestation.Environment(attestation, lane, null);
            fingerprint = LiveLaneFingerprint.Compute(sources, root, candidateSha, lane, attestation);
            var tree = LiveLaneFingerprint.ReadTree(sources.Git, root, candidateSha);
            var declaration = LiveLaneFingerprint.ReadDeclaration(sources.Git, root, tree, lane);
            roster = LiveLaneFingerprint.ManifestRoster(sources.Git, root, tree, declaration.Manifest);
            var candidate = new LiveLaneStateCandidate
            {
                LaneId = lane, Sha = candidateSha, Repository = binding["repository"], Ref = binding["ref"], Host = binding["host"],
                Account = binding["account"], Nonce = binding["nonce"],
                Fingerprint = new LiveLaneMap(("status", fingerprint.Status), ("digest", fingerprint.Digest), ("components", fingerprint.Components),
                    ("environment", fingerprint.EnvironmentJson()))
            };
            var now = LiveLaneReceipt.ToUtc(binding["nowUtc"]);
            var state = LiveLaneReceipt.State(sources.Git, candidate, paths, roster, root, reusePolicy, now, probes, revocations);
            LiveLaneMap use = null;
            if (!string.IsNullOrEmpty(useRecord) && state.State == "SATISFIED") use = LiveLaneReceipt.WriteUse(state, useRecord, now);
            return new LiveLaneMap(("format", Format), ("laneId", lane), ("candidateSha", candidateSha), ("state", state.State), ("reasons", state.Reasons.Cast<object>().ToList()),
                ("effectiveMode", "Full"), ("activation", "shadow-only"), ("gateCredit", false), ("executionEvidence", false),
                ("localCompatibility", state.LocalCompatibility), ("reuseEligible", state.ReuseEligible), ("policy", reusePolicy), ("selected", state.Selected),
                ("lastGreen", state.LastGreen),
                // No use record is an empty object, as the script wrote it ($() of nothing serialised as {}).
                ("useRecord", use == null ? new LiveLaneMap() : new LiveLaneMap(("receiptDigest", use["receiptDigest"]), ("executedNow", use["executedNow"]))),
                ("fingerprint", FingerprintJson(fingerprint)));
        }

        /// <summary>
        /// The Claude versions of the lane's receipts in history for the candidate's code digest: each valid, clean receipt
        /// of this lane whose fingerprint, without its environment component, is the candidate's; the newest receipt of each
        /// version decides (GREEN passes). A run id with two different receipts is left out (the state refuses it).
        /// </summary>
        public static LiveLaneClaudeVersions ClaudeVersions(string lane, string codeDigest, IEnumerable<string> history, IReadOnlyList<string> roster,
            IReadOnlyList<string> required)
        {
            var checks = new List<LiveLaneReceiptCheck>();
            var digestsByRun = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var rosterDigest = LiveLaneReceipt.RosterDigest(roster ?? Array.Empty<string>());
            foreach (var path in history ?? Array.Empty<string>())
            {
                try
                {
                    if (!(LiveLaneAttestation.ReadDocument(path, "history-unreadable").Value is LiveLaneMap map)
                        || !LiveLaneData.Same(map["kind"], LiveLaneReceipt.ExecutionKind)) continue;
                }
                catch (LiveLaneRefusal) { continue; }
                var check = LiveLaneReceipt.Check(path, null, roster, rosterDigest);
                if (check.Receipt?["runId"] is string run)
                {
                    if (!digestsByRun.TryGetValue(run, out var digests)) digestsByRun[run] = digests = new HashSet<string>(StringComparer.Ordinal);
                    digests.Add(check.Digest ?? "");
                }
                if (check.Valid && LiveLaneData.Same(check.Receipt["laneId"], lane) && check.Receipt["sourceClean"] is bool clean && clean
                    && codeDigest != null && LiveLaneFingerprint.CodeDigest(lane, check.FingerprintComponents) == codeDigest) checks.Add(check);
            }
            checks = checks.Where(check => digestsByRun[(string)check.Receipt["runId"]].Count == 1).ToList();
            var versions = new List<LiveLaneMap>();
            var passing = new List<string>();
            var failing = new List<string>();
            foreach (var group in LiveLaneReceipt.Ordered(checks).GroupBy(check => check.Claude.Version, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var latest = group.Last();
                versions.Add(new LiveLaneMap(("version", group.Key), ("raw", latest.Claude.Raw), ("outcome", latest.Outcome), ("receiptDigest", latest.Digest),
                    ("runId", latest.Receipt["runId"]), ("endedUtc", latest.EndedUtc == null ? null : LiveLaneReceipt.Iso(latest.EndedUtc.Value)),
                    ("receipts", (long)group.Count())));
                (latest.Outcome == "GREEN" ? passing : failing).Add(group.Key);
            }
            var requiredList = (required ?? Array.Empty<string>()).ToList();
            return new LiveLaneClaudeVersions
            {
                CodeDigest = codeDigest, Versions = versions, Passing = passing, Failing = failing, Required = requiredList,
                Missing = requiredList.Where(version => !passing.Contains(version, StringComparer.Ordinal)).ToList()
            };
        }

        /// <summary>The INVALID document the script printed for a refusal: the reason when it is a fixed token, else status-failed.</summary>
        public static LiveLaneMap Invalid(string lane, Exception failure)
        {
            var reason = failure?.Message ?? "";
            if (!Reason.IsMatch(reason)) reason = "status-failed";
            return new LiveLaneMap(("format", Format), ("laneId", lane ?? ""), ("state", "INVALID"), ("error", reason), ("effectiveMode", "Full"),
                ("activation", "shadow-only"), ("gateCredit", false), ("executionEvidence", false));
        }

        /// <summary>
        /// One lane, as live-lane-status.ps1 answered it (lane-required, history-required:Context|UseRecord and policy-invalid
        /// first), plus the Claude versions of its receipts. Never throws: a refusal is the INVALID document, exit 2.
        /// </summary>
        public static LiveLaneStatusResult Lane(LiveLaneSources sources, LiveLaneStatusOptions options)
        {
            var lane = options.Lane ?? "";
            try
            {
                if (lane.Length == 0) throw new LiveLaneRefusal("lane-required");
                LiveLaneMap document;
                LiveLaneFingerprintResult fingerprint;
                IReadOnlyList<string> roster = Array.Empty<string>();
                IReadOnlyList<string> history = Array.Empty<string>();
                if (options.History.Length > 0)
                {
                    document = WithHistory(sources, options.RepositoryRoot, lane, options.CandidateSha, options.Attestation, options.History, options.Context,
                        options.ReusePolicy, options.UseRecord, out fingerprint, out roster);
                    history = LiveLaneData.Sorted(Directory.GetFiles(options.History, "*.json"));
                }
                else
                {
                    if (options.Context.Length > 0) throw new LiveLaneRefusal("history-required:Context");
                    if (options.UseRecord.Length > 0) throw new LiveLaneRefusal("history-required:UseRecord");
                    if (!LiveLaneAttestation.Policies.Contains(options.ReusePolicy ?? "", StringComparer.Ordinal)) throw new LiveLaneRefusal("policy-invalid");
                    document = Plain(sources, options.RepositoryRoot, lane, options.CandidateSha, options.Attestation, out fingerprint);
                }
                var claude = ClaudeVersions(lane, fingerprint.CodeDigest, history, roster, options.RequiredClaudeVersions);
                document["claude"] = claude.ToJson();
                return new LiveLaneStatusResult { LaneId = lane, Document = document, Claude = claude, ExitCode = claude.Missing.Count > 0 ? 1 : 0 };
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return new LiveLaneStatusResult { LaneId = lane, Document = Invalid(lane, e), ExitCode = 2 };
            }
        }

        /// <summary>
        /// live-lane-status: the candidate (--candidate-sha, else HEAD), each lane (--lane, else every lane the candidate
        /// declares), one document or table block per lane. Exit 2 when any lane is INVALID, else 1 when any lane misses a
        /// required Claude version, else 0.
        /// </summary>
        public static int Run(LiveLaneStatusOptions options, LiveLaneSources sources, TextWriter output)
        {
            var results = new List<LiveLaneStatusResult>();
            var candidate = options.CandidateSha;
            if (options.Lane != null) results.Add(Lane(sources, options.For(options.Lane, candidate ?? Head(sources, options.RepositoryRoot))));
            else
            {
                IReadOnlyList<string> lanes;
                try
                {
                    candidate ??= Head(sources, options.RepositoryRoot);
                    if (candidate == null) throw new LiveLaneRefusal("full-commit-required");
                    lanes = LiveLaneFingerprint.DeclaredLanes(sources.Git, options.RepositoryRoot, LiveLaneFingerprint.ReadTree(sources.Git, options.RepositoryRoot, candidate));
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    lanes = null;
                    results.Add(new LiveLaneStatusResult { LaneId = "", Document = Invalid("", e), ExitCode = 2 });
                }
                foreach (var lane in lanes ?? Array.Empty<string>()) results.Add(Lane(sources, options.For(lane, candidate)));
                if (lanes != null && lanes.Count == 0) results.Add(Lane(sources, options.For("", candidate)));
            }
            foreach (var result in results)
            {
                if (options.Json) output.WriteLine(LiveLaneData.Canonical(result.Document));
                else WriteTable(result, output);
            }
            return results.Any(r => r.ExitCode == 2) ? 2 : results.Any(r => r.ExitCode == 1) ? 1 : 0;
        }

        // The repository's HEAD commit; null when it cannot be read (the lane then refuses full-commit-required).
        private static string Head(LiveLaneSources sources, string root)
        {
            try { return sources.Git.Run(root, new[] { "rev-parse", "--verify", "HEAD^{commit}" }, new[] { 0 }).Stdout.Trim(); }
            catch (Exception) { return null; }
        }

        private static string Short(object digest) => digest is string text && text.Length >= 12 ? text.Substring(0, 12) : digest as string ?? "-";

        /// <summary>The table for people: the lane, its state and fingerprint, then one line per Claude version and the requirement.</summary>
        public static void WriteTable(LiveLaneStatusResult result, TextWriter output)
        {
            var document = result.Document;
            if (LiveLaneData.Same(document["state"], "INVALID"))
            {
                output.WriteLine((result.LaneId.Length > 0 ? result.LaneId : "(lane)") + "  INVALID  " + document["error"]);
                return;
            }
            var fingerprint = (LiveLaneMap)document["fingerprint"];
            var reasons = ((List<object>)document["reasons"]).Cast<string>().ToList();
            output.WriteLine(result.LaneId + "  " + document["state"] + "  fingerprint " + Short(fingerprint["digest"]) + " (" + fingerprint["status"] + ")"
                + "  code " + Short(result.Claude.CodeDigest) + "  candidate " + Short(document["candidateSha"]));
            if (reasons.Count > 0) output.WriteLine("  reasons: " + string.Join(", ", reasons));
            var unknown = ((List<object>)fingerprint["unknownReasons"]).Count;
            if (unknown > 0) output.WriteLine("  unknown inputs: " + unknown);
            if (result.Claude.Versions.Count == 0) output.WriteLine("  claude: no receipt for this code digest");
            foreach (var version in result.Claude.Versions)
                output.WriteLine("  claude " + ((string)version["version"]).PadRight(12) + " " + ((string)version["outcome"]).PadRight(12) + " receipt "
                    + Short(version["receiptDigest"]) + "  ended " + (version["endedUtc"] ?? "-") + "  receipts " + version["receipts"]);
            if (result.Claude.Required.Count > 0)
                output.WriteLine("  required: " + string.Join(", ", result.Claude.Required) + (result.Claude.Missing.Count == 0 ? " (met)"
                    : "; missing " + string.Join(", ", result.Claude.Missing)));
        }
    }
}
