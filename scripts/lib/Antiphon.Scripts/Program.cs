// antiphon-scripts <command> [arguments]. Every command answers every input itself: exit 0 handled (the command wrote
// its answer), 2 usage, and each command's own refusal codes below. The NotHandled exit (10) that sent a caller back to
// its PowerShell path went with select-release, the last command that used it (2026-11-22 plan, unit 8).
// Exit 9 is reserved for the launchers (scripts/antiphon, scripts/antiphon.cmd, scripts/lib/antiphon-cli.ps1): the
// CLI could not be built or started.
using System;
using System.Collections.Generic;
using System.Linq;
using Antiphon.Checkpoints;

namespace Antiphon.Scripts
{
    public static class Program
    {
        public const int Handled = 0;
        public const int Usage = 2;

        // The commands by name (ordinal). A handler gets the whole argv, the name included, and answers with the exit
        // code. An unknown name, or no name, prints the usage line and exits 2.
        private static readonly IReadOnlyDictionary<string, Func<string[], int>> Commands = new Dictionary<string, Func<string[], int>>(StringComparer.Ordinal)
        {
            // The evidence scan answers every input itself (exit 0 or 2).
            ["live-lane-evidence"] = LiveLaneEvidenceCommand,
            // The rc ledger commands answer every input themselves: exit 0 done, 1 refused (one stderr line), 2 usage.
            ["rc-ledger-stamp"] = RcLedgerStamp,
            ["rc-ledger-import"] = RcLedgerCommand,
            ["rc-ledger-plan"] = RcLedgerCommand,
            ["rc-ledger-status"] = RcLedgerCommand,
            ["rc-input-census"] = RcLedgerCommand,
            ["rc-input-owner-path-census"] = RcLedgerCommand,
            // The post-land early warning answers every input itself: exit 0/1 outcome, 2 refused or usage, 3 not Linux.
            [PostLandCommand.Name] = PostLandCommand.Run,
            // The evidence policy answers every input itself: exit 0 compliant, 1 violations, 2 unverifiable or usage.
            [EvidenceCommand.Diff] = EvidenceCommand.Run,
            [EvidenceCommand.Deletion] = EvidenceCommand.Run,
            [NightlyProbeCommand.Name] = NightlyProbeCommand.Run,
            // The nightly readiness evaluator answers every input itself: exit 0 healthy, 1 unhealthy, 2 usage.
            [NightlyHealthCommand.Name] = NightlyHealthCommand.Run,
            // The nightly run card answers every input itself: exit 0 done, 1 summary missing or unreadable, 2 usage, 3 board unreachable.
            [NightlyReportCommand.Name] = NightlyReportCommand.Run,
            // The nightly tests run answers every input itself: exit 0 green or WhatIf, 1 failed, 2 refused before the run, 3 refusal.
            [NightlyTestsCommand.Name] = NightlyTestsCommand.Run,
            // The nightly run answers every input itself, its completion record the last stdout line: exit 0 green or WhatIf,
            // 1 failed, 2 usage, 3 refusal, otherwise the tests or report exit.
            [NightlyRunCommand.Name] = NightlyRunCommand.Run,
            // The phase-timing summary answers every input itself: exit 0 written, 1 failed (one stderr line), 2 usage.
            [SummarizeTestPhasesCommand.Name] = SummarizeTestPhasesCommand.Run,
            // The failure-history export answers every input itself: exit 0 written, 2 refused (the error record on stdout) or usage.
            [Antiphon.Nightly.NightlySeedCommands.ExportPriorFailures] = NightlySeedCommand.Run,
            // The stopped-run seed export answers every input itself: exit 0 written, 1 refused (one stderr line), 2 usage.
            [Antiphon.Nightly.NightlySeedCommands.ExportStoppedRunSeed] = NightlySeedCommand.Run,
            // The seed merge takes the operator's argv: exit 0 merged, 2 refused, 3 merged under -Strict with an identity unplaced or open.
            [Antiphon.Nightly.NightlySeedCommands.MergeSeeds] = NightlySeedCommand.Run,
            // The toolchain floor warnings answer every input themselves: exit 0 whatever the warnings, 2 usage.
            [ToolchainFloorCommand.Name] = ToolchainFloorCommand.Run,
            // The timing report answers every input itself: exit 0 printed, 1 the selection cannot be read (one stderr line), 2 usage.
            [TestTimingReportCommand.Name] = TestTimingReportCommand.Run,
            // The timing journal writer answers every input itself: exit 0 written or already there, 1 refused (one stderr line), 2 usage.
            [TestTimingEventCommand.Name] = TestTimingEventCommand.Run,
            // The change-aware selection proposal answers every input itself: exit 0 proposed, 2 refused (one stderr line) or usage.
            [TestSelectionCommand.Name] = TestSelectionCommand.Run,
            // The impacted-classes selector answers every input itself: exit 0 selected, 1 the table, catalog or index refused
            // or the audit failed, 2 usage, git failed or a release-evidence refusal.
            [ImpactedClassesCommand.Name] = ImpactedClassesCommand.Run,
            // The qualification ETA answers every input itself: exit 0 printed, 1 refused or unreadable (one stderr line), 2 usage.
            [QualificationEtaCommand.Name] = QualificationEtaCommand.Run,
            // The qualification watcher answers every input itself: exit 0 the window or the run ended, 1 the state file cannot be
            // read (one stderr line), 2 usage.
            [WatchQualCommand.Name] = WatchQualCommand.Run,
            // The expected-duration roster export answers every input itself: exit 0 written, 1 refused (one stderr line), 2 usage.
            [ExportExpectedDurationRosterCommand.Name] = ExportExpectedDurationRosterCommand.Run,
            // The test trace summary answers every input itself: exit 0 printed, 1 unreadable or no span (one stderr line), 2 usage.
            [SummarizeTestTraceCommand.Name] = SummarizeTestTraceCommand.Run,
            // The class-cost refresh answers every input itself: exit 0 written, 1 refused (one stderr line), 2 usage.
            [RefreshNightlyClassCostsCommand.Name] = RefreshNightlyClassCostsCommand.Run,
            // The away digest send answers every input itself: exit 0 sent, 1 refused or send_failed (one stderr line), 2 usage.
            [DigestCommand.Name] = DigestCommand.Run,
            // The rc qualification status answers every input itself: exit 0 a coherent observation, 2 incomplete, invalid or usage.
            [RcQualificationStatusCommand.Name] = RcQualificationStatusCommand.Run,
            // The supervisor set answers every input itself. observe: exit 0 completed/running/supervisor-lost-run-alive, 3 run-lost,
            // 2 otherwise; preflight: 0 or 2 longpaths-not-enabled; launch: 1 unreadable record, 2 refused, 3 a live run.lock, else
            // build-slot's code; start-supervisor: 0 started, 1 refused or failed; supervise: 0, 1 after SUPERVISOR-ERROR; stop: 0
            // stopped, 2 dry run or refused, 3 incomplete, 4 lock retained, 5 not Windows. Usage is 2 for each.
            [QualificationObserveCommand.Name] = QualificationObserveCommand.Run,
            [QualificationPreflightCommand.Name] = QualificationPreflightCommand.Run,
            [QualificationLaunchCommand.Name] = QualificationLaunchCommand.Run,
            [QualificationStartSupervisorCommand.Name] = QualificationStartSupervisorCommand.Run,
            [QualificationSuperviseCommand.Name] = QualificationSuperviseCommand.Run,
            [QualificationStopCommand.Name] = QualificationStopCommand.Run,
            // The status snapshot answers every input itself: exit 0 every fact read, 2 partial facts or usage.
            [StatusSnapshotCommand.Name] = StatusSnapshotCommand.Run,
            // The live-lane gate catalog answers every input itself: exit 0 written, 1 refused (one stderr line), 2 usage.
            ["live-lane-scan-gates"] = LiveLaneScanGates,
            // The live-lane roster, bindings and fixture answer every input themselves: exit 0 done (the roster: its
            // result line; 1 when there was no discovery), 1 refused (one stderr line), 2 usage.
            ["live-lane-roster"] = LiveLaneRosterCommand,
            ["live-lane-bind-manifest"] = LiveLaneBindManifest,
            ["live-lane-write-fixture"] = LiveLaneWriteFixture,
            // The live-lane census (the CP-30 row) answers every input itself: exit 0 accepted, 1 a witness, a refusal case or
            // residue failed (or refused: one stderr line), 2 no context or the census refused. The policy hash: exit 0
            // printed, 1 refused, 2 usage. The S1a group: the checkpoint tool's exit code, 1 refused, 2 usage.
            ["live-lane-census"] = LiveLaneCensusCommand,
            ["live-lane-emit-policy-hash"] = LiveLaneEmitPolicyHash,
            ["live-lane-group"] = LiveLaneGroup,
            // The rc project batches answer every input themselves: exit 0 proposed, 2 refused (one stderr line) or usage.
            [SelectRcProjectBatchesCommand.Name] = SelectRcProjectBatchesCommand.Run,
            // The nightly watchdog preflight or deploy answers every input itself: exit 0 done, 1 failed (one stderr line),
            // 2 usage, 3 no or invalid deploy profile (nothing contacted).
            [DeployNightlyWatchdogCommand.Name] = DeployNightlyWatchdogCommand.Run,
            // The release status answers every input itself: exit 0 (its state on the JSON line), 2 usage.
            [ReleaseStatusCommand.Name] = ReleaseStatusCommand.Run,
            // The release-gate registration answers every input itself: exit 0 done, 1 a write or readback failed, 2 usage, 3 refused.
            [RegisterReleaseGatesCommand.Name] = RegisterReleaseGatesCommand.Run,
            // The release candidate answers every input itself: exit 0 journalled or WhatIf, 1 a clone, fetch, push, SHA or
            // journal-write failure, 2 usage, 3 refused (its JSON line last on stdout).
            [ReleaseCandidateCommand.Name] = ReleaseCandidateCommand.Run,
            // The release cut answers every input itself: exit 0 published, or green with --skip-publish or --what-if, 3
            // refused, otherwise the candidate's, the run's or the publisher's (its JSON record last on stdout); 1 a candidate
            // journal or state root that cannot be written; 2 usage.
            [ReleaseCutCommand.Name] = ReleaseCutCommand.Run,
            // The publisher and the promoter answer every input themselves: exit 0 published, promoted, resumed or --what-if,
            // 1 a remote write (or the promoter's journal record) failed, 2 usage, 3 refused.
            [PublishReleaseCommand.Name] = PublishReleaseCommand.Run,
            [PromoteReleaseCommand.Name] = PromoteReleaseCommand.Run,
            // The land graph audit answers every input itself: exit 0 approved, 1 refused, 2 usage. The pre-check: exit 0 matched
            // or written, 1 mismatch, refusal or a failed tool, 2 a git failure or usage. The land refresh: exit 0 current,
            // refreshed or an inherited mismatch warned, 1 refused (one LAND-GRAPH-NOTICE line) or failed, 2 usage.
            [LandGraphAuditCommand.Name] = LandGraphAuditCommand.Run,
            [LandGraphAuditCommand.PrecheckName] = LandGraphAuditCommand.RunPrecheck,
            [LandGraphLandRefreshCommand.Name] = LandGraphLandRefreshCommand.Run,
            // The operator API commands answer every input themselves (unit 21). host-load: exit 0 read, 1 failed (one fixed
            // stderr line). runner-drain: 0 done, 1 not eligible, failed or no token file. workspace-slots: 0 every runner
            // answered (release: removed), 1 a call failed (release: refused or removal_failed), 2 an unreadable token file.
            // herdr-pane: 0 answered or dry run, 1 the request failed. worktree-residue: 0 done, 1 a request or batch item failed.
            // runner-slots (unit 23): 0 answered, 1 failed or no token file. keep-six (unit 23): 0 answered, 1 a refused value, no
            // task token or a failed request. Usage is 2 for each.
            [OperatorCommands.HostLoad] = OperatorCommands.RunHostLoad,
            [OperatorCommands.RunnerDrain] = OperatorCommands.RunRunnerDrain,
            [OperatorCommands.WorkspaceSlots] = OperatorCommands.RunWorkspaceSlots,
            [OperatorCommands.HerdrPane] = OperatorCommands.RunHerdrPane,
            [OperatorCommands.WorktreeResidue] = OperatorCommands.RunWorktreeResidue,
            [OperatorCommands.RunnerSlots] = OperatorCommands.RunRunnerSlots,
            [OperatorCommands.KeepSix] = OperatorCommands.RunKeepSix,
            // The board API commands answer every input themselves (unit 24): exit 0 done, 1 a refused value or a failed request
            // (one stderr line; capability: also an unknown or ambiguous name, an answer without a token, or issue/rotate off
            // Windows; schedule: also an unacknowledged spend), 2 usage.
            [BoardCommands.Capability] = BoardCommands.RunCapability,
            [BoardCommands.ModelAvailability] = BoardCommands.RunModelAvailability,
            [BoardCommands.SpecialistRouting] = BoardCommands.RunSpecialistRouting,
            [BoardCommands.PipelineDefinition] = BoardCommands.RunPipelineDefinition,
            [BoardCommands.Project] = BoardCommands.RunProject,
            [BoardCommands.Schedule] = BoardCommands.RunSchedule,
            [BoardCommands.DispatchConcurrency] = BoardCommands.RunDispatchConcurrency,
            [BoardCommands.RoutingPin] = BoardCommands.RunRoutingPin,
            // The repository child journal recovery answers every input itself (unit 23): exit 0 no record retained, 3 busy,
            // retained, unconfirmed or refused, 2 usage.
            [RecoverRepositoryChildrenCommand.Name] = RecoverRepositoryChildrenCommand.Run,
            // The cleanups answer every input themselves (unit 21). cleanup-build-junk: exit 0. cleanup-codex-test-residue: 0 done,
            // 1 the threads could not be read, 4 over --max-per-run, 5 a delete failed. Usage is 2 for each.
            [CleanupCommands.BuildJunk] = CleanupCommands.RunBuildJunk,
            [CleanupCommands.CodexResidue] = CleanupCommands.RunCodexResidue,
            // The session reapers answer every input themselves (unit 22). cleanup-claude-sessions: exit 0 done, 1 the list could not
            // be read, 2 no or stale credentials, 3 a truncated list, 4 over --max-per-run, 5 a mutation failed. reap-zombie-agents: 0
            // no positives or every kill verified, 1 a leaf still alive, 2 a prerequisite did not answer, 3 a dry run found
            // positives. Usage is 2 for each.
            [SessionReaperCommands.ClaudeSessions] = SessionReaperCommands.RunClaudeSessions,
            [SessionReaperCommands.ZombieAgents] = SessionReaperCommands.RunZombieAgents,
            // The agent and operator helpers answer every input themselves (unit 25). check-agent-context: exit 0 within the
            // budget, 1 over it or unreadable. task-commit: 0 committed, 1 refused, pending inspection, no token or task id.
            // new-review-scratch and remove-review-scratch: 0 done, 1 refused. trx-failures: 0. watch-master: 0. watch-tasks: 0,
            // 1 an unusable state file. stage-value-report: 0 printed, 1 a failed request. logs: 0 printed, 1 a missing log, no
            // live session, a failed request or ssh. hangfire-dashboard: 0 opened, 1 refused or failed, 2 not Windows.
            // github-sync: 0 clean, 1 the request failed or a board reported an error. Usage is 2 for each.
            [AgentHelperCommands.CheckAgentContext] = AgentHelperCommands.RunCheckAgentContext,
            [AgentHelperCommands.TaskCommit] = AgentHelperCommands.RunTaskCommit,
            [AgentHelperCommands.NewReviewScratch] = AgentHelperCommands.RunNewReviewScratch,
            [AgentHelperCommands.RemoveReviewScratch] = AgentHelperCommands.RunRemoveReviewScratch,
            [AgentHelperCommands.TrxFailures] = AgentHelperCommands.RunTrxFailures,
            [AgentHelperCommands.WatchMaster] = AgentHelperCommands.RunWatchMaster,
            [AgentHelperCommands.WatchTasks] = AgentHelperCommands.RunWatchTasks,
            [AgentHelperCommands.StageValueReport] = AgentHelperCommands.RunStageValueReport,
            [AgentHelperCommands.Logs] = AgentHelperCommands.RunLogs,
            [AgentHelperCommands.HangfireDashboard] = AgentHelperCommands.RunHangfireDashboard,
            [AgentHelperCommands.GithubSync] = AgentHelperCommands.RunGithubSync,
        };

