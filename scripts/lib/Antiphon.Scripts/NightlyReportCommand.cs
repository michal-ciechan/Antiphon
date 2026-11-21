// W2-nightly S06: antiphon-scripts nightly-report --summary <summary.json> [--previous-summary <summary.json>] [--dry-run]
// [--board <name|guid>] [--api <url>]. The nightly run card (Antiphon.Nightly.NightlyReport.Run with the production seams:
// the system clock, HTTP without a token, ANTIPHON_API). It replaced scripts/nightly-report.ps1; the run core's report hop
// (scripts/lib/nightly-run-impl.ps1) starts it through scripts/lib/antiphon-cli.ps1. Its lines go to stdout. Exit 0 done
// (dry-run and nothing to file included), 1 the summary is missing or unreadable, 2 usage, 3 the board could not be read
// or written (card.md is written next to the summary). There is no seams flag.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlyReportCommand
    {
        internal const string Name = "nightly-report";

        internal const string Usage = NightlyReportOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = NightlyReportOptions.Parse(args.Skip(1).ToArray());
            var seams = NightlyReportSeams.Production(NightlyHost.Production(Console.Out, Console.Error), Environment.GetEnvironmentVariable);
            return NightlyReport.Run(options, seams).ExitCode;
        }
    }
}
