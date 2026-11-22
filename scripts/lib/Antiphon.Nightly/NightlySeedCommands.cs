// W2-nightly S26: the operator's failure-history and stopped-run seed commands (plan 2026-11-21-w2-nightly-port-slices.md,
// section 4). They replaced scripts/export-nightly-prior-failures.ps1, scripts/export-nightly-stopped-run-seed.ps1 and
// scripts/merge-nightly-seeds.ps1; run them as scripts/antiphon <command> (scripts/antiphon.cmd on Windows). The CLI
// (antiphon-scripts, NightlySeedCommand) hands its whole argv here with the console's streams; the tests call Run
// in-process with their own writers. Plain C#: no PowerShell types. ASCII only.
//
// export-nightly-prior-failures --run-directory <dir> --output-path <file>
//   Explicit export from closed retained evidence (PriorFailureHistory.Export); no live services. stdout
//   "HISTORY SHA256 <digest>", exit 0. A refusal is separate from the immutable history: it never publishes complete=true
//   or overwrites a source or output file. Its stdout is one compact record
//   {"format":"antiphon-nightly-prior-failures-error-v1","complete":false,"reason":"<reason>"}, exit 2.
//
// export-nightly-stopped-run-seed --run-directory <dir> --output-path <file> [--source-run-id <id>]
//   [--source-candidate-id <id>] [--source-sha <sha>] [--timing-root <dir>]
//   Build an ordering-only seed from an operator-stopped run's retained logs (StoppedSeedFiles.Export). Read-only source
//   directory; output must be outside it. No liveness inference, process operations, receipt reuse or execution credit.
//   Supply source metadata when absent from owner JSON; conflicting planned/owner identity refuses export. stdout
//   "SEED <path> SHA256 <digest>", exit 0. A refusal is one stderr line (the command and the reason), exit 1.
//
// merge-nightly-seeds -Seed <seed.json> [-Seed <seed.json>]... -OutputPath <merged.json> -ReportPath <report.json>
//   [-Decision <decisions.txt>] [-Repo <checkout>] [-Proven <suite|class|method:runId>]... [-Strict]
//   The argv after the command name goes to SeedMerge.Run unchanged (the operator command repeats -Seed and -Proven).
//   A later green proves an identity only when that seed's source SHA contains the fix commit, or --Proven names that run.
//   Merge one or more stopped-run seeds into a single operator-seed-from-stopped-run envelope and a deterministic report.
//   Chunk rows and evidence come from the newest seed. failedTests lists identities that are not yet proven. Launch
//   defaults stay on the existing prior-failure switches. When -Repo is set, a decision SHA already contained in the
//   last-failing run source (ancestor or cherry-pick trailer) is ignored for proven and for fixed-pending-proof. With no
//   remaining SHA the identity stays open. --Proven remains the manual override. Proof requires the later green source to
//   contain a fix the failing run did not already contain. Without -Repo, git proof stays off.
//   A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.
//   Its lines go to stdout. Exit 0 merged, 2 refused (the reason is the output), 3 merged under -Strict with an identity
//   unplaced or open without a decision.
//
// A malformed command line of either exporter (an unknown, repeated or valueless flag, a missing --run-directory or
// --output-path) is a NightlyUsageException: the CLI prints it and exits 2.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The export-nightly-prior-failures, export-nightly-stopped-run-seed and merge-nightly-seeds commands.</summary>
    public static class NightlySeedCommands
    {
        public const string ExportPriorFailures = "export-nightly-prior-failures";
        public const string ExportStoppedRunSeed = "export-nightly-stopped-run-seed";
        public const string MergeSeeds = "merge-nightly-seeds";

        public const string Usage =
            ExportPriorFailures + " --run-directory <dir> --output-path <file>"
            + " | " + ExportStoppedRunSeed + " --run-directory <dir> --output-path <file> [--source-run-id <id>] [--source-candidate-id <id>]"
            + " [--source-sha <sha>] [--timing-root <dir>]"
            + " | " + MergeSeeds + " -Seed <seed.json>... -OutputPath <file> -ReportPath <file> [-Decision <file>] [-Repo <dir>]"
            + " [-Proven <suite|class|method:runId>]... [-Strict]";

        private const string HistoryErrorFormat = "antiphon-nightly-prior-failures-error-v1";

        private static readonly string[] HistoryFlags = { "--run-directory", "--output-path" };
        private static readonly string[] SeedFlags = { "--run-directory", "--output-path", "--source-run-id", "--source-candidate-id", "--source-sha", "--timing-root" };

        /// <summary>
        /// One command: args is the CLI's argv, the command name first. Writes the command's lines to output and error and
        /// returns its exit code. A malformed exporter command line throws NightlyUsageException.
        /// </summary>
        public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
        {
            if (args == null || args.Count == 0) throw new NightlyUsageException("missing command");
            var rest = args.Skip(1).ToArray();
            switch (args[0])
            {
                case ExportPriorFailures: return PriorFailures(rest, output);
                case ExportStoppedRunSeed: return StoppedRunSeed(rest, output, error);
                case MergeSeeds: return Merge(rest, output);
                default: throw new NightlyUsageException("unknown command " + args[0]);
            }
        }

        private static int PriorFailures(string[] rest, TextWriter output)
        {
            var flags = NightlyArgs.Parse(rest, HistoryFlags, Array.Empty<string>());
            var runDirectory = flags.Required("--run-directory");
            var outputPath = flags.Required("--output-path");
            object history;
            try
            {
                history = PriorFailureHistory.Export(runDirectory, outputPath);
            }
            catch (Exception e)
            {
                // The refusal record, as ConvertTo-Json -Compress wrote it.
                output.WriteLine("{\"format\":\"" + HistoryErrorFormat + "\",\"complete\":false,\"reason\":" + EvidencePolicy.JsonString(StoppedSeedFiles.Message(e) ?? "") + "}");
                return 2;
            }
            output.WriteLine("HISTORY SHA256 " + PsValue.Str(PsValue.Prop(history, "Digest")));
            return 0;
        }

        private static int StoppedRunSeed(string[] rest, TextWriter output, TextWriter error)
        {
            var flags = NightlyArgs.Parse(rest, SeedFlags, Array.Empty<string>());
            var runDirectory = flags.Required("--run-directory");
            var outputPath = flags.Required("--output-path");
            object seed;
            try
            {
                seed = StoppedSeedFiles.Export(runDirectory, outputPath, flags.Value("--source-run-id", ""), flags.Value("--source-candidate-id", ""),
                    flags.Value("--source-sha", ""), flags.Value("--timing-root", ""));
            }
            catch (Exception e)
            {
                error.WriteLine(ExportStoppedRunSeed + ": " + StoppedSeedFiles.Message(e));
                return 1;
            }
            output.WriteLine("SEED " + PsValue.Str(PsValue.Prop(seed, "Path")) + " SHA256 " + PsValue.Str(PsValue.Prop(seed, "Digest")));
            return 0;
        }

        private static int Merge(string[] rest, TextWriter output)
        {
            SeedMergeOutcome merge;
            try
            {
                merge = SeedMerge.Run(rest);
            }
            catch (Exception e)
            {
                output.WriteLine(StoppedSeedFiles.Message(e));
                return 2;
            }
            foreach (var line in merge.Lines ?? new string[0]) output.WriteLine(line);
            return merge.Code;
        }
    }
}
