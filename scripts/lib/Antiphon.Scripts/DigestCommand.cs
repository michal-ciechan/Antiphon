// CARD-0036: antiphon-scripts digest [--channel <guid|title>] [--since <time>] [--base-url http://localhost:17202]. Sends
// Antiphon's server-composed away digest now (Antiphon.Nightly.AwayDigestSend.Run on the production host and the board
// client's HTTP transport); it replaced scripts/digest.ps1 (deleted 2026-11-23, unit 5 of
// 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. The server's answer goes to stdout. Exit 0
// sent; 1 the channel is not unique, the server refused or a channel's result is send_failed (one stderr line); 2 usage.
// ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DigestCommand
    {
        internal const string Name = "digest";

        internal const string Usage = AwayDigestSendOptions.Usage;

        internal static int Run(string[] args) =>
            AwayDigestSend.Run(AwayDigestSendOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error), NightlyReport.HttpTransport());
    }
}
