// The routing matrix and orchestrator workspace commands (unit 26 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// wave W7): complexity-chain, migrate-stage-pins-to-cells and orchestrator-workspace. Each replaced the scripts/<name>.ps1 of
// the same name (deleted 2026-11-23), with the same parameters in kebab case and the verb (and the orchestrator workspace's
// target) first, as the scripts took them. The behaviour is Antiphon.Nightly's (ComplexityChainCli, StagePinMigration,
// OrchestratorWorkspace) on the production host, the real file system and an HTTP transport; this file reads the
// environment and passes it in: ANTIPHON_API (default http://localhost:17202), ANTIPHON_TASK_TOKEN and USERPROFILE (else
// the user profile folder; the orchestrator workspace's ~/.claude.json). It binds the workspace's junction seam (mklink /J
// on the host's launcher, on Windows only). Tokens are sent as headers and never printed. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RoutingWorkspaceCommands
    {
        internal const string ComplexityChain = "complexity-chain";
        internal const string MigrateStagePins = "migrate-stage-pins-to-cells";
        internal const string OrchestratorWorkspace = "orchestrator-workspace";

        internal const string Usage = ComplexityChainCliOptions.Usage + " | " + StagePinMigrationOptions.Usage + " | " + OrchestratorWorkspaceOptions.Usage;

        private static string Api => Environment.GetEnvironmentVariable("ANTIPHON_API");

        private static string TaskToken => Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");

        private static NightlyHost Host() => NightlyHost.Production(Console.Out, Console.Error);

        // CARD-0090: exit 0 done; 1 a refused value, a missing reason file or a failed request (a partial All: its status line).
        internal static int RunComplexityChain(string[] args) =>
            ComplexityChainCli.Run(ComplexityChainCliOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(),
                NightlyReport.HttpTransport(timeout: BoardCli.Deadline));

        // CARD-0097 S-E: exit 0 previewed, applied or restored; 1 refused or a failed request.
        internal static int RunMigrateStagePins(string[] args) =>
            StagePinMigration.Run(StagePinMigrationOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken), Host(), new FileSystem(),
                NightlyReport.HttpTransport(timeout: StagePinMigration.Deadline));

        // CARD-0251: exit 0 done; 1 an unresolvable project, no checkout, an unreadable marker or ~/.claude.json, or a failed request.
        internal static int RunOrchestratorWorkspace(string[] args)
        {
            var options = OrchestratorWorkspaceOptions.Parse(args.Skip(1).ToArray(), Api, TaskToken, UserProfile());
            var host = Host();
            return Antiphon.Nightly.OrchestratorWorkspace.Run(options, host, new FileSystem(), NightlyReport.HttpTransport(timeout: BoardCli.Deadline),
                OperatingSystem.IsWindows() ? Antiphon.Nightly.OrchestratorWorkspace.Mklink(host) : null);
        }

        // USERPROFILE, else the user profile folder (where the Claude CLI keeps .claude.json).
        private static string UserProfile()
        {
            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            return string.IsNullOrEmpty(profile) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : profile;
        }
    }
}
