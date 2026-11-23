// CARD-1753: antiphon-scripts land-graph-land-refresh --base <sha> [--repo <dir>] [--card <id>] [--note-path <file>].
// Refreshes the land-graph records for one landing batch and notifies, never asking (Antiphon.Nightly.LandGraphLandRefresh
// on the production host, the real file system and an HTTP transport with the 5 s deadline). It replaced
// scripts/land-graph-land-refresh.ps1 (deleted 2026-11-23, unit 11 of 2026-11-22-remaining-ps1-to-csharp.md). This
// command reads ANTIPHON_API and ANTIPHON_TASK_TOKEN (the card note) and this process's environment (the children's)
// and passes them in; --repo defaults to the checkout this executable was built in (else the current directory's).
// Exit 0 current, refreshed or an inherited mismatch warned (the LAND-GRAPH-NOTICE lines on stdout); 1 refused (one
// LAND-GRAPH-NOTICE action=refuse line) or failed (one stderr line); 2 usage. See docs/orchestration-loop.md
// (#land-graph-refresh). ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class LandGraphLandRefreshCommand
    {
        internal const string Name = "land-graph-land-refresh";

        internal const string Usage = LandGraphLandRefreshOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = LandGraphLandRefreshOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? Environment.CurrentDirectory,
                Environment.GetEnvironmentVariable("ANTIPHON_API"), Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN"), LandGraphAuditCommand.EnvironmentSnapshot());
            return LandGraphLandRefresh.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(),
                NightlyReport.HttpTransport(timeout: TimeSpan.FromSeconds(5)));
        }
    }
}
