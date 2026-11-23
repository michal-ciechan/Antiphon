// CARD-0545: antiphon-scripts deploy-nightly-watchdog [--profile <deploy-profile.json>] [--deploy]. The preflight (default,
// read-only on the host) or deploy of the independent nightly watchdog (Antiphon.Nightly.NightlyWatchdogDeploy.Run on the
// production host, the real file system and an HTTP transport with the 15 s deadline). It replaced
// scripts/deploy-nightly-watchdog.ps1 (deleted 2026-11-23, unit 10 of 2026-11-22-remaining-ps1-to-csharp.md). This
// command reads ANTIPHON_WATCHDOG_DEPLOY_PROFILE and passes it in; the watchdog project, templates and *.local.json
// profiles are the checkout this executable was built in (else the current directory's). Exit 0 done; 1 failed (one
// stderr line); 2 usage; 3 no or invalid deploy profile (nothing contacted). See docs/nightly-watchdog.md. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DeployNightlyWatchdogCommand
    {
        internal const string Name = "deploy-nightly-watchdog";

        internal const string Usage = NightlyWatchdogDeployOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = NightlyWatchdogDeployOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable("ANTIPHON_WATCHDOG_DEPLOY_PROFILE"),
                SourceCheckout.Root() ?? Environment.CurrentDirectory);
            return NightlyWatchdogDeploy.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(),
                NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(15))).ExitCode;
        }
    }
}
