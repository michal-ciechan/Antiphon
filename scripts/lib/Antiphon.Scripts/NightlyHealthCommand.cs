// W2-nightly S05: antiphon-scripts nightly-health [--state-root <dir>] [--expected-script-hash <hash>]
// [--expected-policy-hash <hash>] [--authorized-destination <id>] [--repository-path <dir>] [--project-id <guid>]
// [--watchdog-snapshot-url <url>] [--expected-watchdog-instance-id <id>] [--readiness-config-path <json>]
// [--result-file <json>]. The nightly readiness evaluator (Antiphon.Nightly.NightlyHealth.Run with the production
// seams); scripts/nightly-health.ps1 is its Windows PowerShell 5.1 launcher (Windmill u/lndcobra/antiphon_nightly_readiness).
// It writes last-monitor.json under the state root and, with --result-file, the same record there. Nothing goes to
// stdout; a failed read or write is one stderr line. Exit 0 healthy, 1 unhealthy, 2 usage. There is no seams flag.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlyHealthCommand
    {
        internal const string Name = "nightly-health";

        internal const string Usage =
            "nightly-health [--state-root <dir>] [--expected-script-hash <hash>] [--expected-policy-hash <hash>] [--authorized-destination <id>]"
            + " [--repository-path <dir>] [--project-id <guid>] [--watchdog-snapshot-url <url>] [--expected-watchdog-instance-id <id>]"
            + " [--readiness-config-path <json>] [--result-file <json>]";

        private static readonly string[] Valued =
        {
            "--state-root", "--expected-script-hash", "--expected-policy-hash", "--authorized-destination", "--repository-path", "--project-id",
            "--watchdog-snapshot-url", "--expected-watchdog-instance-id", "--readiness-config-path", "--result-file",
        };

        internal static int Run(string[] args)
        {
            var flags = NightlyArgs.Parse(args.Skip(1).ToArray(), Valued, Array.Empty<string>());
            var stateRoot = flags.Value("--state-root");
            var options = new NightlyHealthOptions
            {
                StateRoot = string.IsNullOrEmpty(stateRoot) ? NightlyHealth.DefaultStateRoot : stateRoot,
                ExpectedScriptHash = flags.Value("--expected-script-hash", ""),
                ExpectedPolicyHash = flags.Value("--expected-policy-hash", ""),
                AuthorizedDestination = flags.Value("--authorized-destination", ""),
                RepositoryPath = flags.Value("--repository-path", ""),
                ProjectId = flags.Value("--project-id", ""),
                WatchdogSnapshotUrl = flags.Value("--watchdog-snapshot-url", ""),
                ExpectedWatchdogInstanceId = flags.Value("--expected-watchdog-instance-id", ""),
                ReadinessConfigPath = flags.Value("--readiness-config-path", ""),
            };
            NightlyHealthResult result;
            try
            {
                result = NightlyHealth.Run(options, NightlyHealthSeams.Production(Environment.GetEnvironmentVariable, Console.Error));
            }
            catch (Exception e)
            {
                // Nothing was recorded: last-monitor.json goes stale and readiness fails closed.
                Console.Error.WriteLine("nightly-health: " + e.GetType().Name + ": " + NightlyHealth.Message(e));
                return 1;
            }
            var resultFile = flags.Value("--result-file");
            if (!string.IsNullOrEmpty(resultFile))
            {
                try { NightlyHealth.WriteResult(resultFile, result); }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("nightly-health: cannot write " + resultFile + ": " + NightlyHealth.Message(e));
                }
            }
            return result.ExitCode;
        }
    }
}
