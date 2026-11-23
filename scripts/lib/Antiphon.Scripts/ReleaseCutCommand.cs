// CARD-1311: antiphon-scripts release-cut --ref <rc/YYYY-MM-DD> [--release-root <dir>] [--coordination-root <dir>]
// [--remote-url <url>] [--repository <owner/name>] [--run-id <id>] [--schedule-slot <slot>] [--job-id <id>]
// [--selection-mode <Full|Shadow|ChangeAware>] [--baseline-sha <sha>] ... [--attempt <n>] [--skip-publish] [--what-if]
// [--result-file <json>]. The RC lane (Antiphon.Nightly.ReleaseCut.Run on the production host): journal the candidate, run
// the rc profile on it in this process (NightlyRun.Run, as the nightly-run command does), publish only on RC
// complete-green. It replaced the body of scripts/release-cut.ps1 (unit 15 of 2026-11-22-remaining-ps1-to-csharp.md); that
// script is now Windmill's thin launcher of this command (u/lndcobra/antiphon_release_candidates starts it by path), with
// the same parameters as kebab-case flags; its test-only -SeamsPath is not a flag (the launcher refuses it). This command
// reads ANTIPHON_API (the release card's API) and ANTIPHON_RELEASE_GATE_TEST_MODE and passes them in; the pinned scripts
// and source trees are those of the checkout this executable was built in (else the current directory's). The
// [release-cut], [release-candidate], nightly and [publish-release] lines, then the record as one compact JSON line on
// stdout; --result-file writes the same record (the launcher's -PassThru). Exit: the record's exitCode (0 published, or
// green with --skip-publish or --what-if; 3 refused; otherwise the candidate's, the run's or the publisher's); 1 a candidate
// journal or state root that cannot be written (one stderr line, no record); 2 usage. An operator step: never run it from a
// delegate. ASCII only.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ReleaseCutCommand
    {
        internal const string Name = "release-cut";

        internal const string Usage = ReleaseCutOptions.Usage;

        internal static int Run(string[] args)
        {
            var scripts = Path.Combine(SourceCheckout.Root() ?? Environment.CurrentDirectory, "scripts");
            var options = ReleaseCutOptions.Parse(args.Skip(1).ToArray(), scripts, Environment.GetEnvironmentVariable);
            ReleaseCutResult result;
            try
            {
                var host = NightlyHost.Production(Console.Out, Console.Error, NightlyTestsCommand.PrepareChild);
                result = ReleaseCut.Run(options, ReleaseCutHost.Production(host, System.Reflection.Assembly.GetEntryAssembly()?.Location ?? ""));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine(Name + ": " + e.Message);
                return 1;
            }

            var line = result.ToJson();
            Console.Out.WriteLine(line);
            if (!string.IsNullOrWhiteSpace(options.ResultFile))
            {
                try
                {
                    var path = Path.GetFullPath(options.ResultFile);
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, line, new UTF8Encoding(false));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
                {
                    Console.Error.WriteLine(Name + ": cannot write " + options.ResultFile + ": " + e.Message);
                }
            }
            return result.ExitCode;
        }
    }
}
