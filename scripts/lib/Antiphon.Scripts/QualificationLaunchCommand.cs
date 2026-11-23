// antiphon-scripts qualification-launch --config <launch-record.json> [--powershell-path <pwsh>]. The launch of one rc
// qualification run (CARD-1667; Antiphon.Nightly.QualificationLaunch.Run on the production host); qualification-supervise
// starts it as its launch child. It replaced scripts/qualification/launch-rc.ps1 (deleted 2026-11-23, unit 7 of
// 2026-11-22-remaining-ps1-to-csharp.md). The bound run id is ANTIPHON_QUALIFICATION_RUN_ID (the supervisor sets it); the
// default PowerShell is pwsh on PATH. It runs scripts/build-slot.ps1 -- scripts/nightly-run.ps1 of the record's checkout on
// this process's console and streams. Exit 1 an unreadable record; 2 refused or usage; 3 a live run.lock; else
// build-slot's exit code. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationLaunchCommand
    {
        internal const string Name = "qualification-launch";

        internal const string Usage = QualificationLaunchOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = QualificationLaunchOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable(QualificationSupervise.RunIdVariable),
                OwnedLauncher.FindApplication("pwsh") ?? "pwsh");
            return QualificationLaunch.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }
    }
}