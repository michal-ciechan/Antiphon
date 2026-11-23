// CARD-1842 S4: antiphon-scripts qualification-eta --timing-root <dir> [--now-utc <iso-8601>] [--class-cost-path <json>].
// The read-only batch ETA of a qualification timing root (Antiphon.Nightly.QualificationEta.Run on the production host
// and the real file system); it replaced scripts/qualification-eta.ps1 (deleted 2026-11-23, unit 6 of
// 2026-11-22-remaining-ps1-to-csharp.md). One "ETA outstanding=... p95=..." line on stdout; --now-utc defaults to the
// clock. It reads; it never writes the timing root. Exit 0 printed; 1 refused or unreadable (one stderr line); 2 usage,
// an unreadable --now-utc included. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationEtaCommand
    {
        internal const string Name = "qualification-eta";

        internal const string Usage = QualificationEtaOptions.Usage;

        internal static int Run(string[] args) =>
            QualificationEta.Run(QualificationEtaOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
    }
}
