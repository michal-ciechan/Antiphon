// antiphon-scripts qualification-stop --task-root <dir> --coordination-root <dir> --run-id <id> --reason <text>
// [--evidence-root <dir>] [--confirm]. The operator's stop of one recorded Windows rc qualification (CARD-1507;
// Antiphon.Nightly.QualificationStop.Run on the production host, a 30 s census). It replaced
// scripts/qualification/stop-supervisor.ps1 and scripts/qualification/qualification-stop.ps1 (deleted 2026-11-23, unit 7
// of 2026-11-22-remaining-ps1-to-csharp.md). Without --confirm: the dry run, nothing killed, exit 2. Exit codes: 0 stopped,
// 2 dry run, refused or usage, 3 incomplete, 4 lock retained, 5 not Windows. Stopping the live qualification run is the
// operator's decision: agents never aim it at a live run. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationStopCommand
    {
        internal const string Name = "qualification-stop";

        internal const string Usage = QualificationStopOptions.Usage;

        internal static int Run(string[] args) =>
            QualificationStop.Run(QualificationStopOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}