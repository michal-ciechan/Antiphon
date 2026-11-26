// W2 slice S21: the live lane's CLI commands over the isolated Antiphon.LiveLane project (the dependency goes Scripts ->
// LiveLane, never the reverse). live-lane-run [--lane --candidate-ref --expected-sha --manifest --coordination-root
// --results-root]: run-live-lane.ps1's native mode, always native-unqualified, exit 20, the compact record on stdout (no lock,
// no process, no provider). live-lane-input-digest [--root <dir>] [--check]: the protected-input digest that
// test-live-lane-run.ps1 -PrintInputDigest printed (exit 0); --check also compares it with LiveLaneInputDigest.Pinned and
// reads the census sentinels with this CLI's compiled policy and census (exit 1 when either fails). Usage is 2 for both.
// ASCII only.
using System;
using System.Linq;
using Antiphon.LiveLane;

namespace Antiphon.Scripts
{
    internal static class LiveLaneCommands
    {
        internal const string Usage = LiveLaneRunOptions.Usage + " | " + LiveLaneInputDigestOptions.Usage;

        internal static int RunLiveLaneRun(string[] args) =>
            LiveLaneRunCommand.Run(LiveLaneRunOptions.Parse(args.Skip(1).ToArray()), Console.Out);

        internal static int RunInputDigest(string[] args) =>
            LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()),
                LiveLaneInputCensusReader.Read, Console.Out, Console.Error);
    }
}
