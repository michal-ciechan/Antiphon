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

        public static Result Run(ProcessStartInfo start, int timeoutMs, bool git, CancellationToken cancellationToken = default)
        {
            var result = RunCaptured(start, timeoutMs, git, ReadTextAsync, cancellationToken);
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
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeoutMs);
            try
            {
                if (git)
                {
                    HardenGit(start);
                    // Disable named filter/textconv drivers too. Config inspection itself has the
                    // same whole-operation deadline and cannot invoke these drivers.
                    var config = new ProcessStartInfo(start.FileName) { WorkingDirectory = start.WorkingDirectory };
                    foreach (var pair in start.Environment) config.Environment[pair.Key] = pair.Value;
                    HardenGit(config);
                    config.ArgumentList.Add("config"); config.ArgumentList.Add("--null");
                    config.ArgumentList.Add("--get-regexp");
                    config.ArgumentList.Add("^(filter\\..*\\.(clean|smudge|process|required)|diff\\..*\\.textconv)$");
                    var settings = RunCoreAsync(config, ReadTextAsync, deadline.Token).GetAwaiter().GetResult();
                    foreach (var entry in settings.Stdout.Split('\0'))
                    {
                        var split = entry.IndexOf('\n');
                        if (split < 1) continue;
                        var key = entry.Substring(0, split);
                        if (!(key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("diff.", StringComparison.OrdinalIgnoreCase))) continue;
                        start.ArgumentList.Insert(0, key + (key.EndsWith(".required", StringComparison.OrdinalIgnoreCase) ? "=false" : "="));
                        start.ArgumentList.Insert(0, "-c");
                    }
                }
                return RunCoreAsync(start, readStdout, deadline.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return new Captured<T> { Exit = 128, TimedOut = true }; }
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
            Func<Process, CancellationToken, Task<T>> readStdout, CancellationToken cancellationToken)
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
            if (!process.Start()) throw new InvalidOperationException("process did not start");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    if (job == IntPtr.Zero || !AssignProcessToJobObject(job, process.Handle))
                        throw new InvalidOperationException("process custody unavailable");
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

        [DllImport("libc", EntryPoint = "kill")] private static extern int Kill(int pid, int signal);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
