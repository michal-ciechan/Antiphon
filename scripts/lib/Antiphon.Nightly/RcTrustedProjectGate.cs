// CARD-1836 S3: the trusted shared-project gate (plan D-4). Trust is a reviewed, revocable exception read ONLY from the
// comparison commit's policy (scripts/lib/rc-trusted-projects.json at B): a candidate cannot authorize its own trust, and
// any policy difference between B and A ignores trust for that decision (trust-policy-changed). A private change inside a
// trusted project executes its own tests and HOLDS its consumers; the holds become carries only when every owned test
// chunk executed clean (pass, zero skipped, every listed case passed) and both B and A contract receipts exist with the
// same extractor and equal hashes. Anything else expands the holds into the same attempt. SDK, toolchain and package
// changes bypass trust (global Full). The shipped allowlist is empty. Plain C#, ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One chunk the gate places: its id, project and every project it consumes (transitively), and whether it has an independent direct hit.</summary>
    public sealed class RcTrustChunk
    {
        public string Id { get; set; } = "";
        public string Project { get; set; } = "";
        public string[] Consumes { get; set; } = Array.Empty<string>();
        public bool DirectHit { get; set; }
    }

    /// <summary>An owned test chunk's result at the candidate.</summary>
    public sealed class RcOwnResult
    {
        public string Chunk { get; set; } = "";
        /// <summary>executed, missing or unexecuted.</summary>
        public string Disposition { get; set; } = "";
        public string Result { get; set; } = "";
        public bool TimedOut { get; set; }
        public int Skipped { get; set; }
        public string[] PassedCases { get; set; } = Array.Empty<string>();
    }

    /// <summary>A contract hash receipt for one project at one side (B or A).</summary>
    public sealed class RcContractReceipt
    {
        public string Project { get; set; } = "";
        public string Side { get; set; } = "";
        public string ExtractorVersion { get; set; } = "";
        public string ContractHash { get; set; } = "";
    }

    public sealed class RcTrustChunkDecision
    {
        public string Chunk { get; set; } = "";
        /// <summary>execute, held or carry.</summary>
        public string Decision { get; set; } = "carry";
        public string[] Reasons { get; set; } = Array.Empty<string>();
    }

    public sealed class RcTrustDecision
    {
        /// <summary>Scoped or Full.</summary>
        public string Mode { get; set; } = "Scoped";
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public string[] Refused { get; set; } = Array.Empty<string>();
        public string PolicyDigestB { get; set; } = "";
        public string PolicyDigestA { get; set; } = "";
        public RcTrustChunkDecision[] Chunks { get; set; } = Array.Empty<RcTrustChunkDecision>();

        public RcTrustChunkDecision Of(string chunk) => Chunks.FirstOrDefault(c => string.Equals(c.Chunk, chunk, StringComparison.Ordinal));
    }

    /// <summary>One admitted policy entry.</summary>
    public sealed class RcTrustEntry
    {
        public string Project { get; set; } = "";
        public string[] Configurations { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> OwnedTests { get; set; } = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public string[] Downstream { get; set; } = Array.Empty<string>();
        public string ContractProducer { get; set; } = "";
    }

    public static class RcTrustedProjectGate
    {
        public const string PolicyPath = "scripts/lib/rc-trusted-projects.json";
        public const string Format = "antiphon-rc-trusted-projects-v1";

        static readonly Regex GlobalInput = new Regex(@"(^|/)(global\.json|NuGet\.Config|nuget\.config|Directory\.Packages\.props|Directory\.Build\.(props|targets)|packages\.lock\.json|package-lock\.json|package\.json|dotnet-tools\.json)$",
            RegexOptions.CultureInvariant);
        static readonly Regex ExternalEntry = new Regex(@"(^(nuget|npm|sdk|package|runtime|toolchain):)|(^|/)(global\.json|Directory\.Packages\.props|Directory\.Build\.(props|targets)|packages\.lock\.json|package-lock\.json|package\.json)$|Microsoft\.NET\.Sdk",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The SHA-256 of a policy text's UTF-8 bytes; "absent" for a missing policy.</summary>
        public static string PolicyDigest(string policy) =>
            policy == null ? "absent" : Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(policy))).ToLowerInvariant();

        /// <summary>
        /// The entries of a policy document, each refused one listed as trust-entry-refused:&lt;project&gt;:&lt;reason&gt;
        /// (wildcard, no-owned-tests, no-contract, not-a-project, external). An unreadable document refuses everything.
        /// </summary>
        public static RcTrustEntry[] Entries(string policy, List<string> refused)
        {
            var entries = new List<RcTrustEntry>();
            if (string.IsNullOrWhiteSpace(policy)) return entries.ToArray();
            JsonDocument doc;
            try { doc = JsonDocument.Parse(policy); }
            catch (JsonException) { refused.Add("trust-policy-unreadable"); return entries.ToArray(); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != Format
                    || !root.TryGetProperty("entries", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    refused.Add("trust-policy-format");
                    return entries.ToArray();
                }
                foreach (var e in list.EnumerateArray())
                {
                    var project = Text(e, "project");
                    string why = null;
                    if (project.Length == 0) why = "no-project";
                    else if (ExternalEntry.IsMatch(project)) why = "external";
                    else if (project.IndexOfAny(new[] { '*', '?', '[' }) >= 0) why = "wildcard";
                    else if (!project.EndsWith(".csproj", StringComparison.Ordinal) || Path.IsPathRooted(project) || project.Contains("..", StringComparison.Ordinal)) why = "not-a-project";
                    var owned = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    if (e.TryGetProperty("ownedTests", out var tests) && tests.ValueKind == JsonValueKind.Array)
                        foreach (var t in tests.EnumerateArray())
                        {
                            var cases = t.TryGetProperty("cases", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>();
                            var chunk = Text(t, "chunk");
                            if (chunk.Length > 0 && cases.Length > 0) owned[chunk] = cases;
                        }
                    if (why == null && owned.Count == 0) why = "no-owned-tests";
                    if (why == null && Text(e, "contractProducer").Length == 0) why = "no-contract";
                    if (why != null)
                    {
                        refused.Add("trust-entry-refused:" + project + ":" + why);
                        continue;
                    }
                    entries.Add(new RcTrustEntry
                    {
                        Project = project, OwnedTests = owned, ContractProducer = Text(e, "contractProducer"),
                        Configurations = Strings(e, "configurations"), Downstream = Strings(e, "downstream"),
                    });
                }
            }
            return entries.ToArray();
        }

        /// <summary>
        /// The gate's decision. policyB/policyA are the policy texts at B and A (null: absent). ownResults null is the first
        /// pass (holds unresolved); a list resolves every hold.
        /// </summary>
        public static RcTrustDecision Decide(string policyB, string policyA, IReadOnlyList<string> changedPaths, IReadOnlyList<RcTrustChunk> chunks,
            IReadOnlyList<RcOwnResult> ownResults, IReadOnlyList<RcContractReceipt> contracts)
        {
            var decision = new RcTrustDecision { PolicyDigestB = PolicyDigest(policyB), PolicyDigestA = PolicyDigest(policyA) };
            var reasons = new List<string>();
            var refused = new List<string>();
            var paths = (changedPaths ?? Array.Empty<string>()).ToArray();
            foreach (var path in paths) if (GlobalInput.IsMatch(path)) reasons.Add("global-input:" + path);
            if (reasons.Count > 0)
            {
                decision.Mode = "Full";
                decision.Reasons = reasons.ToArray();
                decision.Chunks = chunks.Select(c => new RcTrustChunkDecision { Chunk = c.Id, Decision = "execute", Reasons = new[] { "full" } }).ToArray();
                return decision;
            }

            // Trust comes from B only, and only when A carries the identical policy.
            var trusted = Entries(policyB, refused);
            if (decision.PolicyDigestA != decision.PolicyDigestB)
            {
                reasons.Add("trust-policy-changed");
                Entries(policyA, refused);
                trusted = Array.Empty<RcTrustEntry>();
            }
            decision.Refused = refused.Distinct(StringComparer.Ordinal).ToArray();

            var changedProjects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
                foreach (var project in chunks.SelectMany(c => c.Consumes.Append(c.Project)).Distinct(StringComparer.Ordinal))
                    if (path.StartsWith(Dir(project), StringComparison.Ordinal)) changedProjects.Add(project);

            // Review r1 D-5: package versions live in each project file here (no central package file), so a changed project
            // file inside a trusted project, or any changed MSBuild import (.props/.targets, which any project may import),
            // is an SDK/package change: trust is void for that decision and its consumers execute.
            var voided = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in trusted)
                foreach (var path in paths.Where(BuildInput))
                    if (path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(Dir(entry.Project), StringComparison.Ordinal))
                    {
                        voided.Add(entry.Project);
                        reasons.Add("trust-void:" + entry.Project + ":" + path);
                    }

            var placed = chunks.ToDictionary(c => c.Id, c => new List<string>(), StringComparer.Ordinal);
            var held = chunks.ToDictionary(c => c.Id, c => new List<string>(), StringComparer.Ordinal);
            foreach (var project in changedProjects)
            {
                var entry = voided.Contains(project) ? null : trusted.FirstOrDefault(t => t.Project == project);
                foreach (var chunk in chunks)
                {
                    if (chunk.Project == project) { placed[chunk.Id].Add("changed-project:" + project); continue; }
                    if (!chunk.Consumes.Contains(project, StringComparer.Ordinal)) continue;
                    if (entry == null)
                        placed[chunk.Id].Add("consumer:" + project + (voided.Contains(project) ? ":trust-void" : reasons.Contains("trust-policy-changed") ? ":trust-policy-changed" : ":untrusted"));
                    else if (entry.OwnedTests.ContainsKey(chunk.Id)) placed[chunk.Id].Add("trust-gate-own:" + project);
                    // Only a consumer reached through a declared downstream edge may wait for the gate (Review r1 D-5).
                    else if (!entry.Downstream.Contains(chunk.Project, StringComparer.Ordinal) && !chunk.Consumes.Any(c => entry.Downstream.Contains(c, StringComparer.Ordinal)))
                        placed[chunk.Id].Add("consumer:" + project + ":undeclared-edge");
                    else held[chunk.Id].Add(project);
                }
                if (entry != null)
                    foreach (var own in entry.OwnedTests.Keys) if (placed.ContainsKey(own)) placed[own].Add("trust-gate-own:" + project);
            }
            foreach (var chunk in chunks) if (chunk.DirectHit) placed[chunk.Id].Add("direct-hit");

            // Resolve each influencing gate; every one must pass for a hold to carry.
            var gate = new Dictionary<string, string>(StringComparer.Ordinal);
            if (ownResults != null)
                foreach (var entry in trusted.Where(t => changedProjects.Contains(t.Project) && !voided.Contains(t.Project))) gate[entry.Project] = GateFailure(entry, ownResults, contracts);

            var answer = new List<RcTrustChunkDecision>();
            foreach (var chunk in chunks)
            {
                var why = placed[chunk.Id];
                var holds = held[chunk.Id];
                if (holds.Count > 0)
                {
                    if (ownResults == null) why.AddRange(holds.Select(p => "held:" + p));
                    else
                    {
                        var failures = holds.Where(p => gate[p] != null).Select(p => "trust-gate-failed:" + p + ":" + gate[p]).ToArray();
                        if (failures.Length > 0) why.AddRange(failures);
                        else if (why.Count == 0)
                        {
                            answer.Add(new RcTrustChunkDecision { Chunk = chunk.Id, Decision = "carry", Reasons = holds.Select(p => "trust-gate-passed:" + p).ToArray() });
                            continue;
                        }
                    }
                }
                var state = why.Count == 0 ? "carry" : why.Any(r => !r.StartsWith("held:", StringComparison.Ordinal)) ? "execute" : "held";
                answer.Add(new RcTrustChunkDecision { Chunk = chunk.Id, Decision = state, Reasons = why.Distinct(StringComparer.Ordinal).ToArray() });
            }
            decision.Reasons = reasons.ToArray();
            decision.Chunks = answer.ToArray();
            return decision;
        }

        /// <summary>null when the gate passed; otherwise why it failed (own-missing, own-red, own-skip, own-timeout, own-case-missing, contract-absent, contract-drift).</summary>
        static string GateFailure(RcTrustEntry entry, IReadOnlyList<RcOwnResult> ownResults, IReadOnlyList<RcContractReceipt> contracts)
        {
            foreach (var own in entry.OwnedTests)
            {
                var result = ownResults.FirstOrDefault(r => r.Chunk == own.Key);
                if (result == null || result.Disposition != "executed") return "own-missing";
                if (result.TimedOut || result.Result == "TIMEOUT") return "own-timeout";
                if (result.Skipped > 0) return "own-skip";
                if (result.Result != "pass") return "own-red";
                var passed = new HashSet<string>(result.PassedCases ?? Array.Empty<string>(), StringComparer.Ordinal);
                if (own.Value.Any(c => !passed.Contains(c))) return "own-case-missing";
            }
            var b = (contracts ?? Array.Empty<RcContractReceipt>()).Where(c => c.Project == entry.Project && c.Side == "B").ToArray();
            var a = (contracts ?? Array.Empty<RcContractReceipt>()).Where(c => c.Project == entry.Project && c.Side == "A").ToArray();
            if (b.Length != 1 || a.Length != 1 || string.IsNullOrEmpty(b[0].ContractHash) || string.IsNullOrEmpty(a[0].ContractHash)) return "contract-absent";
            if (b[0].ExtractorVersion != a[0].ExtractorVersion || b[0].ContractHash != a[0].ContractHash) return "contract-drift";
            return null;
        }

        /// <summary>A project file or MSBuild import that can carry a package or SDK reference.</summary>
        static bool BuildInput(string path) =>
            path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) || path.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase);

        static string Dir(string project)
        {
            var slash = project.LastIndexOf('/');
            return slash < 0 ? "" : project.Substring(0, slash + 1);
        }

        static string Text(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        static string[] Strings(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
    }
}
