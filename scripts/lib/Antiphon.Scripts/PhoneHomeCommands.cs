// The Windows phone-home runner and the autostart installers' commands (unit 36 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): autostart-session-runner (the body of
// scripts/autostart-session-runner.ps1, which the logon task "Antiphon Session Runner" still starts by path: a thin launcher),
// start-windows-phone-home-runner, verify-phone-home-grok, install-autostart and install-cleanup-task (their scripts are
// deleted). The behaviour is Antiphon.Nightly's (SessionRunnerAutostart, PhoneHomeRunnerStart, PhoneHomeGrokVerify,
// AutostartInstall on the Task Scheduler's ScheduledTaskRegistry) on the production host; this file binds them. Each
// command's checkout is the one this executable was built in, else the current directory. ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PhoneHomeCommands
    {
        internal const string AutostartSessionRunner = "autostart-session-runner";
        internal const string StartWindowsPhoneHomeRunner = "start-windows-phone-home-runner";
        internal const string VerifyPhoneHomeGrok = "verify-phone-home-grok";
        internal const string InstallAutostart = "install-autostart";
        internal const string InstallCleanupTask = "install-cleanup-task";

        internal const string Usage = SessionRunnerAutostartOptions.Usage + " | " + PhoneHomeRunnerStartOptions.Usage + " | " + PhoneHomeGrokVerifyOptions.Usage
            + " | " + AutostartInstallOptions.Usage + " | " + CleanupTaskInstallOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Exit 2 refused (one line in logs/session-runner.log); else 0 once supervision ends; usage 2.
        internal static int RunAutostartSessionRunner(string[] args)
        {
            var options = SessionRunnerAutostartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return SessionRunnerAutostart.Run(options, NightlyHost.Production(Console.Out, Console.Error),
                new SessionRunnerAutostartMachine(new FileSystem(), OwnedLauncher.FindApplication, Home, DaemonSupervisor.StartService, BuildSlotCommands.Runner()));
        }

        // The runner's exit code; 1 refused (one stderr line); usage 2. Ctrl+C reaches the runner, and this process waits for it.
        internal static int RunStartWindowsPhoneHomeRunner(string[] args)
        {
            var options = PhoneHomeRunnerStartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            Console.CancelKeyPress += (sender, e) => e.Cancel = true;
            return PhoneHomeRunnerStart.Run(options, NightlyHost.Production(Console.Out, Console.Error),
                new PhoneHomeRunnerMachine(new FileSystem(), OperatingSystem.IsWindows(), OwnedLauncher.FindApplication, Home, BuildSlotCommands.Runner(),
                    PhoneHomeRunnerStart.ProductVersion, PhoneHomeRunnerStart.RunProcess));
        }

        // Exit 0 done, 2 blockers remain, 1 a failure (one stderr line); usage 2.
        internal static int RunVerifyPhoneHomeGrok(string[] args)
        {
            var options = PhoneHomeGrokVerifyOptions.Parse(args.Skip(1).ToArray(), Checkout, Environment.CurrentDirectory);
            return PhoneHomeGrokVerify.Run(options, NightlyHost.Production(Console.Out, Console.Error), new PhoneHomeGrokVerify.Machine(new FileSystem()));
        }

        // Exit 0 done; 1 a failure or not Windows (one stderr line); usage 2.
        internal static int RunInstallAutostart(string[] args)
        {
            var options = AutostartInstallOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(InstallAutostart)) return 1;
            return AutostartInstall.Run(options, NightlyHost.Production(Console.Out, Console.Error), Machine());
        }

        // Exit 0 done; 1 a failure or not Windows (one stderr line); usage 2.
        internal static int RunInstallCleanupTask(string[] args)
        {
            var options = CleanupTaskInstallOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(InstallCleanupTask)) return 1;
            return AutostartInstall.RunCleanupTask(options, NightlyHost.Production(Console.Out, Console.Error), Machine());
        }

        private static AutostartInstallMachine Machine()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("the Task Scheduler is a Windows service");
            return new AutostartInstallMachine(new FileSystem(), new ScheduledTaskRegistry(), Environment.GetEnvironmentVariable, OwnedLauncher.FindApplication, Docker);
        }

        // docker with the arguments; captured output is read and dropped (docker info's), otherwise it is this console's.
        private static int Docker(IReadOnlyList<string> arguments, bool capture)
        {
            var start = new System.Diagnostics.ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = capture, RedirectStandardError = capture };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            try
            {
                using var process = System.Diagnostics.Process.Start(start);
                if (process == null) return -1;
                if (capture)
                {
                    var error = process.StandardError.ReadToEndAsync();
                    process.StandardOutput.ReadToEnd();
                    error.Wait();
                }
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // docker is not installed: the same answer as a daemon that is down.
                return -1;
            }
        }

        private static bool WindowsOnly(string command)
        {
            if (OperatingSystem.IsWindows()) return true;
            Console.Error.WriteLine(command + ": the per-user Scheduled Tasks are a Windows service.");
            return false;
        }
    }
}
