// antiphon-scripts rc-qualification-status --candidate-id <id> --run-id <id> --state-root <dir> --coordination-root <dir>
// [--release-root <dir>]. The read-only status of one explicitly selected rc qualification (CARD-1500;
// Antiphon.Nightly.RcQualificationStatus.Run on the production host and the real file system). It replaced
// scripts/rc-qualification-status.ps1 and scripts/lib/rc-qualification-status.ps1 (deleted 2026-11-23, unit 7 of
// 2026-11-22-remaining-ps1-to-csharp.md). One compact JSON line on stdout. Exit 0 a coherent observation (a failed run
// included); 2 incomplete or invalid input, or usage. Neither exit grants release credit or permission to stop a process
// or clear a lock. Run it on the execution host. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RcQualificationStatusCommand
    {
        internal const string Name = "rc-qualification-status";

        internal const string Usage = RcQualificationStatusOptions.Usage;

        internal static int Run(string[] args) =>
            RcQualificationStatus.Run(RcQualificationStatusOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}