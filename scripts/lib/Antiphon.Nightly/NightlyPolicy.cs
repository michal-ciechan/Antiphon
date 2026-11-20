// The test-execution policy (tests/test-execution-policy.json), moved from nightly-policy.ps1: load and hash, the
// project-universe census, suite selection, the safe child environment and the profiles (required suites and
// declared exclusions). The nightly runner, the release gate and the release authority all read it from here; the
// release authority pins these sources by digest (ReleaseAuthoritySources) as it pinned the PowerShell file.
// Comparisons follow the PowerShell they replace: -eq and -contains ignore case, -ceq and -cnotcontains do not.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class NightlyPolicy
    {
        public const int SchemaVersion = 4;
        private static readonly int[] SupportedSchemaVersions = { 1, 2, 3, 4 };
        private static readonly string[] Profiles = { "nightly", "rc" };
        private static readonly string[] FallbackSuites = { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts" };
        private static readonly Regex TestProjectMarker = new Regex(@"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex ProjectPath = new Regex(@"^[^\\]+\.csproj$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);
        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        private static bool Blank(string s) => string.IsNullOrWhiteSpace(s);
        private static string Str(object o, string name) => PsValue.Str(PsValue.Prop(o, name));

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        // Sort-Object over strings: the current culture, ignoring case.
        private static string[] Sorted(IEnumerable<string> values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
            return list.ToArray();
        }

        private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
        {
            foreach (var v in values) if (Same(v, value)) return true;
            return false;
        }

        // ---- load and hash ---------------------------------------------------------------------------------

        /// <summary>Get-NightlyPolicyPath: tests/test-execution-policy.json under the repository root.</summary>
        public static string PolicyPath(string repoRoot) => Path.Combine(repoRoot ?? "", "tests", "test-execution-policy.json");

        /// <summary>ConvertTo-NightlyPolicyMap: a dictionary's keys or an object's note properties, first wins.</summary>
        public static OrderedDictionary Map(object o)
        {
            var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            if (o == null) return map;
            if (PsOps.Base(o) is IDictionary d)
            {
                foreach (DictionaryEntry e in d)
                {
                    var name = PsValue.Str(e.Key);
                    if (!Blank(name) && !map.Contains(name)) map.Add(name, e.Value);
                }
                return map;
            }
            foreach (var p in PSObject.AsPSObject(o).Properties)
            {
                if (!(p is PSNoteProperty) || Blank(p.Name) || map.Contains(p.Name)) continue;
                map.Add(p.Name, p.Value);
            }
            return map;
        }

        /// <summary>Get-NightlyPolicyHash: SHA-256 of the canonical JSON of every top-level key but policyHash.</summary>
        public static string Hash(object policy)
        {
            var copy = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in Map(policy))
            {
                var name = PsValue.Str(e.Key);
                if (Same(name, "policyHash") || Blank(name) || copy.Contains(name)) continue;
                copy.Add(name, e.Value);
            }
            return TestPhaseRecords.Sha256Text(NightlyDocuments.CanonicalJson(copy));
        }

        /// <summary>
        /// Read-NightlyExecutionPolicy: { Path, Object, Hash, SchemaVersion } of the policy file (policyPath, or the
        /// repository's when blank). Refuses a missing or unreadable file, an unsupported schema and a stale hash.
        /// </summary>
        public static PSObject Read(string repoRoot, string policyPath)
        {
            if (Blank(policyPath)) policyPath = PolicyPath(repoRoot);
            if (!File.Exists(policyPath)) throw Refuse("policy missing: " + policyPath);
            var obj = NightlyJson.ReadFile(policyPath, true) ?? throw Refuse("policy unreadable: " + policyPath);
            var version = 0;
            var stated = PsValue.Prop(obj, "schemaVersion");
            if (LanguagePrimitives.IsTrue(stated)) version = (int)LanguagePrimitives.ConvertTo(stated, typeof(int), CultureInfo.InvariantCulture);
            if (Array.IndexOf(SupportedSchemaVersions, version) < 0) throw Refuse("unsupported policy schema " + version.ToString(CultureInfo.InvariantCulture));
            var computed = Hash(obj);
            var statedHash = Str(obj, "policyHash");
            if (Blank(statedHash) || !string.Equals(statedHash, computed, StringComparison.OrdinalIgnoreCase))
                throw Refuse("stale policy hash stated=" + statedHash + " computed=" + computed);
            return Note("Path", policyPath, "Object", obj, "Hash", computed, "SchemaVersion", version);
        }

        // ---- the project universe ----------------------------------------------------------------------

        // Directories git ignores at the root of a checkout (run output, packages, build output), as full paths ending
        // in a separator: a project in one can never be committed. Not a checkout, or git unavailable: none. A
        // repository named by the environment (a hook's GIT_DIR) does not answer for this root.
        private static string[] GitIgnoredDirectories(string root)
        {
            if (!Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git"))) return new string[0];
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
                var full = Path.GetFullPath(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) full += Path.DirectorySeparatorChar;
                dirs.Add(full);
            }
            return dirs.ToArray();
        }

        /// <summary>
        /// Get-NightlyIsTestProjectPaths: repository-relative paths (forward slashes, sorted) of every committed
        /// *.csproj marked IsTestProject, outside bin, obj, bin-* and git-ignored directories.
        /// </summary>
        public static string[] TestProjectPaths(string repoRoot)
        {
            var root = Path.GetFullPath(repoRoot.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            if (root.Length > Path.GetPathRoot(root).Length) root = root.TrimEnd(Path.DirectorySeparatorChar);
            var hits = new List<string>();
            foreach (var full in NightlyRepository.CandidateProjectFiles(root, GitIgnoredDirectories(root)))
            {
                var skip = false;
                foreach (var part in full.Substring(root.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (Same(part, "bin") || Same(part, "obj") || part.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
                }
                if (skip || !TestProjectMarker.IsMatch(File.ReadAllText(full))) continue;
                hits.Add(full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/'));
            }
            return Sorted(hits);
        }

        /// <summary>Get-NightlyPolicyProjectUniverse: the projects the policy accounts for.</summary>
        public static string[] ProjectUniverse(object policy)
        {
            if (SchemaOf(policy) >= 3)
            {
                ProjectDispositions(policy);
                var rows = new List<string>();
                foreach (var row in PsValue.AsArray(PsValue.Prop(policy, "projectDispositions"))) if (row != null) rows.Add(Str(row, "project"));
                return rows.ToArray();
            }
            var ids = new List<string>();
            foreach (var suite in SuiteValues(policy))
            {
                var project = PsValue.Prop(suite, "project");
                if (LanguagePrimitives.IsTrue(project)) ids.Add(PsValue.Str(project).Replace('\\', '/'));
            }
            var projects = PsValue.Prop(policy, "projects");
            if (LanguagePrimitives.IsTrue(projects)) foreach (var p in PsValue.AsArray(projects)) ids.Add(PsValue.Str(p).Replace('\\', '/'));
            var unique = new List<string>();
            foreach (var id in ids) if (!unique.Contains(id)) unique.Add(id);
            return Sorted(unique);
        }

        private static int SchemaOf(object policy)
        {
            var value = PsValue.Prop(policy, "schemaVersion");
            return value == null ? 0 : (int)LanguagePrimitives.ConvertTo(value, typeof(int), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Test-NightlyProjectDispositions: one row per project; a primary row for each suite project and a child row
        /// naming the suite projects that consume it. Throws on the first broken row.
        /// </summary>
        public static void ProjectDispositions(object policy)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<object>();
            foreach (var row in PsValue.AsArray(PsValue.Prop(policy, "projectDispositions"))) if (row != null) rows.Add(row);
            var suiteProjects = new List<string>();
            foreach (var suite in SuiteValues(policy))
            {
                var project = PsValue.Prop(suite, "project");
                if (LanguagePrimitives.IsTrue(project)) suiteProjects.Add(PsValue.Str(project));
            }
            var primary = 0;
            foreach (var row in rows) if (Same(Str(row, "role"), "primary")) primary++;
            if (primary != suiteProjects.Count) throw Refuse("project-dispositions primary census mismatch");
            foreach (var row in rows)
            {
                var path = Str(row, "project");
                if (!ProjectPath.IsMatch(path) || path.StartsWith("/", StringComparison.Ordinal) || Array.IndexOf(path.Split('/'), "..") >= 0 || !seen.Add(path))
                    throw Refuse("project-dispositions invalid or duplicate project");
                if (Blank(Str(row, "reason"))) throw Refuse("project-dispositions missing reason");
                var role = Str(row, "role");
                if (Same(role, "primary"))
                {
                    if (!suiteProjects.Contains(path)) throw Refuse("project-dispositions undefined primary");
                }
                else if (Same(role, "child"))
                {
                    var consumers = PsValue.Prop(row, "consumers");
                    var count = 0;
                    if (consumers != null)
                    {
                        foreach (var consumer in PsValue.AsArray(consumers))
                        {
                            count++;
                            if (!suiteProjects.Contains(PsValue.Str(consumer))) throw Refuse("project-dispositions unknown consumer");
                        }
                    }
                    if (count == 0) throw Refuse("project-dispositions child without consumer");
                }
                else throw Refuse("project-dispositions unknown role");
            }
        }

        /// <summary>
        /// Test-NightlyPolicyProjectUniverse: { Ok, Missing, Extra, Disk, Policy } comparing the test projects on disk
        /// with the policy's; when they differ also Unmatched and Message.
        /// </summary>
        public static PSObject TestProjectUniverse(string repoRoot, object policy)
        {
            var disk = TestProjectPaths(repoRoot);
            var declared = ProjectUniverse(policy);
            var missing = new List<string>();
            foreach (var d in disk) if (!ContainsIgnoreCase(declared, d)) missing.Add(d);
            var extra = new List<string>();
            foreach (var p in declared) if (!ContainsIgnoreCase(disk, p)) extra.Add(p);
            if (missing.Count == 0 && extra.Count == 0)
                return Note("Ok", true, "Missing", new object[0], "Extra", new object[0], "Disk", disk, "Policy", declared);
            var unmatched = new List<string>(missing);
            unmatched.AddRange(extra);
            return Note("Ok", false, "Missing", missing.ToArray(), "Extra", extra.ToArray(), "Unmatched", unmatched.ToArray(),
                "Disk", disk, "Policy", declared, "Message", "unmatched project " + string.Join(", ", unmatched));
        }

        // ---- suites --------------------------------------------------------------------------------------

        private static IEnumerable<object> SuiteValues(object policy)
        {
            var suites = PsValue.Prop(policy, "suites");
            if (suites == null) yield break;
            foreach (DictionaryEntry e in Map(suites)) yield return e.Value;
        }

        /// <summary>Get-NightlySuiteMap: suite id to suite row (keys ignore case).</summary>
        public static Hashtable SuiteMap(object policy)
        {
            var map = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var suites = PsValue.Prop(policy, "suites");
            if (suites != null) foreach (DictionaryEntry e in Map(suites)) map[PsValue.Str(e.Key)] = e.Value;
            return map;
        }

        // Get-NightlyRequiredSuiteUniverse: defaultSuites (or the fallback list) less the manual suites.
        private static string[] RequiredSuiteUniverse(object policy)
        {
            var selected = new List<string>();
            var defaults = PsValue.Prop(policy, "defaultSuites");
            if (LanguagePrimitives.IsTrue(defaults)) foreach (var id in PsValue.AsArray(defaults)) if (!Blank(PsValue.Str(id))) selected.Add(PsValue.Str(id));
            if (selected.Count == 0) selected.AddRange(FallbackSuites);
            var map = policy != null ? SuiteMap(policy) : new Hashtable();
            var required = new List<string>();
            foreach (var id in selected)
            {
                var suite = map.ContainsKey(id) ? map[id] : null;
                if (suite != null && Same(Str(suite, "mode"), "manual")) continue;
                required.Add(id);
            }
            return required.ToArray();
        }

        /// <summary>Test-NightlyRequiredSuitesPresent: { Ok, Missing } of the required suites the selection lacks.</summary>
        public static PSObject RequiredSuitesPresent(string[] selected, string[] required)
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in selected ?? new string[0]) if (!Blank(s)) have.Add(s);
            var missing = new List<string>();
            foreach (var r in required ?? new string[0]) if (!Blank(r) && !have.Contains(r)) missing.Add(r);
            return Note("Ok", missing.Count == 0, "Missing", missing.ToArray());
        }

        /// <summary>
        /// Resolve-NightlySelectedSuites: the comma-separated suite ids named (lower-cased, in first-seen order), or the
        /// required universe when none is named. An unknown id or an empty selection throws.
        /// </summary>
        public static string[] ResolveSelectedSuites(object policy, string[] suites)
        {
            var valid = SuiteMap(policy);
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
                    if (!valid.ContainsKey(n)) throw Refuse("unknown suite " + n);
                    if (!selected.Contains(n)) selected.Add(n);
                }
            }
            if (selected.Count == 0) throw Refuse("empty suite selection");
            return selected.ToArray();
        }

        /// <summary>Get-NightlyScriptCensus: the policy's scriptCensus rows.</summary>
        public static object[] ScriptCensus(object policy)
        {
            var census = PsValue.Prop(policy, "scriptCensus");
            return LanguagePrimitives.IsTrue(census) ? PsValue.AsArray(census) : new object[0];
        }

        /// <summary>
        /// Get-NightlySafeChildEnvironment: the base environment (the process's when null) with the policy's
        /// safeEnvironment.clear names emptied and safeEnvironment.set applied; messaging also gets
        /// ANTIPHON_BROKER_TESTS=1. Names ignore case.
        /// </summary>
        public static Hashtable SafeChildEnvironment(object policy, string suiteId, IDictionary baseEnvironment)
        {
            var env = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (baseEnvironment != null) foreach (DictionaryEntry e in baseEnvironment) env[PsValue.Str(e.Key)] = e.Value;
            else foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) env[PsValue.Str(e.Key)] = PsValue.Str(e.Value);
            var safe = PsValue.Prop(policy, "safeEnvironment");
            if (LanguagePrimitives.IsTrue(safe))
            {
                var clear = PsValue.Prop(safe, "clear");
                if (LanguagePrimitives.IsTrue(clear)) foreach (var name in PsValue.AsArray(clear)) env[PsValue.Str(name)] = "";
                var set = PsValue.Prop(safe, "set");
                if (LanguagePrimitives.IsTrue(set)) foreach (DictionaryEntry e in Map(set)) env[PsValue.Str(e.Key)] = PsValue.Str(e.Value);
            }
            if (Same(suiteId, "messaging")) env["ANTIPHON_BROKER_TESTS"] = "1";
            return env;
        }

        /// <summary>Test-NightlyCredentialLeak: the text contains one of the (non-blank) sentinels.</summary>
        public static bool CredentialLeak(string text, string[] sentinels)
        {
            foreach (var s in sentinels ?? new string[0]) if (!Blank(s) && (text ?? "").Contains(s, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---- profiles ----------------------------------------------------------------------------------

        /// <summary>
        /// Get-NightlyPolicyProfile: { Name, RequiredSuites, Source, Exclusions, Object } of the profile (nightly when
        /// blank). A schema-1 policy has only nightly (its defaultSuites); an unknown or undefined profile, or one
        /// naming no required suites, throws.
        /// </summary>
        public static PSObject Profile(object policy, string profile)
        {
            var name = (profile ?? "").Trim().ToLowerInvariant();
            if (Blank(name)) name = "nightly";
            if (Array.IndexOf(Profiles, name) < 0) throw Refuse("unknown profile " + name);
            object profiles = null;
            if (policy != null && PSObject.AsPSObject(policy).Properties["profiles"] != null) profiles = PsValue.Prop(policy, "profiles");
            if (profiles == null)
            {
                if (name == "nightly") return Note("Name", "nightly", "RequiredSuites", RequiredSuiteUniverse(policy), "Source", "defaultSuites", "Exclusions", new object[0], "Object", null);
                throw Refuse("profile " + name + " requires policy schema 2");
            }
            var row = PsValue.Prop(profiles, name) ?? throw Refuse("profile " + name + " is not defined");
            var required = new List<string>();
            foreach (var id in PsValue.AsArray(PsValue.Prop(row, "requiredSuites"))) if (!Blank(PsValue.Str(id))) required.Add(PsValue.Str(id));
            if (required.Count == 0) throw Refuse("profile " + name + " names no required suites");
            return Note("Name", name, "RequiredSuites", required.ToArray(), "Source", "profiles", "Exclusions", PsValue.AsArray(PsValue.Prop(row, "exclusions")), "Object", row);
        }

        /// <summary>Get-NightlyProfileRequiredSuites: the profile's required suites; each must be a policy suite.</summary>
        public static string[] ProfileRequiredSuites(object policy, string profile)
        {
            var p = Profile(policy, profile);
            var map = policy != null ? SuiteMap(policy) : new Hashtable();
            var name = PsValue.Str(p.Properties["Name"].Value);
            var required = (string[])p.Properties["RequiredSuites"].Value;
            foreach (var id in required) if (!map.ContainsKey(id)) throw Refuse("profile " + name + " requires unknown suite " + id);
            return required;
        }

        /// <summary>
        /// Test-NightlyProfileSuiteRunnable (D-6): a suite the profile requires runs in it; otherwise a known suite runs
        /// unless it is manual.
        /// </summary>
        public static bool ProfileSuiteRunnable(object policy, string profile, string suiteId)
        {
            if (ContainsIgnoreCase(ProfileRequiredSuites(policy, profile), suiteId)) return true;
            var map = SuiteMap(policy);
            if (!map.ContainsKey(suiteId ?? "")) return false;
            return !Same(Str(map[suiteId], "mode"), "manual");
        }

        /// <summary>
        /// Get-NightlyProfileExclusions: the profile's declared exclusions for one suite (every suite when blank) and
        /// platform, as { Suite, Class, Methods, Reason, Owner } rows. Every row is checked first: one without reason
        /// and owner, or with a platform other than windows or linux, throws even when filtered out.
        /// </summary>
        public static object[] ProfileExclusions(object policy, string profile, string suiteId, string platform)
        {
            if (platform != "windows" && platform != "linux") throw Refuse("unsupported exclusion platform " + platform);
            var p = Profile(policy, profile);
            var name = PsValue.Str(p.Properties["Name"].Value);
            var rows = new List<object>();
            foreach (var row in (object[])p.Properties["Exclusions"].Value)
            {
                if (row == null) continue;
                var reason = Str(row, "reason");
                var owner = Str(row, "owner");
                var cls = Str(row, "class");
                if (Blank(reason) || Blank(owner)) throw Refuse("profile " + name + " exclusion for " + cls + " needs reason and owner");
                // The 'platform' entry as Map would key it: the exact name, note properties only, first wins.
                object rowPlatform = null;
                var hasPlatform = false;
                if (PsOps.Base(row) is IDictionary d)
                {
                    foreach (DictionaryEntry e in d) if (PsValue.Str(e.Key) == "platform") { hasPlatform = true; rowPlatform = e.Value; break; }
                }
                else
                {
                    foreach (var prop in PSObject.AsPSObject(row).Properties) if (prop is PSNoteProperty && prop.Name == "platform") { hasPlatform = true; rowPlatform = prop.Value; break; }
                }
                if (hasPlatform && (!(PsOps.Base(rowPlatform) is string text) || (text != "windows" && text != "linux")))
                    throw Refuse("profile " + name + " exclusion for " + cls + " has invalid platform");
                if (!Blank(suiteId) && !Same(Str(row, "suite"), suiteId)) continue;
                if (rowPlatform != null && PsValue.Str(rowPlatform) != platform) continue;
                var methods = new List<object>();
                foreach (var m in PsValue.AsArray(PsValue.Prop(row, "methods"))) if (!Blank(PsValue.Str(m))) methods.Add(PsValue.Str(m));
                rows.Add(Note("Suite", Str(row, "suite"), "Class", cls, "Methods", methods.ToArray(), "Reason", reason, "Owner", owner));
            }
            return rows.ToArray();
        }

        /// <summary>New-NightlyProfileExclusionIndex: the class-level index of exclusion rows.</summary>
        public static ProfileExclusionIndex ExclusionIndex(object exclusions) => new ProfileExclusionIndex(exclusions);

        /// <summary>
        /// Test-NightlyProfileNodeExcluded (D-5): a declared class row (all methods, or the node's method) names the
        /// node's simple class name, Type or ClassName, ignoring case. OptIn alone is not exclusion.
        /// </summary>
        public static bool NodeExcluded(object node, object exclusions) =>
            node != null && new ProfileExclusionIndex(exclusions).Excluded(node);
    }
}
