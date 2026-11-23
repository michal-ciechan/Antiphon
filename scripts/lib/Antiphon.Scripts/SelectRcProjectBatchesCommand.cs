// CARD-1545: antiphon-scripts select-rc-project-batches --baseline-sha <sha> --candidate-sha <sha> [--repository-root <dir>]
// [--output-path <file>]. The advisory rc project batches of a baseline and a candidate (Antiphon.Nightly.RcProjectSelection.Run
// on the production host); it replaced scripts/select-rc-project-batches.ps1 and scripts/lib/rc-project-selection.ps1
// (deleted 2026-11-23, unit 8 of 2026-11-22-remaining-ps1-to-csharp.md). The repository defaults to the checkout this
// executable was built in (the script's own checkout), else the current directory. The selection's canonical JSON (UTF-8)
// on stdout, and once into --output-path when given (an existing file is never replaced). Shadow only: exit 0 proposed;
// 2 refused ("project selection refused: <reason>" on stderr) or usage. ASCII only.
using System;
using System.Linq;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class SelectRcProjectBatchesCommand
    {
        internal const string Name = "select-rc-project-batches";

        internal const string Usage = SelectRcProjectBatchesOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = SelectRcProjectBatchesOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? Environment.CurrentDirectory);
            // The JSON pipe is UTF-8 even when a Windows child has no console code page.
            Console.OutputEncoding = new UTF8Encoding(false);
            return RcProjectSelection.Run(options, NightlyHost.Production(Console.Out, Console.Error));
        }
    }
}
