// antiphon-scripts summarize-test-trace --path <trace.jsonl> [--top 25] [--test <substring>] [--min-ms 1]. Summarizes a
// test trace file written by ANTIPHON_TEST_TRACE_FILE (tests/Shared/TestTrace.cs) on stdout
// (Antiphon.Nightly.TestTraceSummary.Run on the production host); it replaced scripts/summarize-test-trace.ps1 (deleted
// 2026-11-23, unit 5 of 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. It only reads. Exit 0
// printed; 1 the file cannot be read or holds no span (one stderr line); 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class SummarizeTestTraceCommand
    {
        internal const string Name = "summarize-test-trace";

        internal const string Usage = SummarizeTestTraceOptions.Usage;

        internal static int Run(string[] args) =>
            TestTraceSummary.Run(SummarizeTestTraceOptions.Parse(args.Skip(1).ToArray(), Environment.CurrentDirectory),
                NightlyHost.Production(Console.Out, Console.Error));
    }
}
