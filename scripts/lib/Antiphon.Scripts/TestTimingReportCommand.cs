// CARD-1313: antiphon-scripts test-timing-report [--roots <dir,dir>] [--group-by class|roster|category] [--top 20]
// [--selection-path <selection.json>]. The timing report over the antiphon-test-timing-v1 journals
// (Antiphon.Nightly.TestTimings.Run); it replaced scripts/test-timing-report.ps1 (deleted 2026-11-22). One JSON line on
// stdout: the report (antiphon-test-timing-report-v1), its groups ranked by median and cut to --top; with
// --selection-path, that selection with the shown groups' estimates instead. It reads; it never changes a result or a
// selection file. Exit 0; 1 the selection cannot be read (one stderr line); 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class TestTimingReportCommand
    {
        internal const string Name = "test-timing-report";

        internal const string Usage = TestTimingReportOptions.Usage;

        internal static int Run(string[] args) =>
            TestTimings.Run(TestTimingReportOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error));
    }
}
