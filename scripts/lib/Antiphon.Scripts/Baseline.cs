// Read-ReleaseSelectionBaseline (scripts/lib/release-selection.ps1): which baseline B an attempt proposes against,
// the digest of the receipt that names it, and the fallback reason when there is none or it is incompatible.
//
// A receipt is answered only when every check the PowerShell makes passes on values of the exact types it expects:
// each comparison here is the strict form of the PowerShell one (strings compared ordinally where it compares
// case-sensitively, a real number where it compares loosely, a list where it would wrap a scalar), so a receipt
// accepted here is one the PowerShell accepts and binds to the same B and digest. Any refusal, and any value whose
// loose PowerShell reading would have to be guessed, is NotHandled.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Scripts
{
    internal static class Baseline
    {
        internal sealed class Choice
        {
            public string Sha = "";
            public string Digest = "";
            public string Reason = "";
            public bool Compatible;
        }

        private static readonly string[] RequiredFiles = { "authority/authority.json", "authority/execution-plan.json", "authority/discovery.json",
            "authority/ledger.json", "authority/publication-authority.json", "release-manifest.json", "publish.json", "complete-green.json" };
        private static readonly string[] RcSuites = { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts", "e2e" };
        // ConvertFrom-ReleaseAuthorityPolicyBytes (scripts/lib/release-authority.ps1): from schema 3 on, each mirrored
        // test project the policy declares in projectDispositions adds its own rc suite. Keep this list in the same
        // order as the PowerShell; a project added there and not here makes every baseline receipt NotHandled
        // (policy-rc-suites), which ReleaseSelectionCliTests.BaselineMatchesWorker reports as route=worker.
        private static readonly (string Project, string Suite)[] DeclaredProjectSuites =
        {
            ("tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj", "observability"),
            ("tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj", "server-git"),
            ("tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj", "resilience"),
            ("tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj", "git-in-process"),
        };
        private static readonly Regex Sha256 = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex LongLivedRef = new Regex(@"^rc/(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StampRef = new Regex(@"^release/rc-(?<stamp>\d{8}T\d{6}Z)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static NotHandledException Refused(string reason) => new NotHandledException("baseline:" + reason);

        internal static Choice Read(PsObject request, string repositoryRoot, string releaseRoot, PsObject contract)
        {
            object requested = request.GetExact("BaselineSha");
            if (requested != null && !(requested is string)) throw new NotHandledException("request-type:BaselineSha");
            string requestedSha = (string)requested ?? "";
            object receiptValue = request.GetExact("BaselineReceiptPath");
            if (receiptValue != null && !(receiptValue is string)) throw new NotHandledException("request-type:BaselineReceiptPath");
            string path = (string)receiptValue;
            // $empty: [string]$Request.BaselineSha, no receipt digest, baseline-absent.
            var empty = new Choice { Sha = requestedSha, Reason = "baseline-absent" };
            if (string.IsNullOrEmpty(path))
            {
                string archive = Path.Combine(releaseRoot, "baselines");
                if (!ReleaseSelection.PathExists(archive)) return empty;
                if (!Directory.Exists(archive)) throw Refused("archive-shape");
                // Every archived receipt is validated before one is chosen; the last compatible one (by name)
                // naming the requested B, if any, wins. Get-ChildItem -File -Filter '*.json' leaves out hidden and
                // system files and anything else: such an archive is NotHandled rather than filtered here.
                var files = new List<FileInfo>();
                foreach (FileInfo file in new DirectoryInfo(archive).EnumerateFiles())
                {
                    if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) throw Refused("archive-file-attributes");
                    if (!file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw Refused("archive-file-name");
                    files.Add(file);
                }
                Choice chosen = null;
                foreach (FileInfo file in PsSort.CultureIgnoreCase(files, f => f.Name))
                {
                    // The recursive read with BaselineReceiptPath = FullName and BaselineSha = '': the same path checks.
                    string receipt = ReleaseSelection.AssertStatePath(file.FullName, releaseRoot, repositoryRoot);
                    if (!File.Exists(receipt)) throw Refused("archive-file-gone");
                    Choice candidate = Receipt(request, repositoryRoot, releaseRoot, contract, receipt, "");
                    if (candidate.Compatible && (requestedSha.Length == 0 || candidate.Sha == requestedSha)) chosen = candidate;
                }
                return chosen ?? empty;
            }
            string full = ReleaseSelection.AssertStatePath(path, releaseRoot, repositoryRoot);
            if (!File.Exists(full)) return empty;
            return Receipt(request, repositoryRoot, releaseRoot, contract, full, requestedSha);
        }

        // ----- strict reads of ConvertFrom-Json documents ([pscustomobject]: names ignore case) -----

        private static PsObject Doc(object value, string reason) => value as PsObject ?? throw Refused(reason);

        private static string Str(PsObject obj, string name, string reason) => obj.Get(name) as string ?? throw Refused(reason);

        private static bool Is(PsObject obj, string name, string expected) => obj.Get(name) is string s && s == expected;

        private static List<object> List(object value, string reason) => value as List<object> ?? throw Refused(reason);

        private static long Number(PsObject obj, string name, string reason) => obj.Get(name) is long n ? n : throw Refused(reason);

        private static readonly string[] AdaptedNames = { "Count", "Keys", "Values", "SyncRoot", "IsFixedSize", "IsReadOnly", "IsSynchronized" };

        /// <summary>ConvertTo-NightlyCanonicalJson of a parsed document. Its [pscustomobject] branch skips a few member
        /// names; a document carrying one of them is NotHandled rather than reproduced.</summary>
        private static string Canonical(object value)
        {
            void Check(object node)
            {
                if (node is PsObject obj)
                    foreach (var pair in obj.Pairs)
                    {
                        if (AdaptedNames.Any(n => string.Equals(n, pair.Key, StringComparison.OrdinalIgnoreCase))) throw Refused("canonical-member-name");
                        Check(pair.Value);
                    }
                else if (node is List<object> list) foreach (object item in list) Check(item);
            }
            Check(value);
            return PsJson.NightlyCanonical(value);
        }

        private static object Json(string path) => PsJson.Parse(File.ReadAllText(path, Encoding.UTF8));

        private static Choice Receipt(PsObject request, string repositoryRoot, string releaseRoot, PsObject contract, string path, string requestedSha)
        {
            PsObject receipt = Doc(Json(path), "receipt-shape");
            string repository = request.GetExact("Repository") as string ?? throw Refused("request-repository");
            if (!Is(receipt, "format", "antiphon-full-release-baseline-v1")) throw Refused("baseline-unqualified");
            if (!Is(receipt, "coverageScope", "Full") || !Is(receipt, "profile", "rc")) throw Refused("baseline-full-only");
            if (!Is(receipt, "repository", repository)) throw Refused("baseline-repository");
            if (!Is(receipt, "platform", (string)contract.Get("platform"))) throw Refused("baseline-platform");
            string sha = Str(receipt, "sha", "baseline-sha");
            if (requestedSha.Length > 0 && sha != requestedSha) throw Refused("baseline-sha");
            ReleaseSelection.AssertCommit(repositoryRoot, sha);
            string candidateRoot = Str(receipt, "candidateRoot", "candidate-root");
            if (candidateRoot.Length == 0 || Path.IsPathRooted(candidateRoot)) throw Refused("candidate-root");
            string root = ReleaseSelection.AssertStatePath(Path.Combine(releaseRoot, candidateRoot), releaseRoot, repositoryRoot);
            PsObject files = Doc(receipt.Get("files"), "baseline-digest");
            var documents = new Dictionary<string, PsObject>(StringComparer.Ordinal);
            var digests = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in RequiredFiles)
            {
                string file = ReleaseSelection.AssertStatePath(Path.Combine(root, name), root, "");
                if (!(files.Get(name) is string expected) || !Sha256.IsMatch(expected) || !File.Exists(file) || ReleaseSelection.FileSha256(file) != expected)
                    throw Refused("baseline-digest:" + name);
                digests[name] = expected;
                documents[name] = Doc(Json(file), "document-shape:" + name);
            }
            PsObject authority = documents["authority/authority.json"], ledger = documents["authority/ledger.json"];
            PsObject publication = documents["authority/publication-authority.json"], manifest = documents["release-manifest.json"];
            PsObject published = documents["publish.json"], green = documents["complete-green.json"];
            if (Number(authority, "schemaVersion", "baseline-full-only") != 1 || !Is(authority, "kind", "release-authority") ||
                !Is(authority, "profile", "rc") || !Is(authority, "repository", repository) || !Is(authority, "sha", sha) ||
                !Is(ledger, "selection", "full") || !Is(ledger, "sha", sha) ||
                !(ledger.Get("seamed") is bool seamed) || seamed || !(ledger.Get("diagnostic") is bool diagnostic) || diagnostic ||
                !(ledger.Get("noReport") is bool noReport) || noReport || !(ledger.Get("teardownSucceeded") is bool teardown) || !teardown ||
                !GreenCredit(green) || !Is(green, "profile", "rc") || !Is(green, "sha", sha)) throw Refused("baseline-full-only");
            PsObject inputs = Doc(publication.Get("inputs"), "baseline-publication-binding");
            foreach (var (name, file) in new[] { ("authority", "authority"), ("executionPlan", "execution-plan"), ("discovery", "discovery"), ("ledger", "ledger") })
                if (!Is(inputs, name + "Digest", digests["authority/" + file + ".json"])) throw Refused("baseline-publication-binding");
            string digest = PsJson.Sha256Text(Canonical(inputs));
            if (!Is(publication, "digest", digest) || !Is(manifest, "publicationAuthorityDigest", digest) || !Is(published, "publicationAuthorityDigest", digest) ||
                !Is(manifest, "sha", sha) || !Is(published, "sha", sha) || !Is(published, "manifestDigest", PsJson.Sha256Text(Canonical(manifest))))
                throw Refused("baseline-publication-binding");
            var steps = List(published.Get("steps"), "baseline-publication-required");
            if (!steps.Any(s => s is string step && step == "published") || !(published.Get("releaseId") is string releaseId) || releaseId.Length == 0 ||
                !(published.Get("releaseUrl") is string releaseUrl) || releaseUrl.Length == 0) throw Refused("baseline-publication-required");
            PsObject prepublication = Doc(ledger.Get("prepublicationReceipt"), "baseline-receipt");
            PsObject correlation = Doc(prepublication.Get("correlation"), "baseline-receipt");
            PsObject recipient = Doc(prepublication.Get("recipient"), "baseline-receipt");
            if (!Is(prepublication, "kind", "prepublication") || !Is(correlation, "sha", sha) ||
                !Is(correlation, "runId", Str(ledger, "runId", "baseline-receipt")) || !Is(recipient, "cardId", Str(authority, "releaseCardId", "baseline-receipt")) ||
                Number(recipient, "revision", "baseline-receipt") < 1) throw Refused("baseline-receipt");

            PsObject historical = ReleaseSelection.Contract(repositoryRoot, sha, "rc");
            if (Canonical(historical) != Canonical(Doc(receipt.Get("contract"), "baseline-contract"))) throw Refused("baseline-contract");
            if (!Is(authority, "policyHash", (string)historical.Get("policyHash"))) throw Refused("baseline-policy");
            var policy = PinnedPolicy(repositoryRoot, sha, out string blobId, out string rawSha256);
            if (!Is(authority, "policyBlobId", blobId) || !Is(authority, "policyRawSha256", rawSha256) ||
                Canonical(List(authority.Get("requiredSuites"), "baseline-policy")) != Canonical(policy.RequiredSuites.Cast<object>().ToList())) throw Refused("baseline-policy");

            PsObject plan = documents["authority/execution-plan.json"], discovery = documents["authority/discovery.json"];
            string intentId = Str(authority, "intentId", "baseline-ledger-binding"), candidateId = Str(authority, "candidateId", "baseline-ledger-binding");
            if (!Is(ledger, "authorityDigest", digests["authority/authority.json"]) || !Is(ledger, "executionPlanDigest", digests["authority/execution-plan.json"]) ||
                !Is(plan, "authorityDigest", digests["authority/authority.json"]) || !Is(plan, "discoveryDigest", digests["authority/discovery.json"]) ||
                !Is(plan, "sha", sha) || !Is(plan, "intentId", intentId) || !Is(ledger, "intentId", intentId) ||
                !Is(ledger, "runId", Str(green, "runId", "baseline-ledger-binding")) || !Is(ledger, "candidateId", candidateId) || !Is(manifest, "candidateId", candidateId))
                throw Refused("baseline-ledger-binding");

            var planSuites = List(plan.Get("suites"), "baseline-suite-universe").Select(s => Doc(s, "baseline-suite-universe")).ToList();
            PsObject evidence = Doc(inputs.Get("evidence"), "baseline-trx-digest");
            foreach (string suite in policy.RequiredSuites)
            {
                var planned = planSuites.Where(s => IdIs(s, "id", suite)).ToList();
                if (planned.Count != 1) throw Refused("baseline-suite-universe");
                if (suite == "client" || suite == "scripts")
                {
                    var rows = List(ledger.Get("rosters"), "baseline-roster-incomplete").Select(r => Doc(r, "baseline-roster-incomplete")).Where(r => IdIs(r, "suite", suite)).ToList();
                    if (rows.Count != 1 || Number(rows[0], "exitCode", "baseline-roster-incomplete") != 0) throw Refused("baseline-roster-incomplete");
                    var plannedEntries = Strings(planned[0].Get("entries"), "baseline-roster-incomplete");
                    var entries = List(rows[0].Get("entries"), "baseline-roster-incomplete").Select(e => Doc(e, "baseline-roster-incomplete")).ToList();
                    if (plannedEntries.Count == 0 || entries.Any(e => !Is(e, "outcome", "passed")) ||
                        Joined(entries.Select(e => Str(e, "id", "baseline-roster-incomplete"))) != Joined(plannedEntries)) throw Refused("baseline-roster-incomplete");
                    continue;
                }
                PsObject discoverySuites = Doc(discovery.Get("suites"), "baseline-uids-incomplete");
                var requiredUids = RequiredUids(List(discoverySuites.Get(suite), "baseline-uids-incomplete"), policy.Exclusions, suite);
                var executed = new List<string>();
                foreach (object chunkValue in List(planned[0].Get("chunks"), "baseline-chunk-incomplete"))
                {
                    PsObject chunk = Doc(chunkValue, "baseline-chunk-incomplete");
                    string chunkId = Str(chunk, "id", "baseline-chunk-incomplete");
                    var rows = List(ledger.Get("chunks"), "baseline-chunk-incomplete").Select(r => Doc(r, "baseline-chunk-incomplete")).Where(r => IdIs(r, "chunk", chunkId)).ToList();
                    if (rows.Count != 1 || Number(rows[0], "exitCode", "baseline-chunk-incomplete") != 0) throw Refused("baseline-chunk-incomplete");
                    string trxName = Str(rows[0], "trx", "baseline-trx");
                    if (trxName.Length == 0 || Path.IsPathRooted(trxName)) throw Refused("baseline-trx");
                    string trx = ReleaseSelection.AssertStatePath(Path.Combine(root, trxName), root, "");
                    string trxSha = Str(rows[0], "trxSha256", "baseline-trx-digest");
                    if (!File.Exists(trx) || ReleaseSelection.FileSha256(trx) != trxSha || !Is(evidence, chunkId, trxSha)) throw Refused("baseline-trx-digest");
                    var terminals = TrxTerminals(trx);
                    if (terminals.Any(t => t.Outcome != "Passed") || Joined(terminals.Select(t => t.TestId)) != Joined(Strings(chunk.Get("uids"), "baseline-chunk-incomplete")))
                        throw Refused("baseline-chunk-incomplete");
                    executed.AddRange(terminals.Select(t => t.TestId));
                }
                if (requiredUids.Count == 0 || Joined(executed) != Joined(requiredUids)) throw Refused("baseline-uids-incomplete");
            }
            bool compatible = Canonical(historical) == Canonical(contract);
            return new Choice { Sha = sha, Digest = ReleaseSelection.FileSha256(path), Compatible = compatible, Reason = compatible ? "" : "baseline-incompatible" };
        }

        /// <summary>Where-Object name -CEQ value: a string property equal ordinally; null never matches; any other type is not handled.</summary>
        private static bool IdIs(PsObject obj, string name, string value)
        {
            object v = obj.Get(name);
            if (v == null) return false;
            if (v is string s) return s == value;
            throw Refused("where-type:" + name);
        }

        private static List<string> Strings(object value, string reason) =>
            List(value, reason).Select(v => v as string ?? throw Refused(reason)).ToList();

        /// <summary>(@($values | Sort-Object) -join ','): the current culture ignoring case.</summary>
        private static string Joined(IEnumerable<string> values) => string.Join(",", PsSort.CultureIgnoreCase(values.ToList(), v => v));

        /// <summary>Get-ReleaseGateCreditVerdict($green).Ok for an rc record.</summary>
        private static bool GreenCredit(PsObject green)
        {
            string Text(string name) { object v = green.Get(name); return v == null ? "" : v as string ?? throw Refused("green-type:" + name); }
            if (Text("profile").Trim().ToLowerInvariant() != "rc" || Text("trigger").Trim().ToLowerInvariant() != "rc") return false;
            string reference = Text("ref").Trim();
            bool heads = reference.StartsWith("refs/heads/", StringComparison.Ordinal);
            // $normalised.StartsWith('refs/heads/') is a culture comparison: only the inputs where it agrees are handled.
            if (heads != reference.StartsWith("refs/heads/", StringComparison.CurrentCulture)) throw Refused("ref-prefix-culture");
            if (heads) reference = reference.Substring("refs/heads/".Length);
            if (!CandidateRef(reference)) return false;
            if (green.Has("coverageScope") && !Is(green, "coverageScope", "Full")) return false;
            foreach (string flag in new[] { "coverageComplete", "testsPassed", "reportDelivered" })
                if (!(green.Get(flag) is bool value) || !value) return false;
            if (!(green.Get("exitCode") is long exit) || exit != 0) return false;
            if (string.IsNullOrWhiteSpace(Text("runId")) || string.IsNullOrWhiteSpace(Text("policyHash"))) return false;
            string greenSha = Text("sha"), expected = Text("expectedSha");
            if (!ReleaseSelection.FullSha.IsMatch(greenSha) || !ReleaseSelection.FullSha.IsMatch(expected) || greenSha != expected) return false;
            return !string.IsNullOrWhiteSpace(Text("candidateId"));
        }

        /// <summary>Test-ReleaseGateCandidateRef(...).Ok for a ref already stripped of refs/heads/.</summary>
        private static bool CandidateRef(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return false;
            var styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            Match stamp = StampRef.Match(reference);
            if (stamp.Success) return DateTime.TryParseExact(stamp.Groups["stamp"].Value, "yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture, styles, out _);
            Match date = LongLivedRef.Match(reference);
            if (date.Success)
                return DateTime.TryParseExact(date.Groups["y"].Value + "-" + date.Groups["m"].Value + "-" + date.Groups["d"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out _);
            // Any other ref is not a candidate: no credit, so the PowerShell refuses it as not Full.
            if (reference.Any(c => c > 0x7f)) throw Refused("ref-non-ascii");
            return false;
        }

        // ----- Read-ReleaseAuthorityPinnedBlob / ConvertFrom-ReleaseAuthorityPolicyBytes -----

        private sealed class Policy
        {
            public List<string> RequiredSuites;
            public List<(string Suite, string Class, List<string> Methods)> Exclusions = new List<(string, string, List<string>)>();
        }

        private static Policy PinnedPolicy(string root, string sha, out string blobId, out string rawSha256)
        {
            const string PolicyPath = "tests/test-execution-policy.json";
            blobId = ReleaseSelection.Git(root, new[] { "rev-parse", "--verify", "--quiet", sha + ":" + PolicyPath }, new[] { 0 }).Stdout.Trim();
            if (!ReleaseSelection.FullSha.IsMatch(blobId)) throw Refused("policy-blob-unresolved");
            // The contract read these bytes (BOM-less strict UTF-8, so the text gives them back exactly).
            string text = ReleaseSelection.Texts(root, sha, new[] { PolicyPath })[PolicyPath];
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            byte[] header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
            if (Convert.ToHexString(SHA1.HashData(header.Concat(bytes).ToArray())).ToLowerInvariant() != blobId) throw Refused("policy-blob-identity");
            rawSha256 = PsJson.Sha256Hex(bytes);

            PsObject obj = Doc(PsJson.Parse(text), "policy-unparseable");
            if (!(obj.Get("schemaVersion") is long version) || version < 2 || version > 4) throw Refused("policy-schema-unsupported");
            if (!(obj.Get("policyHash") is string stated) || stated != ReleaseSelection.PolicyHash(obj)) throw Refused("policy-hash-stale");
            PsObject profile = Doc(Doc(obj.Get("profiles"), "policy-rc-profile-missing").Get("rc"), "policy-rc-profile-missing");
            var suites = Strings(profile.Get("requiredSuites"), "policy-rc-profile-missing").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (suites.Count == 0) throw Refused("policy-rc-profile-missing");
            var required = new List<string>(RcSuites);
            if (version >= 3)
            {
                required.Add("markdown-pdf");
                TestSelection.ProjectDispositions(obj);
                var dispositions = List(obj.Get("projectDispositions") ?? new List<object>(), "dispositions");
                foreach (var (project, suite) in DeclaredProjectSuites)
                    if (dispositions.Any(row => Doc(row, "dispositions").Get("project") is string p && p == project))
                        required.Add(suite);
            }
            if (required.Any(r => !suites.Contains(r)) || suites.Any(s => !required.Contains(s))) throw Refused("policy-rc-suites");
            PsObject suiteMap = Doc(obj.Get("suites"), "policy-rc-suite-undefined");
            foreach (string id in suites) if (!suiteMap.Pairs.Any(p => p.Key == id)) throw Refused("policy-rc-suite-undefined");
            var result = new Policy { RequiredSuites = suites };
            foreach (string id in suites) result.Exclusions.AddRange(ProfileExclusions(profile, id));
            return result;
        }

        /// <summary>Get-NightlyProfileExclusions -Profile rc -SuiteId id -Platform windows.</summary>
        private static List<(string, string, List<string>)> ProfileExclusions(PsObject profile, string suiteId)
        {
            object value = profile.Get("exclusions");
            var rows = value == null ? new List<object>() : List(value, "exclusions-shape");
            var result = new List<(string, string, List<string>)>();
            foreach (object item in rows)
            {
                if (item == null) continue;
                PsObject row = Doc(item, "exclusion-shape");
                string Optional(string name) { object v = row.Get(name); return v == null ? "" : v as string ?? throw Refused("exclusion-type:" + name); }
                if (string.IsNullOrWhiteSpace(Optional("reason")) || string.IsNullOrWhiteSpace(Optional("owner"))) throw Refused("exclusion-reason-owner");
                object platform = null;
                bool hasPlatform = false;
                foreach (var pair in row.Pairs) if (pair.Key == "platform") { hasPlatform = true; platform = pair.Value; break; }
                if (!hasPlatform && row.Has("platform")) throw Refused("exclusion-platform-case");
                if (hasPlatform && !(platform is string p && (p == "windows" || p == "linux"))) throw Refused("exclusion-platform");
                string suite = Optional("suite");
                if (suite != suiteId)
                {
                    if (string.Equals(suite, suiteId, StringComparison.OrdinalIgnoreCase) || string.Compare(suite, suiteId, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
                        throw Refused("exclusion-suite-case");
                    continue;
                }
                if (platform != null && (string)platform != "windows") continue;
                object methodsValue = row.Get("methods");
                var methods = (methodsValue is string one ? new List<object> { one } : methodsValue == null ? new List<object>() : List(methodsValue, "exclusion-methods"))
                    .Select(m => m as string ?? throw Refused("exclusion-methods")).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                result.Add((suite, Optional("class"), methods));
            }
            return result;
        }

        /// <summary>Get-ReleaseAuthorityRequiredUids: discovery nodes minus the suite's class or class+method rows.</summary>
        private static List<string> RequiredUids(List<object> nodes, List<(string Suite, string Class, List<string> Methods)> exclusions, string suiteId)
        {
            var rows = exclusions.Where(e => e.Suite == suiteId).ToList();
            var required = new List<string>();
            foreach (object value in nodes)
            {
                PsObject node = Doc(value, "discovery-node");
                string Optional(string name) { object v = node.Get(name); return v == null ? "" : v as string ?? throw Refused("discovery-type:" + name); }
                string className = Optional("class"), method = Optional("method");
                string simple = SimpleClassName(className);
                if (string.IsNullOrWhiteSpace(simple)) simple = className;
                bool excluded = false;
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.Class)) continue;
                    if (!string.Equals(row.Class, simple, StringComparison.OrdinalIgnoreCase) && !string.Equals(row.Class, className, StringComparison.OrdinalIgnoreCase)) continue;
                    if (row.Methods.Count == 0 || row.Methods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase))) { excluded = true; break; }
                }
                if (!excluded) required.Add(Optional("uid"));
            }
            return required;
        }

        /// <summary>Get-NightlyClassNameFromIdentity.</summary>
        private static string SimpleClassName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            int open = name.IndexOf('(');
            string bare = open > 0 && name.EndsWith(")", StringComparison.Ordinal) ? name.Substring(0, open) : name;
            string[] parts = bare.Split('.');
            return parts[parts.Length - 1];
        }

        // ----- Read-NightlyTrxIdentities: TestId and Outcome of each result -----

        private static List<(string TestId, string Outcome)> TrxTerminals(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw Refused("trx-not-regular");
            string text = File.ReadAllText(path);
            if (text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0) throw Refused("trx-dtd");
            var doc = new XmlDocument { XmlResolver = null };
            try { doc.LoadXml(text); }
            catch (XmlException) { throw Refused("trx-malformed"); }
            string ns = doc.DocumentElement?.NamespaceURI ?? throw Refused("trx-empty");
            var manager = new XmlNamespaceManager(doc.NameTable);
            if (ns.Length > 0) manager.AddNamespace("t", ns);
            XmlNodeList Select(string xpath) => ns.Length > 0 ? doc.SelectNodes(xpath, manager) : doc.SelectNodes(Regex.Replace(xpath, "t:", "", RegexOptions.IgnoreCase));
            // $definitions is a case-insensitive @{}: ids that differ only in case are not handled.
            var definitions = new Dictionary<string, (string Class, string Method)>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode unitNode in Select("//t:TestDefinitions/t:UnitTest"))
            {
                var unit = (XmlElement)unitNode;
                string id = unit.GetAttribute("id");
                if (id.Length == 0) continue;
                XmlElement method = null;
                foreach (XmlNode child in unit.ChildNodes)
                    if (string.Equals(child.LocalName, "TestMethod", StringComparison.OrdinalIgnoreCase)) { method = child as XmlElement ?? throw Refused("trx-method-node"); break; }
                if (definitions.TryGetValue(id, out _) && !definitions.Keys.Contains(id, StringComparer.Ordinal)) throw Refused("trx-id-case");
                definitions[id] = method == null ? (null, null) : (method.GetAttribute("className"), method.GetAttribute("name"));
            }
            var results = Select("//t:Results/t:UnitTestResult").Cast<XmlNode>().ToList();
            if (results.Count == 0) results = Select("//t:UnitTestResult").Cast<XmlNode>().ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<(string, string)>();
            foreach (XmlNode resultNode in results)
            {
                var result = (XmlElement)resultNode;
                string testId = result.GetAttribute("testId");
                if (testId.Length == 0 || !definitions.TryGetValue(testId, out var definition)) throw Refused("trx-missing-definition");
                if (!definitions.Keys.Contains(testId, StringComparer.Ordinal)) throw Refused("trx-id-case");
                if (!seen.Add(testId)) throw Refused("trx-duplicate-identity");
                if (string.IsNullOrWhiteSpace(definition.Class) || string.IsNullOrWhiteSpace(definition.Method)) throw Refused("trx-unresolved-identity");
                rows.Add((testId, result.GetAttribute("outcome")));
            }
            return rows;
        }
    }
}
