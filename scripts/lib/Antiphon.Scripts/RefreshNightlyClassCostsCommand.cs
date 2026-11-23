// CARD-1410 / CARD-1842: antiphon-scripts refresh-nightly-class-costs --trx-path <a.trx,b.trx> --host-id <host>
// --os <windows|linux> --measured-run-id <run> [--output-path <json>] [--source <text>] [--set-platform]. Rewrites
// scripts/lib/nightly-class-costs.json from nightly TRX files (Antiphon.Nightly.ClassCostRefresh.Run on the production
// host); it replaced scripts/refresh-nightly-class-costs.ps1 (deleted 2026-11-23, unit 5 of
// 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case (the TRX list comma separated). The output
// defaults to the checkout this executable was built in. Re-run export-expected-duration-roster afterwards. Exit 0
// written; 1 refused (one stderr line); 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RefreshNightlyClassCostsCommand
    {
        internal const string Name = "refresh-nightly-class-costs";

        internal const string Usage = RefreshNightlyClassCostsOptions.Usage;

        internal static int Run(string[] args)
        {
            var current = Environment.CurrentDirectory;
            var options = RefreshNightlyClassCostsOptions.Parse(args.Skip(1).ToArray(), current, SourceCheckout.Root() ?? current);
            return ClassCostRefresh.Run(options, NightlyHost.Production(Console.Out, Console.Error));
        }
    }
}
