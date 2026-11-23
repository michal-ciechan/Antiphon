// CARD-0599 D-7: antiphon-scripts register-release-gates --profile <profile.json> [--apply] [--enable-schedule
// nightly|readiness|rc] [--definition-root <dir>]. The idempotent registration front door for the three release-gate
// Windmill jobs (Antiphon.Nightly.ReleaseGateRegistration.Run on the production host and the HTTP transport): preview by
// default (no write, no token read); --apply registers or reconciles through the Windmill HTTP API with the token from the
// profile's tokenFile (never printed); --enable-schedule turns on one named schedule and requires --apply; schedules are
// created disabled. The definitions default to scripts/windmill of the checkout this executable was built in (else the
// current directory's). It replaced scripts/register-release-gates.ps1 (deleted 2026-11-23, unit 12 of
// 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. Exit 0 done; 1 a write or readback failed;
// 2 usage; 3 refused. An operator S5/S6 step: never run against the live workspace from a delegate. ASCII only.
using System;
using System.IO;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RegisterReleaseGatesCommand
    {
        internal const string Name = "register-release-gates";

        internal const string Usage = ReleaseGateRegistrationOptions.Usage;

        internal static int Run(string[] args)
        {
            var definitions = Path.Combine(SourceCheckout.Root() ?? Environment.CurrentDirectory, "scripts", "windmill");
            return ReleaseGateRegistration.Run(ReleaseGateRegistrationOptions.Parse(args.Skip(1).ToArray(), definitions),
                NightlyHost.Production(Console.Out, Console.Error), NightlyReport.HttpTransport()).ExitCode;
        }
    }
}
