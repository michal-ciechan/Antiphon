// The production IDevStackSeams (unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): children with
// literal argv in their directory (stdin from a file, stdout into a file or line by line, stderr line by line, UTF-8), the build
// slot of the caller (BuildSlot.Runner, bound by the CLI), the default browser (shell execute), a terminal tab (Windows Terminal's
// `wt --window 0 new-tab --title <t> -d <dir> -- cmd /k <command>` when wt.exe is on PATH, else a new console window running
// `cmd /k <command>`), the console prompt (the CLI's) and Get-Process -Id's name and path. Tests bind a fake. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The dev stack commands' seams on this machine.</summary>
    public sealed class DevStackOs : IDevStackSeams
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly NightlyHost _host;
        private readonly BuildSlotRunner _buildSlot;
        private readonly Func<string, string> _prompt;

        /// <summary>host gives PATH; buildSlot is the caller's build slot; prompt writes its text and reads a line (null at the end of the input).</summary>
        public DevStackOs(NightlyHost host, BuildSlotRunner buildSlot, Func<string, string> prompt)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _buildSlot = buildSlot ?? throw new ArgumentNullException(nameof(buildSlot));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        }

        /// <inheritdoc />
        public int Run(DevStackLaunch launch, Action<string> line)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (line == null) throw new ArgumentNullException(nameof(line));
            var start = new ProcessStartInfo(launch.FileName)
            {
                UseShellExecute = false,
                WorkingDirectory = launch.WorkingDirectory ?? "",
                RedirectStandardInput = launch.InputFile != null,
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
            if (launch.OutputFile == null) process.OutputDataReceived += (sender, e) => Line(e.Data);
            process.ErrorDataReceived += (sender, e) => Line(e.Data);
            if (!process.Start()) throw new InvalidOperationException("the child did not start: " + launch.FileName);
            process.BeginErrorReadLine();
            Task copy = null;
            if (launch.OutputFile == null)
            {
                process.BeginOutputReadLine();
            }
            else
            {
                copy = Task.Run(() =>
                {
                    using var output = new FileStream(launch.OutputFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                    process.StandardOutput.BaseStream.CopyTo(output);
                });
            }
            if (launch.InputFile != null)
            {
                try
                {
                    using var input = new FileStream(launch.InputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    input.CopyTo(process.StandardInput.BaseStream);
                }
                catch (IOException)
                {
                    // The child closed its input early (it failed); its exit code says so.
                }
                finally
                {
                    try { process.StandardInput.Close(); }
                    catch (IOException) { }
                }
            }
            copy?.GetAwaiter().GetResult();
            process.WaitForExit();
            return process.ExitCode;
        }

        /// <inheritdoc />
        public int BuildSlot(BuildSlotOptions options, TextWriter capture) => _buildSlot(options, capture);

        /// <inheritdoc />
        public void OpenBrowser(string url)
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <inheritdoc />
        public void OpenTab(string title, string workingDirectory, IReadOnlyList<string> command)
        {
            var terminal = OnPath("wt.exe");
            if (terminal != null)
            {
                var start = new ProcessStartInfo(terminal) { UseShellExecute = false };
                // --window 0: a tab in the most recently used Windows Terminal window.
                foreach (var argument in new[] { "--window", "0", "new-tab", "--title", title, "-d", workingDirectory, "--", "cmd", "/k" }) start.ArgumentList.Add(argument);
                foreach (var argument in command) start.ArgumentList.Add(argument);
                using var tab = Process.Start(start) ?? throw new InvalidOperationException("wt.exe did not start");
                return;
            }
            var window = new ProcessStartInfo("cmd.exe", "/k " + QualificationDetach.CommandLine(command))
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
            };
            using var console = Process.Start(window) ?? throw new InvalidOperationException("cmd.exe did not start");
        }

        /// <inheritdoc />
        public string Prompt(string text) => _prompt(text);

        /// <inheritdoc />
        public DevStackProcess Describe(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                string path = "";
                try { path = process.MainModule?.FileName ?? ""; }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is NotSupportedException) { }
                return new DevStackProcess(process.ProcessName, path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // Get-Command <name>: the first PATH directory that holds it.
        private string OnPath(string name)
        {
            foreach (var directory in (_host.Variables("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string candidate;
                try { candidate = Path.Combine(directory.Trim(), name); }
                catch (ArgumentException) { continue; }
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
