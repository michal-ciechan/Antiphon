// CARD-1782: antiphon-scripts impacted-classes --range <git-range> | --path <a,b> | --file-list <file> | --merge-subset
// [...] [--json] [--audit] [--repo <dir>] [--table <json>] | --release-evidence ... The pre-land impacted-classes selector
// (Antiphon.Nightly.ImpactedClasses.Run on the production host); it replaced scripts/impacted-classes.ps1 (deleted
// 2026-11-22, unit 4 of 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. The repository
// defaults to the checkout this executable was built in (the script's own checkout), and so does the release
// evidence's algorithm source. --json prints UTF-8. Exit codes are the selector's. ASCII only.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ImpactedClassesCommand
    {
        internal const string Name = "impacted-classes";

        internal const string Usage = ImpactedClassesOptions.Usage;

        internal static int Run(string[] args)
        {
            var current = Environment.CurrentDirectory;
            var options = ImpactedClassesOptions.Parse(args.Skip(1).ToArray(), current, SourceRoot() ?? current);
            // The JSON pipe is UTF-8 even when a Windows child has no console code page.
            if (options.Json) Console.OutputEncoding = new UTF8Encoding(false);
            return ImpactedClasses.Run(options, NightlyHost.Production(Console.Out, Console.Error));
        }

        // The checkout this executable was built in (scripts/antiphon builds it under <checkout>/bin-launcher), else the
        // one the current directory is in.
        private static string SourceRoot()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "scripts", "lib", "Antiphon.Nightly", "Antiphon.Nightly.csproj"))) return directory.FullName;
            }
            return null;
        }
    }
}
