using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Antiphon.Checkpoints
{
    // Compiled by the tool and loaded by PowerShell: one exit-and-drain implementation.
    public static class CheckpointProcessRunner
    {
        public sealed class Result
        {
            public int Exit { get; set; }
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        public sealed class BytesResult
        {
            public int Exit { get; set; }
            public byte[] Stdout { get; set; } = Array.Empty<byte>();
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        // An already-exited child lost the job-assignment race and still has a result.
        // A live child that cannot be assigned is process custody, not a timeout.
        // string, not string?: this file is also Add-Type'd without a nullable context.
        public static string CustodyRefusal(bool assigned, bool exited) =>
            assigned || exited ? null : "process custody unavailable";

        public static Result Run(ProcessStartInfo start, int timeoutMs, bool git, CancellationToken cancellationToken = default)
            => RunObserved(start, timeoutMs, git, null, cancellationToken);

        // Invocation-local observation keeps process-count witnesses on the actual launch path.
        internal static Result RunObserved(ProcessStartInfo start, int timeoutMs, bool git,
            Action<ProcessStartInfo> beforeStart, CancellationToken cancellationToken = default)
            => RunObserved(start, timeoutMs, git, beforeStart, -1, cancellationToken);

        // driverSettingsCacheMs below 0 means DriverSettingsCacheMilliseconds; tests pass their own.
        internal static Result RunObserved(ProcessStartInfo start, int timeoutMs, bool git,
            Action<ProcessStartInfo> beforeStart, int driverSettingsCacheMs, CancellationToken cancellationToken = default)
        {
            var result = RunCaptured(start, timeoutMs, git, ReadTextAsync, cancellationToken, beforeStart, driverSettingsCacheMs);
            return new Result { Exit = result.Exit, Stdout = result.Stdout ?? "", Stderr = result.Stderr, TimedOut = result.TimedOut };
        }

        public static BytesResult RunBytes(ProcessStartInfo start, int timeoutMs, bool git, CancellationToken cancellationToken = default)
        {
            var result = RunCaptured(start, timeoutMs, git, ReadBytesAsync, cancellationToken);
            return new BytesResult { Exit = result.Exit, Stdout = result.Stdout ?? Array.Empty<byte>(), Stderr = result.Stderr, TimedOut = result.TimedOut };
        }

        private sealed class Captured<T>
        {
            public int Exit { get; set; }
            public T Stdout { get; set; } = default!;
            public string Stderr { get; set; } = "";
            public bool TimedOut { get; set; }
        }

        private static Captured<T> RunCaptured<T>(ProcessStartInfo start, int timeoutMs, bool git,
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken,
            Action<ProcessStartInfo> beforeStart = null, int driverSettingsCacheMs = -1)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeoutMs);
            try
            {
                if (git)
                {
                    // This exact index-only query never reads file contents or invokes
                    // filter/textconv drivers. Keep its per-entry Git revalidation, without
                    // starting a second process to discover drivers it cannot invoke.
                    var arguments = start.ArgumentList;
                    var indexOnly = arguments.Count >= 6 && arguments[0] == "-C"
                        && arguments[2] == "ls-files" && arguments[3] == "-z" && arguments[4] == "--";
                    HardenGit(start);
                    if (indexOnly)
                        return RunCoreAsync(start, readStdout, deadline.Token, beforeStart).GetAwaiter().GetResult();
                    // Disable named filter/textconv drivers too. Config inspection itself has the
                    // same whole-operation deadline and cannot invoke these drivers.
                    var config = new ProcessStartInfo(start.FileName) { WorkingDirectory = start.WorkingDirectory };
                    foreach (var pair in start.Environment) config.Environment[pair.Key] = pair.Value;
                    HardenGit(config);
                    config.ArgumentList.Add("config"); config.ArgumentList.Add("--null");
                    config.ArgumentList.Add("--get-regexp");
                    config.ArgumentList.Add("^(filter\\..*\\.(clean|smudge|process|required)|diff\\..*\\.textconv)$");
                    var settings = DriverSettings(config, driverSettingsCacheMs < 0 ? DriverSettingsCacheMilliseconds : driverSettingsCacheMs, deadline.Token, beforeStart);
                    foreach (var entry in settings.Split('\0'))
                    {
                        var split = entry.IndexOf('\n');
                        if (split < 1) continue;
                        var key = entry.Substring(0, split);
                        if (!(key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("diff.", StringComparison.OrdinalIgnoreCase))) continue;
                        start.ArgumentList.Insert(0, key + (key.EndsWith(".required", StringComparison.OrdinalIgnoreCase) ? "=false" : "="));
                        start.ArgumentList.Insert(0, "-c");
                    }
                }
                return RunCoreAsync(start, readStdout, deadline.Token, beforeStart).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return new Captured<T> { Exit = 128, TimedOut = true }; }
        }

        // How long one driver-settings answer is reused for the same git, directory and environment.
        // 0 (the default) asks git before every call. The release-selection worker turns it on for its
        // own short life: a config change made while it runs is then seen by the next worker.
        public static int DriverSettingsCacheMilliseconds { get; set; }

        private static readonly object DriverSettingsGate = new object();
        private static readonly Dictionary<string, KeyValuePair<long, string>> DriverSettingsCache = new Dictionary<string, KeyValuePair<long, string>>();

        private static string DriverSettings(ProcessStartInfo config, int lifetime, CancellationToken cancellationToken, Action<ProcessStartInfo> beforeStart)
        {
            string key = null;
            if (lifetime > 0)
            {
                var parts = new List<string> { config.FileName, config.WorkingDirectory };
                foreach (var pair in config.Environment) parts.Add(pair.Key + "=" + pair.Value);
                parts.Sort(2, parts.Count - 2, StringComparer.Ordinal);
                parts.AddRange(config.ArgumentList);
                key = string.Join("\0", parts);
                lock (DriverSettingsGate)
                {
                    if (DriverSettingsCache.TryGetValue(key, out var cached) && Environment.TickCount64 - cached.Key < lifetime)
                        return cached.Value;
                }
            }
            var settings = RunCoreAsync(config, ReadTextAsync, cancellationToken, beforeStart).GetAwaiter().GetResult();
            var stdout = settings.Stdout ?? "";
            // Exit 1 is "no driver configured"; anything else is not an answer worth keeping.
            if (key != null && !settings.TimedOut && (settings.Exit == 0 || settings.Exit == 1))
                lock (DriverSettingsGate) DriverSettingsCache[key] = new KeyValuePair<long, string>(Environment.TickCount64, stdout);
            return stdout;
        }

        private static Task<string> ReadTextAsync(Process process, CancellationToken cancellationToken) =>
            process.StandardOutput.ReadToEndAsync(cancellationToken);

        private static async Task<byte[]> ReadBytesAsync(Process process, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return output.ToArray();
        }

        private static void HardenGit(ProcessStartInfo start)
        {
            var nullPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" }) start.Environment.Remove(name);
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = nullPath;
            // Disable fsmonitor, hooks, external diff, attributes, auto-gc and
            // auto-maintenance for this child. These settings do not identify or
            // remove a Windows pipe holder. Desktop C1313_BaselineAuthority still
            // has to read holder= before that hang can be called fixed.
            foreach (var setting in new[] { "core.fsmonitor=false", "core.hooksPath=" + nullPath, "diff.external=", "core.attributesFile=" + nullPath, "gc.auto=0", "maintenance.auto=false" })
            { start.ArgumentList.Insert(0, setting); start.ArgumentList.Insert(0, "-c"); }
        }

        private static async Task<Captured<T>> RunCoreAsync<T>(ProcessStartInfo start,
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken,
            Action<ProcessStartInfo> beforeStart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsLinux())
            {
                var executable = start.FileName;
                var exists = File.Exists(executable);
                if (!exists && !executable.Contains("/"))
                    foreach (var directory in (start.Environment["PATH"] ?? "").Split(Path.PathSeparator))
                        if (File.Exists(Path.Combine(directory, executable))) { exists = true; break; }
                if (!exists) throw new System.ComponentModel.Win32Exception(2);
            }
            start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.CreateNoWindow = true;
            if (OperatingSystem.IsLinux())
            {
                // A private session keeps descendants addressable after the direct child exits.
                var executable = start.FileName; start.FileName = "setsid";
                start.ArgumentList.Insert(0, executable);
            }
            using var process = new Process { StartInfo = start };
            IntPtr job = IntPtr.Zero;
            beforeStart?.Invoke(start);
            if (!process.Start()) throw new InvalidOperationException("process did not start");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var assigned = job != IntPtr.Zero && AssignProcessToJobObject(job, process.Handle);
                    var refusal = CustodyRefusal(assigned, Exited(process));
                    if (refusal is not null)
                        throw new InvalidOperationException(refusal);
                }
                var stdout = readStdout(process, cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                // This token covers BOTH direct-child exit and inherited redirected descriptors.
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdout, stderr).WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Captured<T> { Exit = process.ExitCode, Stdout = await stdout.ConfigureAwait(false), Stderr = await stderr.ConfigureAwait(false) };
            }
            finally
            {
                if (OperatingSystem.IsLinux()) Kill(-process.Id, 9);
                if (job != IntPtr.Zero) { TerminateJobObject(job, 128); CloseHandle(job); }
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                // Abandon pipes on expiry; never synchronously join a drain here.
                try { if (!process.HasExited) process.WaitForExit(1000); } catch (InvalidOperationException) { }
            }
        }

        private static bool Exited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        [DllImport("libc", EntryPoint = "kill")] private static extern int Kill(int pid, int signal);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
