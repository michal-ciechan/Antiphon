// The production world of the live server2 bridge (unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): the
// children scripts/c590-real.ps1 started, with the same argument vectors and streams. A name the script ran with &amp; (ssh, scp, git,
// gh, bw) is found as PowerShell found it: each PATH directory in order with the PATHEXT extensions on Windows (so bw.cmd runs); the
// coordinator's ssh, which the script started with ProcessStartInfo, is found as .NET finds it. Children inherit this console's
// input unless the script piped them text; their errors go to this command's error stream and their output to its output, unless
// the script captured or discarded it. Text piped to ssh ends with this platform's newline, as PowerShell 7 wrote it (CRLF on
// Windows). An environment, when given, is laid over each child's and its PATH finds the executable (the Linux fixtures' shims).
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
    /// <summary>The bridge's real children.</summary>
    public sealed class C590BridgeOs : IC590BridgeWorld
    {
        /// <summary>The ssh options and target of the bridge's case and coordinator children.</summary>
        public static readonly string[] SshOptions =
        {
            "-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "-o", "ServerAliveInterval=30", "-o", "ServerAliveCountMax=240", "mc@server2",
        };

        private readonly TextWriter _out;
        private readonly TextWriter _error;
        private readonly IReadOnlyDictionary<string, string> _environment;

        /// <summary>A world whose children write to output and error; environment (optional) is laid over each child's.</summary>
        public C590BridgeOs(TextWriter output, TextWriter error, IReadOnlyDictionary<string, string> environment = null)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
            _error = error ?? throw new ArgumentNullException(nameof(error));
            _environment = environment;
        }

        private string PathValue => _environment != null && _environment.TryGetValue("PATH", out var path) && path != null ? path : Environment.GetEnvironmentVariable("PATH") ?? "";

        /// <summary>PowerShell's lookup of an application: the first PATH directory holding the name with a PATHEXT extension (Windows) or as is.</summary>
        public string Find(string name)
        {
            if (name.IndexOfAny(new[] { '/', '\\' }) >= 0) return File.Exists(name) ? name : null;
            var extensions = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                var pathExt = (_environment != null && _environment.TryGetValue("PATHEXT", out var ext) ? ext : Environment.GetEnvironmentVariable("PATHEXT")) ?? ".COM;.EXE;.BAT;.CMD";
                if (Path.HasExtension(name)) extensions.Add("");
                extensions.AddRange(pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries));
            }
            else extensions.Add("");
            foreach (var directory in PathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim('"'), name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            return null;
        }

        // The coordinator's ProcessStartInfo("ssh"): with an overlay PATH, its first directory that holds ssh (with .exe on Windows).
        private string NetFind(string name)
        {
            if (_environment == null || !_environment.TryGetValue("PATH", out var path) || path == null) return name;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                foreach (var extension in OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" })
                {
                    var candidate = Path.Combine(directory, name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            return name;
        }

        private ProcessStartInfo Start(string file, IEnumerable<string> arguments, bool input, IReadOnlyDictionary<string, string> extra = null)
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardInput = input,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            foreach (var overlay in new[] { _environment, extra })
            {
                if (overlay == null) continue;
                foreach (var pair in overlay)
                {
                    if (pair.Value == null) start.Environment.Remove(pair.Key);
                    else start.Environment[pair.Key] = pair.Value;
                }
            }
            return start;
        }

        private string Application(string name) =>
            Find(name) ?? throw new C590Failure("The term '" + name + "' is not recognized as a name of a cmdlet, function, script file, or executable program.");

        // Runs a child to its end: each output line to onOutput, each error line to onError (null: discarded); the exit code.
        private int Run(ProcessStartInfo start, Action<string> onOutput, Action<string> onError, string input = null)
        {
            using var process = new Process { StartInfo = start };
            var gate = new object();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) onOutput?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) onError?.Invoke(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (input != null)
            {
                using (var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) stdin.Write(input);
            }
            process.WaitForExit();
            return process.ExitCode;
        }

        /// <inheritdoc />
        public int Ssh(string remote, Action<string> line) =>
            Run(Start(Application("ssh"), SshOptions.Concat(new[] { remote }), input: false), line, line);

        /// <inheritdoc />
        public int SshWithInput(string remote, string input) =>
            Run(Start(Application("ssh"), new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "mc@server2", remote }, input: true), null, _error.WriteLine,
                (input ?? "") + Environment.NewLine);

        /// <inheritdoc />
        public int Scp(IReadOnlyList<string> arguments, bool discardOutput) =>
            Run(Start(Application("scp"), arguments, input: false), discardOutput ? null : _out.WriteLine, _error.WriteLine);

        /// <inheritdoc />
        public string GitHead(string repo)
        {
            var lines = new List<string>();
            Run(Start(Application("git"), new[] { "-C", repo, "rev-parse", "HEAD" }, input: false), lines.Add, _error.WriteLine);
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        // Out-String: each line and a newline.
        private static string OutString(IEnumerable<string> lines) => string.Concat(lines.Select(l => l + Environment.NewLine));

        /// <inheritdoc />
        public string GhToken()
        {
            var lines = new List<string>();
            Run(Start(Application("gh"), new[] { "auth", "token" }, input: false), lines.Add, _error.WriteLine);
            return OutString(lines);
        }

        /// <inheritdoc />
        public bool HasBw() => Find("bw") != null;

        /// <inheritdoc />
        public (int Exit, string Output) BwPassword(string item, string session)
        {
            var lines = new List<string>();
            var exit = Run(Start(Application("bw"), new[] { "get", "password", item, "--nointeraction" }, input: false,
                new Dictionary<string, string> { ["BW_SESSION"] = session }), lines.Add, null);
            return (exit, OutString(lines));
        }

        /// <inheritdoc />
        public IC1609Duplex StartCoordinator(string remote, string workingDirectory)
        {
            var start = Start(NetFind("ssh"), SshOptions.Concat(new[] { remote }), input: true);
            if (!string.IsNullOrEmpty(workingDirectory)) start.WorkingDirectory = workingDirectory;
            var process = new Process { StartInfo = start };
            try
            {
                process.Start();
            }
            catch (Exception)
            {
                process.Dispose();
                throw;
            }
            process.StandardInput.NewLine = "\n";
            return new ProcessDuplex(process);
        }

        private sealed class ProcessDuplex : IC1609Duplex
        {
            private readonly Process _process;

            public ProcessDuplex(Process process) => _process = process;

            public TextReader Stdout => _process.StandardOutput;

            public TextReader Stderr => _process.StandardError;

            public TextWriter Stdin => _process.StandardInput;

            public bool HasExited => _process.HasExited;

            public int ExitCode => _process.ExitCode;

            public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

            public void Kill() => _process.Kill(true);

            public void Dispose() => _process.Dispose();
        }
    }
}
