// The W8 deploy commands (unit 49 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C): deploy-local, the
// desktop production deploy. It replaced scripts/deploy-local.ps1 (nothing started it by path; the orchestrator's deploy step runs
// scripts\antiphon.cmd deploy-local), with the script's parameters in kebab case. The behaviour is Antiphon.Nightly's DeployLocal
// on the production host and the real file system. Its restart and verification steps are the restart-apphost and
// verify-dev-stack commands in this process (AppHostCommands.RestartOn and VerifyOn) on one AppHostOs of the checkout, answering
// as those commands did when the script started them as children. The checkout is the one this executable was built in, else the
// current directory. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DeployCommands
    {
        internal const string DeployLocalName = "deploy-local";

        internal const string Usage = DeployLocalOptions.Usage;

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
    }
}
