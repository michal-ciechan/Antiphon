// antiphon-scripts qualification-preflight --checkout-root <dir>. Refuses a release checkout whose local core.longpaths is
// not true (CARD-2044; Antiphon.Nightly.QualificationLaunch.Preflight on the production host's git). It replaced
// scripts/qualification/preflight.ps1 (deleted 2026-11-23, unit 7 of 2026-11-22-remaining-ps1-to-csharp.md). Exit 0
// core.longpaths true; 2 "REFUSED: longpaths-not-enabled" and the fix command on stdout, or usage. It does not change the
// checkout. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class QualificationPreflightCommand
    {
        internal const string Name = "qualification-preflight";

        internal const string Usage = QualificationPreflightOptions.Usage;

        internal static int Run(string[] args) =>
            QualificationLaunch.Preflight(QualificationPreflightOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error));
    }
}