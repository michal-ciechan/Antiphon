// The production IReviewEngineSeams (unit 42 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): a tool runs with
// literal argv, its stdout and stderr read as UTF-8 and its stdin written as UTF-8 then closed; a bare name is looked up on PATH
// (in each directory: PATHEXT's .com, .exe, .bat and .cmd on Windows, so the npm shim bw.cmd starts; never a .ps1). A detached
// child, a process's command line and the tree stop are the qualification seams' (QualificationOs: its own hidden console, output in
// its files; ProcessCommandLineInformation; taskkill /PID /T /F), and the build slot the caller's (BuildSlot.Runner, bound by the
// CLI). Tests bind a fake. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>The review engine commands' seams on this machine.</summary>
    public sealed class ReviewEngineOs : IReviewEngineSeams
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] Startable = { ".com", ".exe", ".bat", ".cmd" };
        private readonly IQualificationHost _processes;
        private readonly BuildSlotRunner _buildSlot;
        private readonly Func<string, string> _variables;

        /// <summary>processes starts, reads and stops processes; buildSlot is the caller's build slot; variables gives PATH and PATHEXT.</summary>
        public ReviewEngineOs(IQualificationHost processes, BuildSlotRunner buildSlot, Func<string, string> variables)
        {
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            _buildSlot = buildSlot ?? throw new ArgumentNullException(nameof(buildSlot));
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        /// <inheritdoc />
        public ReviewToolResult Run(ReviewToolCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            var start = new ProcessStartInfo(Resolve(call.FileName))
            {
                UseShellExecute = false,
                RedirectStandardInput = call.Input != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            if (call.Input != null) start.StandardInputEncoding = Utf8NoBom;
            foreach (var argument in call.Arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            foreach (var pair in call.Environment ?? new Dictionary<string, string>())
            {
                if (pair.Value == null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
            }
            var output = new StringBuilder();
            var error = new StringBuilder();
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => Append(output, e.Data);
            process.ErrorDataReceived += (_, e) => Append(error, e.Data);
            if (!process.Start()) throw new InvalidOperationException("cannot start " + call.FileName);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (call.Input != null)
            {
                try
                {
                    process.StandardInput.Write(call.Input);
                }
                catch (IOException)
                {
                    // The tool closed its input early (it failed); its exit code says so.
                }
                finally
                {
                    try { process.StandardInput.Close(); }
                    catch (IOException) { }
                }
            }
            process.WaitForExit();
            lock (output) lock (error) return new ReviewToolResult(process.ExitCode, output.ToString(), error.ToString());
        }

        /// <inheritdoc />
        public int Start(ReviewDetachedStart start)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            using var child = _processes.Start(new QualificationStart(start.FileName, start.Arguments, start.StandardOutput, start.StandardError,
                Environment: start.Environment, WorkingDirectory: start.WorkingDirectory));
            return child.Pid;
        }

        /// <inheritdoc />
        public string CommandLine(int pid) => _processes.CommandLine(pid);

        /// <inheritdoc />
        public void StopTree(int pid) => _processes.StopTree(pid);

        /// <inheritdoc />
        public int BuildSlot(BuildSlotOptions options) => _buildSlot(options, null);

        private static void Append(StringBuilder builder, string line)
        {
            if (line == null) return;
            lock (builder) builder.Append(line).Append('\n');
        }

        // A bare name on Windows: the first PATH directory with <name><ext> for PATHEXT's startable extensions; anything else as given
        // (the process start looks a bare name up itself elsewhere).
        private string Resolve(string name)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(name) || Path.HasExtension(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) return name;
            var pathExt = _variables("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathExt)) pathExt = ".COM;.EXE;.BAT;.CMD";
            var extensions = pathExt.Split(';').Select(extension => extension.Trim().ToLowerInvariant()).Where(Startable.Contains).Distinct().ToList();
            foreach (var directory in (_variables("PATH") ?? "").Split(Path.PathSeparator))
            {
                var root = directory.Trim().Trim('"');
                if (root.Length == 0 || !Path.IsPathRooted(root)) continue;
                foreach (var extension in extensions)
                {
                    string candidate;
                    try { candidate = Path.Combine(root, name + extension); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return name;
        }
    }
}
