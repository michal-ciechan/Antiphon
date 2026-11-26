// The W8 operator tools (unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C):
// refresh-server2-github-token (CARD-0817), desktop-slot-provision (CARD-0824 S2) and desktop-win-phase1-secrets (CARD-1744
// DW-3). Each replaced the scripts/<name>.ps1 of the same name, with the script's parameters in kebab case; nothing started
// them by path (the operator runs them by hand from docs/agent-credentials.md, docs/superpowers/plans/2026-11-19-desktop-hot-
// worktree-slots-plan.md and the CARD-1744 runbook), so none keeps a launcher. The behaviour is Antiphon.Nightly's
// (GithubTokenRefresh, DesktopSlotProvision, DesktopWinPhase1Secrets) on the production host, the real file system,
// OperatorToolOs (bw, ssh, git, icacls, chmod and stat with literal argv) and, for the phase 1 secrets, DesktopWinAclOs. This
// file passes in the home directory (~ for .bw-session, PowerShell's $HOME) and the checkout's scripts directory (the phase 1
// example). No secret is ever an argument or printed. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class OperatorToolCommands
    {
        internal const string RefreshServer2GithubToken = "refresh-server2-github-token";
        internal const string DesktopSlotProvisionName = "desktop-slot-provision";
        internal const string DesktopWinPhase1SecretsName = "desktop-win-phase1-secrets";

        internal const string Usage = GithubTokenRefreshOptions.Usage + " | " + DesktopSlotProvisionOptions.Usage + " | " + DesktopWinPhase1Options.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // CARD-0817: exit 0 refreshed (two metadata lines), 1 not confirmed, 2 skipped (no relay session, bw or ssh, or token); usage 2.
        internal static int RunRefreshServer2GithubToken(string[] args)
        {
            var options = GithubTokenRefreshOptions.Parse(args.Skip(1).ToArray(), Home);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return GithubTokenRefresh.Run(options, host, new FileSystem(), new OperatorToolOs(host.Variables));
        }

        // CARD-0824 S2: exit 0 done or the dry run (the default), 2 refused; usage 2.
        internal static int RunDesktopSlotProvision(string[] args)
        {
            var options = DesktopSlotProvisionOptions.Parse(args.Skip(1).ToArray());
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return DesktopSlotProvision.Run(options, host, new FileSystem(), new DesktopSlotSeams { Tools = new OperatorToolOs(host.Variables) });
        }

        // CARD-1744 DW-3: exit 0 done or the dry run (the default), 2 refused (REFUSED: <code>), 1 an unexpected error before the
        // apply; usage 2.
        internal static int RunDesktopWinPhase1Secrets(string[] args)
        {
            var options = DesktopWinPhase1Options.Parse(args.Skip(1).ToArray(), Home, Path.Combine(Checkout, "scripts"));
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return DesktopWinPhase1Secrets.Run(options, host, new FileSystem(), new DesktopWinSecretSeams
            {
                Tools = new OperatorToolOs(host.Variables),
                Acl = new DesktopWinAclOs(),
            });
        }
    }
}
