// CARD-0599 D-8/D-15: antiphon-scripts publish-release --candidate-id <id> [--release-root <dir>] [--checkout-root <dir>]
// [--repository <owner/name>] [--expected-policy-hash <hash>] [--required-suites <a,b>] [--antiphon-api-url <url>]
// [--what-if]. Publishes one CalVer tag and GitHub release for an RC complete-green candidate
// (Antiphon.Nightly.ReleasePublish.Run on the production release-gate host). It replaced scripts/publish-release.ps1
// (deleted 2026-11-23, unit 14 of 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. This command
// reads ANTIPHON_API (the release card's API when --antiphon-api-url is not given) and ANTIPHON_RELEASE_GATE_TEST_MODE and
// passes them in; the pinned scripts and source trees are those of the checkout this executable was built in (else the
// current directory's). The [publish-release] lines, then one compact JSON line on stdout. Exit 0 published or
// --what-if; 1 a remote write failed; 2 usage; 3 refused. An operator step: never run it from a delegate. ASCII only.
using System;
using System.IO;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class PublishReleaseCommand
    {
        internal const string Name = "publish-release";

        internal const string Usage = ReleasePublishOptions.Usage;

        internal static int Run(string[] args)
        {
            var scripts = Path.Combine(SourceCheckout.Root() ?? Environment.CurrentDirectory, "scripts");
            var options = ReleasePublishOptions.Parse(args.Skip(1).ToArray(), scripts, Environment.GetEnvironmentVariable);
            var result = ReleasePublish.Run(options, ReleaseGateHost.Production(Console.Out));
            Console.Out.WriteLine(result.ToJson());
            return result.ExitCode;
        }
    }
}
