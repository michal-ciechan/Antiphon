// The W8 deploy commands (docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C), with the scripts' parameters
// in kebab case. Nothing started any of the scripts by path.
// - deploy-local (unit 49), the desktop production deploy. It replaced scripts/deploy-local.ps1 (the orchestrator's deploy step runs
//   scripts\antiphon.cmd deploy-local). The behaviour is Antiphon.Nightly's DeployLocal on the production host and the real file
//   system. Its restart and verification steps are the restart-apphost and verify-dev-stack commands in this process
//   (AppHostCommands.RestartOn and VerifyOn) on one AppHostOs of the checkout, answering as those commands did when the script
//   started them as children.
// - deploy-am-service (unit 50), the am-service gateway deploy to one explicit SSH target. It replaced scripts/deploy-am-service.ps1.
//   The behaviour is Antiphon.Nightly's AmServiceDeploy on the production host (ssh, scp and tar as children), the real file system,
//   the production HTTP and this console's input for the confirmation.
// - deploy-server2-postgres (unit 54, W8 slice D), the CARD-1406 server2 host Postgres service and scratch restore drill. It replaced
//   scripts/deploy-server2-postgres.ps1. The behaviour is Antiphon.Nightly's Server2PostgresDeploy on the production host (docker,
//   hostname and tailscale as children, found on PATH by OperatorToolOs), the real file system and this machine's POSIX mode check.
// - deploy-server2-server, deploy-server2-staging-server and restart-linux-server (unit 64, W8 slice C and W10), the CARD-1775
//   production server and the CARD-1742 staging server on the server2 host, and the CARD-1745 Linux server restart gate. They replaced
//   scripts/deploy-server2-server.ps1, scripts/deploy-server2-staging-server.ps1 and scripts/restart-linux-server.ps1. The behaviour is
//   Antiphon.Nightly's Server2ServerDeploy, Server2StagingDeploy and LinuxServerRestart on the production host (docker, hostname, git
//   and curl as children, found on PATH by OperatorToolOs), the real file system, this machine's POSIX mode calls and the production
//   HTTP; the production deploy runs the gate in this process.
// The checkout is the one this executable was built in, else the current directory. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DeployCommands
    {
        internal const string DeployLocalName = "deploy-local";

        internal const string DeployAmServiceName = "deploy-am-service";

        internal const string DeployServer2PostgresName = "deploy-server2-postgres";

        internal const string DeployServer2ServerName = "deploy-server2-server";

        internal const string DeployServer2StagingServerName = "deploy-server2-staging-server";

        internal const string RestartLinuxServerName = "restart-linux-server";

        internal const string Usage = DeployLocalOptions.Usage + " | " + AmServiceDeployOptions.Usage + " | " + Server2PostgresDeployOptions.Usage
            + " | " + Server2ServerDeployOptions.Usage + " | " + Server2StagingDeployOptions.Usage + " | " + LinuxServerRestartOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        // Exit 0 deployed (DEPLOY VERDICT: ok), 1 a step failed, 3 refused (a linked or unverifiable worktree; nothing touched);
        // usage 2.
        internal static int RunDeployLocal(string[] args)
        {
            var options = DeployLocalOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            var files = new FileSystem();
            var platform = new AppHostOs(host, files, options.RepoRoot, OperatorCommands.OperatorTokenFile());
            return DeployLocal.Run(options, host, files, new DeployLocalSteps
            {
                RestartAppHost = (restart, stepHost) => AppHostCommands.RestartOn(restart, stepHost, platform),
                VerifyDevStack = (verify, stepHost) => AppHostCommands.VerifyOn(verify, stepHost, platform),
            });
        }

        // Exit 0 REMOTE DEPLOY VERDICT: ok (the preflight, and the deploy when asked and approved), 1 failed or refused; usage 2.
        internal static int RunDeployAmService(string[] args)
        {
            var options = AmServiceDeployOptions.Parse(args.Skip(1).ToArray(), Checkout);
            return AmServiceDeploy.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), new AmServiceDeploySeams
            {
                Http = AmServiceDeploy.Http(),
                ReadLine = Console.In.ReadLine,
            });
        }

        // Exit 0 the plan (the default, --what-if; --restore-drill prints the drill) or ok (--execute on the server2 host daemon), 2
        // refused (one SERVER2-POSTGRES line), 3 ambiguous-mode, 1 an I/O failure; usage 2.
        internal static int RunDeployServer2Postgres(string[] args)
        {
            var options = Server2PostgresDeployOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return Server2PostgresDeploy.Run(options, host, new FileSystem(), new Server2PostgresDeploySeams { Resolve = new OperatorToolOs(host.Variables).Find });
        }

        // Exit 0 the plan (the default, --what-if) or ok (--execute on the server2 host daemon, the verify-only gate passed), 2 refused
        // (one SERVER2-SERVER line), 3 ambiguous-mode, 1 an I/O failure; usage 2.
        internal static int RunDeployServer2Server(string[] args)
        {
            var options = Server2ServerDeployOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return Server2ServerDeploy.Run(options, host, new FileSystem(), new Server2ServerDeploySeams
            {
                Resolve = new OperatorToolOs(host.Variables).Find,
                GateHttp = LinuxServerRestart.Http(),
            });
        }

        // Exit 0 the plan (the default, --what-if) or ok (--execute on the server2 host daemon, accepted), 2 refused (one SERVER2-STAGING
        // line), 3 ambiguous-mode, 1 an I/O failure; usage 2.
        internal static int RunDeployServer2StagingServer(string[] args)
        {
            var options = Server2StagingDeployOptions.Parse(args.Skip(1).ToArray(), Checkout);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return Server2StagingDeploy.Run(options, host, new FileSystem(), new Server2ServerDeploySeams { Resolve = new OperatorToolOs(host.Variables).Find });
        }

        // Exit 0 VERIFIED, 1 a what-if /health that is not 200 (or a terminating error), 3 refused before the probe, 5 unverified
        // (the lock written); usage 2.
        internal static int RunRestartLinuxServer(string[] args)
        {
            var options = LinuxServerRestartOptions.Parse(args.Skip(1).ToArray());
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return LinuxServerRestart.Run(options, host, new FileSystem(), new LinuxServerRestartSeams
            {
                Http = LinuxServerRestart.Http(),
                Resolve = new OperatorToolOs(host.Variables).Find,
            });
        }
    }
}
