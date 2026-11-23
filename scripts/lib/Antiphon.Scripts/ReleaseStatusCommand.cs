// CARD-0599 D-9: antiphon-scripts release-status [--release-root <dir>] [--version-url <url>] [--sha <sha>]. Whether the
// running build is a published release (Antiphon.Nightly.ReleaseStatus.Run on the production host and an HTTP transport
// bounded at 10 s): the running SHA is looked up by exact match in the published release manifests; missing data is
// 'unknown', never the latest release. It replaced scripts/release-status.ps1 (deleted 2026-11-23, unit 12 of
// 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. One compact JSON line on stdout; exit 0
// (2 usage). Read-only: no restart, deploy, checkout or kill. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ReleaseStatusCommand
    {
        internal const string Name = "release-status";

        internal const string Usage = ReleaseStatusOptions.Usage;

        internal static int Run(string[] args)
        {
            ReleaseStatus.Run(ReleaseStatusOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error),
                NightlyReport.HttpTransport(timeout: ReleaseStatus.VersionTimeout));
            return 0;
        }
    }
}
