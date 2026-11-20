// The test-execution policy's rules (moved from nightly-policy.ps1) over the plain records of ExecutionPolicy: load and
// hash, the project-universe census, suite selection, the safe child environment and the profiles (required suites and
// declared exclusions). The nightly runner, the release gate and the release authority all read it from here; the
// release authority pins these sources by digest. Comparisons follow the PowerShell they replaced: -eq and -contains
// ignore case, -ceq and -cnotcontains do not. Only ExclusionIndex and NodeExcluded take PowerShell objects (discovery
// nodes); everything else is plain C#.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A policy file read and checked: its path, the policy, its hash and schema version.</summary>
    public sealed class PolicyFile
    {
        public string Path { get; set; } = "";
        public ExecutionPolicy Object { get; set; }
        public string Hash { get; set; } = "";
        public int SchemaVersion { get; set; }
    }

    /// <summary>A profile resolved against the policy.</summary>
    public sealed class ResolvedProfile
    {
        public string Name { get; set; } = "";
        public string[] RequiredSuites { get; set; } = new string[0];
        /// <summary>"profiles", or "defaultSuites" for the nightly profile of a schema-1 policy.</summary>
        public string Source { get; set; } = "";
        public ProfileExclusion[] Exclusions { get; set; } = new ProfileExclusion[0];
        /// <summary>The policy's profile row; null for the schema-1 nightly profile.</summary>
        public PolicyProfile Object { get; set; }
    }

    /// <summary>A profile's exclusion for one suite and platform, as the executors use it.</summary>
    public sealed class ExclusionRow : IJsonWritable
    {
        public string Suite { get; set; } = "";
        public string Class { get; set; } = "";
        public string[] Methods { get; set; } = new string[0];
        public string Reason { get; set; } = "";
        public string Owner { get; set; } = "";

        public void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("Suite", Suite);
            writer.WriteString("Class", Class);
            writer.WriteStartArray("Methods");
            foreach (var m in Methods ?? new string[0]) writer.WriteStringValue(m);
            writer.WriteEndArray();
            writer.WriteString("Reason", Reason);
            writer.WriteString("Owner", Owner);
            writer.WriteEndObject();
        }
    }

    /// <summary>The test projects on disk against the policy's: Missing (on disk only), Extra (policy only).</summary>
    public sealed class ProjectUniverseCheck
    {
        public bool Ok { get; set; }
        public string[] Missing { get; set; } = new string[0];
        public string[] Extra { get; set; } = new string[0];
        public string[] Unmatched { get; set; } = new string[0];
        public string[] Disk { get; set; } = new string[0];
        public string[] Policy { get; set; } = new string[0];
        public string Message { get; set; } = "";
    }

    public sealed class RequiredSuitesCheck
    {
        public bool Ok { get; set; }
        public string[] Missing { get; set; } = new string[0];
    }

    public static class NightlyPolicy
    {
        private static readonly int[] SupportedSchemaVersions = { 1, 2, 3, 4 };
        private static readonly string[] Profiles = { "nightly", "rc" };
        private static readonly string[] FallbackSuites = { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts" };
        private static readonly Regex TestProjectMarker = new Regex(@"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex ProjectPath = new Regex(@"^[^\\]+\.csproj$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);
        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        private static bool Blank(string s) => string.IsNullOrWhiteSpace(s);

        // Sort-Object over strings: the current culture, ignoring case.
        private static string[] Sorted(IEnumerable<string> values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
            return list.ToArray();
        }

        // ---- load and hash ---------------------------------------------------------------------------------

        /// <summary>tests/test-execution-policy.json under the repository root.</summary>
        public static string PolicyPath(string repoRoot) => System.IO.Path.Combine(repoRoot ?? "", "tests", "test-execution-policy.json");

        /// <summary>The policy hash: the canonical JSON of every top-level key but policyHash.</summary>
        public static string Hash(ExecutionPolicy policy) => policy.ComputedHash;

        /// <summary>The policy hash of policy text (for a policy built or edited as JSON).</summary>
        public static string HashText(string json) => ExecutionPolicy.HashOf(json);

        /// <summary>
        /// Read-NightlyExecutionPolicy: the policy file (policyPath, or the repository's when blank) read and checked.
        /// Refuses a missing or unreadable file, an unsupported schema and a stale hash.
        /// </summary>
        public static PolicyFile Read(string repoRoot, string policyPath)
        {
            if (Blank(policyPath)) policyPath = PolicyPath(repoRoot);
            if (!File.Exists(policyPath)) throw Refuse("policy missing: " + policyPath);
            ExecutionPolicy policy;
            try { policy = ExecutionPolicy.Parse(File.ReadAllText(policyPath), policyPath); }
            catch (JsonException) { throw Refuse("policy unreadable: " + policyPath); }
            if (Array.IndexOf(SupportedSchemaVersions, policy.SchemaVersion) < 0) throw Refuse("unsupported policy schema " + policy.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            if (Blank(policy.PolicyHash) || !string.Equals(policy.PolicyHash, policy.ComputedHash, StringComparison.OrdinalIgnoreCase))
                throw Refuse("stale policy hash stated=" + policy.PolicyHash + " computed=" + policy.ComputedHash);
            return new PolicyFile { Path = policyPath, Object = policy, Hash = policy.ComputedHash, SchemaVersion = policy.SchemaVersion };
        }

        // ---- the project universe ----------------------------------------------------------------------

        // Directories git ignores at the root of a checkout (run output, packages, build output), as full paths ending
        // in a separator: a project in one can never be committed. Not a checkout, or git unavailable: none. A
        // repository named by the environment (a hook's GIT_DIR) does not answer for this root.
        private static string[] GitIgnoredDirectories(string root)
        {
            if (!Directory.Exists(System.IO.Path.Combine(root, ".git")) && !File.Exists(System.IO.Path.Combine(root, ".git"))) return new string[0];
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] { "-C", root, "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--directory" }) start.ArgumentList.Add(arg);
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR" }) start.Environment.Remove(name);
            string output;
            try
            {
                using var process = Process.Start(start);
                var error = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                error.Wait();
                if (process.ExitCode != 0) return new string[0];
            }
            catch (Exception) { return new string[0]; }
            var dirs = new List<string>();
            foreach (var entry in output.Split('\0'))
            {
                if (!entry.EndsWith("/", StringComparison.Ordinal)) continue;
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, entry.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) full += System.IO.Path.DirectorySeparatorChar;
                dirs.Add(full);
            }
            return dirs.ToArray();
        }

        /// <summary>
        /// Repository-relative paths (forward slashes, sorted) of every committed *.csproj marked IsTestProject, outside
        /// bin, obj, bin-* and git-ignored directories.
        /// </summary>
        public static string[] TestProjectPaths(string repoRoot)
        {
            var root = System.IO.Path.GetFullPath(repoRoot.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar));
            if (root.Length > System.IO.Path.GetPathRoot(root).Length) root = root.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var hits = new List<string>();
            foreach (var full in NightlyRepository.CandidateProjectFiles(root, GitIgnoredDirectories(root)))
            {
                var skip = false;
                foreach (var part in full.Substring(root.Length).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                {
                    if (Same(part, "bin") || Same(part, "obj") || part.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
                }
                if (skip || !TestProjectMarker.IsMatch(File.ReadAllText(full))) continue;
                hits.Add(full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/'));
            }
            return Sorted(hits);
        }

        /// <summary>The projects the policy accounts for.</summary>
        public static string[] ProjectUniverse(ExecutionPolicy policy)
        {
            if (policy.SchemaVersion >= 3)
            {
                ProjectDispositions(policy);
                return (policy.ProjectDispositions ?? new PolicyProjectDisposition[0]).Select(r => r.Project).ToArray();
            }
            var ids = new List<string>();
            foreach (var suite in policy.Suites.Values) if (!Blank(suite.Project)) ids.Add(suite.Project.Replace('\\', '/'));
            foreach (var p in policy.Projects ?? new string[0]) ids.Add(p.Replace('\\', '/'));
            return Sorted(ids.Distinct(StringComparer.Ordinal));
        }

        /// <summary>
        /// One row per project; a primary row for each suite project and a child row naming the suite projects that
        /// consume it. Throws on the first broken row.
        /// </summary>
        public static void ProjectDispositions(ExecutionPolicy policy)
        {
            var rows = policy.ProjectDispositions ?? new PolicyProjectDisposition[0];
            var suiteProjects = policy.Suites.Values.Where(s => !Blank(s.Project)).Select(s => s.Project).ToList();
            if (rows.Count(r => Same(r.Role, "primary")) != suiteProjects.Count) throw Refuse("project-dispositions primary census mismatch");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var path = row.Project ?? "";
                if (!ProjectPath.IsMatch(path) || path.StartsWith("/", StringComparison.Ordinal) || Array.IndexOf(path.Split('/'), "..") >= 0 || !seen.Add(path))
                    throw Refuse("project-dispositions invalid or duplicate project");
                if (Blank(row.Reason)) throw Refuse("project-dispositions missing reason");
                if (Same(row.Role, "primary"))
                {
                    if (!suiteProjects.Contains(path)) throw Refuse("project-dispositions undefined primary");
                }
                else if (Same(row.Role, "child"))
                {
                    var consumers = row.Consumers ?? new string[0];
                    if (consumers.Length == 0) throw Refuse("project-dispositions child without consumer");
                    foreach (var consumer in consumers) if (!suiteProjects.Contains(consumer)) throw Refuse("project-dispositions unknown consumer");
                }
                else throw Refuse("project-dispositions unknown role");
            }
        }

        /// <summary>The test projects on disk against the policy's.</summary>
        public static ProjectUniverseCheck TestProjectUniverse(string repoRoot, ExecutionPolicy policy)
        {
            var disk = TestProjectPaths(repoRoot);
            var declared = ProjectUniverse(policy);
            var missing = disk.Where(d => !declared.Contains(d, StringComparer.OrdinalIgnoreCase)).ToArray();
            var extra = declared.Where(p => !disk.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
            var unmatched = missing.Concat(extra).ToArray();
            return new ProjectUniverseCheck
            {
                Ok = unmatched.Length == 0, Missing = missing, Extra = extra, Unmatched = unmatched, Disk = disk, Policy = declared,
                Message = unmatched.Length == 0 ? "" : "unmatched project " + string.Join(", ", unmatched),
            };
        }

        // ---- suites --------------------------------------------------------------------------------------

        /// <summary>Suite id to suite row (keys ignore case).</summary>
        public static Dictionary<string, PolicySuite> SuiteMap(ExecutionPolicy policy) => policy.Suites;

        // defaultSuites (or the fallback list) less the manual suites.
        private static string[] RequiredSuiteUniverse(ExecutionPolicy policy)
        {
            var selected = (policy.DefaultSuites ?? new string[0]).Where(s => !Blank(s)).ToList();
            if (selected.Count == 0) selected.AddRange(FallbackSuites);
            return selected.Where(id => !(policy.Suites.TryGetValue(id, out var suite) && Same(suite.Mode, "manual"))).ToArray();
        }

        /// <summary>The required suites the selection lacks.</summary>
        public static RequiredSuitesCheck RequiredSuitesPresent(string[] selected, string[] required)
        {
            var have = new HashSet<string>((selected ?? new string[0]).Where(s => !Blank(s)), StringComparer.OrdinalIgnoreCase);
            var missing = (required ?? new string[0]).Where(r => !Blank(r) && !have.Contains(r)).ToArray();
            return new RequiredSuitesCheck { Ok = missing.Length == 0, Missing = missing };
        }

        /// <summary>
        /// The comma-separated suite ids named (lower-cased, in first-seen order), or the required universe when none is
        /// named. An unknown id or an empty selection throws.
        /// </summary>
        public static string[] ResolveSelectedSuites(ExecutionPolicy policy, string[] suites)
        {
            if (suites == null || suites.Length == 0)
            {
                var required = RequiredSuiteUniverse(policy);
                return required.Length > 0 ? required : (string[])FallbackSuites.Clone();
            }
            var selected = new List<string>();
            foreach (var suite in suites)
            {
                foreach (var part in (suite ?? "").Split(','))
                {
                    var n = part.Trim().ToLowerInvariant();
                    if (Blank(n)) continue;
                    if (!policy.Suites.ContainsKey(n)) throw Refuse("unknown suite " + n);
                    if (!selected.Contains(n)) selected.Add(n);
                }
            }
            if (selected.Count == 0) throw Refuse("empty suite selection");
            return selected.ToArray();
        }

        /// <summary>The policy's script census rows.</summary>
        public static PolicyCensusRow[] ScriptCensus(ExecutionPolicy policy) => policy.ScriptCensus ?? new PolicyCensusRow[0];

        /// <summary>
        /// The base environment (the process's when null) with safeEnvironment.clear emptied and safeEnvironment.set
        /// applied; messaging also gets ANTIPHON_BROKER_TESTS=1. Names ignore case.
        /// </summary>
        public static Hashtable SafeChildEnvironment(ExecutionPolicy policy, string suiteId, IDictionary baseEnvironment)
        {
            var env = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (baseEnvironment != null) foreach (DictionaryEntry e in baseEnvironment) env[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
            else foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value;
            if (policy?.SafeEnvironment != null)
            {
                foreach (var name in policy.SafeEnvironment.Clear) env[name] = "";
                foreach (var pair in policy.SafeEnvironment.Set) env[pair.Key] = pair.Value;
            }
            if (Same(suiteId, "messaging")) env["ANTIPHON_BROKER_TESTS"] = "1";
            return env;
        }

        /// <summary>The text contains one of the (non-blank) sentinels.</summary>
        public static bool CredentialLeak(string text, string[] sentinels) =>
            (sentinels ?? new string[0]).Any(s => !Blank(s) && (text ?? "").Contains(s, StringComparison.Ordinal));

        // ---- profiles ----------------------------------------------------------------------------------

        /// <summary>
        /// The profile (nightly when blank). A schema-1 policy has only nightly (its defaultSuites); an unknown or
        /// undefined profile, or one naming no required suites, throws.
        /// </summary>
        public static ResolvedProfile Profile(ExecutionPolicy policy, string profile)
        {
            var name = (profile ?? "").Trim().ToLowerInvariant();
            if (Blank(name)) name = "nightly";
            if (Array.IndexOf(Profiles, name) < 0) throw Refuse("unknown profile " + name);
            if (policy.Profiles == null)
            {
                if (name == "nightly") return new ResolvedProfile { Name = "nightly", RequiredSuites = RequiredSuiteUniverse(policy), Source = "defaultSuites" };
                throw Refuse("profile " + name + " requires policy schema 2");
            }
            if (!policy.Profiles.TryGetValue(name, out var row)) throw Refuse("profile " + name + " is not defined");
            var required = row.RequiredSuites.Where(s => !Blank(s)).ToArray();
            if (required.Length == 0) throw Refuse("profile " + name + " names no required suites");
            return new ResolvedProfile { Name = name, RequiredSuites = required, Source = "profiles", Exclusions = row.Exclusions, Object = row };
        }

        /// <summary>The profile's required suites; each must be a policy suite.</summary>
        public static string[] ProfileRequiredSuites(ExecutionPolicy policy, string profile)
        {
            var p = Profile(policy, profile);
            foreach (var id in p.RequiredSuites) if (!policy.Suites.ContainsKey(id)) throw Refuse("profile " + p.Name + " requires unknown suite " + id);
            return p.RequiredSuites;
        }

        /// <summary>D-6: a suite the profile requires runs in it; otherwise a known suite runs unless it is manual.</summary>
        public static bool ProfileSuiteRunnable(ExecutionPolicy policy, string profile, string suiteId)
        {
            if (ProfileRequiredSuites(policy, profile).Contains(suiteId ?? "", StringComparer.OrdinalIgnoreCase)) return true;
            return policy.Suites.TryGetValue(suiteId ?? "", out var suite) && !Same(suite.Mode, "manual");
        }

        /// <summary>
        /// The profile's declared exclusions for one suite (every suite when blank) and platform. Every row is checked
        /// first: one without reason and owner, or with a platform other than windows or linux, throws even when
        /// filtered out.
        /// </summary>
        public static ExclusionRow[] ProfileExclusions(ExecutionPolicy policy, string profile, string suiteId, string platform)
        {
            if (platform != "windows" && platform != "linux") throw Refuse("unsupported exclusion platform " + platform);
            var p = Profile(policy, profile);
            var rows = new List<ExclusionRow>();
            foreach (var row in p.Exclusions)
            {
                if (Blank(row.Reason) || Blank(row.Owner)) throw Refuse("profile " + p.Name + " exclusion for " + row.Class + " needs reason and owner");
                if (row.PlatformInvalid) throw Refuse("profile " + p.Name + " exclusion for " + row.Class + " has invalid platform");
                if (!Blank(suiteId) && !Same(row.Suite, suiteId)) continue;
                if (row.Platform != null && row.Platform != platform) continue;
                rows.Add(new ExclusionRow { Suite = row.Suite, Class = row.Class, Methods = row.Methods, Reason = row.Reason, Owner = row.Owner });
            }
            return rows.ToArray();
        }

        /// <summary>The class-level index of exclusion rows (discovery nodes are PowerShell objects).</summary>
        public static ProfileExclusionIndex ExclusionIndex(object exclusions) => new ProfileExclusionIndex(exclusions);

        /// <summary>
        /// D-5: a declared class row (all methods, or the node's method) names the node's simple class name, Type or
        /// ClassName, ignoring case. OptIn alone is not exclusion.
        /// </summary>
        public static bool NodeExcluded(object node, object exclusions) =>
            node != null && new ProfileExclusionIndex(exclusions).Excluded(node);
    }
}
