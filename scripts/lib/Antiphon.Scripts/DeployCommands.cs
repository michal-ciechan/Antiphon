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

        internal const string Usage = DeployLocalOptions.Usage + " | " + AmServiceDeployOptions.Usage + " | " + Server2PostgresDeployOptions.Usage;

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
    }
}