        public static int Main(string[] args)
        {
            // One process answers one request: the filter/textconv driver settings are asked of git once.
            CheckpointProcessRunner.DriverSettingsCacheMilliseconds = 60000;
            if (args.Length == 0 || !Commands.TryGetValue(args[0], out var command)) return WriteUsage();
            try
            {
                return command(args);
            }
            catch (Antiphon.Nightly.NightlyUsageException e)
            {
                // A command that parses its flags with NightlyArgs: the reason and the command, one line.
                Console.Error.WriteLine("usage: antiphon-scripts " + args[0] + ": " + e.Message);
                return Usage;
            }
        }

        private static int WriteUsage()
        {
            Console.Error.WriteLine("usage: antiphon-scripts live-lane-evidence <case> [<context.json>] | " + LiveLaneScanGatesUsage + " | " + LiveLaneRosterUsage + " | " + LiveLaneBindManifestUsage
                + " | " + LiveLaneWriteFixtureUsage + " | " + LiveLaneCensusUsage + " | " + LiveLaneEmitPolicyHashUsage + " | " + LiveLaneGroupUsage + " | " + RcLedgerStampUsage + " | " + PostLandCommand.Usage + " | " + EvidenceCommand.Usage + " | " + NightlyProbeCommand.Usage + " | " + NightlyHealthCommand.Usage + " | " + NightlyReportCommand.Usage + " | " + NightlyTestsCommand.Usage + " | " + NightlyRunCommand.Usage + " | " + SummarizeTestPhasesCommand.Usage + " | " + NightlySeedCommand.Usage + " | " + ToolchainFloorCommand.Usage
                + " | " + TestTimingReportCommand.Usage + " | " + TestTimingEventCommand.Usage + " | " + TestSelectionCommand.Usage + " | " + ImpactedClassesCommand.Usage
                + " | " + QualificationEtaCommand.Usage + " | " + WatchQualCommand.Usage
                + " | " + ExportExpectedDurationRosterCommand.Usage + " | " + SummarizeTestTraceCommand.Usage
                + " | " + RefreshNightlyClassCostsCommand.Usage + " | " + DigestCommand.Usage
                + " | " + RcQualificationStatusCommand.Usage
                + " | " + QualificationObserveCommand.Usage + " | " + QualificationPreflightCommand.Usage + " | " + QualificationLaunchCommand.Usage
                + " | " + QualificationStartSupervisorCommand.Usage + " | " + QualificationSuperviseCommand.Usage + " | " + QualificationStopCommand.Usage
                + " | " + StatusSnapshotCommand.Usage + " | " + SelectRcProjectBatchesCommand.Usage
                + " | " + DeployNightlyWatchdogCommand.Usage + " | " + ReleaseStatusCommand.Usage + " | " + RegisterReleaseGatesCommand.Usage
                + " | " + ReleaseCandidateCommand.Usage + " | " + ReleaseCutCommand.Usage
                + " | " + PublishReleaseCommand.Usage + " | " + PromoteReleaseCommand.Usage
                + " | " + LandGraphAuditCommand.Usage + " | " + LandGraphAuditCommand.PrecheckUsage + " | " + LandGraphLandRefreshCommand.Usage
                + " | " + OperatorCommands.Usage + " | " + CleanupCommands.Usage + " | " + SessionReaperCommands.Usage
                + " | " + RecoverRepositoryChildrenCommand.Usage + " | " + BoardCommands.Usage + " | " + AgentHelperCommands.Usage);
            return Usage;
        }

