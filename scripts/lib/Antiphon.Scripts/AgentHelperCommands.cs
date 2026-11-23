// The agent and operator helpers (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, wave W7):
// check-agent-context, task-commit, new-review-scratch, remove-review-scratch, trx-failures, watch-master, watch-tasks,
// stage-value-report, logs, hangfire-dashboard and github-sync. Each replaced the scripts/<name>.ps1 of the same name (the
// three watchers were scripts/orchestration/*.ps1; deleted 2026-11-23, except scripts/github-sync.ps1, which stays as the
// thin launcher Windmill's u/lndcobra/antiphon_github_sync starts by path), with the same parameters in kebab case. The
// behaviour is Antiphon.Nightly's (AgentContextCheck, TaskCommit, ReviewScratch, TrxFailures, WatchMaster, WatchTasks,
// StageValueReport, AntiphonLogs, HangfireDashboard, GithubSync) on the production host, the real file system and an HTTP
// transport. This file reads the environment and passes it in: ANTIPHON_API (default http://localhost:17202),
// ANTIPHON_TASK_TOKEN, ANTIPHON_TASK_ID (task-commit), ANTIPHON_OPERATOR_TOKEN_FILE (else the server's default path), the
// system temp path, the working directory and the checkout the CLI was built in. It binds the browser seam of
// hangfire-dashboard (the default browser, on Windows only). Tokens are sent as headers and never printed. ASCII only.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class AgentHelperCommands
    {
        internal const string CheckAgentContext = "check-agent-context";
        internal const string TaskCommit = "task-commit";
        internal const string NewReviewScratch = "new-review-scratch";
        internal const string RemoveReviewScratch = "remove-review-scratch";
        internal const string TrxFailures = "trx-failures";
        internal const string WatchMaster = "watch-master";
        internal const string WatchTasks = "watch-tasks";
        internal const string StageValueReport = "stage-value-report";
        internal const string Logs = "logs";
        internal const string HangfireDashboard = "hangfire-dashboard";
        internal const string GithubSync = "github-sync";

        internal const string Usage = AgentContextCheckOptions.Usage + " | " + TaskCommitOptions.Usage + " | " + NewReviewScratchOptions.Usage
            + " | " + RemoveReviewScratchOptions.Usage + " | " + Antiphon.Nightly.TrxFailures.Usage + " | " + WatchMasterOptions.Usage + " | " + WatchTasksOptions.Usage
            + " | " + StageValueReportOptions.Usage + " | " + AntiphonLogsOptions.Usage + " | " + HangfireDashboardOptions.Usage + " | " + GithubSyncOptions.Usage;

        private static string Api => Environment.GetEnvironmentVariable("ANTIPHON_API");

        private static string TaskToken => Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // CARD-0254: exit 0 within the target; 1 over it, over the ceiling, or AGENTS.md missing, not UTF-8 or without sections.
        internal static int RunCheckAgentContext(string[] args) =>
            AgentContextCheck.Run(AgentContextCheckOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()), Host(), new FileSystem());

        // CARD-0527: exit 0 committed; 1 no token or task id, no message file, a refusal or a pending inspection (one stderr line).
        internal static int RunTaskCommit(string[] args) =>
            Antiphon.Nightly.TaskCommit.Run(TaskCommitOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, Environment.GetEnvironmentVariable("ANTIPHON_TASK_ID")),
                Host(), new FileSystem(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.TaskCommit.Deadline));

        // CARD-1896: exit 0 created (its path printed); 1 refused.
        internal static int RunNewReviewScratch(string[] args) =>
            ReviewScratch.New(NewReviewScratchOptions.Parse(args.Skip(1).ToArray(), Path.GetTempPath()), Host(), new FileSystem());

        // CARD-1981: exit 0 removed; 1 refused (the container kept). git never prompts for credentials here.
        internal static int RunRemoveReviewScratch(string[] args)
        {
            var options = RemoveReviewScratchOptions.Parse(args.Skip(1).ToArray(), Path.GetTempPath());
            Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");
            return ReviewScratch.Remove(options, Host(), new FileSystem());
        }

        // A diagnostic: exit 0 whatever the files hold.
        internal static int RunTrxFailures(string[] args) =>
            Antiphon.Nightly.TrxFailures.Run(Antiphon.Nightly.TrxFailures.Parse(args.Skip(1).ToArray()), Host(), new FileSystem());

        // Exit 0 when master moved or the window ended.
        internal static int RunWatchMaster(string[] args) =>
            Antiphon.Nightly.WatchMaster.Run(WatchMasterOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()), Host());

        // Exit 0 when the window ended; 1 an unusable state file.
        internal static int RunWatchTasks(string[] args) =>
            Antiphon.Nightly.WatchTasks.Run(WatchTasksOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(),
                NightlyReport.HttpTransport(timeout: Antiphon.Nightly.WatchTasks.Deadline));

        // CARD-0272: exit 0 printed; 1 a failed request.
        internal static int RunStageValueReport(string[] args) =>
            Antiphon.Nightly.StageValueReport.Run(StageValueReportOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, Environment.CurrentDirectory), Host(),
                NightlyReport.HttpTransport(timeout: BoardCli.Deadline));

        // Exit 0 printed; 1 a missing log, no live session, a failed request or ssh.
        internal static int RunLogs(string[] args) =>
            AntiphonLogs.Run(AntiphonLogsOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root(), Path.GetTempPath(), Api, TaskToken,
                OperatorCommands.OperatorTokenFile()), Host(), new FileSystem(), NightlyReport.HttpTransport(timeout: AntiphonLogs.Deadline));

        // CARD-0658: exit 0 opened; 1 refused or failed; 2 not Windows.
        internal static int RunHangfireDashboard(string[] args) =>
            Antiphon.Nightly.HangfireDashboard.Run(HangfireDashboardOptions.Parse(args.Skip(1).ToArray(), Api, OperatorCommands.OperatorTokenFile()), Host(), new FileSystem(),
                NightlyReport.HttpTransport(timeout: Antiphon.Nightly.HangfireDashboard.Deadline), OperatingSystem.IsWindows() ? new Action<string>(OpenDefaultBrowser) : null);

        // CARD-0171: exit 0 every board synced cleanly; 1 the request failed or a board reported an error.
        internal static int RunGithubSync(string[] args) =>
            Antiphon.Nightly.GithubSync.Run(GithubSyncOptions.Parse(args.Skip(1).ToArray()), Host(), NightlyReport.HttpTransport(timeout: Antiphon.Nightly.GithubSync.Deadline));

        // The shell opens the address in the default browser (Start-Process on a URL).
        private static void OpenDefaultBrowser(string address)
        {
            using var browser = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
    }
}
