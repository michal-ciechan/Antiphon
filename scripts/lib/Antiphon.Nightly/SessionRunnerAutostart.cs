// CARD-1744 S4: the logon auto-start of the desktop session runner (http://localhost:17204; unit 36 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced the body of scripts/autostart-session-runner.ps1,
// which stays a thin launcher because the per-user Scheduled Task "Antiphon Session Runner" (the install-autostart command)
// starts it by path at logon, and the phase 1 runner clone (C:\Antiphon\runner\app) is started the same way by hand. The CLI
// runs it as
//
//   antiphon-scripts autostart-session-runner [--phone-home-config-path <file>] [--supervisor-pid <pid>]
//
// The launcher passes its own PID as --supervisor-pid: that process is the supervisor this command records in
// logs/session-runner.supervisor.pid (the AppHost adopts and stops it there, restart-session-runner stops its tree), the
// startup records and the runner name it, and supervision ends once it is gone (DaemonSupervisor's rule for the run-daemon
// launcher, unit 30). The supervisor runs in this process: no run-daemon launcher or second CLI in between.
//
// The plan is WindowsPhoneHome.AutostartPlan: no configuration file (C:\Antiphon\runner\phone-home.json, or the explicit
// path) is the direct runner; a present one must parse (at the default path only port 17204; an explicit path, the phase 1
// file, may name 17214), its root's secrets\phone-home must exist, and the phone-home variables then go to the runner's
// environment only. A refused, unreadable or unloadable configuration starts nothing: one "[autostart] phone-home config
// refused: <reason>" line in logs/session-runner.log and exit 2. Otherwise the desired state is forced to running (a dashboard
// Stop wrote stopped), the supervisor PID is recorded, and the supervisor builds and launches the BUILT executable (never
// `dotnet run`, whose kill-on-close job would capture the detached pty-hosts) until the desired state stops it: exit 0.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>The autostart-session-runner command's options (autostart-session-runner.ps1 -PhoneHomeConfigPath, and the launcher's PID).</summary>
    public sealed class SessionRunnerAutostartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "autostart-session-runner [--phone-home-config-path <file>] [--supervisor-pid <pid>]";

        /// <summary>The phone-home configuration (default C:\Antiphon\runner\phone-home.json).</summary>
        public string ConfigPath { get; init; } = WindowsPhoneHome.DefaultConfigPath;

        /// <summary>The path was given (the phase 1 file may then name port 17214).</summary>
        public bool ExplicitConfigPath { get; init; }

        /// <summary>The supervisor the records name (the launcher); null: this process.</summary>
        public int? SupervisorPid { get; init; }

        /// <summary>The checkout whose logs/ and src/Antiphon.SessionRunner are used.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI. A supervisor PID below 1 is a usage error.</summary>
        public static SessionRunnerAutostartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--phone-home-config-path", "--supervisor-pid" }, Array.Empty<string>());
            int? supervisor = null;
            if (flags.Has("--supervisor-pid"))
            {
                supervisor = flags.Int("--supervisor-pid", 0);
                if (supervisor <= 0) throw new NightlyUsageException("--supervisor-pid expects a process id above 0");
            }
            var given = flags.Has("--phone-home-config-path");
            return new SessionRunnerAutostartOptions
            {
                ConfigPath = given ? flags.Value("--phone-home-config-path", "") : WindowsPhoneHome.DefaultConfigPath,
                ExplicitConfigPath = given,
                SupervisorPid = supervisor,
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>
    /// The machine the autostart reads and starts on: the file system, Get-Command -CommandType Application (a name's first
    /// match on PATH, null when none), the user's home ($HOME), the service starter and the build slot of the supervisor.
    /// </summary>
    public sealed record SessionRunnerAutostartMachine(IFileSystem Files, Func<string, string> FindApplication, string HomeDirectory,
        DaemonServiceStarter StartService, BuildSlotRunner BuildSlot);

    /// <summary>The logon auto-start (autostart-session-runner.ps1's body).</summary>
    public static class SessionRunnerAutostart
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>The service name the AppHost and the restart know the runner by (logs/session-runner.*).</summary>
        public const string ServiceName = "session-runner";

        /// <summary>The agents whose executables the phone-home runner is handed (PhoneHome__Executables__&lt;name&gt;).</summary>
        public static readonly IReadOnlyList<string> Agents = new[] { "claude", "codex", "grok" };

        /// <summary>Exit 2 after one refusal line when the configuration is refused or unreadable; otherwise the supervisor's exit (0).</summary>
        public static int Run(SessionRunnerAutostartOptions options, NightlyHost host, SessionRunnerAutostartMachine machine)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            var files = machine.Files;
            var root = options.RepoRoot;
            var logDir = files.Path.Combine(root, "logs");
            files.Directory.CreateDirectory(logDir);
            var stateFile = files.Path.Combine(logDir, ServiceName + ".state");
            var supervisorPidFile = files.Path.Combine(logDir, ServiceName + ".supervisor.pid");
            var servicePidFile = files.Path.Combine(logDir, ServiceName + ".service.pid");
            var logFile = files.Path.Combine(logDir, ServiceName + ".log");
            var runnerDir = files.Path.Combine(root, "src", "Antiphon.SessionRunner");

            void Append(string line)
            {
                try { files.File.AppendAllText(logFile, line + Environment.NewLine, Utf8); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { /* Add-Content -ErrorAction SilentlyContinue */ }
            }
            string Stamp() => host.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int Refuse(string reason)
            {
                Append(Stamp() + " [autostart] phone-home config refused: " + reason);
                return 2;
            }

            // Test-Path: a file or a directory is present; a directory then fails the read (config-unreadable).
            var configPath = options.ConfigPath ?? "";
            var present = configPath.Length > 0 && (files.File.Exists(configPath) || files.Directory.Exists(configPath));
            string configText = null;
            var secretPresent = true;
            if (present)
            {
                try
                {
                    if (files.Directory.Exists(configPath)) throw new UnauthorizedAccessException("the configuration path is a directory");
                    configText = files.File.ReadAllText(configPath);
                    var parsed = WindowsPhoneHome.ParseConfig(configText, options.ExplicitConfigPath ? 0 : WindowsPhoneHome.BrokerPort);
                    if (parsed.Ok)
                    {
                        var secret = WindowsPhoneHome.ChildPath(parsed.Root, WindowsPhoneHome.SecretChild);
                        secretPresent = files.File.Exists(secret) || files.Directory.Exists(secret);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
                {
                    return Refuse("config-unreadable");
                }
            }

            var pwshPath = machine.FindApplication("pwsh") ?? "";
            var executables = new List<KeyValuePair<string, string>>();
            foreach (var name in Agents)
            {
                var found = machine.FindApplication(name);
                if (found != null) executables.Add(new KeyValuePair<string, string>(name, found));
            }
            var plan = WindowsPhoneHome.AutostartPlan(configText, options.ExplicitConfigPath, secretPresent, machine.HomeDirectory, pwshPath, executables, root);
            if (plan.Mode == SessionRunnerAutostartMode.Refused) return Refuse(plan.Reason);

            // A prior dashboard Stop wrote stopped, which would end supervision at once: every logon forces running.
            files.File.WriteAllText(stateFile, "running" + Environment.NewLine, Utf8);
            // The recorded supervisor: the launcher (the task's process), which the AppHost tracks, restarts and stops.
            var supervisor = options.SupervisorPid ?? host.Processes.CurrentPid;
            files.File.WriteAllText(supervisorPidFile, supervisor.ToString(CultureInfo.InvariantCulture) + Environment.NewLine, Utf8);
            Append(Stamp() + " [autostart] logon supervisor starting (PID " + supervisor.ToString(CultureInfo.InvariantCulture) + ")");

            var daemon = new DaemonSupervisorOptions
            {
                Name = ServiceName,
                WorkDir = runnerDir,
                Exe = files.Path.Combine(runnerDir, "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe"),
                ExeArgs = plan.ExeArgs,
                LogFile = logFile,
                ServicePidFile = servicePidFile,
                StateFile = stateFile,
                BuildProjectDir = runnerDir,
                SupervisorPid = supervisor,
                ServiceEnvironment = plan.Environment,
            };
            return DaemonSupervisor.Run(daemon, host, files, machine.StartService, machine.BuildSlot);
        }
    }
}
