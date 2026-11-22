// CARD-1313: antiphon-scripts test-timing-event --root <dir> --event <json>. Appends one event to the
// antiphon-test-timing-v1 journal under the root (Antiphon.Nightly.TestTimings.Run, the writer of the deleted
// scripts/lib/test-timings.ps1 Write-TestTimingEvent): run-checkpoint.ps1's Write-WrapperTiming runs it through
// scripts/lib/antiphon-cli.ps1 and ignores its exit (a failed write is not a test result). Nothing on stdout. Exit 0
// written or already there; 1 refused (path-component-refused, event-conflict, event-not-an-object or a write that
// failed: one stderr line); 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class TestTimingEventCommand
    {
        internal const string Name = "test-timing-event";

        internal const string Usage = TestTimingEventOptions.Usage;

        internal static int Run(string[] args) =>
            TestTimings.Run(TestTimingEventOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error));
    }
}
