// The SessionRunner restart chain's commands (unit 30 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md):
// restart-session-runner (it replaced scripts/restart-session-runner.ps1 and scripts/session-runner-restart-health.ps1),
// run-daemon (the body of scripts/run-daemon.ps1, which the AppHost and the logon task still start by path: a thin launcher)
// and check-daemon-build (the body of scripts/check-daemon-build.ps1, which restart-apphost.ps1 and dev-aspire.ps1 still run
// by path: a thin launcher). The behaviour is Antiphon.Nightly's RunnerRestart (on RunnerRestartOs), DaemonSupervisor and
// DaemonBuildCheck on the production host; this file binds them (run-daemon's build runs under the build slot in this
// process, BuildSlotCommands.Runner). Each command's checkout (the runner's logs and Debug executable, the history compared)
// is the one this executable was built in, else the current directory. ASCII only.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RunnerDaemonCommands
    {
        internal const string RestartSessionRunner = "restart-session-runner";
        internal const string RunDaemon = "run-daemon";
        internal const string CheckDaemonBuild = "check-daemon-build";

        internal const string Usage = RunnerRestartOptions.Usage + " | " + DaemonSupervisorOptions.Usage + " | " + DaemonBuildCheckOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        // Exit 0 healthy; 2 wait-expired; 1 action-failed or wait-failed (the result line last on stdout), or not Windows (one
        // stderr line); usage 2. The command clock starts here, so the result's commandElapsedMs covers the setup.
        internal static int RunRestartSessionRunner(string[] args)
        {
            var clock = Stopwatch.StartNew();
            var root = Checkout;
            var options = RunnerRestartOptions.Parse(args.Skip(1).ToArray(), root);
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("restart-session-runner: the desktop runner restart runs on Windows only (taskkill, the logon task, the TCP owner table).");
                return 1;
            }
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return RunnerRestart.Run(options, host, new RunnerRestartOs(root, host, new FileSystem(), clock));
        }

        // Exit 0 once supervision ends (the desired state stopped it, or the named supervisor is gone); usage 2.
        internal static int RunRunDaemon(string[] args)
        {
            var options = DaemonSupervisorOptions.Parse(args.Skip(1).ToArray());
            return DaemonSupervisor.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), DaemonSupervisor.StartService,
                BuildSlotCommands.Runner());
        }

        // Exit 0, or 1 with --fail-on-stale when a daemon is stale; usage 2.
        internal static int RunCheckDaemonBuild(string[] args) =>
            DaemonBuildCheck.Run(DaemonBuildCheckOptions.Parse(args.Skip(1).ToArray(), Checkout), NightlyHost.Production(Console.Out, Console.Error),
                NightlyReport.HttpTransport(timeout: DaemonBuildCheck.Deadline));
    }
}
