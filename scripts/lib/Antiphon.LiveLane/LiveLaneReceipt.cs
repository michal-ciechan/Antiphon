// CARD-1545 S2b: live-lane execution receipts, lane state and use records, ported from scripts/lib/live-lane-receipt.ps1
// (W2 slice S22; the script is deleted). Diagnostic and shadow-only: no receipt is trusted (D-9 is S2c), no state carries
// gate credit, and a reuse is a use record, never an execution. Every verdict is recomputed from the receipt's own facts,
// in the PowerShell's order. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>Test-LiveLaneReceipt's answer.</summary>
    public sealed class LiveLaneReceiptCheck
    {
        public bool Valid { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        /// <summary>GREEN, RED, INCONCLUSIVE or INVALID.</summary>
        public string Outcome { get; set; } = "INVALID";
        public int Code { get; set; } = 2;
        /// <summary>The SHA-256 of the receipt file's bytes (null when it could not be read).</summary>
        public string Digest { get; set; }
        /// <summary>The receipt document (null when it is not an object, carries a credential or could not be read).</summary>
        public LiveLaneMap Receipt { get; set; }
        public DateTimeOffset? EndedUtc { get; set; }
        public long Sequence { get; set; }


        internal string FingerprintDigest => (Receipt?["fingerprint"] as LiveLaneMap)?["digest"] as string;
        internal LiveLaneMap FingerprintComponents => (Receipt?["fingerprint"] as LiveLaneMap)?["components"] as LiveLaneMap;
    }

    /// <summary>Test-LiveLaneUseRecord's answer.</summary>
    public sealed class LiveLaneUseCheck
    {
        public bool Valid { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        public string Digest { get; set; }
        public LiveLaneMap Record { get; set; }
    }

    /// <summary>The candidate a lane state is computed for.</summary>
    public sealed class LiveLaneStateCandidate
    {
        public string LaneId { get; set; }
        public string Sha { get; set; }
        /// <summary>The binding context's values (strings from the context file; compared as the PowerShell's -ceq did).</summary>
        public object Repository { get; set; }
        public object Ref { get; set; }
        public object Host { get; set; }
        public object Account { get; set; }
        public object Nonce { get; set; }
        /// <summary>The candidate's fingerprint as the state reads it: status, digest, components and environment.</summary>
        public LiveLaneMap Fingerprint { get; set; }
    }

    /// <summary>Get-LiveLaneState's answer (antiphon-live-lane-state-v1).</summary>
    public sealed class LiveLaneState
    {
        public const string Format = "antiphon-live-lane-state-v1";

        public string LaneId { get; set; }
        public string CandidateSha { get; set; }
        public string State { get; set; } = "DUE";
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        public string LocalCompatibility { get; set; } = "DUE";
        public bool ReuseEligible { get; set; }
        public string Policy { get; set; }
        /// <summary>{receiptDigest, runId, fingerprintDigest} of the selected green, or null.</summary>
        public LiveLaneMap Selected { get; set; }
        public string LastGreen { get; set; }
        public LiveLaneFreshness Freshness { get; set; }
        public string EffectiveMode => "Full";
        public string Activation => "shadow-only";
        public bool GateCredit => false;
        public bool ExecutionEvidence => false;

        public LiveLaneMap ToJson() => new LiveLaneMap(("format", Format), ("laneId", LaneId), ("candidateSha", CandidateSha),
            ("state", State), ("reasons", Reasons.Cast<object>().ToList()), ("localCompatibility", LocalCompatibility), ("reuseEligible", ReuseEligible),
            ("policy", Policy), ("selected", Selected), ("lastGreen", LastGreen), ("freshness", Freshness?.ToJson()),
            ("effectiveMode", EffectiveMode), ("activation", Activation), ("gateCredit", GateCredit), ("executionEvidence", ExecutionEvidence));
    }

    /// <summary>Receipts, use records and the lane state.</summary>
    public static class LiveLaneReceipt
    {
        public const string ExecutionKind = "live-lane-execution";
        public const string UseKind = "live-lane-use";
        public const string RevocationKind = "live-lane-revocation";


        private static readonly string[] ReceiptFields =
        {
            "account", "artifacts", "authority", "classifierVerdict", "durationMs", "endedUtc", "executed", "external", "failed",
            "finalExit", "fingerprint", "host", "inputAfter", "inputBefore", "kind", "laneId", "missing", "passed", "planned", "ref", "repository", "rows", "runId",
            "schemaVersion", "sequence", "skipped", "sourceClean", "sourceSha", "startedUtc"
        };
        private static readonly string[] UseFields =
        {
            "candidateSha", "createdUtc", "executed", "executedNow", "failed", "fingerprintDigest", "gateCredit", "kind", "laneId",
            "passed", "receiptDigest", "reused", "runId", "schemaVersion", "skipped"
        };
        private const string RevocationFields = "kind,reason,receiptDigest,schemaVersion";
        private static readonly string[] RowOutcomes = { "Passed", "Failed", "Skipped" };
        private static readonly long[] Verdicts = { 0, 1, 2, 20 };
        private static readonly string[] FingerprintNames = { "environment", "instructions", "laneContract", "liveRoots", "repositoryClosure", "runtimeInputs", "testSources" };
        private static readonly string[] CountNames = { "sequence", "durationMs", "planned", "executed", "missing", "passed", "failed", "skipped", "classifierVerdict", "finalExit" };

        private static readonly Regex Utc = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$");
        private static readonly Regex Sha1 = new Regex("^[0-9a-f]{40}$");
        private static readonly Regex Sha256Text = new Regex("^[0-9a-f]{64}$");
        private static readonly Regex PlainName = new Regex("^[A-Za-z][A-Za-z0-9]{0,40}$");
        private static readonly Regex ArtifactName = new Regex("^[A-Za-z0-9_.-]{1,64}$");

        /// <summary>Get-LiveLaneRosterDigest: the digest of the sorted roster.</summary>
        public static string RosterDigest(IEnumerable<string> roster) => LiveLaneData.Digest(LiveLaneData.Sorted(roster).ToList());

        /// <summary>Read-LiveLaneRosterFile: a manifest copy on disk, its expanded core roster (manifest-invalid otherwise).</summary>
        public static IReadOnlyList<string> RosterFile(string path)
        {
            object manifest;
            try { manifest = LiveLaneData.Parse(File.ReadAllText(path), 64); }
            catch (Exception) { throw new LiveLaneRefusal("manifest-invalid"); }
            return LiveLaneAttestation.Roster(manifest);
        }

        /// <summary>ConvertTo-LiveLaneUtc: a recorded time (an ISO-8601 'Z' string, or a UTC DateTime); null otherwise.</summary>
        public static DateTimeOffset? ToUtc(object value)
        {
            switch (value)
            {
                case DateTime time: return time.Kind == DateTimeKind.Utc ? new DateTimeOffset(time) : (DateTimeOffset?)null;
                case DateTimeOffset offset: return offset.Offset == TimeSpan.Zero ? offset : (DateTimeOffset?)null;
                case string text when Utc.IsMatch(text):
                    return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTimeOffset?)null;
                default: return null;
            }
        }

        /// <summary>The recorded form of a time: yyyy-MM-ddTHH:mm:ssZ.</summary>
        public static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Test-LiveLaneField: one typed field; a null is a missing requirement, a wrong type a type fault.
        private static bool Field(List<string> reasons, LiveLaneMap map, string name, string type, string label = null)
        {
            label ??= name;
            var value = map[name];
            if (value == null) { reasons.Add("required:" + label); return false; }
            bool ok;
            switch (type)
            {
                case "string": ok = value is string s && s.Length > 0 && s.Length <= 256; break;
                case "int": ok = LiveLaneData.IsLong(value, out _); break;
                case "bool": ok = value is bool; break;
                case "sha1": ok = value is string a && Sha1.IsMatch(a); break;
                case "sha256": ok = value is string b && Sha256Text.IsMatch(b); break;
                case "time": ok = ToUtc(value) != null; break;
                case "object": ok = value is LiveLaneMap; break;
                case "list": ok = value is IList<object>; break;
                default: ok = false; break;
            }
            if (!ok) { reasons.Add("type:" + label); return false; }
            if (type == "int" && LiveLaneData.IsLong(value, out var number) && number < 0) { reasons.Add("negative:" + label); return false; }
            return true;
        }

        // Test-LiveLaneExactKeys: unknown fields are named only when the name is a plain identifier; a missing one is required.
        private static void ExactKeys(List<string> reasons, LiveLaneMap map, IReadOnlyCollection<string> keys, string prefix = "")
        {
            foreach (var key in map.Keys)
                if (!keys.Contains(key, StringComparer.Ordinal)) reasons.Add(PlainName.IsMatch(key) ? "field:" + prefix + key : "field:unnamed");
            foreach (var key in keys) if (!map.Contains(key)) reasons.Add("required:" + prefix + key);
        }

        /// <summary>Get-LiveLaneReceiptOutcome.</summary>
        public static string Outcome(int code) => code switch { 0 => "GREEN", 1 => "RED", 20 => "INCONCLUSIVE", _ => "INVALID" };

        /// <summary>
        /// Test-LiveLaneReceipt: validates one live-lane-execution receipt against the lane roster (manifest, a manifest
        /// copy's path, or roster). rosterDigest, when given, is the roster's digest.
        /// </summary>
        public static LiveLaneReceiptCheck Check(string receipt, string manifest = null, IEnumerable<string> roster = null, string rosterDigest = null)
        {
            LiveLaneReceiptCheck Invalid(IEnumerable<string> why, string digest, LiveLaneMap document) =>
                new LiveLaneReceiptCheck { Valid = false, Reasons = LiveLaneData.Sorted(why), Outcome = "INVALID", Code = 2, Digest = digest, Receipt = document };
            var reasons = new List<string>();
            LiveLaneDocument read;
            try { read = LiveLaneAttestation.ReadDocument(receipt, "receipt-unreadable"); }
            catch (LiveLaneRefusal refusal) { return Invalid(new[] { refusal.Message }, null, null); }
            if (!(read.Value is LiveLaneMap document)) return Invalid(new[] { "receipt-unreadable" }, read.Sha256, null);
            try { LiveLaneAttestation.AssertCredentialFree(document); }
            catch (LiveLaneRefusal) { return Invalid(new[] { "credential-field-present" }, read.Sha256, null); }
            if (!string.IsNullOrEmpty(manifest)) roster = RosterFile(manifest);
            var rosterSet = new HashSet<string>((roster ?? Array.Empty<string>()).Where(uid => !string.IsNullOrEmpty(uid)), StringComparer.Ordinal);
            if (string.IsNullOrEmpty(rosterDigest)) rosterDigest = RosterDigest(rosterSet);

            ExactKeys(reasons, document, ReceiptFields);
            if (!LiveLaneData.Same(document["kind"], ExecutionKind)) reasons.Add("kind");
            if (!LiveLaneData.IsLong(document["schemaVersion"], out var version) || version != 1) reasons.Add("schema");
            foreach (var name in new[] { "laneId", "runId", "repository", "ref" }) Field(reasons, document, name, "string");
            Field(reasons, document, "sourceSha", "sha1");
            Field(reasons, document, "sourceClean", "bool");
            foreach (var name in new[] { "inputBefore", "inputAfter" }) Field(reasons, document, name, "sha256");
            // D-9 is out of scope: only a diagnostic receipt exists in S2, and it earns nothing.
            if (!LiveLaneData.Same(document["authority"], "diagnostic")) reasons.Add("issuer-unqualified");
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in CountNames)
                if (Field(reasons, document, name, "int")) { LiveLaneData.IsLong(document[name], out var count); counts[name] = count; }
            foreach (var side in new[] { "host", "account" })
            {
                if (!Field(reasons, document, side, "object")) continue;
                var map = (LiveLaneMap)document[side];
                ExactKeys(reasons, map, new[] { "enrollmentId" }, side + ".");
                Field(reasons, map, "enrollmentId", "string", side + ".enrollmentId");
            }
            if (Field(reasons, document, "external", "object"))
            {
                var external = (LiveLaneMap)document["external"];
                ExactKeys(reasons, external, new[] { "epoch" }, "external.");
                if (external["epoch"] != null && !(external["epoch"] is string)) reasons.Add("type:external.epoch");
            }
            DateTimeOffset? started = null, ended = null;
            if (Field(reasons, document, "startedUtc", "time")) started = ToUtc(document["startedUtc"]);
            if (Field(reasons, document, "endedUtc", "time")) ended = ToUtc(document["endedUtc"]);
            if (started != null && ended != null && ended < started) reasons.Add("time-order");
            if (document["inputBefore"] is string before && !LiveLaneData.Same(before, document["inputAfter"])) reasons.Add("input-drift");

            // Fingerprint: the stored digest must recompute from its components; nothing is trusted.
            if (Field(reasons, document, "fingerprint", "object"))
            {
                var fingerprint = (LiveLaneMap)document["fingerprint"];
                ExactKeys(reasons, fingerprint, new[] { "components", "digest", "rosterDigest" }, "fingerprint.");
                var digestOk = Field(reasons, fingerprint, "digest", "sha256", "fingerprint.digest");
                Field(reasons, fingerprint, "rosterDigest", "sha256", "fingerprint.rosterDigest");
                if (Field(reasons, fingerprint, "components", "object", "fingerprint.components"))
                {
                    var components = (LiveLaneMap)fingerprint["components"];
                    var componentsOk = components.All(pair => FingerprintNames.Contains(pair.Key, StringComparer.Ordinal) && pair.Value is string text && Sha256Text.IsMatch(text));
                    if (!componentsOk) reasons.Add("type:fingerprint.components");
                    else if (digestOk && document["laneId"] is string laneId && LiveLaneFingerprint.Digest(laneId, components) != (string)fingerprint["digest"])
                        reasons.Add("fingerprint-tamper");
                }
                if (fingerprint["rosterDigest"] is string recorded && recorded != rosterDigest) reasons.Add("roster-changed");
            }

            // Rows: an exact multiset equal to the roster, closed outcomes, counts equal to the rows.
            var tally = new Dictionary<string, long>(StringComparer.Ordinal) { ["Passed"] = 0, ["Failed"] = 0, ["Skipped"] = 0 };
            var rows = new List<object>();
            if (Field(reasons, document, "rows", "list"))
            {
                rows = ((IList<object>)document["rows"]).ToList();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (rosterSet.Count == 0 || rows.Count == 0) reasons.Add("roster-empty");
                foreach (var item in rows)
                {
                    if (!(item is LiveLaneMap row)) { reasons.Add("type:rows"); continue; }
                    ExactKeys(reasons, row, new[] { "outcome", "uid" }, "rows.");
                    if (!(row["uid"] is string uid) || uid.Length == 0) { reasons.Add("type:rows.uid"); continue; }
                    if (!seen.Add(uid)) reasons.Add("roster-duplicate");
                    if (!rosterSet.Contains(uid)) reasons.Add("roster-unexpected");
                    if (!(row["outcome"] is string outcome) || !RowOutcomes.Contains(outcome, StringComparer.Ordinal)) reasons.Add("outcome-enum");
                    else tally[outcome]++;
                }
                foreach (var uid in rosterSet) if (!seen.Contains(uid)) reasons.Add("roster-incomplete");
            }
            if (counts.Count == 10)
            {
                if (counts["passed"] != tally["Passed"] || counts["failed"] != tally["Failed"] || counts["skipped"] != tally["Skipped"]
                    || counts["executed"] != rows.Count || counts["planned"] != counts["executed"] + counts["missing"]) reasons.Add("counts-mismatch");
                if (counts["missing"] != 0 || counts["planned"] != rosterSet.Count || counts["executed"] < counts["planned"]) reasons.Add("roster-incomplete");
                if (!Verdicts.Contains(counts["classifierVerdict"])) reasons.Add("classifier-enum");
                else if (counts["finalExit"] != counts["classifierVerdict"]) reasons.Add("classifier-mismatch");
            }

            // Artifacts: each named file beside the receipt must hash to its recorded digest.
            if (Field(reasons, document, "artifacts", "list"))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(receipt));
                foreach (var item in (IList<object>)document["artifacts"])
                {
                    if (!(item is LiveLaneMap artifact) || !(artifact["name"] is string name) || !ArtifactName.IsMatch(name)
                        || !(artifact["sha256"] is string expected) || !Sha256Text.IsMatch(expected) || artifact.Count != 2) { reasons.Add("type:artifacts"); continue; }
                    var path = Path.Combine(directory, name);
                    string actual;
                    try
                    {
                        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) { reasons.Add("artifact-digest:" + name); continue; }
                        actual = LiveLaneData.Sha256(File.ReadAllBytes(path));
                    }
                    catch (Exception) { reasons.Add("artifact-missing:" + name); continue; }
                    if (actual != expected) reasons.Add("artifact-digest:" + name);
                }
            }

            var sorted = LiveLaneData.Sorted(reasons);
            if (sorted.Length > 0) return Invalid(sorted, read.Sha256, document);
            // Outcome: the worse of the rows and the classifier; a skip is never green.
            var rowsCode = tally["Failed"] > 0 ? 1 : tally["Skipped"] > 0 ? 20 : 0;
            var verdict = counts["classifierVerdict"];
            var code = verdict == 2 ? 2 : rowsCode == 1 || verdict == 1 ? 1 : rowsCode == 20 || verdict == 20 ? 20 : 0;
            return new LiveLaneReceiptCheck
            {
                Valid = true, Reasons = Array.Empty<string>(), Outcome = Outcome(code), Code = code, Digest = read.Sha256, Receipt = document,
                EndedUtc = ended, Sequence = counts["sequence"]
            };
        }

        /// <summary>Test-LiveLaneUseRecord: a use record says a receipt was reused; it executed nothing and earns nothing.</summary>
        public static LiveLaneUseCheck CheckUse(string path)
        {
            var reasons = new List<string>();
            LiveLaneDocument read;
            try { read = LiveLaneAttestation.ReadDocument(path, "use-unreadable"); }
            catch (LiveLaneRefusal refusal) { return new LiveLaneUseCheck { Valid = false, Reasons = new[] { refusal.Message } }; }
            if (!(read.Value is LiveLaneMap record)) return new LiveLaneUseCheck { Valid = false, Reasons = new[] { "use-unreadable" }, Digest = read.Sha256 };
            ExactKeys(reasons, record, UseFields);
            if (!LiveLaneData.Same(record["kind"], UseKind)) reasons.Add("kind");
            if (!LiveLaneData.IsLong(record["schemaVersion"], out var version) || version != 1) reasons.Add("schema");
            foreach (var name in new[] { "laneId", "runId" }) Field(reasons, record, name, "string");
            Field(reasons, record, "candidateSha", "sha1");
            foreach (var name in new[] { "receiptDigest", "fingerprintDigest" }) Field(reasons, record, name, "sha256");
            Field(reasons, record, "createdUtc", "time");
            foreach (var name in new[] { "executedNow", "executed", "passed", "failed", "skipped" })
                if (Field(reasons, record, name, "int") && LiveLaneData.IsLong(record[name], out var count) && count != 0) reasons.Add("use-executed");
            if (!(record["reused"] is bool reused) || !reused) reasons.Add("type:reused");
            if (!(record["gateCredit"] is bool credit) || credit) reasons.Add("use-gate-credit");
            return new LiveLaneUseCheck { Valid = reasons.Count == 0, Reasons = LiveLaneData.Sorted(reasons), Digest = read.Sha256, Record = record };
        }

        /// <summary>A lane history read: every record validated (the receipts in check order), and the reasons it gave.</summary>
        internal sealed class History
        {
            public List<LiveLaneReceiptCheck> Executions { get; } = new List<LiveLaneReceiptCheck>();
            public HashSet<string> Revoked { get; } = new HashSet<string>(StringComparer.Ordinal);
            public List<string> Reasons { get; } = new List<string>();
        }

        // The history loop of Get-LiveLaneState: one run id has one receipt; a roster-changed receipt of this fingerprint is a
        // reason, of another fingerprint nothing.
        internal static History ReadHistory(IEnumerable<string> paths, IReadOnlyList<string> roster, string rosterDigest, string fingerprintDigest)
        {
            var history = new History();
            var runs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in paths ?? Array.Empty<string>())
            {
                object kind;
                try
                {
                    var document = LiveLaneAttestation.ReadDocument(path, "history-unreadable").Value;
                    if (!(document is LiveLaneMap map)) throw new LiveLaneRefusal("history-unreadable");
                    kind = map["kind"];
                }
                catch (LiveLaneRefusal) { history.Reasons.Add("history-invalid"); continue; }
                if (LiveLaneData.Same(kind, ExecutionKind))
                {
                    var check = Check(path, null, roster, rosterDigest);
                    if (check.Receipt != null && check.Receipt["runId"] is string run)
                    {
                        if (runs.TryGetValue(run, out var known) && known != check.Digest) throw new LiveLaneRefusal("contradictory-receipts");
                        runs[run] = check.Digest;
                    }
                    if (check.Valid) history.Executions.Add(check);
                    else if (check.Reasons.Contains("roster-changed", StringComparer.Ordinal))
                    {
                        if (check.Receipt?["fingerprint"] is LiveLaneMap recorded && LiveLaneData.Same(recorded["digest"], fingerprintDigest)) history.Reasons.Add("roster-changed");
                    }
                    else history.Reasons.Add("history-invalid");
                }
                else if (LiveLaneData.Same(kind, UseKind))
                {
                    if (!CheckUse(path).Valid) history.Reasons.Add("history-invalid");
                }
                else if (LiveLaneData.Same(kind, RevocationKind))
                {
                    var revocation = (LiveLaneMap)LiveLaneAttestation.ReadDocument(path, "history-unreadable").Value;
                    if (string.Join(",", LiveLaneData.Sorted(revocation.Keys)) != RevocationFields || !LiveLaneData.IsLong(revocation["schemaVersion"], out var version)
                        || version != 1 || !(revocation["receiptDigest"] is string digest) || !Sha256Text.IsMatch(digest)) history.Reasons.Add("history-invalid");
                    else history.Revoked.Add(digest);
                }
                else history.Reasons.Add("history-invalid");
            }
            return history;
        }

        // Recorded time, then sequence, then digest: never the enumeration order.
        internal static List<LiveLaneReceiptCheck> Ordered(IEnumerable<LiveLaneReceiptCheck> executions) =>
            executions.OrderBy(check => check.EndedUtc?.UtcTicks ?? 0).ThenBy(check => check.Sequence).ThenBy(check => check.Digest, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Get-LiveLaneState: the shadow-only lane state for one candidate. Unknown input decides before any receipt; then
        /// the newest attempt for this fingerprint decides; then identity, lineage (real git ancestry) and D-6 freshness.
        /// </summary>
        public static LiveLaneState State(ILiveLaneGit git, LiveLaneStateCandidate candidate, IEnumerable<string> history, IReadOnlyList<string> roster, string root,
            string reusePolicy, DateTimeOffset? nowUtc, IEnumerable<object> probes, IEnumerable<object> revocations)
        {
            reusePolicy ??= LiveLaneAttestation.AlwaysRerun;
            var reasons = new List<string>();
            var result = new LiveLaneState { LaneId = candidate.LaneId, CandidateSha = candidate.Sha, Policy = reusePolicy };
            LiveLaneState Finish()
            {
                result.Reasons = LiveLaneData.Sorted(reasons);
                return result;
            }

            var fingerprint = candidate.Fingerprint;
            if (fingerprint == null || !LiveLaneData.Same(fingerprint["status"], "complete")) reasons.Add("unknown-input");
            var environment = fingerprint?["environment"] as LiveLaneMap;
            var components = fingerprint?["components"] as LiveLaneMap;
            if (fingerprint == null || components == null || !components.Contains("environment") || environment == null || !LiveLaneData.Same(environment["status"], "complete"))
                reasons.Add("environment-unknown");
            if (reasons.Count > 0) return Finish();
            var fingerprintDigest = fingerprint["digest"] as string;
            var read = ReadHistory(history, roster, RosterDigest(roster), fingerprintDigest);
            reasons.AddRange(read.Reasons);
            // Another lane's receipt never stands for this lane.
            if (read.Executions.Any(check => !LiveLaneData.Same(check.Receipt["laneId"], candidate.LaneId))) reasons.Add("foreign-lane");
            var ordered = Ordered(read.Executions.Where(check => LiveLaneData.Same(check.Receipt["laneId"], candidate.LaneId)));
            var greens = ordered.Where(check => check.Outcome == "GREEN").ToList();
            if (greens.Count > 0) result.LastGreen = greens[^1].Digest;

            var same = ordered.Where(check => LiveLaneData.Same(check.FingerprintDigest, fingerprintDigest)).ToList();
            if (same.Any(check => !(check.Receipt["sourceClean"] is bool clean && clean))) reasons.Add("source-dirty");
            same = same.Where(check => check.Receipt["sourceClean"] is bool clean && clean).ToList();
            if (same.Count == 0)
            {
                reasons.Add("no-execution-receipt");
                if (ordered.Count > 0)
                {
                    var other = ordered[^1].FingerprintComponents;
                    foreach (var name in LiveLaneData.Sorted(other.Keys.Concat(components.Keys)))
                        if (!LiveLaneData.Same(other[name], components[name])) reasons.Add("fingerprint-changed:" + name);
                }
                return Finish();
            }
            var latest = same[^1];
            switch (latest.Outcome)
            {
                case "RED": reasons.Add("newer-red"); break;
                case "INCONCLUSIVE": reasons.Add("newer-inconclusive"); break;
                case "INVALID": reasons.Add("newer-invalid"); break;
            }
            if (reasons.Count > 0) return Finish();

            // Identity and lineage of the selected green, all before any freshness question.
            var receipt = latest.Receipt;
            var host = ((LiveLaneMap)receipt["host"])["enrollmentId"];
            var account = ((LiveLaneMap)receipt["account"])["enrollmentId"];
            if (read.Revoked.Contains(latest.Digest)) reasons.Add("revoked");
            if (!LiveLaneData.Same(receipt["repository"], candidate.Repository)) reasons.Add("foreign-repository");
            if (!LiveLaneData.Same(receipt["ref"], candidate.Ref)) reasons.Add("foreign-ref");
            if (!LiveLaneData.Same(host, candidate.Host)) reasons.Add("host-mismatch");
            if (!LiveLaneData.Same(account, candidate.Account)) reasons.Add("account-mismatch");
            var sourceSha = (string)receipt["sourceSha"];
            var present = git.Run(root, new[] { "cat-file", "-e", sourceSha + "^{commit}" }, new[] { 0, 1, 128 }).Exit == 0;
            if (!present) reasons.Add("source-missing");
            else
            {
                var ancestry = git.Run(root, new[] { "merge-base", "--is-ancestor", sourceSha, candidate.Sha ?? "" }, new[] { 0, 1, 128 }).Exit;
                if (ancestry == 1) reasons.Add("not-ancestor");
                else if (ancestry != 0) reasons.Add("source-missing");
            }
            if (reasons.Count > 0) return Finish();
            result.LocalCompatibility = "SATISFIED";
            result.Selected = new LiveLaneMap(("receiptDigest", latest.Digest), ("runId", receipt["runId"]), ("fingerprintDigest", fingerprintDigest));

            // D-6 with the receipt's own end time; AlwaysRerun refuses here.
            var external = (LiveLaneMap)receipt["external"];
            var freshness = LiveLaneAttestation.Freshness(reusePolicy, latest.EndedUtc, nowUtc,
                new LiveLaneBinding { Nonce = candidate.Nonce, Host = candidate.Host, Account = candidate.Account },
                new LiveLaneBinding { Epoch = external["epoch"], Host = host, Account = account, ReceiptId = latest.Digest }, probes, revocations);
            result.Freshness = freshness;
            result.ReuseEligible = freshness.ReuseEligible;
            if (freshness.ReuseEligible) result.State = "SATISFIED";
            else reasons.AddRange(freshness.Reasons);
            return Finish();
        }

        /// <summary>
        /// New-LiveLaneUseRecord: records a reuse of the selected receipt. Writes a new file once (use-record-exists when the
        /// path is taken); never touches the receipt.
        /// </summary>
        public static LiveLaneMap WriteUse(LiveLaneState state, string path, DateTimeOffset? nowUtc)
        {
            if (state == null || state.State != "SATISFIED" || state.Selected == null) throw new LiveLaneRefusal("state-not-satisfied");
            if (nowUtc == null) throw new LiveLaneRefusal("time-invalid");
            var record = new LiveLaneMap(("kind", UseKind), ("schemaVersion", 1), ("laneId", state.LaneId), ("candidateSha", state.CandidateSha),
                ("receiptDigest", state.Selected["receiptDigest"]), ("runId", state.Selected["runId"]), ("fingerprintDigest", state.Selected["fingerprintDigest"]),
                ("reused", true), ("executedNow", 0), ("executed", 0), ("passed", 0), ("failed", 0), ("skipped", 0), ("gateCredit", false),
                ("createdUtc", Iso(nowUtc.Value)));
            WriteNew(path, LiveLaneData.Canonical(record), "use-record-exists", "use-record-unwritable");
            return record;
        }

        // A new file, never a replaced one: exists when the path is taken, unwritable for any other fault.
        internal static void WriteNew(string path, string text, string exists, string unwritable)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException)
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new LiveLaneRefusal(exists);
                throw new LiveLaneRefusal(unwritable);
            }
            catch (Exception) { throw new LiveLaneRefusal(unwritable); }
            using (stream) stream.Write(bytes, 0, bytes.Length);
        }
    }
}
