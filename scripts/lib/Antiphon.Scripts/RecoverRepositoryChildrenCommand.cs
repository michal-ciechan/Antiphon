// CARD-0448: antiphon-scripts recover-repository-children --repository <checkout> [--execute] [--confirm-descendants-exited].
// Previews, or with both switches recovers, the crash-orphaned repository child journals that fence every land and dispatch
// (Antiphon.Nightly.RepositoryChildRecovery.Run on the production host's git, the real file system and the process identity
// read now). It replaced scripts/recover-repository-children.ps1 (deleted 2026-11-23, unit 23 of
// 2026-11-22-remaining-ps1-to-csharp.md), with the same flags in kebab case. Exit 0 no record retained; 3 busy, retained or
// unconfirmed evidence, or refused; 2 usage. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class RecoverRepositoryChildrenCommand
    {
        internal const string Name = "recover-repository-children";

        internal const string Usage = RepositoryChildRecoveryOptions.Usage;

        internal static int Run(string[] args) =>
            RepositoryChildRecovery.Run(RepositoryChildRecoveryOptions.Parse(args.Skip(1).ToArray()), NightlyHost.Production(Console.Out, Console.Error),
                new FileSystem(), RepositoryChildRecovery.Identify);
    }
}
