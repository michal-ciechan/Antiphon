// The desktop AppHost chain's commands (unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md):
// restart-apphost, watchdog-apphost, set-apphost-maintenance, apphost-watchdog-state-observer, autostart-apphost and
// verify-dev-stack. Each replaced the body of the script of the same name (scripts/<name>.ps1, verify-dev-stack.ps1 at the
// repository root), which stays a thin launcher where a Scheduled Task or a kept script starts it by path. The behaviour is
// Antiphon.Nightly's (AppHostRestart, AppHostWatchdog, AppHostMaintenance, AppHostWatchdogStateObserver, AppHostAutostart,
// DevStackVerify) on the production host and AppHostOs; this file binds them. The checkout is the one this executable was
// built in, else the current directory; the graceful stop's operator token file is ANTIPHON_OPERATOR_TOKEN_FILE, else the
// server's default path (the token is never printed). autostart-apphost runs dev-aspire in its process (DevStackCommands,
// unit 37). ASCII only.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class AppHostCommands
    {
        internal const string RestartAppHost = "restart-apphost";
        internal const string WatchdogAppHost = "watchdog-apphost";
        internal const string SetAppHostMaintenance = "set-apphost-maintenance";
        internal const string WatchdogStateObserver = "apphost-watchdog-state-observer";
        internal const string AutostartAppHost = "autostart-apphost";
        internal const string VerifyDevStack = "verify-dev-stack";

        internal const string Usage = AppHostRestartOptions.Usage + " | " + AppHostWatchdogOptions.Usage + " | " + AppHostMaintenanceOptions.Usage
            + " | " + AppHostObserverOptions.Usage + " | " + AppHostAutostartOptions.Usage + " | " + DevStackVerifyOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        // The production host and platform of the checkout.
        private static (NightlyHost Host, AppHostOs Platform) Production(string root)
        {
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return (host, new AppHostOs(host, new FileSystem(), root, OperatorCommands.OperatorTokenFile()));
        }

        // Exit 0 restarted (or --stop-only done), 1 failed, 3 refused, 4 DCP dependency timeout, 5 server build unverified; 1 off
        // Windows (one stderr line); usage 2.
        internal static int RunRestart(string[] args)
        {
            var options = AppHostRestartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(RestartAppHost)) return 1;
            var (host, platform) = Production(options.RepoRoot);
            return AppHostRestart.Run(options, host, platform);
        }

        // Exit 0 (every skip and every restart outcome), 1 an unreadable state file or off Windows; usage 2.
        internal static int RunWatchdog(string[] args)
        {
            var options = AppHostWatchdogOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(WatchdogAppHost)) return 1;
            var (host, platform) = Production(options.RepoRoot);
            return Guarded(WatchdogAppHost, () => AppHostWatchdog.Run(options, host, platform, () => AppHostWatchdog.CaptureRestart(options.RepoRoot, host, platform)));
        }

        // Exit 0 done, 2 --clear with --enter-if-absent or the marker already present (--enter-if-absent), 1 a task change failed; usage 2.
        internal static int RunMaintenance(string[] args)
        {
            var options = AppHostMaintenanceOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, platform) = Production(options.Root);
            return Guarded(SetAppHostMaintenance, () => AppHostMaintenance.Run(options, host, platform));
        }

        // Exit 0 sampled; 1 the document could not be written; usage 2.
        internal static int RunObserver(string[] args)
        {
            var options = AppHostObserverOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, platform) = Production(options.Root);
            return Guarded(WatchdogStateObserver, () => AppHostWatchdogStateObserver.Run(options, host, platform));
        }

        // Exit 0 healthy or already up, 1 Docker or health did not come up, or off Windows; usage 2.
        internal static int RunAutostart(string[] args)
        {
            var options = AppHostAutostartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(AutostartAppHost)) return 1;
            var (host, platform) = Production(options.RepoRoot);
            return Guarded(AutostartAppHost, () => AppHostAutostart.Run(options, host, platform, DevStackCommands.DevAspireOn(platform)));
        }

        // Exit 0 verified (or the port list printed), 1 a core check failed or full Aspire mode off Windows; usage 2.
        internal static int RunVerifyDevStack(string[] args)
        {
            var options = DevStackVerifyOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var (host, platform) = Production(options.RepoRoot);
            return Guarded(VerifyDevStack, () => DevStackVerify.Run(options, host, platform));
        }

        private static bool WindowsOnly(string command)
        {
            if (OperatingSystem.IsWindows()) return true;
            Console.Error.WriteLine(command + ": the desktop AppHost runs on Windows only (its ports, processes and Scheduled Tasks).");
            return false;
        }

        // A failure outside the command's own answers (a file, a task change) is one stderr line and exit 1.
        private static int Guarded(string command, Func<int> run)
        {
            try
            {
                return run();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is PlatformNotSupportedException
                || e is System.ComponentModel.Win32Exception || e is ArgumentException)
            {
                Console.Error.WriteLine(command + ": " + e.Message);
                return 1;
            }
        }
    }
}
