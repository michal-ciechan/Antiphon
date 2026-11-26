// CARD-1545 S2a-2: the live-lane environment attestation consumer, ported from scripts/lib/live-lane-attestation.ps1 (W2
// slice S22; the script is deleted). Diagnostic and shadow-only: it never earns gate credit. It recomputes every verdict
// from the document's own facts, trusts no summary field, and makes the attestation unknown whenever an executable,
// settings source or runtime fact is not proven. Schema faults throw a LiveLaneRefusal (status exit 2); nothing echoes a
// value. Also the D-6 external freshness rule, the bounded document reader and the lane roster the receipts bind to.
// Every check runs in the order the PowerShell ran it, so the same document gives the same reasons and the same refusal.
// Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>A document read from disk: the value and the SHA-256 of its bytes.</summary>
    public sealed class LiveLaneDocument
    {
        public LiveLaneDocument(object value, string sha256)
        {
            Value = value;
            Sha256 = sha256;
        }

        public object Value { get; }
        public string Sha256 { get; }
    }

    /// <summary>Get-LiveLaneEnvironmentAttestation's answer: complete with a digest and components, or unknown with reasons.</summary>
    public sealed class LiveLaneEnvironment
    {
        public const string Format = "antiphon-live-lane-environment-v1";

        public string LaneId { get; set; }
        public string Status { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        public string Digest { get; set; }
        /// <summary>executable, runtime, settings, account (null when unknown).</summary>
        public LiveLaneMap Components { get; set; }
        /// <summary>The external epoch the document named (only on a complete attestation).</summary>
        public string Epoch { get; set; }

        public bool Complete => Status == "complete";

        /// <summary>The ordered hashtable the PowerShell returned.</summary>
        public LiveLaneMap ToJson()
        {
            var map = new LiveLaneMap(("format", Format), ("laneId", LaneId), ("status", Status), ("reasons", Reasons.Cast<object>().ToList()),
                ("digest", Digest), ("components", Components));
            if (Complete) map["external"] = new LiveLaneMap(("epoch", Epoch));
            return map;
        }
    }

    /// <summary>A binding of the D-6 rule: a nonce, host, account, epoch and receipt id (each may be absent).</summary>
    public sealed class LiveLaneBinding
    {
        public object Nonce { get; set; }
        public object Host { get; set; }
        public object Account { get; set; }
        public object Epoch { get; set; }
        public object ReceiptId { get; set; }

        internal object Fact(string key)
        {
            switch (key)
            {
                case "nonce": return Nonce;
                case "host": return Host;
                case "account": return Account;
                case "epoch": return Epoch;
                case "receiptId": return ReceiptId;
                default: return null;
            }
        }
    }

    /// <summary>Test-LiveLaneExternalFreshness's answer.</summary>
    public sealed class LiveLaneFreshness
    {
        public string Policy { get; set; }
        public bool ReuseEligible { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        public bool ExternalProof => false;

        public LiveLaneMap ToJson() => new LiveLaneMap(("policy", Policy), ("reuseEligible", ReuseEligible),
            ("reasons", Reasons.Cast<object>().ToList()), ("externalProof", ExternalProof));
    }

    /// <summary>The attestation, the D-6 rule, the bounded reader and the roster.</summary>
    public static class LiveLaneAttestation
    {
        public const string AttestationFormat = "antiphon-live-lane-attestation-v1";
        public const string AlwaysRerun = "AlwaysRerun";
        public const string Bounded24h = "Bounded24h";
        public static readonly IReadOnlyList<string> Policies = new[] { AlwaysRerun, Bounded24h };

        private static readonly string[] AttestationFields = { "account", "environment", "external", "format", "host", "installedTarget", "laneId", "model", "rows", "runtime", "schemaVersion", "settings" };
        private static readonly string[] RuntimeFields = { "backend", "dotnetRuntime", "dotnetSdk", "node", "pwsh", "restoredPackagesDigest" };
        private static readonly string[] EnvironmentNames = { "ANTHROPIC_BASE_URL", "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN", "DISABLE_AUTOUPDATER" };
        private static readonly string[] SettingsKinds = { "global", "local", "managed", "project", "user" };
        private static readonly string[] CredentialNames =
        {
            "oauthAccount", "accessToken", "refreshToken", "apiKey", "primaryApiKey", "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN",
            "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY_FILE", "ANTHROPIC_AUTH_TOKEN_FILE", "CLAUDE_CODE_OAUTH_TOKEN_FILE", "ANTIPHON_TASK_TOKEN"
        };

        // A PATH-resolved launcher is not an owned attested image.
        private static readonly Regex PathSearchLeaf = new Regex(@"^cl(\.(exe|bat|cmd|ps1))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LeafPrefix = new Regex(@"^.*[\\/]");
        private static readonly Regex AnySha = new Regex("^[0-9A-Fa-f]{64}$");
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_.]*$");
        private static readonly Regex Argument = new Regex("^[A-Za-z0-9_.-]+$");
        private static readonly Regex EnvironmentState = new Regex("^(absent|empty|value:.+)$");

        public static readonly TimeSpan ReuseHorizon = TimeSpan.FromHours(24);
        public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(60);

        /// <summary>Attestation, receipt, use and context documents are small; anything larger is refused unread.</summary>
        public const int DocumentCap = 1048576;

        /// <summary>
        /// Read-LiveLaneJsonDocument: one local JSON document read without following a link (the file and its directory must
        /// not be reparse points), refused above the cap. Every fault is a fixed token: invalid, attestation-link or
        /// attestation-too-large.
        /// </summary>
        public static LiveLaneDocument ReadDocument(string path, string invalid, int maxDepth = 64)
        {
            string full;
            FileAttributes attributes, parent;
            try
            {
                full = Path.GetFullPath(path);
                attributes = File.GetAttributes(full);
                parent = File.GetAttributes(Path.GetDirectoryName(full));
            }
            catch (Exception) { throw new LiveLaneRefusal(invalid); }
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || parent.HasFlag(FileAttributes.ReparsePoint)) throw new LiveLaneRefusal("attestation-link");
            if (attributes.HasFlag(FileAttributes.Directory)) throw new LiveLaneRefusal(invalid);
            byte[] buffer;
            int count = 0;
            bool tooLarge;
            try
            {
                using var stream = File.OpenRead(full);
                if (stream.Length > DocumentCap)
                {
                    tooLarge = true;
                    buffer = Array.Empty<byte>();
                }
                else
                {
                    buffer = new byte[DocumentCap + 1];
                    while (count < buffer.Length)
                    {
                        var read = stream.Read(buffer, count, buffer.Length - count);
                        if (read <= 0) break;
                        count += read;
                    }
                    tooLarge = count > DocumentCap;
                }
            }
            catch (Exception) { throw new LiveLaneRefusal(invalid); }
            if (tooLarge) throw new LiveLaneRefusal("attestation-too-large");
            var bytes = new byte[count];
            Array.Copy(buffer, bytes, count);
            object document;
            try { document = LiveLaneData.Parse(new UTF8Encoding(false, true).GetString(bytes), maxDepth); }
            catch (Exception) { throw new LiveLaneRefusal(invalid); }
            return new LiveLaneDocument(document, LiveLaneData.Sha256(bytes));
        }

        /// <summary>ConvertTo-LiveLaneRoster: the lane roster of a manifest's core rows, namespace.class.method(arguments), exact and unique.</summary>
        public static IReadOnlyList<string> Roster(object manifest)
        {
            var map = LiveLaneData.AsObject(manifest);
            var core = map == null ? null : LiveLaneData.AsList(map["core"]);
            if (core == null) throw new LiveLaneRefusal("manifest-invalid");
            var roster = new List<string>();
            foreach (var item in core)
            {
                var row = LiveLaneData.AsObject(item);
                if (row == null) throw new LiveLaneRefusal("manifest-invalid");
                foreach (var field in new[] { "namespace", "class", "method" })
                    if (!(row[field] is string text) || !Identifier.IsMatch(text)) throw new LiveLaneRefusal("manifest-invalid");
                var value = row["arguments"];
                var arguments = value == null ? new List<object>() : LiveLaneData.AsList(value);
                if (arguments == null || arguments.Any(a => !(a is string s) || !Argument.IsMatch(s))) throw new LiveLaneRefusal("manifest-invalid");
                var uid = row["namespace"] + "." + row["class"] + "." + row["method"];
                if (arguments.Count > 0) uid += "(" + string.Join(",", arguments.Cast<string>()) + ")";
                if (roster.Contains(uid, StringComparer.Ordinal)) throw new LiveLaneRefusal("manifest-invalid");
                roster.Add(uid);
            }
            return LiveLaneData.Sorted(roster);
        }

        /// <summary>Assert-LiveLaneCredentialFree: a credential-named key anywhere (any letter case) refuses; its value is never read.</summary>
        public static void AssertCredentialFree(object value)
        {
            if (value is LiveLaneMap map)
            {
                foreach (var pair in map)
                {
                    if (CredentialNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) throw new LiveLaneRefusal("credential-field-present");
                    AssertCredentialFree(pair.Value);
                }
            }
            else if (value is IList<object> list)
                foreach (var item in list) AssertCredentialFree(item);
        }

        // Assert-LiveLaneObject: exact keys; optional keys may be absent.
        private static LiveLaneMap AssertObject(object value, string[] keys, params string[] optional)
        {
            if (!(value is LiveLaneMap map)) throw new LiveLaneRefusal("attestation-invalid");
            foreach (var key in map.Keys) if (!keys.Contains(key, StringComparer.Ordinal)) throw new LiveLaneRefusal("attestation-invalid");
            foreach (var key in keys) if (!optional.Contains(key, StringComparer.Ordinal) && !map.Contains(key)) throw new LiveLaneRefusal("attestation-invalid");
            return map;
        }

        // Assert-LiveLaneString.
        private static void AssertString(object value, bool nullable = false)
        {
            if (nullable && value == null) return;
            if (!(value is string text) || text.Length == 0) throw new LiveLaneRefusal("attestation-invalid");
        }

        // Get-LiveLaneSha: a typed SHA-256 or null (an unresolved image); case is not identity.
        private static string Sha(object value)
        {
            if (value == null) return null;
            if (!(value is string text) || !AnySha.IsMatch(text)) throw new LiveLaneRefusal("attestation-invalid");
            return text.ToLowerInvariant();
        }

        // Read-LiveLaneChain.
        private static List<object> Chain(object value)
        {
            var list = LiveLaneData.AsList(value);
            if (list == null) throw new LiveLaneRefusal("attestation-invalid");
            var chain = new List<object>();
            foreach (var entry in list) chain.Add(Sha(AssertObject(entry, new[] { "sha256" })["sha256"]));
            return chain;
        }

        private static string Joined(List<object> chain) => string.Join(",", chain.Select(item => item as string ?? ""));

        // Get-LiveLaneSortedBy: ordinal order by a unique string key (a later item of the same key wins).
        private static List<object> SortedBy(IEnumerable<LiveLaneMap> items, string key)
        {
            var byKey = new Dictionary<string, LiveLaneMap>(StringComparer.Ordinal);
            foreach (var item in items) byKey[(string)item[key]] = item;
            return byKey.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => (object)byKey[k]).ToList();
        }

        /// <summary>
        /// Get-LiveLaneEnvironmentAttestation. roster is the lane's expanded core uids: rows must match it exactly, and a uid
        /// outside it is never echoed (its row is named by index). Without a roster the document is unknown (roster-unbound).
        /// </summary>
        public static LiveLaneEnvironment Environment(string path, string lane, IEnumerable<string> roster)
        {
            var document = ReadDocument(path, "attestation-invalid").Value;
            AssertCredentialFree(document);
            var doc = AssertObject(document, AttestationFields);
            if (!LiveLaneData.Same(doc["format"], AttestationFormat) || !LiveLaneData.IsLong(doc["schemaVersion"], out var version) || version != 1)
                throw new LiveLaneRefusal("attestation-invalid");
            if (!(doc["laneId"] is string laneId) || laneId != lane) throw new LiveLaneRefusal("attestation-invalid");
            var reasons = new List<string>();

            // Executable identity: every row, never a prefix. Paths and process facts are checked here and kept out of the
            // digest; bytes, version, chain shape and the installed target are hashed.
            var rows = LiveLaneData.AsList(doc["rows"]);
            if (rows == null || rows.Count == 0) throw new LiveLaneRefusal("attestation-invalid");
            var uids = new HashSet<string>(StringComparer.Ordinal);
            var rosterSet = new HashSet<string>((roster ?? Array.Empty<string>()).Where(uid => !string.IsNullOrEmpty(uid)), StringComparer.Ordinal);
            if (rosterSet.Count == 0) reasons.Add("roster-unbound");
            var index = 0;
            var executables = new List<LiveLaneMap>();
            foreach (var item in rows)
            {
                var row = AssertObject(item, new[] { "uid", "attested", "observed", "pre", "post" }, "observed");
                if (!(row["uid"] is string rowUid) || rowUid.Length == 0 || !uids.Add(rowUid)) throw new LiveLaneRefusal("attestation-invalid");
                var uid = rosterSet.Contains(rowUid) ? rowUid : "row-" + index;
                if (rosterSet.Count > 0 && !rosterSet.Contains(rowUid)) reasons.Add("roster-unexpected");
                index++;
                var attested = AssertObject(row["attested"], new[] { "route", "path", "sha256", "version", "chain" });
                foreach (var field in new[] { "route", "path", "version" }) AssertString(attested[field]);
                var image = Sha(attested["sha256"]);
                var chain = Chain(attested["chain"]);
                foreach (var side in new[] { "pre", "post" }) AssertObject(row[side], new[] { "sha256" });
                var pre = Sha(((LiveLaneMap)row["pre"])["sha256"]);
                var post = Sha(((LiveLaneMap)row["post"])["sha256"]);
                var attestedPath = (string)attested["path"];
                if ((string)attested["route"] != "owned" || PathSearchLeaf.IsMatch(LeafPrefix.Replace(attestedPath, "", 1))) reasons.Add("route-not-owned:" + uid);
                if (image == null || chain.Any(link => link == null)) reasons.Add("chain-unresolved:" + uid);
                if (pre == null || post == null || pre != post || pre != image) reasons.Add("executable-drift:" + uid);
                var observedValue = row["observed"];
                if (observedValue == null) reasons.Add("observation-missing:" + uid);
                else
                {
                    var observed = AssertObject(observedValue, new[] { "path", "sha256", "chain", "pid", "started", "session" });
                    AssertString(observed["path"]);
                    var observedChain = Chain(observed["chain"]);
                    if (observedChain.Any(link => link == null)) reasons.Add("chain-unresolved:" + uid);
                    var process = LiveLaneData.IsLong(observed["pid"], out var pid) && pid > 0 && observed["started"] is string started && started.Length > 0
                        && observed["session"] is string session && session.Length > 0;
                    // In the PowerShell's order: a later test runs only when the earlier ones pass (the observed digest is
                    // typed only then).
                    if (!process || (string)observed["path"] != attestedPath || Sha(observed["sha256"]) != image
                        || Joined(observedChain) != Joined(chain) || observedChain.Count != chain.Count) reasons.Add("executable-mismatch:" + uid);
                }
                executables.Add(new LiveLaneMap(("uid", rowUid), ("route", attested["route"]), ("sha256", image), ("version", attested["version"]), ("chain", chain)));
            }
            foreach (var expected in rosterSet) if (!uids.Contains(expected)) reasons.Add("roster-missing:" + expected);
            var installed = Sha(AssertObject(doc["installedTarget"], new[] { "sha256" })["sha256"]);
            if (installed == null) reasons.Add("installed-target-unresolved");

            // Runtime: every field named, typed and present; a null is a missing proof, not a value.
            var runtime = AssertObject(doc["runtime"], RuntimeFields);
            var host = AssertObject(doc["host"], new[] { "enrollmentId", "os", "arch" });
            foreach (var field in RuntimeFields)
            {
                AssertString(runtime[field], nullable: true);
                if (runtime[field] == null) reasons.Add("runtime-missing:" + field);
            }
            foreach (var field in new[] { "enrollmentId", "os", "arch" }) AssertString(host[field]);

            // Settings: eligibility is recomputed from the source states; the document's own reuseEligible can only
            // withdraw reuse, never grant it.
            var settings = AssertObject(doc["settings"], new[] { "digest", "reuseEligible", "sources" });
            AssertString(settings["digest"]);
            var sourceList = LiveLaneData.AsList(settings["sources"]);
            if (!LiveLaneData.IsBool(settings["reuseEligible"], out var reuseEligible) || sourceList == null) throw new LiveLaneRefusal("attestation-invalid");
            if (!reuseEligible) reasons.Add("settings-not-reusable");
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<LiveLaneMap>();
            foreach (var item in sourceList)
            {
                var source = AssertObject(item, new[] { "kind", "state", "projectionDigest" });
                AssertString(source["kind"]);
                AssertString(source["state"]);
                var projection = Sha(source["projectionDigest"]);
                var kind = (string)source["kind"];
                var state = (string)source["state"];
                if (!kinds.Add(kind)) throw new LiveLaneRefusal("attestation-invalid");
                if (!SettingsKinds.Concat(new[] { "overlay" }).Contains(kind, StringComparer.Ordinal) || state == "unresolved") reasons.Add("source-unresolved");
                else if (state == "unreadable") reasons.Add("source-unreadable:" + kind);
                else if (state == "absent-unverified" || state == "unverified") reasons.Add("source-unverified:" + kind);
                else if (!new[] { "absent", "empty", "value" }.Contains(state, StringComparer.Ordinal)) reasons.Add("source-unresolved");
                if (projection == null) throw new LiveLaneRefusal("attestation-invalid");
                sources.Add(new LiveLaneMap(("kind", kind), ("state", state), ("projectionDigest", projection)));
            }
            foreach (var kind in SettingsKinds) if (!kinds.Contains(kind)) reasons.Add("source-missing:" + kind);
            var model = AssertObject(doc["model"], new[] { "mode", "requested", "effective" });
            foreach (var field in new[] { "mode", "requested", "effective" }) AssertString(model[field], nullable: true);
            if (model["effective"] == null) reasons.Add("model-unknown");
            var environment = AssertObject(doc["environment"], EnvironmentNames);
            foreach (var name in EnvironmentNames)
                if (!(environment[name] is string value) || !EnvironmentState.IsMatch(value)) throw new LiveLaneRefusal("attestation-invalid");
            var account = AssertObject(doc["account"], new[] { "enrollmentId", "authMethod", "subscriptionClass" });
            foreach (var field in new[] { "enrollmentId", "authMethod", "subscriptionClass" }) AssertString(account[field]);
            var external = AssertObject(doc["external"], new[] { "epoch" });
            AssertString(external["epoch"], nullable: true);

            var unknownReasons = LiveLaneData.Sorted(reasons);
            if (unknownReasons.Length > 0)
                // No partial digest: one refused row makes the whole environment unknown.
                return new LiveLaneEnvironment { LaneId = lane, Status = "unknown", Reasons = unknownReasons };
            var components = new LiveLaneMap(
                ("executable", LiveLaneData.Digest(new LiveLaneMap(("rows", SortedBy(executables, "uid")), ("installedTarget", installed)))),
                ("runtime", LiveLaneData.Digest(new LiveLaneMap(("runtime", runtime), ("os", host["os"]), ("arch", host["arch"])))),
                ("settings", LiveLaneData.Digest(new LiveLaneMap(("sources", SortedBy(sources, "kind")), ("model", model), ("environment", environment)))),
                ("account", LiveLaneData.Digest(new LiveLaneMap(("host", host["enrollmentId"]), ("account", account)))));
            var material = "antiphon-live-lane-environment\n" + LiveLaneData.Canonical(new LiveLaneMap(("schemaVersion", 1), ("laneId", lane), ("components", components)));
            return new LiveLaneEnvironment
            {
                LaneId = lane, Status = "complete", Reasons = Array.Empty<string>(), Digest = LiveLaneData.Sha256Text(material), Components = components,
                Epoch = external["epoch"] as string
            };
        }

        /// <summary>
        /// Test-LiveLaneExternalFreshness (D-6). AlwaysRerun is the default until the operator accepts reuse. Bounded24h is
        /// the only exception: an epoch, age under a fixed 24 h horizon, fresh auth and control probes bound to this
        /// candidate, host and account, and no revocation. Remote stability is never proven. A probe is a JSON object (kind,
        /// nonce, host, account, result, atUtc: a DateTimeOffset, a DateTime or a string); a revocation names an epoch or a
        /// receipt id. The horizon is not configurable (there is no parameter for it).
        /// </summary>
        public static LiveLaneFreshness Freshness(string policy, DateTimeOffset? executedAtUtc, DateTimeOffset? nowUtc, LiveLaneBinding candidate,
            LiveLaneBinding executed, IEnumerable<object> probes, IEnumerable<object> revocations)
        {
            policy ??= AlwaysRerun;
            if (!Policies.Contains(policy, StringComparer.Ordinal)) throw new LiveLaneRefusal("policy-invalid");
            LiveLaneFreshness Refuse(IEnumerable<string> why) => new LiveLaneFreshness { Policy = policy, ReuseEligible = false, Reasons = LiveLaneData.Sorted(why) };
            if (policy == AlwaysRerun) return Refuse(new[] { "policy-always-rerun" });
            if (nowUtc == null || executedAtUtc == null) throw new LiveLaneRefusal("time-invalid");
            var now = nowUtc.Value;
            var why = new List<string>();
            // A binding is a non-empty string on both sides; missing equals nothing, not missing.
            static bool Bound(object value) => value is string text && text.Length > 0;
            object CandidateFact(string key) => candidate?.Fact(key);
            object ExecutedFact(string key) => executed?.Fact(key);
            if (!(Bound(CandidateFact("nonce")) && Bound(CandidateFact("host")) && Bound(CandidateFact("account")))) why.Add("candidate-binding-missing");
            if (!(Bound(ExecutedFact("host")) && Bound(ExecutedFact("account")))) why.Add("executed-binding-missing");
            if (!Bound(ExecutedFact("epoch"))) why.Add("external-epoch-unknown");
            var age = now - executedAtUtc.Value;
            if (age < -ClockSkew) why.Add("clock-skew");
            else if (age >= ReuseHorizon) why.Add("reuse-expired");
            var probeList = (probes ?? Array.Empty<object>()).ToList();
            foreach (var kind in new[] { "auth", "control" })
            {
                var matching = probeList.OfType<LiveLaneMap>().Where(p => LiveLaneData.Same(p["kind"], kind)).ToList();
                if (matching.Count == 0) { why.Add("probe-missing:" + kind); continue; }
                foreach (var probe in matching)
                {
                    if (!(Bound(probe["nonce"]) && Bound(probe["host"]) && Bound(probe["account"]))) why.Add("probe-binding-missing:" + kind);
                    if (!LiveLaneData.Same(probe["nonce"], CandidateFact("nonce"))) why.Add("probe-foreign");
                    if (!LiveLaneData.Same(probe["host"], CandidateFact("host")) || !LiveLaneData.Same(probe["host"], ExecutedFact("host"))) why.Add("host-mismatch");
                    if (!LiveLaneData.Same(probe["account"], CandidateFact("account")) || !LiveLaneData.Same(probe["account"], ExecutedFact("account"))) why.Add("account-mismatch");
                    if (!LiveLaneData.Same(probe["result"], "Ok")) why.Add("probe-failed:" + kind);
                    // A malformed time is refused under a fixed token; its value is never echoed.
                    var probeAt = ProbeTime(probe["atUtc"]);
                    if (probeAt == null) { why.Add("probe-time-invalid:" + kind); continue; }
                    var probeAge = now - probeAt.Value;
                    if (probeAge > ProbeWindow || probeAge < -ClockSkew) why.Add("probe-stale:" + kind);
                }
            }
            foreach (var revocation in revocations ?? Array.Empty<object>())
            {
                if (!(revocation is LiveLaneMap map)) { why.Add("revoked"); continue; }
                if ((LiveLaneData.Truthy(map["epoch"]) && LiveLaneData.Same(map["epoch"], ExecutedFact("epoch")))
                    || (LiveLaneData.Truthy(map["receiptId"]) && LiveLaneData.Same(map["receiptId"], ExecutedFact("receiptId")))) why.Add("revoked");
            }
            if (why.Count > 0) return Refuse(why);
            return new LiveLaneFreshness { Policy = policy, ReuseEligible = true, Reasons = new[] { "d6-bounded-exception" } };
        }

        private static DateTimeOffset? ProbeTime(object value)
        {
            switch (value)
            {
                case DateTimeOffset offset: return offset;
                case DateTime time: return new DateTimeOffset(time);
                case string text:
                    return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed : (DateTimeOffset?)null;
                default: return null;
            }
        }
    }
}
