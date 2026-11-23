// The operator API commands (units 21 and 23 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): host-load,
// runner-drain, workspace-slots, herdr-pane, worktree-residue, runner-slots and keep-six. Each replaced the scripts/<name>.ps1
// of the same name (deleted 2026-11-23), with the same flags in kebab case and the verb first where the script took it first.
// The behaviour is Antiphon.Nightly's (HostLoad, RunnerDrain, WorkspaceSlots, HerdrPane, WorktreeResidue, RunnerSlots,
// KeepSix) on the production host and an HTTP transport; this file reads the environment and passes it in: ANTIPHON_API
// (default http://localhost:17202), ANTIPHON_TASK_TOKEN, ANTIPHON_TASK_ID (the workspace-slots requester) and
// ANTIPHON_OPERATOR_TOKEN_FILE (else the server's default operator token path). Tokens are sent as headers and never printed.
// ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class OperatorCommands
    {
        internal const string HostLoad = "host-load";
        internal const string RunnerDrain = "runner-drain";
        internal const string WorkspaceSlots = "workspace-slots";
        internal const string HerdrPane = "herdr-pane";
        internal const string WorktreeResidue = "worktree-residue";
        internal const string RunnerSlots = "runner-slots";
        internal const string KeepSix = "keep-six";

        internal const string Usage = HostLoadOptions.Usage + " | " + RunnerDrainOptions.Usage + " | " + WorkspaceSlotsOptions.Usage + " | " + HerdrPaneOptions.Usage
            + " | " + WorktreeResidueOptions.Usage + " | " + RunnerSlotsOptions.Usage + " | " + KeepSixOptions.Usage;

        private static string Api => Environment.GetEnvironmentVariable("ANTIPHON_API");

        private static string TaskToken => Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");

        // CARD-1514 S1: exit 0 read; 1 a failed read, placement or validation (one fixed stderr line, no stdout).
        internal static int RunHostLoad(string[] args) =>
            Antiphon.Nightly.HostLoad.Run(HostLoadOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), NightlyHost.Production(Console.Out, Console.Error),
                Antiphon.Nightly.HostLoad.Transport());

        // CARD-0727: exit 0 done; 1 not eligible, failed or no token file; 2 usage.
        internal static int RunRunnerDrain(string[] args) =>
            Antiphon.Nightly.RunnerDrain.Run(RunnerDrainOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, OperatorTokenFile()),
                NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.RunnerDrain.Deadline));

        // CARD-0824 S1, CARD-1960: exit 0 every runner answered (release: removed); 1 a call failed (release: refused or
        // removal_failed); 2 usage or an unreadable token file.
        internal static int RunWorkspaceSlots(string[] args) =>
            Antiphon.Nightly.WorkspaceSlots.Run(WorkspaceSlotsOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, OperatorTokenFile(),
                Environment.GetEnvironmentVariable("ANTIPHON_TASK_ID")), NightlyHost.Production(Console.Out, Console.Error), new FileSystem(),
                NightlyReport.HttpTransport(timeout: Antiphon.Nightly.WorkspaceSlots.Deadline));

        // CARD-0461: exit 0 answered (or the dispose dry run); 1 the request failed (its error printed); 2 usage.
        internal static int RunHerdrPane(string[] args) =>
            Antiphon.Nightly.HerdrPane.Run(HerdrPaneOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), NightlyHost.Production(Console.Out, Console.Error),
                new FileSystem(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.HerdrPane.Deadline));

        // CARD-0459: exit 0 done; 1 a request or a batch item failed; 2 usage. The task token is --token, as it was -Token.
        internal static int RunWorktreeResidue(string[] args) =>
            Antiphon.Nightly.WorktreeResidue.Run(WorktreeResidueOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error),
                new FileSystem(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.WorktreeResidue.Deadline));

        // CARD-0653: exit 0 answered; 1 a failed request or no token file; 2 usage.
        internal static int RunRunnerSlots(string[] args) =>
            Antiphon.Nightly.RunnerSlots.Run(RunnerSlotsOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, OperatorTokenFile()),
                NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.RunnerSlots.Deadline));

        // CARD-1320: exit 0 answered; 1 a refused value, no task token (hold, clear, ack) or a failed request; 2 usage.
        internal static int RunKeepSix(string[] args) =>
            Antiphon.Nightly.KeepSix.Run(KeepSixOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), NightlyHost.Production(Console.Out, Console.Error),
                NightlyReport.HttpTransport(timeout: Antiphon.Nightly.KeepSix.Deadline));

        // ANTIPHON_OPERATOR_TOKEN_FILE, else where the server creates the token (OperatorTokenFile.DefaultPath): on Windows
        // %LOCALAPPDATA%\Antiphon\operator-token, elsewhere $XDG_DATA_HOME (default ~/.local/share)/antiphon/operator-token.
        internal static string OperatorTokenFile()
        {
            var configured = Environment.GetEnvironmentVariable("ANTIPHON_OPERATOR_TOKEN_FILE");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antiphon", "operator-token");
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome)) dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(dataHome, "antiphon", "operator-token");
        }
    }
}
