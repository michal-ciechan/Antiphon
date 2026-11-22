// W2-nightly S17: antiphon-scripts nightly-tests <flags>, the nightly tests run (Antiphon.Nightly.NightlyTests.Run). It
// replaces scripts/nightly-tests.ps1 and scripts/lib/nightly-tests-impl.ps1 (deleted): the C# run core (NightlyRun, the
// nightly-run command since S20) runs it with `dotnet exec <this dll> nightly-tests`. The host is the production one (the system clock, git, both launch custodies, the process table, the host
// probe), its owned children prepared as Set-AntiphonBuildDriverEnvironment prepared them; the bindings are
// NightlyTestsBindings.Production (the evidence recorder, the real E2E check, the heartbeat's active-chunk identity and
// this checkout's class-cost file), as nightly-tests-impl.ps1 bound them. The flags are NightlyTestsOptions' (section 4
// of the plan; there is no seams flag). Evidence writes are deferred (write-behind) for the run, as the entry script did,
// unless ANTIPHON_NIGHTLY_DEFERRED_WRITES is off; a deferred write that failed is one stderr warning. Exit: the run's (0
// green or WhatIf, 1 failed, 2 refused before the run), 3 on a refusal; a summary that could not be written is one stderr
// line and exit 1. ASCII only.
using System;
using System.Diagnostics;
using System.Linq;
using Antiphon.Build;
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
                var host = NightlyHost.Production(Console.Out, Console.Error, PrepareChild);
                return NightlyTests.Run(options, host, NightlyTestsBindings.Production(host)).ProcessExitCode;
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

        /// <summary>
        /// Set-AntiphonBuildDriverEnvironment (scripts/lib/build-driver-environment.ps1) for an owned child: on Windows the
        /// build-driver policy's variables (BuildDriverEnvironment, CARD-1506); elsewhere only when the child's environment
        /// opts in with ANTIPHON_LINUX_COMPILER_POLICY=1.
        /// </summary>
        internal static void PrepareChild(ProcessStartInfo start)
        {
            if (start == null) return;
            var windows = OperatingSystem.IsWindows();
            if (!windows && (!start.EnvironmentVariables.ContainsKey("ANTIPHON_LINUX_COMPILER_POLICY")
                || !string.Equals(start.EnvironmentVariables["ANTIPHON_LINUX_COMPILER_POLICY"], "1", StringComparison.Ordinal))) return;
            var policy = new BuildDriverEnvironment();
            if (windows) policy.Apply(start.EnvironmentVariables, true);
            else policy.Apply(start, 0);
        }
    }
}
