// antiphon-scripts qualification-supervise --task-root <dir> --launch-record <json> --log-root <dir> --state-root <dir>
// --run-id <id> [--poll-seconds 30] [--progress-minutes 10] [--hang-minutes 20] [--detach new-console|setsid|none]
// [--powershell-path <pwsh>]. The detached supervisor of one rc qualification launch (CARD-1667;
// Antiphon.Nightly.QualificationSupervise.Run on the production host); only qualification-start-supervisor starts it. It
// replaced scripts/qualification/supervise-rc.ps1 (deleted 2026-11-23, unit 7 of 2026-11-22-remaining-ps1-to-csharp.md).
// It observes and records (supervisor.log, the identity, the heartbeat, launch-child.json, own-last-run.json,
// commission-exit.txt) and never stops a process or clears a lock. Exit 0; 1 after SUPERVISOR-ERROR; 2 usage. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationSuperviseCommand
    {
        internal const string Name = "qualification-supervise";

        internal const string Usage = QualificationSuperviseOptions.Usage;

        internal static int Run(string[] args) =>
            QualificationSupervise.Run(QualificationSuperviseOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}