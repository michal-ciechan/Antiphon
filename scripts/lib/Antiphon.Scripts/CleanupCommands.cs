// The two cleanup commands Windmill reaches through their thin launchers (unit 21 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): cleanup-build-junk (scripts/cleanup-build-junk.ps1,
// u/lndcobra/antiphon_build_junk_cleanup) and cleanup-codex-test-residue (scripts/cleanup-codex-test-residue.ps1,
// u/lndcobra/antiphon_codex_residue_cleanup). The behaviour is Antiphon.Nightly's BuildJunkCleanup and CodexTestResidue on the
// production host; this file reads the environment and passes it in. A separate process holds no producer creation records, so
// cleanup-build-junk here always leaves every bin-* output in place (a producer calls BuildJunkCleanup.Run in process with its
// ledger). The repository defaults (the bin-* walk root, the residue report directory logs/codex-test-residue) are the
// checkout this executable was built in, else the current directory. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class CleanupCommands
    {
        internal const string BuildJunk = "cleanup-build-junk";
        internal const string CodexResidue = "cleanup-codex-test-residue";

        internal const string Usage = BuildJunkCleanupOptions.Usage + " | " + CodexTestResidueOptions.Usage;

        // Exit 0 (the count on the last line).
        internal static int RunBuildJunk(string[] args)
        {
            var options = BuildJunkCleanupOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? Environment.CurrentDirectory);
            BuildJunkCleanup.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), removeOwned: null);
            return 0;
        }

        // Exit 0 done; 1 the threads could not be read; 4 over --max-per-run; 5 a delete failed; 2 usage. CODEX_HOME and
        // USERPROFILE (HOME elsewhere) choose the default Codex home.
        internal static int RunCodexResidue(string[] args)
        {
            var profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(profile)) profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var options = CodexTestResidueOptions.Parse(args.Skip(1).ToArray(), Environment.GetEnvironmentVariable("CODEX_HOME"), profile,
                SourceCheckout.Root() ?? Environment.CurrentDirectory, Path.GetTempPath());
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return CodexTestResidue.Run(options, host, new FileSystem(), CodexTestResidue.Production(host)).ExitCode;
        }
    }
}
