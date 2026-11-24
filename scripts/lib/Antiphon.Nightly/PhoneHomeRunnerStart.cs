// CARD-1744: a native Windows phone-home session runner in the foreground (a laptop or a second Windows host; unit 36 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced scripts/start-windows-phone-home-runner.ps1,
// which nothing started by path). The CLI runs it as
//
//   antiphon-scripts start-windows-phone-home-runner --runner-id <id> [--server-origin https://antiphon.desktop.codeperf.net]
//       [--root C:\Antiphon\runner] [--port 17214] [--capacity 2] [--no-build]
//
// The runner connects OUT to the desktop server (POST /api/session-runners/register, then a WebSocket to
// /api/session-runners/<id>/connect) and opens no inbound port beyond its local /health and /build-slots on --port. Every
// runner path lives under --root in drive-rooted, forward-slash form (C:/Antiphon/runner/work), the spelling the runner
// reports back and the desktop's PhoneHomeRunner:Runners:<id> entry must use (printed before the start). The shared secret is
// <root>\secrets\phone-home; claude, codex and grok are resolved from PATH once and passed as PhoneHome:Executables. Herdr is
// off. Without --no-build the runner is built first under a build slot, stamped with HEAD (an unreadable HEAD refuses); with
// it, an executable without a source stamp starts after a warning. The runner then runs in the foreground with the
// WindowsPhoneHome.RunnerEnvironment variables until Ctrl+C, and its exit code is this command's. A refusal is one line and
// exit 1 (a reserved id, not Windows, no secret, a failed build, no executable, no pwsh). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The start-windows-phone-home-runner command's options (the script's parameters).</summary>
    public sealed class PhoneHomeRunnerStartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "start-windows-phone-home-runner --runner-id <id> [--server-origin https://antiphon.desktop.codeperf.net] [--root C:\\Antiphon\\runner]"
            + " [--port 17214] [--capacity 2] [--no-build]";

        /// <summary>The desktop server the runner phones home to.</summary>
        public const string DefaultServerOrigin = "https://antiphon.desktop.codeperf.net";

        /// <summary>The runner id (PhoneHomeRunner:Runners:&lt;id&gt; on the desktop).</summary>
        public string RunnerId { get; init; } = "";

        /// <summary>The server origin.</summary>
        public string ServerOrigin { get; init; } = DefaultServerOrigin;

        /// <summary>The runner root.</summary>
        public string Root { get; init; } = WindowsPhoneHome.DefaultRoot;

        /// <summary>The local port (/health, /build-slots).</summary>
        public int Port { get; init; } = WindowsPhoneHome.SecondPort;

        /// <summary>The runner's capacity.</summary>
        public int Capacity { get; init; } = 2;

        /// <summary>Start the built executable without building it.</summary>
        public bool NoBuild { get; init; }

        /// <summary>The checkout whose runner is built and started.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI. --runner-id is required.</summary>
        public static PhoneHomeRunnerStartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--runner-id", "--server-origin", "--root", "--port", "--capacity" }, new[] { "--no-build" });
            return new PhoneHomeRunnerStartOptions
            {
                RunnerId = flags.Required("--runner-id"),
                ServerOrigin = flags.Value("--server-origin", DefaultServerOrigin),
                Root = flags.Value("--root", WindowsPhoneHome.DefaultRoot),
                Port = flags.Int("--port", WindowsPhoneHome.SecondPort),
                Capacity = flags.Int("--capacity", 2),
                NoBuild = flags.Switch("--no-build"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The runner's foreground start: the executable, its arguments, its working directory and the added variables.</summary>
    public sealed record PhoneHomeRunnerProcess(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory,
        IReadOnlyList<KeyValuePair<string, string>> Environment);

    /// <summary>
    /// The machine the start reads and runs on: the file system, whether it is Windows, Get-Command -CommandType Application
    /// (null when none), the user's home, the build slot, an assembly's ProductVersion (null when unreadable) and the
    /// foreground run of the runner (its exit code).
    /// </summary>
    public sealed record PhoneHomeRunnerMachine(IFileSystem Files, bool IsWindows, Func<string, string> FindApplication, string HomeDirectory,
        BuildSlotRunner BuildSlot, Func<string, string> ProductVersion, Func<PhoneHomeRunnerProcess, int> RunForeground);

    /// <summary>The foreground phone-home runner (start-windows-phone-home-runner.ps1's body).</summary>
    public static class PhoneHomeRunnerStart
    {
        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Stamped = new Regex(@"\+[0-9a-f]{40}(?:$|[.+])", RegexOptions.CultureInvariant);

        private sealed class Refusal : Exception
        {
            public Refusal(string message) : base(message) { }
        }

        /// <summary>The runner's exit code; 1 after one stderr line when the start is refused.</summary>
        public static int Run(PhoneHomeRunnerStartOptions options, NightlyHost host, PhoneHomeRunnerMachine machine)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            try
            {
                return Start(options, host, machine);
            }
            catch (Refusal refusal)
            {
                host.Error.WriteLine("start-windows-phone-home-runner: " + refusal.Message);
                return 1;
            }
        }

        private static int Start(PhoneHomeRunnerStartOptions options, NightlyHost host, PhoneHomeRunnerMachine machine)
        {
            var files = machine.Files;
            var id = options.RunnerId ?? "";
            if (id.Equals("local", StringComparison.OrdinalIgnoreCase) || id.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                throw new Refusal("RunnerId '" + id + "' is reserved for the desktop runner.");
            if (!machine.IsWindows) throw new Refusal("This command starts a native Windows runner; use docker-compose.server2-runner.yml on Linux.");

            var project = files.Path.Combine(options.RepoRoot, "src", "Antiphon.SessionRunner");
            var exe = files.Path.Combine(project, "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe");
            var rootSlash = WindowsPhoneHome.Slash(files.Path.GetFullPath(options.Root));
            var work = rootSlash + "/work";
            var state = rootSlash + "/state";
            var secret = files.Path.Combine(options.Root, "secrets", "phone-home");
            foreach (var directory in new[] { work, work + "/repos", state, state + "/session-runner", state + "/logs", state + "/pty-hosts", files.Path.GetDirectoryName(secret) })
                files.Directory.CreateDirectory(directory);
            if (!files.File.Exists(secret) && !files.Directory.Exists(secret))
                throw new Refusal("Missing shared secret file " + secret + ". Write the value of PhoneHomeRunner:Runners:" + id + ":SharedSecret into it.");

            var executables = new List<KeyValuePair<string, string>>();
            foreach (var name in SessionRunnerAutostart.Agents)
            {
                var found = machine.FindApplication(name);
                if (found != null) executables.Add(new KeyValuePair<string, string>(name, found));
            }
            string Home(string variable, string child)
            {
                var value = host.Variables(variable);
                return string.IsNullOrEmpty(value) ? files.Path.Combine(machine.HomeDirectory ?? "", child) : value;
            }
            var claudeHome = Home("CLAUDE_CONFIG_DIR", ".claude");
            var codexHome = Home("CODEX_HOME", ".codex");
            var grokHome = Home("GROK_HOME", ".grok");

            if (!options.NoBuild)
            {
                string revision;
                try
                {
                    var head = host.RunGit(options.RepoRoot, "rev-parse", "HEAD");
                    revision = head.ExitCode == 0 ? (head.Output ?? "").Trim().ToLowerInvariant() : "";
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    revision = "";
                }
                if (!Sha.IsMatch(revision)) throw new Refusal("git rev-parse HEAD failed; refusing an unstamped runner build.");
                var build = machine.BuildSlot(new BuildSlotOptions
                {
                    Label = "phone-home-runner-" + id,
                    Command = new[] { "dotnet", "build", project, "-nologo", "-v:q", "-p:SourceRevisionId=" + revision },
                }, null);
                if (build != 0) throw new Refusal("Runner build failed (" + build.ToString(CultureInfo.InvariantCulture) + ").");
            }
            if (!files.File.Exists(exe)) throw new Refusal("Runner not built: " + exe);
            if (options.NoBuild)
            {
                var version = machine.ProductVersion(files.Path.ChangeExtension(exe, ".dll"));
                if (version == null || !Stamped.IsMatch(version))
                    host.Out.WriteLine("WARNING: --no-build is launching a runner with an unknown source stamp. Start without --no-build to build from checkout HEAD.");
            }

            var pwsh = machine.FindApplication("pwsh") ?? throw new Refusal("pwsh is not on PATH (the runner's raw-exe allow list names it).");
            var environment = WindowsPhoneHome.RunnerEnvironment(new WindowsPhoneHomeRunner(id, options.Root, options.Port, options.Capacity, options.ServerOrigin,
                claudeHome, codexHome, grokHome, pwsh, executables));

            var port = options.Port.ToString(CultureInfo.InvariantCulture);
            host.Out.WriteLine("Phone-home runner '" + id + "' -> " + options.ServerOrigin + " (local http://localhost:" + port + ")");
            host.Out.WriteLine("Desktop entry PhoneHomeRunner:Runners:" + id + " must use:");
            host.Out.WriteLine("  RunnerWorkspace  = " + work);
            host.Out.WriteLine("  RunnerRepository = " + work + "/repos/antiphon");
            host.Out.WriteLine("  ChildClaudeHome  = " + claudeHome);
            host.Out.WriteLine("  ChildCodexHome   = " + codexHome);
            host.Out.WriteLine("  ChildGrokHome    = " + grokHome);
            host.Out.WriteLine("  RawExeAllowList  = " + pwsh);
            foreach (var executable in executables) host.Out.WriteLine("  executable " + executable.Key + " = " + executable.Value);
            host.Out.Flush();
            return machine.RunForeground(new PhoneHomeRunnerProcess(exe, new[] { "--urls", "http://localhost:" + port }, project, environment));
        }

        /// <summary>The production ProductVersion of an assembly: its file version information; null when it cannot be read.</summary>
        public static string ProductVersion(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).ProductVersion; }
            catch (Exception e) when (e is FileNotFoundException || e is IOException || e is ArgumentException) { return null; }
        }

        /// <summary>
        /// The production foreground run: the executable with its arguments in its directory, this process's console and
        /// streams, the variables added to this process's environment; its exit code once it ends.
        /// </summary>
        public static int RunProcess(PhoneHomeRunnerProcess start)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            var info = new ProcessStartInfo(start.FileName) { UseShellExecute = false, WorkingDirectory = start.WorkingDirectory ?? "" };
            foreach (var argument in start.Arguments ?? Array.Empty<string>()) info.ArgumentList.Add(argument);
            foreach (var pair in start.Environment ?? Array.Empty<KeyValuePair<string, string>>()) info.Environment[pair.Key] = pair.Value;
            using var process = Process.Start(info) ?? throw new InvalidOperationException("the runner did not start: " + start.FileName);
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
