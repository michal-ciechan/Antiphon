// antiphon-scripts qualification-start-supervisor --task-root <dir> --launch-record <json> --log-root <dir> --state-root <dir>
// --run-id <id> [--poll-seconds 30] [--progress-minutes 10] [--hang-minutes 20] [--identity-timeout-seconds 60]
// [--powershell-path <pwsh>]. Starts qualification-supervise detached and returns (CARD-1667;
// Antiphon.Nightly.QualificationStartSupervisor.Run on the production host). It replaced
// scripts/qualification/start-supervisor.ps1 (deleted 2026-11-23, unit 7 of 2026-11-22-remaining-ps1-to-csharp.md). The
// supervisor runs from a copy of this CLI in <task-root>/cli, with its own hidden console (Windows) or setsid (Unix). The
// default PowerShell for the launch is pwsh on PATH. To leave a caller's Job Object too, start this command itself through
// Win32_Process Create (.claude/skills/antiphon-rc-qualification/SKILL.md). Exit 0 the supervisor is alive (or finished);
// 1 refused or failed; 2 usage. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationStartSupervisorCommand
    {
        internal const string Name = "qualification-start-supervisor";

        internal const string Usage = QualificationStartOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = QualificationStartOptions.Parse(args.Skip(1).ToArray(), OwnedLauncher.FindApplication("pwsh") ?? "");
            return QualificationStartSupervisor.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }
    }
}