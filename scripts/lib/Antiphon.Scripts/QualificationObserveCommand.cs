// antiphon-scripts qualification-observe --task-root <dir> --state-root <dir> [--heartbeat-stale-minutes 5]. The read-only
// classification of a supervised rc qualification run (CARD-1667; Antiphon.Nightly.QualificationObserve.Run on the
// production host and the real file system). It replaced scripts/qualification/observe.ps1 (deleted 2026-11-23, unit 7 of
// 2026-11-22-remaining-ps1-to-csharp.md); nothing is loaded from a checkout. One JSON line on stdout. Exit 0 completed,
// running or supervisor-lost-run-alive; 3 run-lost; 2 not-started, launch-failed, unknown or usage. It writes nothing and
// never stops a process or clears a lock. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationObserveCommand
    {
        internal const string Name = "qualification-observe";

        internal const string Usage = QualificationObserveOptions.Usage;

        internal static int Run(string[] args) =>
            QualificationObserve.Run(QualificationObserveOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}