// CARD-1545 S2a-1/S2a-2/S2b: the live-lane status, ported from scripts/live-lane-status.ps1 and the status functions of
// live-lane-fingerprint.ps1 and live-lane-receipt.ps1 (W2 slice S22; the scripts are deleted), as the CLI command
// live-lane-status. Shadow-only: no state carries gate credit.
//
// For each lane (--lane, or every lane the candidate's tests/live-lane-inputs.json declares) it prints the state document
// the script printed (antiphon-live-lane-status-v1: without --history every lane is DUE; with --history and --context
// the receipts decide, D-6 under --reuse-policy). --json prints one canonical document per lane per line (the script's
// text); otherwise a short table. Exit 0; 2 when a lane is INVALID (the document says why, as the script's did). Plain C#,
// framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>One lane's status: the document, the exit it asks for, and the parts the table shows.</summary>
    public sealed class LiveLaneStatusResult
    {
        public string LaneId { get; set; }
        /// <summary>The document (the script's).</summary>
        public LiveLaneMap Document { get; set; }
        public int ExitCode { get; set; }
    }

    /// <summary>live-lane-status's flags.</summary>
    public sealed class LiveLaneStatusOptions
    {
        public const string Usage = "live-lane-status [--lane <id>] [--candidate-sha <sha>] [--repository-root <dir>] [--attestation <file>] [--history <dir> --context <file>"
            + " [--reuse-policy AlwaysRerun|Bounded24h] [--use-record <file>]] [--json]";

        private static readonly string[] Valued = { "--lane", "--candidate-sha", "--repository-root", "--attestation", "--history", "--context", "--reuse-policy", "--use-record" };

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
        public bool Json { get; private set; }

        public static LiveLaneStatusOptions Parse(IReadOnlyList<string> arguments, string defaultRoot)
        {
            var values = LiveLaneArgs.Parse(arguments, Valued, new[] { "--json" });
            var options = new LiveLaneStatusOptions
            {
                Lane = values.Value("--lane"), CandidateSha = values.Value("--candidate-sha"),
                RepositoryRoot = values.Value("--repository-root") ?? defaultRoot ?? Environment.CurrentDirectory,
                Attestation = values.Value("--attestation") ?? "", History = values.Value("--history") ?? "", Context = values.Value("--context") ?? "",
                ReusePolicy = values.Value("--reuse-policy") ?? LiveLaneAttestation.AlwaysRerun, UseRecord = values.Value("--use-record") ?? "",
                Json = values.Switch("--json")
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
            string reusePolicy = LiveLaneAttestation.AlwaysRerun, string useRecord = "") =>
            new LiveLaneStatusOptions
            {
                RepositoryRoot = root, Lane = lane, CandidateSha = candidateSha, Attestation = attestation ?? "", History = history ?? "", Context = context ?? "",
                ReusePolicy = reusePolicy, UseRecord = useRecord ?? ""
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
        /// first). Never throws: a refusal is the INVALID document, exit 2.
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
                if (options.History.Length > 0)
                {
                    document = WithHistory(sources, options.RepositoryRoot, lane, options.CandidateSha, options.Attestation, options.History, options.Context,
                        options.ReusePolicy, options.UseRecord, out fingerprint, out roster);
                }
                else
                {
                    if (options.Context.Length > 0) throw new LiveLaneRefusal("history-required:Context");
                    if (options.UseRecord.Length > 0) throw new LiveLaneRefusal("history-required:UseRecord");
                    if (!LiveLaneAttestation.Policies.Contains(options.ReusePolicy ?? "", StringComparer.Ordinal)) throw new LiveLaneRefusal("policy-invalid");
                    document = Plain(sources, options.RepositoryRoot, lane, options.CandidateSha, options.Attestation, out fingerprint);
                }
                return new LiveLaneStatusResult { LaneId = lane, Document = document, ExitCode = 0 };
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return new LiveLaneStatusResult { LaneId = lane, Document = Invalid(lane, e), ExitCode = 2 };
            }
        }

        /// <summary>
        /// live-lane-status: the candidate (--candidate-sha, else HEAD), each lane (--lane, else every lane the candidate
        /// declares), one document or table block per lane. Exit 2 when any lane is INVALID, else 0.
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
            return results.Any(r => r.ExitCode == 2) ? 2 : 0;
        }

        // The repository's HEAD commit; null when it cannot be read (the lane then refuses full-commit-required).
        private static string Head(LiveLaneSources sources, string root)
        {
            try { return sources.Git.Run(root, new[] { "rev-parse", "--verify", "HEAD^{commit}" }, new[] { 0 }).Stdout.Trim(); }
            catch (Exception) { return null; }
        }

        private static string Short(object digest) => digest is string text && text.Length >= 12 ? text.Substring(0, 12) : digest as string ?? "-";

        /// <summary>The table for people: the lane, its state and fingerprint, its reasons.</summary>
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
                + "  candidate " + Short(document["candidateSha"]));
            if (reasons.Count > 0) output.WriteLine("  reasons: " + string.Join(", ", reasons));
            var unknown = ((List<object>)fingerprint["unknownReasons"]).Count;
            if (unknown > 0) output.WriteLine("  unknown inputs: " + unknown);

        }
    }
}
