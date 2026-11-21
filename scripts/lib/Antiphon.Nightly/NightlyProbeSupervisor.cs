// CARD-1375 nightly probe capture (moved from scripts/lib/nightly-probe-supervisor.ps1 and
// scripts/capture-nightly-probe.ps1). Local Linux diagnostic probes only: not a session or process custodian.
//
// Run starts one probe with a literal argv and both streams redirected and drained at once. It keeps the PID plus
// /proc start identity of the root and of every descendant it observes through /proc on each loop pass. Completion
// needs the root's exit and both EOFs. Execution expires at 30,000 ms; an injected elapsed provider replaces only
// that clock. Cleanup has one real 5,000 ms allowance for every kill call, join and both drains: kill the live
// root's tree, kill each still-live identity-matched descendant, then join every retained process and drain.
//
// Capture runs the two C1375Producer probes of a built c1375-probe directory under Run, writes each receipt and
// checks the native TRX and diagnostic state independently of the parser. Every refusal is a NightlyProbeRefusal.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Antiphon.Nightly
{
    /// <summary>One line a capture refuses with.</summary>
    public sealed class NightlyProbeRefusal : Exception
    {
        public NightlyProbeRefusal(string message) : base(message) { }
    }

    /// <summary>A process by its /proc identity; the supervisor adds a handle when it retains one.</summary>
    public sealed class NightlyProbeProcess
    {
        public NightlyProbeProcess(int pid, int parentPid, string start)
        {
            Pid = pid;
            ParentPid = parentPid;
            Start = start;
        }

        public int Pid { get; }
        public int ParentPid { get; }

        /// <summary>The starttime field of /proc/&lt;pid&gt;/stat: the kernel identity every observer shares.</summary>
        public string Start { get; }

        public Process Process { get; internal set; }
        public long StartTimeUtcTicks { get; internal set; }
    }

    /// <summary>A retained descendant and whether it was joined within the cleanup allowance.</summary>
    public sealed class NightlyProbeJoin
    {
        public int Pid { get; set; }
        public string Start { get; set; }
        public long StartTimeUtcTicks { get; set; }
        public bool Joined { get; set; }
    }

    public sealed class NightlyProbeReceipt
    {
        public string FilePath { get; set; }
        public string[] Arguments { get; set; }
        public int? Pid { get; set; }
        public string Start { get; set; }
        public long? StartTimeUtcTicks { get; set; }
        public bool Expired { get; set; }
        public long ExecutionElapsedMs { get; set; }
        public long CleanupElapsedMs { get; set; }
        public bool RootJoined { get; set; }
        public NightlyProbeJoin[] Descendants { get; set; }
        public bool StdoutDrained { get; set; }
        public bool StderrDrained { get; set; }
        public bool CleanupSuccess { get; set; }
        public int? ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public string[] Errors { get; set; }

        /// <summary>The receipt's fields in the order the scripts wrote them.</summary>
        public JsonObject ToJson()
        {
            var descendants = new JsonArray();
            foreach (var row in Descendants ?? new NightlyProbeJoin[0])
                descendants.Add(new JsonObject { ["pid"] = row.Pid, ["start"] = row.Start, ["startTimeUtcTicks"] = row.StartTimeUtcTicks, ["joined"] = row.Joined });
            return new JsonObject
            {
                ["filePath"] = FilePath,
                ["arguments"] = Strings(Arguments),
                ["pid"] = Pid,
                ["start"] = Start,
                ["startTimeUtcTicks"] = StartTimeUtcTicks,
                ["expired"] = Expired,
                ["executionElapsedMs"] = ExecutionElapsedMs,
                ["cleanupElapsedMs"] = CleanupElapsedMs,
                ["rootJoined"] = RootJoined,
                ["descendants"] = descendants,
                ["stdoutDrained"] = StdoutDrained,
                ["stderrDrained"] = StderrDrained,
                ["cleanupSuccess"] = CleanupSuccess,
                ["exitCode"] = ExitCode,
                ["stdout"] = Stdout,
                ["stderr"] = Stderr,
                ["errors"] = Strings(Errors),
            };
        }

        internal static JsonArray Strings(IEnumerable<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values ?? new string[0]) array.Add(value);
            return array;
        }
    }

    public static class NightlyProbeSupervisor
    {
        public const long ExecutionDeadlineMs = 30000;
        public const int CleanupAllowanceMs = 5000;
        public const string LinuxOnly = "Nightly probe supervision requires Linux /proc.";

        /// <summary>The probe build's pinned packages, as project.assets.json names them.</summary>
        public static readonly string[] PinnedPackages = { "TUnit/1.44.0", "Microsoft.Testing.Platform/2.2.2" };

        public static readonly string[] ProbeMethods = { "TimeoutProbe", "CancelledProbe" };

        static readonly string[] VersionedAssemblies = { "TUnit.Core", "TUnit.Engine", "Microsoft.Testing.Platform" };

        // The entry's [ValidatePattern('^[0-9a-f]{40}$')]: PowerShell matches it ignoring case.
        const string BuildShaPattern = "^[0-9a-f]{40}$";
        static readonly Regex BuildSha = new Regex(BuildShaPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly JsonSerializerOptions ReceiptJson = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// The /proc identity of a process, or null when its stat file cannot be read (it is gone). Fields after the
        /// command's closing parenthesis: [0] state, [1] parent PID, [19] starttime.
        /// </summary>
        public static NightlyProbeProcess ReadIdentity(int pid)
        {
            string[] fields;
            try { fields = StatFields(pid); }
            catch (IOException) { return null; }
            return new NightlyProbeProcess(pid, int.Parse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture), fields[19]);
        }

        /// <summary>/proc/&lt;pid&gt;/stat split after the command's closing parenthesis.</summary>
        public static string[] StatFields(int pid)
        {
            var stat = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            return stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
        }

        /// <summary>Same start identity in /proc and the retained handle has not reported exit.</summary>
        public static bool IsAlive(NightlyProbeProcess identity)
        {
            var current = ReadIdentity(identity.Pid);
            return current != null && string.Equals(current.Start, identity.Start, StringComparison.Ordinal) && !identity.Process.HasExited;
        }

        /// <summary>
        /// Adds every process whose parent is the live root or a live retained descendant (parent start identity
        /// matching), with a handle whose start identity was confirmed after it was opened. Repeats until none is added.
        /// </summary>
        static void UpdateDescendants(NightlyProbeProcess root, List<NightlyProbeProcess> descendants)
        {
            var known = new Dictionary<int, string>();
            foreach (var identity in new[] { root }.Concat(descendants))
            {
                if (IsAlive(identity)) known[identity.Pid] = identity.Start;
            }
            var inventory = new List<NightlyProbeProcess>();
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var number)) continue;
                var identity = ReadIdentity(number);
                if (identity != null) inventory.Add(identity);
            }
            bool added;
            do
            {
                added = false;
                foreach (var identity in inventory)
                {
                    if (known.ContainsKey(identity.Pid) || !known.ContainsKey(identity.ParentPid)) continue;
                    var parent = ReadIdentity(identity.ParentPid);
                    if (parent == null || !string.Equals(parent.Start, known[identity.ParentPid], StringComparison.Ordinal)) continue;
                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(identity.Pid);
                        var startTicks = process.StartTime.ToUniversalTime().Ticks;
                        var current = ReadIdentity(identity.Pid);
                        if (current == null || !string.Equals(current.Start, identity.Start, StringComparison.Ordinal))
                        {
                            process.Dispose();
                            continue;
                        }
                        identity.Process = process;
                        identity.StartTimeUtcTicks = startTicks;
                        descendants.Add(identity);
                        known[identity.Pid] = identity.Start;
                        added = true;
                    }
                    catch (Exception)
                    {
                        process?.Dispose();
                    }
                }
            } while (added);
        }

        static int Remaining(Stopwatch cleanup) => (int)Math.Max(0, CleanupAllowanceMs - cleanup.ElapsedMilliseconds);

        /// <summary>
        /// Supervises one probe to completion or expiry and returns its receipt; failures are in Errors, never thrown.
        /// elapsedMilliseconds (root, observed descendants) replaces the execution clock only.
        /// </summary>
        public static NightlyProbeReceipt Run(string filePath, IReadOnlyList<string> arguments, string workingDirectory,
            Func<NightlyProbeProcess, NightlyProbeProcess[], long> elapsedMilliseconds = null)
        {
            if (!OperatingSystem.IsLinux()) throw new NightlyProbeRefusal(LinuxOnly);
            var argv = (arguments ?? new string[0]).ToArray();
            var info = new ProcessStartInfo(filePath)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in argv) info.ArgumentList.Add(argument);
            var process = new Process { StartInfo = info };
            var descendants = new List<NightlyProbeProcess>();
            var errors = new List<string>();
            var joined = new List<NightlyProbeJoin>();
            NightlyProbeProcess root = null;
            Task<string> stdout = null;
            Task<string> stderr = null;
            var expired = false;
            var rootJoined = false;
            var started = false;
            var execution = new Stopwatch();
            var cleanup = new Stopwatch();
            try
            {
                if (!process.Start()) throw new InvalidOperationException("Probe process did not start.");
                started = true;
                execution.Start();
                stdout = process.StandardOutput.ReadToEndAsync();
                stderr = process.StandardError.ReadToEndAsync();
                root = ReadIdentity(process.Id) ?? throw new InvalidOperationException("Probe root identity was unavailable.");
                root.Process = process;
                root.StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                while (true)
                {
                    UpdateDescendants(root, descendants);
                    if (process.HasExited && stdout.IsCompleted && stderr.IsCompleted) break;
                    var elapsed = elapsedMilliseconds != null ? elapsedMilliseconds(root, descendants.ToArray()) : execution.ElapsedMilliseconds;
                    if (elapsed >= ExecutionDeadlineMs)
                    {
                        expired = true;
                        break;
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
            }
            finally
            {
                // One real allowance includes all kill calls, joins and both EOF drains.
                cleanup.Start();
                if (started)
                {
                    try { if (!process.HasExited) process.Kill(true); }
                    catch (Exception e) { errors.Add("root kill: " + e.Message); }
                    foreach (var identity in descendants)
                    {
                        try { if (IsAlive(identity)) identity.Process.Kill(); }
                        catch (Exception e) { errors.Add("descendant kill: " + e.Message); }
                    }
                    try { rootJoined = process.WaitForExit(Remaining(cleanup)); }
                    catch (Exception e) { errors.Add("root join: " + e.Message); }
                    foreach (var identity in descendants)
                    {
                        var didJoin = false;
                        try { didJoin = identity.Process.WaitForExit(Remaining(cleanup)); }
                        catch (Exception e) { errors.Add("descendant join: " + e.Message); }
                        joined.Add(new NightlyProbeJoin { Pid = identity.Pid, Start = identity.Start, StartTimeUtcTicks = identity.StartTimeUtcTicks, Joined = didJoin });
                    }
                    foreach (var stream in new[] { stdout, stderr })
                    {
                        if (stream == null) continue;
                        try { stream.Wait(Remaining(cleanup)); }
                        catch (Exception e) { errors.Add("stream drain: " + e.Message); }
                    }
                }
                cleanup.Stop();
            }
            var stdoutDrained = stdout != null && stdout.IsCompletedSuccessfully;
            var stderrDrained = stderr != null && stderr.IsCompletedSuccessfully;
            var receipt = new NightlyProbeReceipt
            {
                FilePath = filePath,
                Arguments = argv,
                Pid = started ? process.Id : null,
                Start = root?.Start,
                StartTimeUtcTicks = root != null ? root.StartTimeUtcTicks : null,
                Expired = expired,
                // As before, the execution clock is read here: the figure includes the cleanup.
                ExecutionElapsedMs = execution.ElapsedMilliseconds,
                CleanupElapsedMs = cleanup.ElapsedMilliseconds,
                RootJoined = rootJoined,
                Descendants = joined.ToArray(),
                StdoutDrained = stdoutDrained,
                StderrDrained = stderrDrained,
                CleanupSuccess = rootJoined && joined.All(row => row.Joined) && stdoutDrained && stderrDrained
                    && cleanup.ElapsedMilliseconds <= CleanupAllowanceMs && errors.Count == 0,
                ExitCode = rootJoined ? process.ExitCode : null,
                Stdout = stdoutDrained ? stdout.Result : null,
                Stderr = stderrDrained ? stderr.Result : null,
                Errors = errors.ToArray(),
            };
            foreach (var identity in descendants) identity.Process.Dispose();
            process.Dispose();
            return receipt;
        }

        public static bool IsBuildSourceSha(string value) => value != null && BuildSha.IsMatch(value);

        /// <summary>
        /// Captures TimeoutProbe and CancelledProbe of the built probe into a fresh results directory: per method a
        /// receipt.json (kept on refusal), the native TRX and diagnostics, and one CAPTURE line on output. Inputs
        /// are checked before anything is written.
        /// </summary>
        public static void Capture(string probeDirectory, string resultsDirectory, string probeBuildSourceSha, TextWriter output)
        {
            if (!IsBuildSourceSha(probeBuildSourceSha))
                throw new NightlyProbeRefusal("--probe-build-source-sha \"" + probeBuildSourceSha + "\" does not match the \"" + BuildShaPattern + "\" pattern.");
            var probe = Guard(() => Path.GetFullPath(probeDirectory));
            if (!Directory.Exists(probe) && !File.Exists(probe)) throw NotFound(probe);
            var results = Guard(() => Path.GetFullPath(resultsDirectory));
            if (Directory.Exists(results) || File.Exists(results)) throw new NightlyProbeRefusal("Capture requires a fresh results directory.");
            var bin = Path.Combine(probe, "bin-c1375-probe");
            if (!Directory.Exists(bin)) throw NotFound(bin);
            var dlls = Guard(() => Directory.EnumerateFiles(bin, "Probe.dll", Visible(recurse: true)).ToArray());
            if (dlls.Length != 1) throw new NightlyProbeRefusal("Expected exactly one built Probe.dll in bin-c1375-probe.");
            var dll = dlls[0];
            RequirePinnedPackages(Path.Combine(probe, "obj", "project.assets.json"));
            var versions = new JsonObject();
            foreach (var assembly in VersionedAssemblies)
                versions[assembly] = Guard(() => AssemblyName.GetAssemblyName(Path.Combine(Path.GetDirectoryName(dll), assembly + ".dll")).Version.ToString());
            if (!OperatingSystem.IsLinux()) throw new NightlyProbeRefusal(LinuxOnly);
            var head = ReleaseAuthority.Git(Environment.CurrentDirectory, "rev-parse", "HEAD");
            if (head.ExitCode != 0) throw new NightlyProbeRefusal("git rev-parse HEAD failed: " + OneLine(head.Error));
            var source = head.Text.Trim();
            var dllSha256 = Guard(() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
            Guard(() => Directory.CreateDirectory(results));
            foreach (var method in ProbeMethods)
            {
                var directory = Path.Combine(results, method);
                Guard(() => Directory.CreateDirectory(directory));
                var argv = new[]
                {
                    "exec", dll, "--treenode-filter", "/*/*/C1375Producer*/" + method + "*",
                    "--detailed-stacktrace", "--report-trx", "--results-directory", directory,
                    "--diagnostic", "--diagnostic-output-directory", Path.Combine(directory, "diag"),
                };
                var receipt = Run("dotnet", argv, probe);
                var json = receipt.ToJson();
                json["captureSourceSha"] = source;
                json["buildSourceSha"] = probeBuildSourceSha;
                json["assemblyVersions"] = versions.DeepClone();
                json["dllSha256"] = dllSha256;
                json["packages"] = NightlyProbeReceipt.Strings(PinnedPackages);
                Guard(() => File.WriteAllText(Path.Combine(directory, "receipt.json"), json.ToJsonString(ReceiptJson) + Environment.NewLine, new UTF8Encoding(false)));
                if (receipt.Expired || !receipt.CleanupSuccess || receipt.ExitCode == 0)
                    throw new NightlyProbeRefusal("Probe " + method + " refused: expiry, cleanup or exit; see retained receipt.");
                var trxs = Guard(() => Directory.EnumerateFiles(directory, "*.trx", Visible(recurse: false)).ToArray());
                if (trxs.Length != 1) throw new NightlyProbeRefusal("Probe " + method + " did not write exactly one TRX.");
                if (!NativeResultMatches(Guard(() => XDocument.Load(trxs[0])))) throw new NightlyProbeRefusal("Probe " + method + " native result mismatch.");
                var diag = Path.Combine(directory, "diag");
                if (!Directory.Exists(diag)) throw NotFound(diag);
                var text = Guard(() => string.Join("\n", Directory.EnumerateFiles(diag, "*", Visible(recurse: true)).Select(File.ReadAllText)));
                var state = method == "TimeoutProbe" ? "Timeout" : "Cancelled";
                if (!NativeStateMatches(text, state)) throw new NightlyProbeRefusal("Probe " + method + " native state/version mismatch.");
                output.WriteLine("CAPTURE " + method + " state=" + state + " executed=1 passed=0 failed=1 skipped=0 exit="
                    + receipt.ExitCode?.ToString(CultureInfo.InvariantCulture) + " expired=false cleanup=true");
            }
        }

        /// <summary>One Failed UnitTestResult; summary total 1, failed+timeout+aborted 1, passed 0, notExecuted 0.</summary>
        public static bool NativeResultMatches(XDocument trx)
        {
            var run = trx.Root;
            if (run == null || run.Name.LocalName != "TestRun") return false;
            var rows = Children(run, "Results").SelectMany(results => Children(results, "UnitTestResult")).ToArray();
            var counters = Children(run, "ResultSummary").SelectMany(summary => Children(summary, "Counters")).ToArray();
            if (rows.Length != 1 || counters.Length != 1) return false;
            if (!string.Equals((string)rows[0].Attribute("outcome"), "Failed", StringComparison.Ordinal)) return false;
            var summary = counters[0];
            if (!string.Equals((string)summary.Attribute("total"), "1", StringComparison.Ordinal)) return false;
            var nonPassing = 0;
            foreach (var name in new[] { "failed", "timeout", "aborted" })
            {
                var value = (string)summary.Attribute(name);
                if (value == null) continue;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return false;
                nonPassing += count;
            }
            return nonPassing == 1
                && string.Equals((string)summary.Attribute("passed"), "0", StringComparison.Ordinal)
                && string.Equals((string)summary.Attribute("notExecuted"), "0", StringComparison.Ordinal);
        }

        /// <summary>Exactly one native &lt;state&gt;TestNodeStateProperty record and both pinned version headers.</summary>
        public static bool NativeStateMatches(string diagnostics, string state) =>
            Regex.Matches(diagnostics, state + @"TestNodeStateProperty \{", RegexOptions.CultureInvariant).Count == 1
            && Regex.IsMatch(diagnostics, @"Version: '1\.44\.0\.0'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && Regex.IsMatch(diagnostics, @"Version: 2\.2\.2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static void RequirePinnedPackages(string assetsPath)
        {
            using var assets = Guard(() => JsonDocument.Parse(File.ReadAllText(assetsPath)));
            var libraries = assets.RootElement.ValueKind == JsonValueKind.Object && assets.RootElement.TryGetProperty("libraries", out var found)
                && found.ValueKind == JsonValueKind.Object ? found : default;
            foreach (var package in PinnedPackages)
            {
                // ConvertFrom-Json property lookup ignores case.
                var present = libraries.ValueKind == JsonValueKind.Object
                    && libraries.EnumerateObject().Any(property => string.Equals(property.Name, package, StringComparison.OrdinalIgnoreCase));
                if (!present) throw new NightlyProbeRefusal("Missing pinned package " + package);
            }
        }

        static IEnumerable<XElement> Children(XElement parent, string localName) => parent.Elements().Where(child => child.Name.LocalName == localName);

        // Get-ChildItem without -Force: hidden entries are neither listed nor entered.
        static EnumerationOptions Visible(bool recurse) => new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchType = MatchType.Simple,
            IgnoreInaccessible = false,
        };

        static NightlyProbeRefusal NotFound(string path) => new NightlyProbeRefusal("Cannot find path '" + path + "' because it does not exist.");

        static string OneLine(string text) => string.Join(" ", (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        // An unreadable, unwritable or malformed path refuses with its own message.
        static T Guard<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is XmlException
                || e is BadImageFormatException || e is ArgumentException || e is NotSupportedException)
            {
                throw new NightlyProbeRefusal(OneLine(e.Message));
            }
        }

        static void Guard(Action write) => Guard(() => { write(); return true; });
    }
}
