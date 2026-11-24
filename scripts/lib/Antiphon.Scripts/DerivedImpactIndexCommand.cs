// CARD-1838: antiphon-scripts regenerate-derived-impact-index [--repo <dir>] [--no-build]. It replaced
// scripts/regenerate-derived-impact-index.ps1 (deleted 2026-11-24, unit 41 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), with -NoBuild and -Repo in kebab case. The behaviour is
// Antiphon.Nightly.ImpactIndexRegeneration on the production host (dotnet build, then dotnet run of the driver project,
// both sharing this console) and the real file system. This file reads the environment and passes it in: the checkout
// this executable was built in (else the current directory's), the current directory (a relative --repo) and
// ANTIPHON_BUILD_SLOT_MAXCPUCOUNT (the build's -maxcpucount under a build-slot lease). Exit 0 the index was written
// (changed or unchanged); 1 the driver project is missing, or the build or the driver failed; 2 usage. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class DerivedImpactIndexCommand
    {
        internal const string Name = "regenerate-derived-impact-index";

        internal const string Usage = ImpactIndexRegenerationOptions.Usage;

        internal static int Run(string[] args)
        {
            var current = Environment.CurrentDirectory;
            var options = ImpactIndexRegenerationOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? current, current,
                Environment.GetEnvironmentVariable("ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"));
            return ImpactIndexRegeneration.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }
    }
}
