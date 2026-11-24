// CARD-1885: one measured Linux build of the shared-compiler experiment (unit 34 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced scripts/measure-linux-shared-compiler.ps1 and
// scripts/lib/linux-compiler-evidence.ps1 (its only reader). One invocation, one foreground build, one outer lease: no
// hidden build loop. The CLI runs it, inside one build slot, as
//
//   scripts/build-slot.ps1 -Label <what> -- scripts/antiphon measure-linux-shared-compiler --mode Baseline|Candidate
//       --phase Restore|Prime|Measured|Concurrent --sample 0..5 [--worktree <dir>] [--evidence-root <dir>]
//       [--project Antiphon.sln] [--experiment-id measurement-58c3a29f] [--compiler-cache-root <abs dir>]
//
// Linux only (the platform check comes first). It requires the outer lease's grant (ANTIPHON_BUILD_SLOT_MAXCPUCOUNT, which
// build-slot exports to a non-dotnet command), an invocation inside the bounded inventory (Restore and Prime sample 0,
// Measured 1 to 3, Concurrent 4 or 5 and Candidate only), an existing project, a plain experiment identity, an absolute
// cache root and a clean checkout. It prepares the private compiler toolset (LinuxCompilerToolset.Prepare through the
// toolset seam, except for Restore), runs dotnet restore or a no-incremental Debug build with the grant, samples the
// private compiler processes (those whose argv names -pipename:<pipe>; never a process chosen by name) from /proc once a
// second for at least a minute, and writes samples.json and receipt.json under <evidence-root>/<mode>-<phase>-<sample>-<guid>.
// Exit 0; 1 when refused, the build or an acceptance check failed, the source moved, a thread escaped the chosen CPUs, the
// peak RSS passed 8 GiB or a Candidate full minute used more than min(6, grant) cores. Files and /proc on IFileSystem, git
// and the clock on the host. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The measure-linux-shared-compiler command's options (the script's parameters).</summary>
    public sealed class LinuxCompilerMeasureOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "measure-linux-shared-compiler --mode Baseline|Candidate --phase Restore|Prime|Measured|Concurrent --sample <0-5>"
            + " [--worktree <dir>] [--evidence-root <dir>] [--project Antiphon.sln] [--experiment-id measurement-58c3a29f] [--compiler-cache-root <dir>]";

        internal static readonly string[] Modes = { "Baseline", "Candidate" };
        internal static readonly string[] Phases = { "Restore", "Prime", "Measured", "Concurrent" };

        /// <summary>Baseline or Candidate.</summary>
        public string Mode { get; init; } = "";

        /// <summary>Restore, Prime, Measured or Concurrent.</summary>
        public string Phase { get; init; } = "";

        /// <summary>0 to 5.</summary>
        public int Sample { get; init; }

        /// <summary>The checkout measured; empty: the CLI's checkout.</summary>
        public string Worktree { get; init; } = "";

        /// <summary>Where the run directories go; empty: &lt;worktree&gt;/.antiphon/c1885/performance.</summary>
        public string EvidenceRoot { get; init; } = "";

        /// <summary>The project or solution, relative to the worktree.</summary>
        public string Project { get; init; } = "Antiphon.sln";

        /// <summary>The private experiment identity (letters, digits and '-').</summary>
        public string ExperimentId { get; init; } = "measurement-58c3a29f";

        /// <summary>XDG_CACHE_HOME for the private toolset; empty: the default.</summary>
        public string CompilerCacheRoot { get; init; } = "";

        /// <summary>ANTIPHON_BUILD_SLOT_MAXCPUCOUNT as the CLI read it (the outer lease's grant).</summary>
        public string Grant { get; init; } = "";

        /// <summary>
        /// The flags after the command name; the worktree default and the grant come from the CLI. A missing or unknown
        /// --mode or --phase, or a --sample outside 0 to 5, is a NightlyUsageException. Choices match ignoring case and keep
        /// their listed spelling.
        /// </summary>
        public static LinuxCompilerMeasureOptions Parse(IReadOnlyList<string> arguments, string defaultWorktree, string grant)
        {
            var flags = NightlyArgs.Parse(arguments, new[]
            {
                "--mode", "--phase", "--sample", "--worktree", "--evidence-root", "--project", "--experiment-id", "--compiler-cache-root",
            }, Array.Empty<string>());
            var sampleText = flags.Required("--sample");
            if (!int.TryParse(sampleText, NumberStyles.None, CultureInfo.InvariantCulture, out var sample) || sample > 5)
                throw new NightlyUsageException("--sample expects 0 to 5, not '" + sampleText + "'");
            var worktree = flags.Value("--worktree", "");
            return new LinuxCompilerMeasureOptions
            {
                Mode = Choice(flags.Required("--mode"), Modes, "--mode"),
                Phase = Choice(flags.Required("--phase"), Phases, "--phase"),
                Sample = sample,
                Worktree = string.IsNullOrWhiteSpace(worktree) ? defaultWorktree ?? "" : worktree,
                EvidenceRoot = flags.Value("--evidence-root", ""),
                Project = flags.Value("--project", "Antiphon.sln"),
                ExperimentId = flags.Value("--experiment-id", "measurement-58c3a29f"),
                CompilerCacheRoot = flags.Value("--compiler-cache-root", ""),
                Grant = grant ?? "",
            };
        }

        private static string Choice(string value, string[] choices, string flag) =>
            choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase))
            ?? throw new NightlyUsageException(flag + " expects " + string.Join(" or ", choices) + ", not '" + value + "'");
    }

    /// <summary>The private compiler toolset (BuildDriverEnvironment.cs's LinuxCompilerToolset, which the CLI binds).</summary>
    public interface ILinuxCompilerToolset
    {
        /// <summary>Installs the private toolset into start's argv and answers the diagnostic line (pipe=, sdk=, cpus=).</summary>
        string Prepare(ProcessStartInfo start, int grant, int idleSeconds, string isolation, bool capped);

        /// <summary>Runs a bounded utility and answers its trimmed stdout.</summary>
        string Probe(string executable, string[] arguments, string workingDirectory, StringDictionary environment);

        /// <summary>One "Key:" value of a /proc status file.</summary>
        string StatusValue(string path, string key);
    }

    /// <summary>The measurement's outside parts: the platform, the toolset, the files (and /proc) and the processes.</summary>
    public sealed class LinuxCompilerMeasureSeams
    {
        /// <summary>True on Linux; anything else is refused before any check.</summary>
        public required bool IsLinux { get; init; }

        /// <summary>The private compiler toolset.</summary>
        public required ILinuxCompilerToolset Toolset { get; init; }

        /// <summary>The evidence files and /proc.</summary>
        public required IFileSystem Files { get; init; }

        /// <summary>A tool's path on PATH (dotnet, getconf); null when missing.</summary>
        public required Func<string, string> FindTool { get; init; }
    }

    /// <summary>One private compiler process as /proc showed it (Get-C1885Compilers).</summary>
    public sealed record LinuxCompilerObservation(int Pid, string Start, long Ticks, long RssBytes, string Cpus, IReadOnlyList<string> Threads, IReadOnlyList<string> Argv)
    {
        /// <summary>The identity a sample counts by: pid and start ticks.</summary>
        public string Identity => Pid.ToString(CultureInfo.InvariantCulture) + ":" + Start;
    }

    /// <summary>One sample of the build: when, the private compilers' cumulative CPU ticks and RSS, and the driver's CPU.</summary>
    public sealed record LinuxCompilerSample(double Seconds, long CumulativeTicks, long RssBytes, IReadOnlyList<LinuxCompilerObservation> Compilers, double DriverCpuSeconds);

    /// <summary>The measurement (measure-linux-shared-compiler.ps1).</summary>
    public static class LinuxCompilerMeasure
    {
        private static readonly Regex Plain = new Regex("^[a-zA-Z0-9-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Served = new Regex("CompilerServer: server - server processed compilation", RegexOptions.CultureInvariant);
        private static readonly Regex FellBack = new Regex("CompilerServer: (?!server - server processed compilation)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private const long PeakRssLimit = 8L * 1024 * 1024 * 1024;

        /// <summary>The refusal before any build, or null: the platform, the grant, the inventory, the project, the identity and the cache root.</summary>
        public static string Refusal(LinuxCompilerMeasureOptions options, LinuxCompilerMeasureSeams seams, out int grant)
        {
            grant = 0;
            if (!seams.IsLinux) return "Linux native measurement required";
            if (!int.TryParse(options.Grant, NumberStyles.Integer, CultureInfo.InvariantCulture, out grant) || grant < 1)
                return "one outer lease and its typed CPU grant are required";
            var phase = options.Phase;
            var sample = options.Sample;
            if (((phase == "Restore" || phase == "Prime") && sample != 0) || (phase == "Measured" && (sample < 1 || sample > 3))
                || (phase == "Concurrent" && ((sample != 4 && sample != 5) || options.Mode != "Candidate")))
                return "invocation is outside the bounded experiment inventory";
            var worktree = seams.Files.Path.GetFullPath(options.Worktree);
            if (!seams.Files.File.Exists(seams.Files.Path.Combine(worktree, options.Project)) && !seams.Files.Directory.Exists(seams.Files.Path.Combine(worktree, options.Project)))
                return "measurement project does not exist";
            if (!Plain.IsMatch(options.ExperimentId ?? "")) return "invalid private experiment identity";
            if (!string.IsNullOrEmpty(options.CompilerCacheRoot) && !seams.Files.Path.IsPathRooted(options.CompilerCacheRoot)) return "compiler cache root must be absolute";
            return null;
        }

        /// <summary>The measurement (see the file header). Exit 0 accepted, 1 refused or not accepted.</summary>
        public static int Run(LinuxCompilerMeasureOptions options, NightlyHost host, LinuxCompilerMeasureSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var refusal = Refusal(options, seams, out var grant);
            if (refusal != null)
            {
                host.Error.WriteLine("measure-linux-shared-compiler: " + refusal);
                return 1;
            }
            var files = seams.Files;
            var worktree = files.Path.GetFullPath(options.Worktree);
            var evidenceRoot = files.Path.GetFullPath(string.IsNullOrEmpty(options.EvidenceRoot) ? files.Path.Combine(worktree, ".antiphon", "c1885", "performance") : options.EvidenceRoot);
            var head = host.RunGit(worktree, "rev-parse", "HEAD");
            if (head.ExitCode != 0) return Fail(host, "cannot bind source SHA");
            var sourceSha = head.Output.Trim();
            if (host.RunGit(worktree, "diff", "--quiet", "HEAD", "--").ExitCode != 0) return Fail(host, "measurement source is dirty");
            var runRoot = files.Path.Combine(evidenceRoot, options.Mode + "-" + options.Phase + "-" + options.Sample.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            files.Directory.CreateDirectory(runRoot);
            var dotnet = seams.FindTool("dotnet") ?? throw new InvalidOperationException("dotnet is not on PATH");
            var start = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false, WorkingDirectory = worktree, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            // This owned experimental child bypasses automatic policy selection. Both paths explicitly install a private
            // toolset/pipe; neither can use the live SDK pipe.
            start.Environment.Remove("ANTIPHON_LINUX_COMPILER_POLICY");
            if (!string.IsNullOrEmpty(options.CompilerCacheRoot)) start.Environment["XDG_CACHE_HOME"] = options.CompilerCacheRoot;
            var restore = options.Phase == "Restore";
            var cpuSwitch = "-maxcpucount:" + grant.ToString(CultureInfo.InvariantCulture);
            var tokens = restore
                ? new[] { "restore", options.Project, "-p:UseAppHost=false", cpuSwitch, "--nologo" }
                : new[] { "build", options.Project, "--configuration", "Debug", "--no-incremental", "--no-restore", "-p:UseAppHost=false", cpuSwitch, "-nodeReuse:false", "--nologo" };
            foreach (var token in tokens) start.ArgumentList.Add(token);
            var roslynLog = files.Path.Combine(runRoot, "roslyn.log");
            start.Environment["RoslynCommandLineLogFile"] = roslynLog;
            string diagnostic = null, pipe = null, sdk = null, cpus = null;
            var prepareBegan = host.Clock.GetTimestamp();
            double clk;
            try
            {
                if (!restore)
                {
                    // The same 600-second temporary lifetime in both serial modes prevents an opposite-mode build from silently
                    // turning a warm sample into a respawn.
                    var idleSeconds = options.Phase == "Concurrent" ? 120 : 600;
                    diagnostic = seams.Toolset.Prepare(start, grant, idleSeconds, options.ExperimentId, options.Mode == "Candidate");
                    host.Out.WriteLine(diagnostic);
                    pipe = Field(diagnostic, "pipe");
                    sdk = Field(diagnostic, "sdk");
                    cpus = Field(diagnostic, "cpus");
                }
                var getconf = seams.FindTool("getconf") ?? throw new InvalidOperationException("getconf is not on PATH");
                clk = double.Parse(seams.Toolset.Probe(getconf, new[] { "CLK_TCK" }, worktree, start.EnvironmentVariables), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidOperationException || e is FormatException)
            {
                // Before the build: nothing to observe and no receipt, as the script's uncaught throw.
                return Fail(host, e.Message);
            }
            var prepareSeconds = host.Clock.GetElapsedTime(prepareBegan).TotalSeconds;
            var records = new List<LinuxCompilerSample>();
            var previous = new Dictionary<string, long>(StringComparer.Ordinal);
            var known = new Dictionary<string, LinuxCompilerObservation>(StringComparer.Ordinal);
            long cumulativeTicks = 0, peakRss = 0;
            var threadEscape = false;
            var initial = pipe == null ? new List<LinuxCompilerObservation>() : Compilers(files, pipe);
            foreach (var compiler in initial) previous[compiler.Identity] = compiler.Ticks;
            var loadStart = files.File.ReadAllText("/proc/loadavg").Trim();
            var memoryStart = files.File.ReadAllText("/proc/meminfo");
            var driverAffinity = seams.Toolset.StatusValue("/proc/self/status", "Cpus_allowed_list");
            var shared = 0;
            bool? fallback = null;
            Process process = null;
            int? exitCode = null;
            string failure = null;
            double? bodySeconds = null, idleExitSeconds = null;
            var startedAtUtc = host.Clock.GetUtcNow();
            System.Threading.Tasks.Task<string> outTask = null, errTask = null;
            var clockBegan = host.Clock.GetTimestamp();
            double observed = 0;
            double Now() => host.Clock.GetElapsedTime(clockBegan).TotalSeconds;
            string sourceEnd = null;
            var cleanEnd = false;
            double? maxMinute = null;
            try
            {
                try
                {
                    clockBegan = host.Clock.GetTimestamp();
                    process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
                    outTask = process.StandardOutput.ReadToEndAsync();
                    errTask = process.StandardError.ReadToEndAsync();
                    bool ended;
                    do
                    {
                        var now = Now();
                        var current = pipe == null ? new List<LinuxCompilerObservation>() : Compilers(files, pipe);
                        long rss = 0;
                        foreach (var compiler in current)
                        {
                            known[compiler.Identity] = compiler;
                            var delta = previous.TryGetValue(compiler.Identity, out var before) ? compiler.Ticks - before : compiler.Ticks;
                            if (delta < 0) throw new InvalidOperationException("non-monotonic private compiler accounting");
                            cumulativeTicks += delta;
                            previous[compiler.Identity] = compiler.Ticks;
                            rss += compiler.RssBytes;
                            if (options.Mode == "Candidate")
                            {
                                var chosen = CpuSet(cpus);
                                foreach (var threadSet in compiler.Threads)
                                    if (CpuSet(threadSet).Any(cpu => !chosen.Contains(cpu))) threadEscape = true;
                            }
                        }
                        peakRss = Math.Max(peakRss, rss);
                        records.Add(new LinuxCompilerSample(now, cumulativeTicks, rss, current, process.TotalProcessorTime.TotalSeconds));
                        if (Now() > 600) throw new InvalidOperationException("one build exceeded the provisional ten-minute bound; do not expand collection");
                        ended = process.WaitForExit(1000);
                        if (ended && bodySeconds == null) bodySeconds = Now();
                        // A small standard project can finish before a complete CPU minute exists. Continue observation,
                        // including explicitly reported idle, without adding another build or charging the idle interval to
                        // build-body wall time.
                        if (ended && pipe != null && Now() < 61) host.Sleep(TimeSpan.FromSeconds(1));
                    } while (!ended || (pipe != null && Now() < 61));
                    observed = Now();
                    exitCode = process.ExitCode;
                    if (!outTask.Wait(3000) || !errTask.Wait(3000)) throw new InvalidOperationException("measurement output drain deadline");
                    if (exitCode != 0) throw new InvalidOperationException("standard Linux project command failed with exit " + exitCode.Value.ToString(CultureInfo.InvariantCulture));
                    if (!restore)
                    {
                        var log = files.File.ReadAllText(roslynLog);
                        shared = Served.Matches(log).Count;
                        fallback = FellBack.IsMatch(log);
                        if (shared < 2 || fallback == true) throw new InvalidOperationException("shared compiler acceptance failed: insufficient responses or fallback");
                        if (known.Count != 1) throw new InvalidOperationException("compiler warm identity changed or was not observed");
                        if (options.Phase == "Measured" && initial.Count != 1) throw new InvalidOperationException("timing sample was not warm; no replacement attempt authorized");
                        if (options.Phase == "Concurrent")
                        {
                            if (bodySeconds < 60) throw new InvalidOperationException("concurrent build body did not provide the required active minute");
                            var idleBegan = host.Clock.GetTimestamp();
                            while (Compilers(files, pipe).Count > 0 && host.Clock.GetElapsedTime(idleBegan).TotalSeconds < 130) host.Sleep(TimeSpan.FromMilliseconds(200));
                            if (Compilers(files, pipe).Count > 0) throw new InvalidOperationException("production 120-second idle release failed");
                            idleExitSeconds = host.Clock.GetElapsedTime(idleBegan).TotalSeconds;
                        }
                    }
                }
                catch (Exception e) when (e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception
                    || e is ArgumentException || e is FormatException)
                {
                    failure = e.Message;
                }
            }
            finally
            {
                if (observed == 0) observed = Now();
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(1000);
                        }
                    }
                    catch (InvalidOperationException) { }
                    process.Dispose();
                }
                if (outTask != null && outTask.IsCompletedSuccessfully) files.File.WriteAllText(files.Path.Combine(runRoot, "build.stdout.log"), outTask.Result);
                if (errTask != null && errTask.IsCompletedSuccessfully) files.File.WriteAllText(files.Path.Combine(runRoot, "build.stderr.log"), errTask.Result);
            }
            // A failed serial body must not leave its private server behind. Successful serial samples retain their owned warm
            // pipe until the controller finishes. Concurrent children never shut down their shared pipe while another body may run.
            try
            {
                if (failure != null && pipe != null && options.Phase != "Concurrent" && Compilers(files, pipe).Count > 0)
                    StopPrivateCompiler(host, dotnet, Path.Combine(sdk ?? "", "Roslyn", "bincore", "VBCSCompiler.dll"), pipe, worktree);
            }
            catch (InvalidOperationException e)
            {
                // The script's shutdown failure escaped its finally: no receipt.
                return Fail(host, (failure ?? "") + "; " + e.Message);
            }
            maxMinute = MaxFullMinuteCores(records, clk);
            var end = host.RunGit(worktree, "rev-parse", "HEAD");
            sourceEnd = end.Output.Trim();
            cleanEnd = host.RunGit(worktree, "diff", "--quiet", "HEAD", "--").ExitCode == 0 && string.Equals(sourceEnd, sourceSha, StringComparison.Ordinal);
            files.File.WriteAllText(files.Path.Combine(runRoot, "samples.json"), SamplesJson(records));
            files.File.WriteAllText(files.Path.Combine(runRoot, "receipt.json"), Receipt(w =>
            {
                w.WriteString("SourceSha", sourceSha);
                w.WriteString("SourceEndSha", sourceEnd);
                w.WriteBoolean("SourceClean", cleanEnd);
                w.WriteString("Worktree", worktree);
                w.WriteString("Project", options.Project);
                w.WriteString("ExperimentId", options.ExperimentId);
                w.WriteString("Mode", options.Mode);
                w.WriteString("Phase", options.Phase);
                w.WriteNumber("Sample", options.Sample);
                w.WriteNumber("Grant", grant);
                Nullable(w, "Diagnostic", diagnostic);
                Nullable(w, "Pipe", pipe);
                Nullable(w, "Sdk", sdk);
                w.WriteStartArray("Command");
                foreach (var token in start.ArgumentList) w.WriteStringValue(token);
                w.WriteEndArray();
                w.WriteString("StartedAtUtc", startedAtUtc.ToString("o", CultureInfo.InvariantCulture));
                w.WriteString("Host", System.Environment.MachineName);
                w.WriteNumber("PrepareSeconds", prepareSeconds);
                Nullable(w, "BuildBodySeconds", bodySeconds);
                if (exitCode.HasValue) w.WriteNumber("ExitCode", exitCode.Value);
                else w.WriteNull("ExitCode");
                w.WriteNumber("CompilerObservationSeconds", observed);
                w.WriteNumber("PostBuildObservationSeconds", Math.Max(0, observed - (bodySeconds ?? 0)));
                Nullable(w, "Failure", failure);
                w.WriteNumber("SharedResponses", shared);
                if (fallback.HasValue) w.WriteBoolean("Fallback", fallback.Value);
                else w.WriteNull("Fallback");
                w.WriteString("LoadStart", loadStart);
                w.WriteString("LoadEnd", files.File.ReadAllText("/proc/loadavg").Trim());
                w.WriteString("MemoryStart", memoryStart);
                w.WriteString("DriverAffinity", driverAffinity);
                Nullable(w, "ChosenCpus", cpus);
                w.WriteNumber("ClockTicksPerSecond", clk);
                w.WriteStartArray("WarmAtStart");
                foreach (var compiler in initial) Observation(w, compiler);
                w.WriteEndArray();
                w.WriteStartArray("OwnedIdentities");
                foreach (var identity in known.Keys) w.WriteStringValue(identity);
                w.WriteEndArray();
                w.WriteNumber("PeakAggregateRssBytes", peakRss);
                Nullable(w, "MaximumFullMinuteCores", maxMinute);
                w.WriteBoolean("ThreadEscape", threadEscape);
                Nullable(w, "IdleExitSeconds", idleExitSeconds);
                w.WriteStartArray("Unknown");
                foreach (var unknown in new[] { "full driver/server GC CPU", "exit-interval ticks if a server died between samples", "unrelated default compiler context CPU", "per-component restore timings" })
                    w.WriteStringValue(unknown);
                w.WriteEndArray();
            }));
            host.Out.WriteLine("C1885 PERFORMANCE mode=" + options.Mode + " phase=" + options.Phase + " sample=" + options.Sample.ToString(CultureInfo.InvariantCulture)
                + " builds=" + (restore ? "0" : "1") + " exit=" + (exitCode?.ToString(CultureInfo.InvariantCulture) ?? "") + " body=" + Real(bodySeconds)
                + " peakRss=" + peakRss.ToString(CultureInfo.InvariantCulture) + " minuteCores=" + Real(maxMinute) + " evidence=" + runRoot);
            if (failure != null || !cleanEnd || threadEscape || peakRss > PeakRssLimit || (maxMinute.HasValue && options.Mode == "Candidate" && maxMinute.Value > Math.Min(6, grant)))
            {
                if (failure != null) host.Error.WriteLine("measure-linux-shared-compiler: " + failure);
                return 1;
            }
            return 0;
        }

        /// <summary>Get-C1885Set: a Cpus_allowed_list ("0-3,8") as its CPU numbers; empty parts are skipped.</summary>
        public static HashSet<int> CpuSet(string list)
        {
            var set = new HashSet<int>();
            foreach (var part in (list ?? "").Split(','))
            {
                var text = part.Trim();
                if (text.Length == 0) continue;
                var range = text.Split('-');
                var first = int.Parse(range[0], NumberStyles.None, CultureInfo.InvariantCulture);
                var last = range.Length == 2 ? int.Parse(range[1], NumberStyles.None, CultureInfo.InvariantCulture) : first;
                for (var cpu = first; cpu <= last; cpu++) set.Add(cpu);
            }
            return set;
        }

        /// <summary>
        /// The highest CPU use over a full minute, in cores: for each sample the latest earlier sample at least 60 s before
        /// it, (ticks difference) / clk / seconds; null when no pair spans a minute.
        /// </summary>
        public static double? MaxFullMinuteCores(IReadOnlyList<LinuxCompilerSample> records, double clk)
        {
            double? best = null;
            for (var last = 0; last < records.Count; last++)
            {
                for (var first = last - 1; first >= 0; first--)
                {
                    var seconds = records[last].Seconds - records[first].Seconds;
                    if (seconds < 60) continue;
                    var cores = (records[last].CumulativeTicks - records[first].CumulativeTicks) / clk / seconds;
                    if (!best.HasValue || cores > best.Value) best = cores;
                    break;
                }
            }
            return best;
        }

        /// <summary>
        /// Get-C1885Compilers: every /proc/&lt;pid&gt; whose argv names -pipename:&lt;pipe&gt; (exactly), with its start ticks,
        /// utime+stime, VmRSS, CPU list and its threads' CPU lists; a process that vanishes mid-read is skipped, and a start
        /// that changes during the read (a reused PID) throws.
        /// </summary>
        public static List<LinuxCompilerObservation> Compilers(IFileSystem files, string pipe)
        {
            var observations = new List<LinuxCompilerObservation>();
            foreach (var directory in files.Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(files.Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var number)) continue;
                try
                {
                    var argv = files.File.ReadAllText(directory + "/cmdline").Split('\0');
                    if (!argv.Contains("-pipename:" + pipe, StringComparer.Ordinal)) continue;
                    var fields = StatFields(files.File.ReadAllText(directory + "/stat"));
                    var status = files.File.ReadAllText(directory + "/status");
                    var cpus = Regex.Match(status, "(?m)^Cpus_allowed_list:\\s*(.+)$").Groups[1].Value.Trim();
                    var rss = Regex.Match(status, "(?m)^VmRSS:\\s*(\\d+)").Groups[1].Value;
                    var threads = new List<string>();
                    foreach (var thread in files.Directory.EnumerateDirectories(directory + "/task"))
                    {
                        try { threads.Add(Regex.Match(files.File.ReadAllText(thread + "/status"), "(?m)^Cpus_allowed_list:\\s*(.+)$").Groups[1].Value.Trim()); }
                        catch (IOException) { }
                    }
                    var again = StatFields(files.File.ReadAllText(directory + "/stat"));
                    if (!string.Equals(fields[19], again[19], StringComparison.Ordinal)) throw new InvalidOperationException("compiler PID was reused during observation");
                    observations.Add(new LinuxCompilerObservation(number, fields[19],
                        long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture),
                        rss.Length > 0 ? long.Parse(rss, CultureInfo.InvariantCulture) * 1024 : 0, cpus, threads, argv));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return observations;
        }

        // The fields of /proc/<pid>/stat after the command name's closing parenthesis.
        private static string[] StatFields(string stat) =>
            stat.Substring(stat.LastIndexOf(')') + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Stop-C1885PrivateCompiler: the shutdown is addressed to the exact pipe this run owns, never to every SDK server.
        private static void StopPrivateCompiler(NightlyHost host, string dotnet, string dll, string pipe, string worktree)
        {
            var stop = host.Launch(new NightlyLaunch(dotnet, new[] { "exec", dll, "-shutdown", "-pipename:" + pipe }, worktree, TimeoutMilliseconds: 15000));
            if (stop.TimedOut) throw new InvalidOperationException("private-pipe shutdown deadline");
            if (stop.ExitCode != 0) throw new InvalidOperationException("private-pipe shutdown failed: " + stop.ExitCode.ToString(CultureInfo.InvariantCulture));
        }

        private static string Field(string diagnostic, string name) => Regex.Match(diagnostic ?? "", " " + name + "=(\\S+)").Groups[1].Value;

        private static int Fail(NightlyHost host, string message)
        {
            host.Error.WriteLine("measure-linux-shared-compiler: " + message);
            return 1;
        }

        private static string Real(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "";

        private static string SamplesJson(IReadOnlyList<LinuxCompilerSample> records) => Receipt(null, records);

        private static string Receipt(Action<Utf8JsonWriter> body, IReadOnlyList<LinuxCompilerSample> records = null)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                if (records != null)
                {
                    writer.WriteStartArray();
                    foreach (var record in records)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("Seconds", record.Seconds);
                        writer.WriteNumber("CumulativeTicks", record.CumulativeTicks);
                        writer.WriteNumber("RssBytes", record.RssBytes);
                        writer.WriteStartArray("Compilers");
                        foreach (var compiler in record.Compilers) Observation(writer, compiler);
                        writer.WriteEndArray();
                        writer.WriteNumber("DriverCpuSeconds", record.DriverCpuSeconds);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteStartObject();
                    body(writer);
                    writer.WriteEndObject();
                }
            }
            return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        }

        private static void Observation(Utf8JsonWriter writer, LinuxCompilerObservation compiler)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Pid", compiler.Pid);
            writer.WriteString("Start", compiler.Start);
            writer.WriteNumber("Ticks", compiler.Ticks);
            writer.WriteNumber("RssBytes", compiler.RssBytes);
            writer.WriteString("Cpus", compiler.Cpus);
            writer.WriteStartArray("Threads");
            foreach (var thread in compiler.Threads) writer.WriteStringValue(thread);
            writer.WriteEndArray();
            writer.WriteStartArray("Argv");
            foreach (var argument in compiler.Argv) writer.WriteStringValue(argument);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void Nullable(Utf8JsonWriter writer, string name, string value)
        {
            if (value == null) writer.WriteNull(name);
            else writer.WriteString(name, value);
        }

        private static void Nullable(Utf8JsonWriter writer, string name, double? value)
        {
            if (value.HasValue) writer.WriteNumber(name, value.Value);
            else writer.WriteNull(name);
        }
    }
}
