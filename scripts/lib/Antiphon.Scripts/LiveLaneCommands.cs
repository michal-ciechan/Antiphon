// W2 slices S21 and S22: the live lane's CLI commands over the isolated Antiphon.LiveLane project (the dependency goes
// Scripts -> LiveLane, never the reverse). live-lane-run [--lane --candidate-ref --expected-sha --manifest --coordination-root
// --results-root --claude]: run-live-lane.ps1's native mode, always native-unqualified, exit 20, the compact record on stdout
// (no lock, no process, no provider); --claude defaults to ANTIPHON_LIVE_CLAUDE_EXE. live-lane-input-digest [--root <dir>]
// [--check]: the protected-input digest that test-live-lane-run.ps1 -PrintInputDigest printed (exit 0); --check also compares
// it with LiveLaneInputDigest.Pinned and reads the census sentinels with this CLI's compiled policy and census (exit 1 when
// either fails). live-lane-status (S22, live-lane-status.ps1): each lane's state with its Claude versions, git and the rc
// project graph read through the nightly core (LiveLaneNightlySources); exit 0, 2 a lane INVALID, 1 a required Claude
// version missing. live-lane-isolation-rows --checkpoint-tool <path> [--controls] and live-lane-evidence-rows
// --checkpoint-tool <path> (test-live-lane-isolation.ps1 and test-live-lane-evidence.ps1 -Case RunPortableRows): the
// portable S1c checkpoint commissions on this machine (LiveLaneOsCommissionHost); exit 0 done, 1 a failed step (or the
// step's own exit), 2 context-absent for the evidence rows without a tool. Usage is 2 for each. ASCII only.
using System;
using System.Linq;
using Antiphon.LiveLane;

namespace Antiphon.Scripts
{
    internal static class LiveLaneCommands
    {
        internal const string Usage = LiveLaneRunOptions.Usage + " | " + LiveLaneInputDigestOptions.Usage + " | " + LiveLaneStatusOptions.Usage
            + " | " + LiveLaneIsolationRows.Usage + " | " + LiveLaneEvidenceRows.Usage;

        internal static int RunLiveLaneRun(string[] args) =>
            LiveLaneRunCommand.Run(LiveLaneRunOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable("ANTIPHON_LIVE_CLAUDE_EXE")), Console.Out);

        internal static int RunInputDigest(string[] args) =>
            LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()),
                LiveLaneInputCensusReader.Read, Console.Out, Console.Error);

        internal static int RunStatus(string[] args) =>
            LiveLaneStatus.Run(LiveLaneStatusOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()), LiveLaneNightlySources.Create(), Console.Out);

        internal static int RunIsolationRows(string[] args)
        {
            var values = LiveLaneArgs.Parse(args.Skip(1).ToArray(), new[] { "--checkpoint-tool" }, new[] { "--controls" });
            var tool = values.Value("--checkpoint-tool");
            if (string.IsNullOrWhiteSpace(tool)) throw new LiveLaneUsageException("--checkpoint-tool is required");
            return new LiveLaneIsolationRows(SourceCheckout.Root(), System.IO.Path.GetFullPath(tool), values.Switch("--controls"), new LiveLaneOsCommissionHost()).Run();
        }

        internal static int RunEvidenceRows(string[] args)
        {
            var values = LiveLaneArgs.Parse(args.Skip(1).ToArray(), new[] { "--checkpoint-tool" }, Array.Empty<string>());
            var tool = values.Value("--checkpoint-tool") ?? "";
            return new LiveLaneEvidenceRows(SourceCheckout.Root(), tool.Length == 0 ? "" : System.IO.Path.GetFullPath(tool), new LiveLaneOsCommissionHost(),
                LiveLaneEvidence.PortableRoster, context => LiveLaneEvidence.Run("OuterPublish", context, Console.Out, null), OperatingSystem.IsWindows()).Run();
        }
    }
}