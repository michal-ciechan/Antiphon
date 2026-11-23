// W2-nightly S20: antiphon-scripts nightly-run <flags>, the nightly run (Antiphon.Nightly.NightlyRun.Run). It replaces
// scripts/lib/nightly-run-impl.ps1 (deleted): scripts/nightly-run.ps1 is its Windows PowerShell 5.1 launcher (Windmill
// u/lndcobra/antiphon_nightly_tests, release-cut.ps1 and the qualification-launch command call that script). The flags are
// NightlyRunOptions' (section 4 of the plan 2026-11-21-w2-nightly-port-slices.md; there is no seams flag). The host is
// the production one (the system clock, git, both launch custodies, the process table, the host probe); the bindings
// are NightlyRunBindings.Production: this checkout as the product root (the self-update hop rule), this process's own
// dll for the tests hop and the selection attempt, the report in-process on its production seams and the shadow
// evidence recorder from ANTIPHON_RC_RUN_EVIDENCE.
//
// Every exit path ends stdout with the compact completion record (CARD-0545 D-10): NightlyRun.Run prints it; a usage
// error prints a refusal record (refusal usage, exit 2) after its stderr line, and a failure outside the run (one stderr
// line) prints one with refusal nightly-run-failed and exit 1. --result-file writes the run's result (the run-core
// names the launcher's -PassThru returns); a write that fails is one stderr line and keeps the exit code. Exit: the
// run's (0 green, WhatIf or a hop's own 0; 1 failed; 3 refused; otherwise the tests or report exit). ASCII only.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class NightlyRunCommand
    {
        internal const string Name = "nightly-run";

        internal const string Usage = "nightly-run [--checkout-root <dir>] [--state-root <dir>] [--log-root <dir>] [--ref <ref>] [--profile <nightly|rc>]"
            + " [--trigger <trigger>] [--expected-sha <sha>] [--release-root <dir>] [--coordination-root <dir>] [--suites <a,b>] ... [--no-sync]"
            + " [--no-report] [--what-if] [--result-file <json>]";

        internal static int Run(string[] args)
        {
            NightlyRunOptions options;
            try { options = NightlyRunOptions.Parse(args.Skip(1).ToArray()); }
            catch (NightlyUsageException e)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + Name + ": " + e.Message);
                Console.Out.WriteLine(NightlyRun.RecordLine(new NightlyRunResult { ExitCode = Program.Usage, Refusal = "usage" }));
                return Program.Usage;
            }

            NightlyRunResult result;
            try
            {
                var host = NightlyHost.Production(Console.Out, Console.Error, NightlyTestsCommand.PrepareChild);
                result = NightlyRun.Run(options, host, NightlyRunBindings.Production(host, System.Reflection.Assembly.GetEntryAssembly()?.Location ?? ""));
            }
            catch (Exception e)
            {
                // NightlyRun.Run answers every failure of the run as its result; this is one outside it (the record was not printed).
                Console.Error.WriteLine(Name + ": " + e.GetType().Name + ": " + e.Message);
                result = new NightlyRunResult { ExitCode = 1, Refusal = "nightly-run-failed", RunId = options.RunId ?? "" };
                Console.Out.WriteLine(NightlyRun.RecordLine(result));
            }

            if (!string.IsNullOrWhiteSpace(options.ResultFile))
            {
                try
                {
                    var path = Path.GetFullPath(options.ResultFile);
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, NightlyRun.ResultJson(result), new UTF8Encoding(false));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NightlyRefusal || e is ArgumentException || e is NotSupportedException)
                {
                    Console.Error.WriteLine(Name + ": cannot write " + options.ResultFile + ": " + e.Message);
                }
            }
            return result.ExitCode;
        }
    }
}
