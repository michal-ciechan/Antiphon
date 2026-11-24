// CARD-1698: antiphon-scripts toolchain-floor --repo-root <dir>. The host toolchain floor warnings
// (Antiphon.Nightly.ToolchainFloor.Run on the production host); it replaced scripts/lib/toolchain-floor.ps1
// (Write-ToolchainFloorWarnings, deleted 2026-11-22). The restart-session-runner and restart-server commands run
// ToolchainFloor.Run in process (units 30 and 37). One WARNING line per tool
// below its floor, or whose floor or version cannot be read, on stdout as Write-Host printed them (yellow on a
// console); nothing else. Warn, never refuse: exit 0 whatever the warnings; 2 usage. ASCII only.
using System;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ToolchainFloorCommand
    {
        internal const string Name = "toolchain-floor";

        internal const string Usage = ToolchainFloorOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = ToolchainFloorOptions.Parse(args.Skip(1).ToArray());
            // The warnings were Write-Host -ForegroundColor Yellow: the same colour on a console, plain text when redirected.
            var colour = !Console.IsOutputRedirected;
            if (colour) Console.ForegroundColor = ConsoleColor.Yellow;
            try
            {
                ToolchainFloor.Run(options, NightlyHost.Production(Console.Out, Console.Error));
            }
            finally
            {
                if (colour) Console.ResetColor();
            }
            return Program.Handled;
        }
    }
}
