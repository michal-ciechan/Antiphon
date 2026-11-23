// CARD-1842: antiphon-scripts export-expected-duration-roster [--cost-path <json>] [--output-path <json>]. Writes
// tests/Shared/expected-duration-roster.json from scripts/lib/nightly-class-costs.json
// (Antiphon.Nightly.ExpectedDurationRosterFile.Run on the production host); it replaced
// scripts/export-expected-duration-roster.ps1 (deleted 2026-11-23, unit 5 of 2026-11-22-remaining-ps1-to-csharp.md), with
// the same flags in kebab case. Both paths default to the checkout this executable was built in. ExpectedDurationSyncTests
// fails until it is re-run after a class-cost refresh. Exit 0 written; 1 refused (one stderr line); 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ExportExpectedDurationRosterCommand
    {
        internal const string Name = "export-expected-duration-roster";

        internal const string Usage = ExportExpectedDurationRosterOptions.Usage;

        internal static int Run(string[] args)
        {
            var current = Environment.CurrentDirectory;
            var options = ExportExpectedDurationRosterOptions.Parse(args.Skip(1).ToArray(), current, SourceCheckout.Root() ?? current);
            return ExpectedDurationRosterFile.Run(options, NightlyHost.Production(Console.Out, Console.Error));
        }
    }
}
