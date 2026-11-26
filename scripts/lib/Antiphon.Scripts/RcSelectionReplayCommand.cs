// CARD-1836 S5: antiphon-scripts replay-rc-selection --input-root <dir> --output <new-dir> --as-of <yyyy-MM-ddTHH:mm:ssZ>
// [--repository-root <git>]. The offline five-run replay oracle (Antiphon.Nightly.RcSelectionReplay): reads only the
// read-only custody input root and git objects, writes only the new output directory. Exit 0 criterion met (enables
// nothing), 1 hard red (a missed failure or unsafe carry), 2 bad input or usage, 3 insufficient evidence. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RcSelectionReplayCommand
    {
        internal const string Name = RcSelectionReplay.Name;

        internal const string Usage = RcSelectionReplay.Usage;

        internal static int Run(string[] args) => RcSelectionReplay.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
    }
}
