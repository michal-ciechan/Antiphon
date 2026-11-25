// CARD-1776: antiphon-scripts server2-repository-mirrors --table-file <file> [--what-if | --execute] [--token-file <file>]
// [--policy-file <file>] [--repos-root <dir>]. The repository mirrors for Linux land checkouts (Antiphon.Nightly's
// ServerRepositoryMirrors on the production host, the real file system and this machine's link test and git resolution). It
// replaced scripts/server2-repository-mirrors.ps1 (unit 52 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8
// slice C); nothing started the script by path, so it keeps no launcher. This command passes this process's environment in (each
// git child's, with GIT_TERMINAL_PROMPT=0). Exit 0 plan or ok, 2 refusal, 3 ambiguous-mode, 1 an I/O failure; usage 2. ASCII only.
using System;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class Server2RepositoryMirrorsCommand
    {
        internal const string Name = "server2-repository-mirrors";

        internal const string Usage = ServerRepositoryMirrorsOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = ServerRepositoryMirrorsOptions.Parse(args.Skip(1).ToArray(), LandGraphAuditCommand.EnvironmentSnapshot());
            return ServerRepositoryMirrors.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }
    }
}
