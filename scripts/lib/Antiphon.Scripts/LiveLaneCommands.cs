// W2 slice S21: the live lane's CLI commands over the isolated Antiphon.LiveLane project (the dependency goes Scripts ->
// LiveLane, never the reverse). live-lane-input-digest [--root <dir>] [--check]: the protected-input digest that
// test-live-lane-run.ps1 -PrintInputDigest printed (exit 0); --check also compares it with LiveLaneInputDigest.Pinned and
// reads the census sentinels with this CLI's compiled policy and census (exit 1 when either fails). Usage is 2.
// ASCII only.
using System;
using System.Linq;
using Antiphon.LiveLane;

namespace Antiphon.Scripts
{
    internal static class LiveLaneCommands
    {
        internal const string Usage = LiveLaneInputDigestOptions.Usage;

        internal static int RunInputDigest(string[] args) =>
            LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root()),
                LiveLaneInputCensusReader.Read, Console.Out, Console.Error);
    }
}