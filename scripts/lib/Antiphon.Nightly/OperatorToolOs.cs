// The tool seam of the W8 operator commands (unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md):
// refresh-server2-github-token (bw and ssh), desktop-slot-provision (git) and desktop-win-phase1-secrets (icacls, chmod and
// stat). Find is Get-Command -CommandType Application: a bare name is looked up in each PATH directory in order (on Windows
// with PATHEXT's startable extensions .com, .exe, .bat and .cmd, so the npm shim bw.cmd starts and never bw.ps1; elsewhere a
// file with an execute bit). Run starts the tool with literal argv, the variables given added to this process's environment
// (a null value removes one), its stdout and stderr read as UTF-8 lines (split as ReadLine splits them), and the input, when
// there is one, written to its stdin as UTF-8 and closed; without input the tool shares this process's stdin. Tests bind a fake.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>
    /// One tool run to its exit: the program (a path, or a bare name looked up on PATH), its literal argv, the variables added to
    /// its environment (null: none; a null value removes one) and the text written to its stdin (null: this process's stdin).
    /// </summary>
    public sealed record OperatorToolCall(string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment = null,
        string Input = null)
    {
        /// <summary>The program and the arguments, space separated.</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>A tool's exit code and its output lines: stdout alone, stderr alone, and both in arrival order ("2&gt;&amp;1").</summary>
    public sealed record OperatorToolResult(int ExitCode, IReadOnlyList<string> OutputLines, IReadOnlyList<string> ErrorLines, IReadOnlyList<string> Lines)
    {
        /// <summary>A result from stdout and stderr lines; Lines is stdout then stderr (a fake's arrival order).</summary>
        public static OperatorToolResult Of(int exitCode, IEnumerable<string> output = null, IEnumerable<string> error = null)
        {
            var outputLines = (output ?? Array.Empty<string>()).ToList();
            var errorLines = (error ?? Array.Empty<string>()).ToList();
            return new OperatorToolResult(exitCode, outputLines, errorLines, outputLines.Concat(errorLines).ToList());
        }
    }

    /// <summary>The operator commands' tools. A test binds a fake.</summary>
    public interface IOperatorToolSeams
    {
        /// <summary>Get-Command &lt;name&gt; -CommandType Application: the first startable file on PATH; null when there is none.</summary>
        string Find(string name);

        /// <summary>Runs the tool to its exit. Throws when it cannot start (as PowerShell's call operator did).</summary>
        OperatorToolResult Run(OperatorToolCall call);
    }

    /// <summary>The operator commands' tools on this machine.</summary>
    public sealed class OperatorToolOs : IOperatorToolSeams
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] Startable = { ".com", ".exe", ".bat", ".cmd" };
        private readonly Func<string, string> _variables;

        /// <summary>variables gives PATH and PATHEXT (the host's).</summary>
        public OperatorToolOs(Func<string, string> variables)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        /// <inheritdoc />
        public string Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.IndexOfAny(new[] { '/', '\\' }) >= 0) return Candidates(Path.GetFullPath(name)).FirstOrDefault(CanStart);
            foreach (var directory in (_variables("PATH") ?? "").Split(Path.PathSeparator))
            {
                var root = directory.Trim().Trim('"');
                if (root.Length == 0 || !Path.IsPathRooted(root)) continue;
                string combined;
                try { combined = Path.Combine(root, name); }
                catch (ArgumentException) { continue; }
                var found = Candidates(combined).FirstOrDefault(CanStart);
                if (found != null) return found;
            }
            return null;
        }

        /// <inheritdoc />
        public OperatorToolResult Run(OperatorToolCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            var start = new ProcessStartInfo(Find(call.FileName) ?? call.FileName)
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
            var output = new List<string>();
            var error = new List<string>();
            var lines = new List<string>();
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => Add(lines, output, e.Data);
            process.ErrorDataReceived += (_, e) => Add(lines, error, e.Data);
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
            lock (lines) return new OperatorToolResult(process.ExitCode, output.ToList(), error.ToList(), lines.ToList());
        }

        private static void Add(List<string> lines, List<string> stream, string line)
        {
            if (line == null) return;
            lock (lines)
            {
                lines.Add(line);
                stream.Add(line);
            }
        }

        // On Windows the name as given when it has a startable extension, then the name with each of PATHEXT's startable
        // extensions; elsewhere the name alone.
        private IEnumerable<string> Candidates(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                yield return path;
                yield break;
            }
            if (Startable.Contains(Path.GetExtension(path).ToLowerInvariant())) yield return path;
            var pathExt = _variables("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathExt)) pathExt = ".COM;.EXE;.BAT;.CMD";
            foreach (var extension in pathExt.Split(';').Select(entry => entry.Trim().ToLowerInvariant()).Where(Startable.Contains).Distinct())
                yield return path + extension;
        }

        // A file that can start: on Windows any file (the extension chose it); elsewhere one with an execute bit.
        private static bool CanStart(string path)
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;
            try
            {
                return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
