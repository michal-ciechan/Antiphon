// The desktop dev stack's commands (unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): dev-aspire and
// its detached AppHost wrapper run-apphost, dev-start, dev-stop, dev-fresh, dev-backup, dev-restore, dev-upgrade,
// restart-dev-stack, restart-server, restart-client, stop-server and assert-dev-ports-clear. Each replaced the body of the
// repository root's script of the same name (restart-dev-stack: restart.ps1), which stays a thin launcher where the runbooks
// name it. The behaviour is Antiphon.Nightly's (DevAspire, DevStack, DevRestart) on the production host, AppHostOs and
// DevStackOs (the build slot in this process, BuildSlotCommands.Runner; the confirmation prompt on this console); this file
// binds them. restart-dev-stack runs restart-session-runner and verify-dev-stack in process. The checkout is the one this
// executable was built in, else the current directory. Every command runs on Windows only (the desktop stack: its ports,
// processes, Docker Desktop and terminals): elsewhere one stderr line and exit 1, after the flags are read. The read-only
// bootstrap-check (unit 38; it replaced scripts/bootstrap-check.ps1) runs anywhere: BootstrapCheck on BootstrapCheckOs, its
// agent context and verify-dev-stack --skip-browser (on AppHostOs) in this process. ASCII only.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DevStackCommands
    {
        internal const string DevAspireName = "dev-aspire";
        internal const string RunAppHost = "run-apphost";
        internal const string DevStart = "dev-start";
        internal const string DevStop = "dev-stop";
        internal const string DevFresh = "dev-fresh";
        internal const string DevBackup = "dev-backup";
        internal const string DevRestore = "dev-restore";
        internal const string DevUpgrade = "dev-upgrade";
        internal const string RestartDevStack = "restart-dev-stack";
        internal const string RestartServer = "restart-server";
        internal const string RestartClient = "restart-client";
        internal const string StopServer = "stop-server";
        internal const string AssertDevPortsClear = "assert-dev-ports-clear";
        internal const string BootstrapCheckName = "bootstrap-check";

        internal const string Usage = DevAspireOptions.Usage + " | " + AppHostRunOptions.Usage + " | " + DevStartOptions.Usage + " | " + DevStopOptions.Usage
            + " | " + DevFreshOptions.Usage + " | " + DevBackupOptions.Usage + " | " + DevRestoreOptions.Usage + " | " + DevUpgradeOptions.Usage
            + " | " + DevRestartOptions.Usage + " | " + DevServerRestartOptions.ServerUsage + " | " + DevServerRestartOptions.ClientUsage
            + " | " + DevServerRestartOptions.StopUsage + " | " + DevPortsOptions.Usage + " | " + BootstrapCheckOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        /// <summary>The production seams on the host: the build slot in this process, the prompt on this console.</summary>
        internal static IDevStackSeams Seams(NightlyHost host) => new DevStackOs(host, BuildSlotCommands.Runner(), Prompt);

        /// <summary>dev-aspire on the platform, for an in-process caller (autostart-apphost).</summary>
        internal static DevAspireRunner DevAspireOn(IAppHostPlatform platform) => (options, host) => DevAspire.Run(options, host, platform, Seams(host));

        // Read-Host: the prompt, then one line of this console's input (null at its end).
        private static string Prompt(string text)
        {
            Console.Write(text + ": ");
            return Console.ReadLine();
        }

        private static (NightlyHost Host, AppHostOs Platform) Production(string root)
        {
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return (host, new AppHostOs(host, new FileSystem(), root, OperatorCommands.OperatorTokenFile()));
        }

        // Exit 0 launched, 1 failed, 3 refused; usage 2.
        internal static int RunDevAspire(string[] args)
        {
            var options = DevAspireOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(DevAspireName)) return 1;
            var (host, platform) = Production(options.RepoRoot);
            return Guarded(DevAspireName, () => DevAspire.Run(options, host, platform, Seams(host)));
        }

        // The build's exit code when it failed, else the AppHost's; usage 2.
        internal static int RunRunAppHost(string[] args)
        {
            var options = AppHostRunOptions.Parse(args.Skip(1).ToArray(), Checkout);
            if (!WindowsOnly(RunAppHost)) return 1;
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return Guarded(RunAppHost, () => DevAspire.RunAppHost(options, host, new FileSystem(), Seams(host)));
        }

        // Exit 0 started, 1 failed; usage 2.
        internal static int RunDevStart(string[] args)
        {
            var options = DevStartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevStart, options.RepoRoot, (host, platform) => DevStack.Start(options, host, platform, Seams(host)));
        }

        // Exit 0; usage 2.
        internal static int RunDevStop(string[] args)
        {
            var options = DevStopOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevStop, options.RepoRoot, (host, platform) => DevStack.Stop(options, host, platform));
        }

        // Exit 0 done or aborted, 1 failed; usage 2.
        internal static int RunDevFresh(string[] args)
        {
            var options = DevFreshOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevFresh, options.RepoRoot, (host, platform) => DevStack.Fresh(options, host, platform, Seams(host)));
        }

        // Exit 0 backed up (the zip's path last), 1 failed; usage 2.
        internal static int RunDevBackup(string[] args)
        {
            var options = DevBackupOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevBackup, options.RepoRoot, (host, platform) => DevStack.Backup(options, host, platform, Seams(host)));
        }

        // Exit 0 restored or aborted, 1 failed; usage 2.
        internal static int RunDevRestore(string[] args)
        {
            var options = DevRestoreOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevRestore, options.RepoRoot, (host, platform) => DevStack.Restore(options, host, platform, Seams(host)));
        }

        // Exit 0 upgraded, 1 failed; usage 2.
        internal static int RunDevUpgrade(string[] args)
        {
            var options = DevUpgradeOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(DevUpgrade, options.RepoRoot, (host, platform) => DevStack.Upgrade(options, host, platform, Seams(host)));
        }

        // Exit 0 restarted, 1 a step failed; usage 2.
        internal static int RunRestartDevStack(string[] args)
        {
            var options = DevRestartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var root = options.RepoRoot;
            return Simple(RestartDevStack, root, (host, platform) => DevRestart.Restart(options, host, platform, Seams(host), new DevRestartSteps
            {
                RestartSessionRunner = stepHost => RunnerRestart.Run(RunnerRestartOptions.Parse(Array.Empty<string>(), root), stepHost,
                    new RunnerRestartOs(root, stepHost, new FileSystem(), Stopwatch.StartNew())),
                VerifyDevStack = stepHost => DevStackVerify.Run(DevStackVerifyOptions.Parse(Array.Empty<string>(), root), stepHost, platform),
            }));
        }

        // Exit 0 restarted, 1 a step failed; usage 2.
        internal static int RunRestartServer(string[] args)
        {
            var options = DevServerRestartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(RestartServer, options.RepoRoot, (host, platform) => DevRestart.RestartServer(options, host, platform, Seams(host)));
        }

        // Exit 0 restarted, 1 a step failed; usage 2.
        internal static int RunRestartClient(string[] args)
        {
            var options = DevServerRestartOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return Simple(RestartClient, options.RepoRoot, (host, platform) => DevRestart.RestartClient(options, host, platform, Seams(host)));
        }

        // Exit 0; usage 2.
        internal static int RunStopServer(string[] args)
        {
            var options = DevServerRestartOptions.Parse(args.Skip(1).ToArray(), Checkout, stopOnly: true);
            return Simple(StopServer, options.RepoRoot, (host, platform) => DevRestart.StopServer(options, host, Seams(host)));
        }

        // Exit 0 clear, 1 a listener remains; usage 2.
        internal static int RunAssertDevPortsClear(string[] args)
        {
            var options = DevPortsOptions.Parse(args.Skip(1).ToArray());
            return Simple(AssertDevPortsClear, Checkout, (host, platform) => DevRestart.AssertPortsClear(options, host, platform, Seams(host)));
        }

        // Exit the number of FAILs (0 when every check passed or only warned); usage 2.
        internal static int RunBootstrapCheck(string[] args)
        {
            var options = BootstrapCheckOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            var files = new FileSystem();
            var platform = new BootstrapCheckOs(host, files, stackHost => DevStackVerify.Run(DevStackVerifyOptions.Parse(new[] { "--skip-browser" }, options.RepoRoot),
                stackHost, new AppHostOs(stackHost, files, options.RepoRoot, OperatorCommands.OperatorTokenFile())));
            return BootstrapCheck.Run(options, host, platform);
        }

        private static int Simple(string command, string root, Func<NightlyHost, AppHostOs, int> run)
        {
            if (!WindowsOnly(command)) return 1;
            var (host, platform) = Production(root);
            return Guarded(command, () => run(host, platform));
        }

        private static bool WindowsOnly(string command)
        {
            if (OperatingSystem.IsWindows()) return true;
            Console.Error.WriteLine(command + ": the desktop dev stack runs on Windows only (its ports, processes, Docker Desktop and terminals).");
            return false;
        }

        // A failure outside the command's own answers (a file, a process that cannot start) is one stderr line and exit 1.
        private static int Guarded(string command, Func<int> run)
        {
            try
            {
                return run();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is PlatformNotSupportedException
                || e is System.ComponentModel.Win32Exception || e is ArgumentException || e is System.IO.InvalidDataException)
            {
                Console.Error.WriteLine(command + ": " + e.Message);
                return 1;
            }
        }
    }
}
