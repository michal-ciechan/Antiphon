// CARD-1313: antiphon-scripts select-test-rosters --baseline-sha <sha> --candidate-sha <sha> [--profile rc]
// [--repository-root <dir>] [--output-path <file>] [--json] [--timing-roots <dir,dir>]. The change-aware selection
// proposal (Antiphon.Nightly.TestSelection.Run on the production host); it replaced scripts/select-test-rosters.ps1
// (deleted 2026-11-22, unit 3 of 2026-11-22-remaining-ps1-to-csharp.md). The repository defaults to the current
// directory. --json prints the selection's canonical JSON (UTF-8), otherwise the summary lines. Intent only: exit 0
// proposed; 2 refused ("selection refused: <reason>" on stderr) or usage. ASCII only.
using System;
using System.Linq;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class TestSelectionCommand
    {
        internal const string Name = "select-test-rosters";

        internal const string Usage = TestSelectionOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = TestSelectionOptions.Parse(args.Skip(1).ToArray(), Environment.CurrentDirectory);
            // The JSON pipe is UTF-8 even when a Windows child has no console code page.
            if (options.Json) Console.OutputEncoding = new UTF8Encoding(false);
            return Antiphon.Nightly.TestSelection.Run(options, NightlyHost.Production(Console.Out, Console.Error));
        }
    }
}
