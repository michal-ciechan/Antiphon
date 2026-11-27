// CARD-2093: the read-only scheduled TRX forecast. Prints an ETA line and any UNVERIFIED observations.
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
