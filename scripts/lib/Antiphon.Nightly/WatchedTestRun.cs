// CARD-0165, CARD-0222: runs a TUnit test executable under a stall watchdog that captures a dump BEFORE killing it. It replaced
// scripts/run-tests-watched.ps1 (deleted 2026-11-24, unit 38 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md;
// nothing started it by path); the CLI runs it as
//
//   antiphon-scripts run-tests-watched --exe <tests>/bin-watch/Antiphon.Tests.exe [--filter "/*/*/*/*"] [--tag run]
//     [--out-dir <checkout>/logs/watched] [--hard-timeout-sec 3600] [--quiet-sec 150] [--cpu-floor 1.0] [--sample-sec 20]
//     [--detailed] [--env-vars NAME=VALUE;NAME2=VALUE2]
//
// A test process that is "still Responding but doing nothing" is only a stall if (a) its stdout has stopped growing AND (b) it is
// burning almost no CPU for a sustained window, and even then only `dotnet-dump analyze <dmp> -c dumpasync` names the cause (the
// thread stacks of an async wedge show nothing). The command starts the exe (--treenode-filter <filter> --no-progress --no-ansi,
// plus --output Detailed with --detailed) in its own directory, its stdout and stderr into <out-dir>/<tag>.log and <tag>.err, and
// every five seconds reads the log's size; every --sample-sec seconds it notes the elapsed time, log size, quiet seconds, CPU
// seconds and their delta, working set, threads and the Testcontainers Postgres connection count into <tag>.progress (and on
// stdout). When the log has not grown for --quiet-sec AND the sample's CPU delta is under --cpu-floor, or --hard-timeout-sec
// passes, it records the process tree (the exe and its children, with their children), the CPU over ten more seconds, the
// Testcontainers containers, pg_stat_activity (<tag>-pg-activity.txt), `dotnet-stack report` (<tag>-stack.txt) and
// `dotnet-dump collect` (<tag>.dmp), and only then kills the exe and its children. dotnet-stack and dotnet-dump are global tools
// (`dotnet tool install -g dotnet-stack` / `dotnet-dump`). Last, the tails of the log (12 lines) and of a non-empty stderr (5).
//
// Read the numbers before calling anything a stall. Measured 2026-08-29 (three watched runs, none stalled): the suite's global
// [NotInParallel] phase runs LAST, sequentially, for 20-26 minutes at 0.2-0.3 CPU-s/min, and under the default `--output Normal`
// it prints NOTHING unless a test fails. Pass --detailed so every passing test prints a line and the log-growth signal means
// something.
//
// The child's environment is this process's, then --env-vars (each noted as "[env] NAME=VALUE"), then the build-driver policy's
// variables (CARD-1506 BuildDriverEnvironment, which the CLI binds to the start). Exit: the exe's own code; 124 when it stalled or
// hit --hard-timeout-sec; 1 when it did not start (the script looped until its hard timeout then). Usage errors (a missing --exe, an exe that is not a file, an --env-vars entry that is not NAME=VALUE)
// are NightlyUsageException (the CLI's exit 2). The child, its samples, the process table, the diagnostic commands and the kill
// are IWatchedTestRunSeams (production WatchedTestRunOs); files are IFileSystem; the clock and the waits are the host's. Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The run-tests-watched command's options (run-tests-watched.ps1's parameters, in kebab case).</summary>
    public sealed class WatchedTestRunOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "run-tests-watched --exe <tests.exe> [--filter \"/*/*/*/*\"] [--tag run] [--out-dir <checkout>/logs/watched] [--hard-timeout-sec 3600]"
            + " [--quiet-sec 150] [--cpu-floor 1.0] [--sample-sec 20] [--detailed] [--env-vars NAME=VALUE;NAME2=VALUE2]";

        /// <summary>The test executable, a full path.</summary>
        public string Exe { get; init; } = "";

        /// <summary>The --treenode-filter value.</summary>
        public string Filter { get; init; } = "/*/*/*/*";

        /// <summary>The file names' stem.</summary>
        public string Tag { get; init; } = "run";

        /// <summary>The directory of the log, stderr, progress and diagnostic files.</summary>
        public string OutDir { get; init; } = "";

        /// <summary>The run is a stall after this many seconds, whatever its output.</summary>
        public int HardTimeoutSec { get; init; } = 3600;

        /// <summary>The log has not grown for this long ...</summary>
        public int QuietSec { get; init; } = 150;

        /// <summary>... and the last sample burned fewer CPU seconds than this: a stall.</summary>
        public double CpuFloor { get; init; } = 1.0;

        /// <summary>Seconds between samples.</summary>
        public int SampleSec { get; init; } = 20;

        /// <summary>--output Detailed: passing tests print too.</summary>
        public bool Detailed { get; init; }

        /// <summary>The variables given to the child, in order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> EnvVars { get; init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>The flags after the command name. repoRoot gives the default --out-dir (logs/watched); a relative --exe is under currentDirectory.</summary>
        public static WatchedTestRunOptions Parse(IReadOnlyList<string> arguments, string repoRoot, string currentDirectory)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--exe", "--filter", "--tag", "--out-dir", "--hard-timeout-sec", "--quiet-sec", "--cpu-floor", "--sample-sec", "--env-vars" },
                new[] { "--detailed" });
            var exe = flags.Required("--exe");
            var floorText = flags.Value("--cpu-floor", "");
            var floor = 1.0;
            if (floorText.Length > 0 && (!double.TryParse(floorText, NumberStyles.Float, CultureInfo.InvariantCulture, out floor) || double.IsNaN(floor) || double.IsInfinity(floor)))
                throw new NightlyUsageException("--cpu-floor expects a number, not '" + floorText + "'");
            var variables = new List<KeyValuePair<string, string>>();
            foreach (var entry in flags.Value("--env-vars", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = entry.IndexOf('=');
                if (equals < 1) throw new NightlyUsageException("--env-vars entry '" + entry + "' is not NAME=VALUE");
                variables.Add(new KeyValuePair<string, string>(entry.Substring(0, equals), entry.Substring(equals + 1)));
            }
            string Text(string flag, string fallback)
            {
                var value = flags.Value(flag, "");
                return value.Length > 0 ? value : fallback;
            }
            var directory = string.IsNullOrEmpty(currentDirectory) ? Directory.GetCurrentDirectory() : currentDirectory;
            return new WatchedTestRunOptions
            {
                Exe = Path.GetFullPath(Path.Combine(directory, exe)),
                Filter = Text("--filter", "/*/*/*/*"),
                Tag = Text("--tag", "run"),
                OutDir = Path.GetFullPath(Path.Combine(directory, Text("--out-dir", Path.Combine(repoRoot ?? directory, "logs", "watched")))),
                HardTimeoutSec = flags.Int("--hard-timeout-sec", 3600),
                QuietSec = flags.Int("--quiet-sec", 150),
                CpuFloor = floor,
                SampleSec = flags.Int("--sample-sec", 20),
                Detailed = flags.Switch("--detailed"),
                EnvVars = variables,
            };
        }
    }

    /// <summary>The child's start: its file, directory, arguments, added variables and the files its stdout and stderr go to.</summary>
    public sealed record WatchedChildStart(string FileName, string WorkingDirectory, IReadOnlyList<string> Arguments,
        IReadOnlyList<KeyValuePair<string, string>> Environment, string StandardOutputPath, string StandardErrorPath);

    /// <summary>The running test executable.</summary>
    public interface IWatchedChild : IDisposable
    {
        /// <summary>Its process id.</summary>
        int Id { get; }

        /// <summary>It has exited.</summary>
        bool HasExited { get; }

        /// <summary>Its exit code (after it exited; the output files complete).</summary>
        int ExitCode { get; }
    }

    /// <summary>Get-Process: the total CPU seconds, the working set and the thread count.</summary>
    public sealed record WatchedSample(double CpuSeconds, long WorkingSetBytes, int Threads);

    /// <summary>Win32_Process: the id, parent, name and command line.</summary>
    public sealed record WatchedProcess(int ProcessId, int ParentProcessId, string Name, string CommandLine);

    /// <summary>A diagnostic command's exit code and output (exit -1 and the reason on stderr when it did not start).</summary>
    public sealed record WatchedCommand(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>The machine the watched run uses.</summary>
    public interface IWatchedTestRunSeams
    {
        /// <summary>Starts the test executable; throws when it cannot start.</summary>
        IWatchedChild Start(WatchedChildStart start);

        /// <summary>The process's sample; null when it is gone.</summary>
        WatchedSample Sample(int pid);

        /// <summary>Every process; throws when the table cannot be read.</summary>
        IReadOnlyList<WatchedProcess> Processes();

        /// <summary>Runs a diagnostic command to its end (docker, dotnet-stack, dotnet-dump).</summary>
        WatchedCommand Run(string file, IReadOnlyList<string> arguments);

        /// <summary>Stop-Process -Force (best effort).</summary>
        void Kill(int pid);
    }

    /// <summary>The watched test run (run-tests-watched.ps1's body).</summary>
    public static class WatchedTestRun
    {
        /// <summary>The exit code of a stall or a hard timeout.</summary>
        public const int StalledExitCode = 124;

        /// <summary>The exit code when the test executable does not start (one progress line says why).</summary>
        public const int NotStartedExitCode = 1;

        private const string PostgresImage = "ancestor=postgres:16-alpine";
        private const string TestcontainersLabel = "label=org.testcontainers";

        /// <summary>Starts the exe, watches it and, on a stall, records the diagnostics and kills it; see the file comment for the exit code.</summary>
        public static int Run(WatchedTestRunOptions options, NightlyHost host, IFileSystem files, IWatchedTestRunSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            if (!files.File.Exists(options.Exe)) throw new NightlyUsageException("--exe names no file: " + options.Exe);

            var tag = options.Tag;
            files.Directory.CreateDirectory(options.OutDir);
            var log = files.Path.Combine(options.OutDir, tag + ".log");
            var err = files.Path.Combine(options.OutDir, tag + ".err");
            var progress = files.Path.Combine(options.OutDir, tag + ".progress");
            foreach (var stale in new[] { log, err, progress })
            {
                try { if (files.File.Exists(stale)) files.File.Delete(stale); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }

            // Tee-Object -Append: stdout and the progress file.
            void Note(string line)
            {
                host.Out.WriteLine(line);
                files.File.AppendAllText(progress, line + Environment.NewLine);
            }

            foreach (var variable in options.EnvVars) Note("[env] " + variable.Key + "=" + variable.Value);

            var arguments = new List<string> { "--treenode-filter", options.Filter, "--no-progress", "--no-ansi" };
            if (options.Detailed) arguments.AddRange(new[] { "--output", "Detailed" });
            var start = host.UtcNow;
            IWatchedChild started;
            try
            {
                started = seams.Start(new WatchedChildStart(options.Exe, files.Path.GetDirectoryName(options.Exe), arguments, options.EnvVars, log, err));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                Note("[" + tag + "] did not start: " + e.Message);
                return NotStartedExitCode;
            }
            using var child = started;
            Note("[" + tag + "] started pid=" + Num(child.Id) + " at " + Local(host, start) + " exe=" + options.Exe + " filter=" + options.Filter);

            long? lastSize = -1;
            var lastGrow = start;
            var lastCpu = 0.0;
            var lastSample = start;
            var stalled = false;
            while (!child.HasExited)
            {
                host.Sleep(TimeSpan.FromSeconds(5));
                var size = Length(files, log);
                if (size != lastSize)
                {
                    lastSize = size;
                    lastGrow = host.UtcNow;
                }
                var now = host.UtcNow;
                if ((now - lastSample).TotalSeconds >= options.SampleSec)
                {
                    var sample = seams.Sample(child.Id);
                    var cpu = sample?.CpuSeconds ?? 0;
                    var delta = cpu - lastCpu;
                    lastCpu = cpu;
                    lastSample = now;
                    var quiet = Whole((now - lastGrow).TotalSeconds);
                    Note(Local(host, now) + " t=" + Num(Whole((now - start).TotalSeconds)) + "s log=" + (size?.ToString(CultureInfo.InvariantCulture) ?? "")
                        + " quiet=" + Num(quiet) + "s cpu=" + Dec(Math.Round(cpu, 1)) + " dcpu=" + Dec(Math.Round(delta, 2))
                        + " ws=" + Dec(Math.Round((sample?.WorkingSetBytes ?? 0) / 1048576.0)) + "MB thr=" + Num(sample?.Threads ?? 0) + " " + PostgresConnections(seams));
                    if (quiet >= options.QuietSec && delta < options.CpuFloor)
                    {
                        stalled = true;
                        break;
                    }
                }
                if ((now - start).TotalSeconds >= options.HardTimeoutSec)
                {
                    stalled = true;
                    Note("[" + tag + "] hard timeout");
                    break;
                }
            }

            var elapsed = Whole((host.UtcNow - start).TotalSeconds);
            if (child.HasExited && !stalled) Note("[" + tag + "] EXITED code=" + Num(child.ExitCode) + " after " + Num(elapsed) + "s");
            else Diagnose(options, host, files, seams, child.Id, elapsed, lastGrow, Note);

            Note("--- stdout tail (" + log + ", " + (Length(files, log)?.ToString(CultureInfo.InvariantCulture) ?? "") + " bytes):");
            foreach (var line in Tail(files, log, 12)) Note(line);
            if ((Length(files, err) ?? 0) > 0)
            {
                Note("--- stderr tail:");
                foreach (var line in Tail(files, err, 5)) Note(line);
            }
            return stalled ? StalledExitCode : child.ExitCode;
        }

        // The stall's evidence, then the kill of the exe and its children (the highest pid first).
        private static void Diagnose(WatchedTestRunOptions options, NightlyHost host, IFileSystem files, IWatchedTestRunSeams seams, int pid, int elapsed, DateTime lastGrow,
            Action<string> note)
        {
            var tag = options.Tag;
            note("[" + tag + "] STALLED after " + Num(elapsed) + "s; stdout last grew " + Num(Whole((host.UtcNow - lastGrow).TotalSeconds)) + "s ago");
            IReadOnlyList<WatchedProcess> table;
            try
            {
                table = seams.Processes() ?? Array.Empty<WatchedProcess>();
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is Win32Exception || e is InvalidOperationException || e is DllNotFoundException
                || e is EntryPointNotFoundException)
            {
                note("  process table unavailable: " + e.Message);
                table = Array.Empty<WatchedProcess>();
            }
            var tree = table.Where(row => row.ParentProcessId == pid || row.ProcessId == pid).ToList();
            // Without a table the exe itself is still the tree.
            if (!tree.Any(row => row.ProcessId == pid)) tree.Add(new WatchedProcess(pid, 0, "", ""));
            foreach (var row in tree)
            {
                var sample = seams.Sample(row.ProcessId);
                note("  proc pid=" + Num(row.ProcessId) + " parent=" + Num(row.ParentProcessId) + " name=" + row.Name + " cpu=" + (sample == null ? "" : Dec(Math.Round(sample.CpuSeconds, 2)))
                    + " threads=" + (sample == null ? "" : Num(sample.Threads)) + " cmd=" + Cut(row.CommandLine));
                foreach (var grandchild in table.Where(other => other.ParentProcessId == row.ProcessId && row.ProcessId != 0))
                    note("    child pid=" + Num(grandchild.ProcessId) + " name=" + grandchild.Name + " cmd=" + Cut(grandchild.CommandLine));
            }
            var before = seams.Sample(pid);
            host.Sleep(TimeSpan.FromSeconds(10));
            var after = seams.Sample(pid);
            note("  cpu before 10s: " + (before == null ? "" : Dec(before.CpuSeconds)) + " ; after: " + (after == null ? "" : Dec(after.CpuSeconds)));
            var containers = seams.Run("docker", new[] { "ps", "-a", "--filter", TestcontainersLabel, "--format", "{{.Names}} {{.Image}} {{.Status}}" });
            note("  docker: " + string.Join(" | ", Lines(containers.StandardOutput)));
            note("  " + PostgresConnections(seams));
            var postgres = PostgresContainer(seams);
            if (postgres != null)
            {
                var activity = seams.Run("docker", new[] { "exec", postgres, "psql", "-U", "test", "-d", "antiphon_test", "-Atc",
                    "select pid, state, wait_event_type, wait_event, left(query,160) from pg_stat_activity where datname='antiphon_test' order by state" });
                files.File.WriteAllText(files.Path.Combine(options.OutDir, tag + "-pg-activity.txt"), activity.StandardOutput ?? "");
                note("  pg_stat_activity -> " + tag + "-pg-activity.txt");
            }
            var stack = seams.Run("dotnet-stack", new[] { "report", "-p", Num(pid) });
            files.File.WriteAllText(files.Path.Combine(options.OutDir, tag + "-stack.txt"), (stack.StandardOutput ?? "") + (stack.StandardError ?? ""));
            note("  managed thread stacks -> " + tag + "-stack.txt");
            var dump = seams.Run("dotnet-dump", new[] { "collect", "-p", Num(pid), "-o", files.Path.Combine(options.OutDir, tag + ".dmp") });
            var last = Lines((dump.StandardOutput ?? "") + "\n" + (dump.StandardError ?? "")).LastOrDefault();
            if (last != null) note("  " + last);
            note("  dump -> " + tag + ".dmp  (analyze: dotnet-dump analyze " + tag + ".dmp -c dumpasync -c exit)");
            foreach (var row in tree.OrderByDescending(row => row.ProcessId)) seams.Kill(row.ProcessId);
            note("  killed");
        }

        // "pg=<connections>/<max_connections>" of the first Testcontainers Postgres, else "pg=none".
        private static string PostgresConnections(IWatchedTestRunSeams seams)
        {
            var container = PostgresContainer(seams);
            if (container == null) return "pg=none";
            var count = seams.Run("docker", new[] { "exec", container, "psql", "-U", "test", "-d", "antiphon_test", "-Atc",
                "select count(*) || '/' || (select setting from pg_settings where name='max_connections') from pg_stat_activity" });
            return "pg=" + string.Join(" ", Lines(count.StandardOutput));
        }

        private static string PostgresContainer(IWatchedTestRunSeams seams) =>
            Lines(seams.Run("docker", new[] { "ps", "--filter", PostgresImage, "--filter", TestcontainersLabel, "--format", "{{.ID}}" }).StandardOutput).FirstOrDefault();

        // The file's size; null when it does not exist.
        private static long? Length(IFileSystem files, string path)
        {
            try
            {
                return files.File.Exists(path) ? files.FileInfo.New(path).Length : (long?)null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // Get-Content -Tail: the last lines, read while another writer may still hold the file.
        private static IEnumerable<string> Tail(IFileSystem files, string path, int count)
        {
            try
            {
                if (!files.File.Exists(path)) return Array.Empty<string>();
                using var stream = files.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var lines = Lines(reader.ReadToEnd()).ToList();
                return lines.Skip(Math.Max(0, lines.Count - count)).ToArray();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        // The non-empty lines, without their line ends.
        private static IEnumerable<string> Lines(string text) =>
            (text ?? "").Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0);

        // The first 200 characters of a command line.
        private static string Cut(string text) => string.IsNullOrEmpty(text) ? "" : text.Substring(0, Math.Min(200, text.Length));

        // [int] rounds half to even.
        private static int Whole(double value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

        private static string Local(NightlyHost host, DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), host.Clock.LocalTimeZone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Dec(double value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The production IWatchedTestRunSeams: this machine.</summary>
    public sealed class WatchedTestRunOs : IWatchedTestRunSeams
    {
        private readonly Action<ProcessStartInfo> _prepareChild;

        /// <summary>prepareChild finishes the test executable's start (the CLI binds the build-driver policy, CARD-1506).</summary>
        public WatchedTestRunOs(Action<ProcessStartInfo> prepareChild) => _prepareChild = prepareChild;

        /// <inheritdoc />
        public IWatchedChild Start(WatchedChildStart start)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            var info = new ProcessStartInfo(start.FileName)
            {
                UseShellExecute = false,
                WorkingDirectory = start.WorkingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in start.Arguments) info.ArgumentList.Add(argument);
            foreach (var variable in start.Environment) info.Environment[variable.Key] = variable.Value;
            _prepareChild?.Invoke(info);
            var process = Process.Start(info) ?? throw new InvalidOperationException("the test executable did not start: " + start.FileName);
            return new Child(process, start.StandardOutputPath, start.StandardErrorPath);
        }

        /// <inheritdoc />
        public WatchedSample Sample(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return new WatchedSample(process.TotalProcessorTime.TotalSeconds, process.WorkingSet64, process.Threads.Count);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<WatchedProcess> Processes()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the process tree is read from the Windows process table.");
            return ZombieAgentReaper.Win32Processes.Read()
                .Select(row => new WatchedProcess(row.Process.ProcessId, row.Process.ParentProcessId, row.Process.Name, row.Process.CommandLine)).ToArray();
        }

        /// <inheritdoc />
        public WatchedCommand Run(string file, IReadOnlyList<string> arguments)
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            try
            {
                using var process = Process.Start(info) ?? throw new InvalidOperationException("did not start: " + file);
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return new WatchedCommand(process.ExitCode, output, error.GetAwaiter().GetResult());
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return new WatchedCommand(-1, "", e.Message);
            }
        }

        /// <inheritdoc />
        public void Kill(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException)
            {
            }
        }

        // The child, its stdout and stderr copied into their files as they arrive (unbuffered, so the size is the signal).
        private sealed class Child : IWatchedChild
        {
            private readonly Process _process;
            private readonly Task _copies;

            public Child(Process process, string output, string error)
            {
                _process = process;
                _copies = Task.WhenAll(Copy(process.StandardOutput.BaseStream, output), Copy(process.StandardError.BaseStream, error));
            }

            public int Id => _process.Id;

            public bool HasExited => _process.HasExited;

            public int ExitCode
            {
                get
                {
                    _process.WaitForExit();
                    // A grandchild that inherited the pipe can hold it open; the files are complete enough after ten seconds.
                    _copies.Wait(TimeSpan.FromSeconds(10));
                    return _process.ExitCode;
                }
            }

            public void Dispose() => _process.Dispose();

            private static Task Copy(Stream source, string path) => Task.Run(() =>
            {
                using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
                var buffer = new byte[16384];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    target.Write(buffer, 0, read);
                    target.Flush();
                }
            });
        }
    }
}
