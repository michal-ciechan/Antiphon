// CARD-1311: antiphon-scripts promote-release --tag <vYYYY.MM.DD.N-rc.K> --candidate-id <id> [--release-root <dir>]
// [--checkout-root <dir>] [--repository <owner/name>] [--antiphon-api-url <url>] [--what-if]. Writes the final
// vYYYY.MM.DD.N at the pre-release tag's peeled SHA (Antiphon.Nightly.ReleasePromote.Run on the production release-gate
// host). It replaced scripts/promote-release.ps1 (deleted 2026-11-23, unit 14 of 2026-11-22-remaining-ps1-to-csharp.md),
// with the same flags in kebab case. This command reads ANTIPHON_API and ANTIPHON_RELEASE_GATE_TEST_MODE and passes them in;
// the pinned scripts and source trees are those of the checkout this executable was built in (else the current
// directory's). The [promote-release] lines, then one compact JSON line on stdout. Exit 0 promoted, resumed or --what-if;
// 1 a remote write or the journal record failed; 2 usage; 3 refused. An operator step: never run it from a delegate.
// ASCII only.
using System;
using System.IO;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PromoteReleaseCommand
    {
        internal const string Name = "promote-release";

        internal const string Usage = ReleasePromoteOptions.Usage;

        internal static int Run(string[] args)
        {
            var scripts = Path.Combine(SourceCheckout.Root() ?? Environment.CurrentDirectory, "scripts");
            var options = ReleasePromoteOptions.Parse(args.Skip(1).ToArray(), scripts, Environment.GetEnvironmentVariable);
            var result = ReleasePromote.Run(options, ReleaseGateHost.Production(Console.Out));
            Console.Out.WriteLine(result.ToJson());
            return result.ExitCode;
        }
    }
}
