// antiphon-scripts watch-qual --log-dir <dir> --state-file <file> --heartbeat <json> [--poll-seconds 60] [--max-minutes 28]
// [--min-file-age-seconds 5] [--heartbeat-stale-minutes 5] [--timing-root <dir>] [--once]. The rc qualification watcher
// (Antiphon.Nightly.WatchQual.Run on the production host, the system clock and sleep, and the real file system); it
// replaced scripts/orchestration/watch-qual.ps1 (deleted 2026-11-23, unit 6 of 2026-11-22-remaining-ps1-to-csharp.md),
// with the same parameters in kebab case. A Monitor task runs it (.claude/skills/antiphon-rc-qualification/SKILL.md).
// The poll lines go to stdout as they happen; with --timing-root each poll prints the qualification-eta line (CARD-1842),
// computed in this process. It writes only the state file. Exit 0 the window or the run ended; 1 the state file cannot
// be read (one stderr line); 2 usage, a value out of range included. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class WatchQualCommand
    {
        internal const string Name = "watch-qual";

        internal const string Usage = WatchQualOptions.Usage;

        internal static int Run(string[] args) =>
            WatchQual.Run(WatchQualOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}