        private static int LiveLaneEvidenceCommand(string[] args)
        {
            if ((args.Length == 2 || args.Length == 3) && Array.IndexOf(LiveLaneEvidence.Cases, args[1]) >= 0)
                return LiveLaneEvidence.RunCommand(args[1], args.Length == 3 ? args[2] : null);
            return WriteUsage();
        }

        private const string LiveLaneScanGatesUsage = "live-lane-scan-gates [--repository <dir>]";

        // W2-nightly S23: the ScanGates case of scripts/test-live-lane-manifest.ps1. Rewrites the gates of the
        // repository's tests/live-lanes.json from the gate scan and prints "gates=<n> unresolved=<n>".
        private static int LiveLaneScanGates(string[] args)
        {
            var flags = Flags(args, new[] { "--repository" }, new string[0]);
            if (flags == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneScanGatesUsage);
                return Usage;
            }
            try
            {
                var written = Antiphon.Nightly.LiveLaneGateScan.WriteCatalog(flags.TryGetValue("--repository", out var repository) ? repository : Environment.CurrentDirectory);
                Console.WriteLine("gates=" + written.Count + " unresolved=" + written.Unresolved);
                return Handled;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        private const string LiveLaneRosterUsage = "live-lane-roster --project <name> --assembly <dll> [--repository <dir>] [--capture-dir <dir>]";
        private const string LiveLaneBindManifestUsage = "live-lane-bind-manifest [--repository <dir>] [--capture-dir <dir>]";
        private const string LiveLaneWriteFixtureUsage = "live-lane-write-fixture [--repository <dir>] [--capture-dir <dir>] [--source-sha <sha>]";

        // W2-nightly S24: the ExactCoreRoster and ExactPtyCoreRoster cases of scripts/test-live-lane-manifest.ps1 for a
        // named assembly (an S1a capture row). Lists its tests into the capture directory, runs the roster matrix, writes
        // the row's export in candidate mode (ANTIPHON_LIVE_PORTABLE_CONTEXT), prints the result line; phases on stderr.
        private static int LiveLaneRosterCommand(string[] args)
        {
            var flags = Flags(args, new[] { "--project", "--assembly", "--repository", "--capture-dir" }, new string[0]);
            if (flags == null || !flags.ContainsKey("--project") || !flags.ContainsKey("--assembly"))
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneRosterUsage);
                return Usage;
            }
            try
            {
                var result = Antiphon.Nightly.LiveLaneRoster.RunCase(flags.TryGetValue("--repository", out var repository) ? repository : Environment.CurrentDirectory,
                    flags["--project"], flags["--assembly"], flags.TryGetValue("--capture-dir", out var capture) ? capture : null, null, Console.Error);
                Console.WriteLine(result.ToJsonLine());
                return result.ExitCode;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        // W2-nightly S24: the BindManifest case. Binds the core rows of tests/live-lanes.json to the captured nodes.
        private static int LiveLaneBindManifest(string[] args)
        {
            var flags = Flags(args, new[] { "--repository", "--capture-dir" }, new string[0]);
            if (flags == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneBindManifestUsage);
                return Usage;
            }
            try
            {
                Antiphon.Nightly.LiveLaneManifestFiles.BindManifest(flags.TryGetValue("--repository", out var repository) ? repository : Environment.CurrentDirectory,
                    flags.TryGetValue("--capture-dir", out var capture) ? capture : null);
                Console.WriteLine("bound");
                return Handled;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        // W2-nightly S24: the WriteFixture case. Writes the census fixture from the captures; the source defaults to HEAD.
        private static int LiveLaneWriteFixture(string[] args)
        {
            var flags = Flags(args, new[] { "--repository", "--capture-dir", "--source-sha" }, new string[0]);
            if (flags == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneWriteFixtureUsage);
                return Usage;
            }
            try
            {
                var repository = flags.TryGetValue("--repository", out var given) ? given : Environment.CurrentDirectory;
                var sourceSha = flags.TryGetValue("--source-sha", out var sha) ? sha : "";
                if (string.IsNullOrWhiteSpace(sourceSha))
                {
                    var head = Antiphon.Nightly.NightlyNativeGit.Run(repository, new[] { "rev-parse", "HEAD" });
                    if (head.ExitCode != 0) throw new InvalidOperationException("head-unresolved " + head.Output.Trim());
                    sourceSha = head.Output.Trim();
                }
                Console.WriteLine(Antiphon.Nightly.LiveLaneManifestFiles.WriteFixture(repository, flags.TryGetValue("--capture-dir", out var capture) ? capture : null, sourceSha));
                return Handled;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        private const string LiveLaneCensusUsage = "live-lane-census [--repository <dir>]";
        private const string LiveLaneEmitPolicyHashUsage = "live-lane-emit-policy-hash --policy-root <dir> [--policy-path <file>]";
        private const string LiveLaneGroupUsage = "live-lane-group --group <S1a|S1a-baseline> --tool-dll <dll> [--repository <dir>] [--checkpoint-manifest <yaml>]"
            + " [--expected-source-sha <sha>] [--census-base-sha <sha>]";

        // W2-nightly S25: the FullCensusUnchanged case (CP-30), a commission-scoped check over the context that
        // ANTIPHON_LIVE_PORTABLE_CONTEXT names (docs/testing-and-build.md); the witnesses first, then the census lines.
        private static int LiveLaneCensusCommand(string[] args)
        {
            var flags = Flags(args, new[] { "--repository" }, new string[0]);
            if (flags == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneCensusUsage);
                return Usage;
            }
            try
            {
                var repository = System.IO.Path.GetFullPath(flags.TryGetValue("--repository", out var given) ? given : Environment.CurrentDirectory);
                return Antiphon.Nightly.LiveLaneCensus.FullCensusUnchanged(repository, () => Antiphon.Nightly.LiveLaneContext.FromEnvironment(), Console.Out);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        // W2-nightly S25: the EmitPolicyHash case. Prints the policy hash of --policy-path, or of the root's
        // tests/test-execution-policy.json.
        private static int LiveLaneEmitPolicyHash(string[] args)
        {
            var flags = Flags(args, new[] { "--policy-root", "--policy-path" }, new string[0]);
            if (flags == null || !flags.TryGetValue("--policy-root", out var root) || string.IsNullOrWhiteSpace(root))
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneEmitPolicyHashUsage);
                return Usage;
            }
            try
            {
                Console.WriteLine(Antiphon.Nightly.LiveLaneCensus.EmitPolicyHash(root, flags.TryGetValue("--policy-path", out var path) ? path : ""));
                return Handled;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        // W2-nightly S25: the Group case (the S1a commission): writes the group's context, then runs its checkpoint rows.
        private static int LiveLaneGroup(string[] args)
        {
            var flags = Flags(args, new[] { "--group", "--tool-dll", "--repository", "--checkpoint-manifest", "--expected-source-sha", "--census-base-sha" }, new string[0]);
            if (flags == null || !flags.ContainsKey("--group") || !flags.ContainsKey("--tool-dll"))
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + LiveLaneGroupUsage);
                return Usage;
            }
            string F(string name) => flags.TryGetValue(name, out var v) ? v : "";
            try
            {
                var repository = System.IO.Path.GetFullPath(flags.TryGetValue("--repository", out var given) ? given : Environment.CurrentDirectory)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                return Antiphon.Nightly.LiveLaneCensus.RunGroup(repository, F("--group"), F("--tool-dll"), F("--checkpoint-manifest"), F("--expected-source-sha"),
                    F("--census-base-sha"));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }

        public const int Refused = 1;

        private const string RcLedgerCommandsUsage =
            "rc-ledger-import --source-run-root <dir> --evidence-root <dir> --repository <dir> --expected-sha <sha> --expected-run-id <id> [--current-manifest <json>]"
            + " | rc-ledger-plan --request <json> --request-digest <sha256> --repository <dir> --expected-sha <sha> --plan <json>"
            + " | rc-ledger-status --evidence-root <dir> --plan-digest <sha256> [--json]"
            + " | rc-input-census [--repository <dir>] [--emit-rows] [--scan-only]"
            + " | rc-input-owner-path-census [--repository <dir>]";

        // --name value flags (and bare switches) into a map; a repeated, unknown or valueless flag is malformed.
        internal static System.Collections.Generic.Dictionary<string, string> Flags(string[] args, string[] valued, string[] switches)
        {
            var flags = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var name = args[i];
                if (Array.IndexOf(switches, name) >= 0 && !flags.ContainsKey(name)) { flags[name] = "true"; continue; }
                if (Array.IndexOf(valued, name) < 0 || i + 1 >= args.Length || flags.ContainsKey(name)) return null;
                flags[name] = args[++i];
            }
            return flags;
        }

        // CARD-1836: the S5b import, the S5c diagnostic plan and its status. None launches, activates or grants credit.
        private static int RcLedgerCommand(string[] args)
        {
            System.Collections.Generic.Dictionary<string, string> flags;
            string F(string name) => flags.TryGetValue(name, out var v) ? v : null;
            try
            {
                switch (args[0])
                {
                    case "rc-ledger-import":
                        flags = Flags(args, new[] { "--source-run-root", "--evidence-root", "--repository", "--expected-sha", "--expected-run-id", "--current-manifest" }, new string[0]);
                        if (flags == null || F("--source-run-root") == null || F("--evidence-root") == null || F("--repository") == null || F("--expected-sha") == null || F("--expected-run-id") == null) break;
                        var report = Antiphon.Nightly.RcLedgerImport.Run(F("--source-run-root"), F("--evidence-root"), F("--repository"), F("--expected-sha"), F("--expected-run-id"),
                            F("--current-manifest"), RepositoryCheckout(F("--repository")));
                        Console.WriteLine("RC IMPORT admitted=0 dirty=" + report["dirty"] + " unproven=" + report["unproven"] + " Full=true");
                        return Handled;
                    case "rc-ledger-plan":
                        flags = Flags(args, new[] { "--request", "--request-digest", "--repository", "--expected-sha", "--plan" }, new string[0]);
                        if (flags == null || new[] { "--request", "--request-digest", "--repository", "--expected-sha", "--plan" }.Any(n => F(n) == null)) break;
                        var request = System.Text.Json.Nodes.JsonNode.Parse(Antiphon.Nightly.RcAdmission.ReadJson(F("--request"), F("--request-digest")).GetRawText());
                        var plan = Antiphon.Nightly.RcIncrementalPlan.NewPlan(request, F("--repository"), F("--expected-sha"));
                        Antiphon.Nightly.RcIncrementalPlan.WritePlan(F("--plan"), plan, RepositoryCheckout(F("--repository")));
                        Console.WriteLine("RC PLAN diagnostic=true effective=Full required=" + plan["partition"]["required"].AsArray().Count + " carried=0 digest=" + plan["digest"]);
                        return Handled;
                    case "rc-input-census":
                        // CARD-1820 S3: exit 1 names every under-selected pair, unanalysable listing and no-op conflict.
                        flags = Flags(args, new[] { "--repository" }, new[] { "--emit-rows", "--scan-only" });
                        if (flags == null) break;
                        return Antiphon.Nightly.RcInputCensus.Run(F("--repository") ?? Environment.CurrentDirectory, F("--emit-rows") != null, F("--scan-only") != null, Console.Out);
                    case "rc-input-owner-path-census":
                        // CARD-2009: exit 1 names every missing literal owner path of the working tree's owner map.
                        flags = Flags(args, new[] { "--repository" }, new string[0]);
                        if (flags == null) break;
                        return Antiphon.Nightly.RcInputCensus.OwnerPaths(F("--repository") ?? Environment.CurrentDirectory, Console.Out);
                    case "rc-ledger-status":
                        flags = Flags(args, new[] { "--evidence-root", "--plan-digest" }, new[] { "--json" });
                        if (flags == null || F("--evidence-root") == null || F("--plan-digest") == null) break;
                        var status = Antiphon.Nightly.RcIncrementalPlan.ReadStatus(F("--evidence-root"), F("--plan-digest"));
                        if (F("--json") != null) { Console.WriteLine(status.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return Handled; }
                        Console.WriteLine("RC STATUS effective=" + status["effectiveMode"] + " planned=" + status["planned"] + " executed=" + status["executed"] + " carried=" + status["carried"]
                            + " sampled=" + status["sampled"] + " failed=" + status["failed"] + " unproven=" + status["unproven"] + " not-run=" + status["notRun"]);
                        Console.WriteLine("coverageComplete=false gateCredit=false revision=" + status["sequence"] + " reasons="
                            + string.Join(",", status["forcedReasons"].AsArray().Select(r => (string)r)));
                        var proposed = status["proposed"];
                        Console.WriteLine("UNVERIFIED PROPOSAL carried=" + proposed["carried"] + " unique-origin-chunks=" + proposed["uniqueOriginChunks"]
                            + " historical-potential-wall-seconds=" + proposed["historicalPotentialWallSeconds"]);
                        foreach (var origin in proposed["origins"].AsArray())
                            Console.WriteLine("ORIGIN envelope=" + origin["envelopeDigest"] + " run=" + origin["runId"] + " source=" + origin["sourceSha"] + " suite=" + origin["suite"]
                                + " chunk=" + origin["chunkId"]);
                        return Handled;
                }
            }
            catch (Exception e) when (e is Antiphon.Nightly.RcAdmissionRefusal || e is Antiphon.Nightly.RcLedgerRefusal || e is Antiphon.Nightly.RcChunkDigestRefusal
                || e is Antiphon.Nightly.RcInputRefusal || e is Antiphon.Nightly.RcBoundaryRefusal || e is InvalidOperationException || e is System.IO.IOException)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
            Console.Error.WriteLine("usage: antiphon-scripts " + RcLedgerCommandsUsage);
            return Usage;
        }

        // The checkout outputs must stay outside: the repository's own working tree.
        private static string RepositoryCheckout(string repository) => System.IO.Path.GetFullPath(repository);

        private const string RcLedgerStampUsage =
            "rc-ledger-stamp <ledger.json> <output.json> [--timing <sidecar>]... [--trx <trx>]... [--include-host --host-id <id> --os <windows|linux>]";

        // CARD-1842 S6: copy a ledger with durations from timing sidecars and TRX; --include-host writes v2 with the
        // given chunk host (never the machine name). Refuses an existing output.
        private static int RcLedgerStamp(string[] args)
        {
            var paths = new System.Collections.Generic.List<string>();
            var timing = new System.Collections.Generic.List<string>();
            var trx = new System.Collections.Generic.List<string>();
            var includeHost = false;
            string hostId = null, os = null;
            var malformed = false;
            for (var i = 1; i < args.Length && !malformed; i++)
            {
                var arg = args[i];
                var hasValue = i + 1 < args.Length;
                if (arg == "--include-host") includeHost = true;
                else if (arg == "--timing" && hasValue) timing.Add(args[++i]);
                else if (arg == "--trx" && hasValue) trx.Add(args[++i]);
                else if (arg == "--host-id" && hasValue) hostId = args[++i];
                else if (arg == "--os" && hasValue) os = args[++i];
                else if (arg.StartsWith("--", StringComparison.Ordinal)) malformed = true;
                else paths.Add(arg);
            }
            if (malformed || paths.Count != 2)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + RcLedgerStampUsage);
                return Usage;
            }
            try
            {
                Antiphon.Nightly.RcLedger.Stamp(paths[0], paths[1], timing, trx, includeHost, hostId, os);
                return Handled;
            }
            catch (Antiphon.Nightly.RcLedgerRefusal e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }
    }
}
