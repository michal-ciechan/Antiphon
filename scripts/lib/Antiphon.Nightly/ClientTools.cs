// The client tool seam and the one shared npm install of the client commands (unit 32 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). ClientToolRunner starts one node or npm child and hands
// each line of its output (stdout and stderr, in arrival order) to the caller, which prints it and, for the gates, tees it
// to a log under logs/ (Tee-Object); the production runner is ClientTools.RunProcess, a test binds its own, so no test runs
// node or npm. ClientNpmInstall replaced scripts/lib/client-npm-install.ps1 (CARD-1394, CARD-1398): when a required client
// binary is missing it runs one `npm ci` (`npm install` without client/package-lock.json) in client/, holding
// logs/client-npm-ci.lock (FileShare.None, released when the holder exits, including a crash) so a second cold caller waits
// for it instead of installing beside it; a waiter retries every 2 s for 15 min, and looks again once it holds the lock.
// Files are IFileSystem's, the clock and the wait the host's, PATH and ComSpec the host's Variables. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>One node or npm child: the program, its arguments (each one argv entry) and its working directory.</summary>
    public sealed record ClientToolLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
    {
        /// <summary>The program and the arguments, space separated.</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>
    /// Runs the child to its exit: each output line (stdout and stderr in arrival order, one call at a time) goes to line;
    /// returns the exit code. Throws when the child cannot start.
    /// </summary>
    public delegate int ClientToolRunner(ClientToolLaunch launch, Action<string> line);

    /// <summary>The client commands' shared parts: the production runner, the tool call, the npm launch and the tee.</summary>
    public static class ClientTools
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// The production runner: starts FileName (resolved on PATH when it has no directory) with the arguments in the
        /// working directory, stdin this process's, stdout and stderr read as UTF-8 line by line, and waits for the exit and
        /// the end of both streams.
        /// </summary>
        public static int RunProcess(ClientToolLaunch launch, Action<string> line)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (line == null) throw new ArgumentNullException(nameof(line));
            var start = new ProcessStartInfo(launch.FileName)
            {
                UseShellExecute = false,
                WorkingDirectory = launch.WorkingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (var argument in launch.Arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            using var process = new Process { StartInfo = start };
            var gate = new object();
            void Line(string data)
            {
                if (data == null) return;
                lock (gate) line(data);
            }
            process.OutputDataReceived += (sender, e) => Line(e.Data);
            process.ErrorDataReceived += (sender, e) => Line(e.Data);
            using (ClientPhase.Start("client child start"))
                if (!process.Start()) throw new InvalidOperationException("the child did not start: " + launch.FileName);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Without a timeout WaitForExit also waits for the redirected streams to reach their end.
            using (ClientPhase.Start("client child exit and output drain"))
                process.WaitForExit();
            return process.ExitCode;
        }

        /// <summary>
        /// Runs the launch that build makes on the runner. A launch that cannot be made or started is one line (its
        /// message) and exit 1, as PowerShell's call operator left a command it could not find.
        /// </summary>
        public static int Invoke(ClientToolRunner tools, Func<ClientToolLaunch> build, Action<string> line)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            try
            {
                return tools(build(), line);
            }
            catch (Exception e) when (e is NightlyRefusal || e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                line(e.Message);
                return 1;
            }
        }

        /// <summary>
        /// npm with the arguments in the directory. On Windows: ComSpec /c with the absolute npm launcher found on PATH
        /// (NightlyTestsPlan.NpmLaunch; a quoted bare npm.cmd makes cmd.exe set %~dp0 to the cwd, CARD-1341); elsewhere npm
        /// itself. A missing launcher refuses ("npm launcher not found").
        /// </summary>
        public static ClientToolLaunch Npm(NightlyHost host, string workingDirectory, params string[] arguments)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!OperatingSystem.IsWindows()) return new ClientToolLaunch("npm", arguments, workingDirectory);
            var comSpec = host.Variables("ComSpec");
            var launch = NightlyTestsPlan.NpmLaunch(arguments, host.Variables("PATH"), string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec);
            return new ClientToolLaunch(launch.FilePath, launch.ArgumentList, workingDirectory);
        }

        /// <summary>An exit code or a count as text, invariant culture.</summary>
        public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>The verdict line's suffix: PASS for 0, else the do-not-report-green warning.</summary>
        public static string Verdict(int code) => code == 0 ? "PASS" : "FAIL - do not report this run as green";

        /// <summary>The client binaries under client/node_modules the gates and the suite run.</summary>
        public static string Vitest(string repoRoot) => Path.Combine(repoRoot, "client", "node_modules", "vitest", "vitest.mjs");

        /// <summary>client/node_modules/typescript/bin/tsc.</summary>
        public static string Tsc(string repoRoot) => Path.Combine(repoRoot, "client", "node_modules", "typescript", "bin", "tsc");

        /// <summary>client/node_modules/eslint/bin/eslint.js.</summary>
        public static string Eslint(string repoRoot) => Path.Combine(repoRoot, "client", "node_modules", "eslint", "bin", "eslint.js");

        /// <summary>logs/ of the checkout, created when missing.</summary>
        public static string Logs(IFileSystem files, string repoRoot)
        {
            var logs = Path.Combine(repoRoot, "logs");
            files.Directory.CreateDirectory(logs);
            return logs;
        }
    }

    /// <summary>
    /// Tee-Object -FilePath: every line goes to the output and to the log, which is created (or, appending, extended) as UTF-8
    /// without a byte-order mark and flushed per line.
    /// </summary>
    public sealed class ClientLog : IDisposable
    {
        private readonly TextWriter _output;
        private readonly StreamWriter _log;

        private ClientLog(TextWriter output, StreamWriter log)
        {
            _output = output;
            _log = log;
        }

        /// <summary>Opens the log; IOException or UnauthorizedAccessException when it cannot be written.</summary>
        public static ClientLog Open(IFileSystem files, string path, bool append, TextWriter output)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var stream = files.File.Open(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
            return new ClientLog(output ?? TextWriter.Null, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
        }

        /// <summary>One line to the output and the log.</summary>
        public void Line(string text)
        {
            _output.WriteLine(text);
            _log.WriteLine(text);
        }

        /// <summary>Closes the log.</summary>
        public void Dispose() => _log.Dispose();
    }

    /// <summary>The one shared client npm install (client-npm-install.ps1 Install-ClientNpmDependencies).</summary>
    public static class ClientNpmInstall
    {
        /// <summary>How long a caller waits for the lock before it gives up (exit 1).</summary>
        public static readonly TimeSpan LockWait = TimeSpan.FromMinutes(15);

        /// <summary>The wait between two attempts at the lock.</summary>
        public static readonly TimeSpan LockRetry = TimeSpan.FromSeconds(2);

        /// <summary>The lock's file name under logs/.</summary>
        public const string LockName = "client-npm-ci.lock";

        /// <summary>
        /// 0 when every required path exists. Otherwise takes the lock, looks again (0 when another holder installed them),
        /// and runs npm ci (npm install when client/package-lock.json is missing) in client/ with its output on host.Out:
        /// npm's exit code, after "npm &lt;verb&gt; exited with code n." when it is not 0. 1 when the lock is still held by
        /// another caller after LockWait.
        /// </summary>
        public static int Install(string repoRoot, IReadOnlyList<string> requiredPaths, NightlyHost host, IFileSystem files, ClientToolRunner tools)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            var required = requiredPaths ?? Array.Empty<string>();
            if (required.All(files.File.Exists)) return 0;
            var lockPath = Path.Combine(ClientTools.Logs(files, repoRoot), LockName);
            var deadline = host.UtcNow + LockWait;
            Stream held = null;
            using (ClientPhase.Start("client npm lock wait"))
            {
                while (held == null)
                {
                    try
                    {
                        held = files.File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        if (host.UtcNow > deadline)
                        {
                            host.Out.WriteLine("timed out waiting for the client npm ci lock at " + lockPath);
                            return 1;
                        }
                        host.Sleep(LockRetry);
                    }
                }
            }
            using (ClientPhase.Start("client npm lock held"))
            using (held)
            {
                // A second look: the caller that held the lock before this one may have installed them.
                if (required.All(files.File.Exists)) return 0;
                host.Out.WriteLine("client dependencies are not installed - installing once under the npm ci lock.");
                var clientDir = Path.Combine(repoRoot, "client");
                var verb = files.File.Exists(Path.Combine(clientDir, "package-lock.json")) ? "ci" : "install";
                int code;
                using (ClientPhase.Start("client npm " + verb))
                    code = ClientTools.Invoke(tools, () => ClientTools.Npm(host, clientDir, verb), host.Out.WriteLine);
                if (code != 0) host.Out.WriteLine("npm " + verb + " exited with code " + ClientTools.Number(code) + ".");
                return code;
            }
        }
    }

    // Local trace records use the test-trace summary's span schema. Names are fixed by the
    // launcher and carry neither command arguments nor output.
    internal sealed class ClientPhase : IDisposable
    {
        private static readonly object WriteGate = new();
        private readonly Activity _activity;
        private readonly string _path;

        private ClientPhase(string name, string directory)
        {
            _path = Path.Combine(directory, "test-trace-client-" + Environment.ProcessId + ".jsonl");
            _activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C).Start();
        }

        public static ClientPhase Start(string name)
        {
            var directory = Environment.GetEnvironmentVariable("ANTIPHON_TEST_TRACE_DIR");
            return string.IsNullOrWhiteSpace(directory) ? null : new ClientPhase(name, directory);
        }

        public void Dispose()
        {
            _activity.Stop();
            try
            {
                var record = JsonSerializer.Serialize(new
                {
                    TraceId = _activity.TraceId.ToHexString(),
                    SpanId = _activity.SpanId.ToHexString(),
                    ParentId = _activity.ParentSpanId == default ? null : _activity.ParentSpanId.ToHexString(),
                    Source = "Antiphon.Nightly",
                    Name = _activity.DisplayName,
                    Start = _activity.StartTimeUtc,
                    DurationMs = _activity.Duration.TotalMilliseconds,
                    Pid = Environment.ProcessId,
                });
                lock (WriteGate) File.AppendAllText(_path, record + "\n");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("client trace write failed: " + ex.GetType().Name);
            }
        }
    }
}
