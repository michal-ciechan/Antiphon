// W2-nightly S26: antiphon-scripts export-nightly-prior-failures --run-directory <dir> --output-path <file>,
// antiphon-scripts export-nightly-stopped-run-seed --run-directory <dir> --output-path <file> [--source-run-id <id>]
// [--source-candidate-id <id>] [--source-sha <sha>] [--timing-root <dir>], and antiphon-scripts merge-nightly-seeds with the
// operator's merge argv (-Seed and -Proven repeat). They replaced scripts/export-nightly-prior-failures.ps1,
// scripts/export-nightly-stopped-run-seed.ps1 and scripts/merge-nightly-seeds.ps1. Antiphon.Nightly.NightlySeedCommands
// is the command (its header gives the outputs and exit codes); this hands it the argv and the console's streams.
// There is no seams flag.
using System;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlySeedCommand
    {
        internal const string Usage = NightlySeedCommands.Usage;

        internal static int Run(string[] args) => NightlySeedCommands.Run(args, Console.Out, Console.Error);
    }
}
