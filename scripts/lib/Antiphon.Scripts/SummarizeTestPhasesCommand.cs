// W2-nightly S09: antiphon-scripts summarize-test-phases --run-directory <dir> [--top 50] [--recover-executed-manifest]
// [--timing-root <dir>] (formerly scripts/summarize-test-phases.ps1). Writes timing/summary.json, timing/tests.csv and
// timing/summary.md of a qualification log directory (Antiphon.Nightly.TestPhaseRunSummary.Summarize); never edits TRX,
// the run's summary.json, receipts or the cost model. stdout: one publication progress line when a journal or flat
// executed manifest is present (the live progress signal; a flat file timestamp is not). --recover-executed-manifest takes
// the writer lock and rebuilds the flat projection and state from the committed journal first (refuses a live writer, never
// writes a terminal seal). Exit 0; 1 when it failed (one stderr line); 2 usage. There is no seams flag.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class SummarizeTestPhasesCommand
    {
        internal const string Name = "summarize-test-phases";

        internal const string Usage = "summarize-test-phases --run-directory <dir> [--top 50] [--recover-executed-manifest] [--timing-root <dir>]";

        private static readonly string[] Valued = { "--run-directory", "--top", "--timing-root" };
        private static readonly string[] Switches = { "--recover-executed-manifest" };

        internal static int Run(string[] args)
        {
            var flags = NightlyArgs.Parse(args.Skip(1).ToArray(), Valued, Switches);
            var options = new TestPhaseSummarizeOptions
            {
                RunDirectory = flags.Required("--run-directory"),
                Top = flags.Int("--top", 50),
                RecoverExecutedManifest = flags.Switch("--recover-executed-manifest"),
                TimingRoot = flags.Value("--timing-root", ""),
            };
            return TestPhaseRunSummary.Summarize(options, TestPhaseRunSummary.CommandScope(), Console.Out, Console.Error);
        }
    }
}
