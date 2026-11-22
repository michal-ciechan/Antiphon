// W2-nightly S19: antiphon-scripts nightly-tests <flags>. The nightly tests run (Antiphon.Nightly.NightlyTests.Run with the
// production host: the system clock, git, the watched launcher, the process table and the host probe), so the C# run core
// (NightlyRun, not wired until S20) can hop to it with `dotnet exec <this dll> nightly-tests`. scripts/nightly-tests.ps1
// still runs the PowerShell for every production caller; S17 makes this the command and deletes it. The flags are
// NightlyTestsOptions' (section 4 of the plan; there is no seams flag). Evidence writes are deferred (write-behind) for
// the run, as the entry script does, unless ANTIPHON_NIGHTLY_DEFERRED_WRITES is off; a deferred write that failed is one
// stderr warning. Exit: the run's (0 green or WhatIf, 1 failed, 2 refused before the run), 3 on a refusal; a summary that
// could not be written is one stderr line and exit 1. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlyTestsCommand
    {
        internal const string Name = "nightly-tests";

        internal const string Usage = "nightly-tests [--repo-root <dir>] [--log-root <dir>] [--suites <a,b>] [--sha <sha>] [--profile <nightly|rc>] ... [--what-if]";

        internal static int Run(string[] args)
        {
            var options = NightlyTestsOptions.Parse(args.Skip(1).ToArray());
            var deferred = !string.Equals(Environment.GetEnvironmentVariable("ANTIPHON_NIGHTLY_DEFERRED_WRITES"), "off", StringComparison.OrdinalIgnoreCase);
            if (deferred) EvidenceFiles.EnableWriteBehind();
            try
            {
                return NightlyTests.Run(options, NightlyHost.Production(Console.Out, Console.Error)).ProcessExitCode;
            }
            catch (Exception e) when (!(e is NightlyUsageException))
            {
                Console.Error.WriteLine("nightly-tests: " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
            finally
            {
                if (deferred)
                    foreach (var failure in EvidenceFiles.DisableWriteBehind()) Console.Error.WriteLine("WARNING: deferred-evidence-write-failed: " + failure);
            }
        }
    }
}
