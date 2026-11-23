// CARD-0599 D-3 and CARD-1311: antiphon-scripts release-candidate [--ref <rc/YYYY-MM-DD|release/rc-YYYYMMDDTHHMMSSZ>]
// [--release-root <dir>] [--checkout-root <dir>] [--remote-url <url>] [--source-ref <ref>] [--schedule-slot <slot>]
// [--what-if]. Journals one release candidate (Antiphon.Nightly.ReleaseCandidate.Run on the production host, the production
// release-gate remote and the disk): an existing rc/YYYY-MM-DD tip is journalled and never pushed; a legacy stamp ref keeps
// its create-only push; an omitted --ref refuses rc-ref-required. It replaced scripts/release-candidate.ps1 (deleted
// 2026-11-23, unit 13 of 2026-11-22-remaining-ps1-to-csharp.md), with the same parameters as kebab-case flags; its test-only
// -SeamsPath and its -PassThru are gone (scripts/release-cut.ps1 calls the C# in process). The "[release-candidate] ..."
// lines, then one compact JSON line (candidateId, ref, sha, pushed, resumed, refusal, exitCode) on stdout. Exit 0
// journalled or WhatIf; 1 a clone, fetch, push or SHA failure, or a journal that cannot be written (one stderr line); 2
// usage; 3 refused. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ReleaseCandidateCommand
    {
        internal const string Name = "release-candidate";

        internal const string Usage = ReleaseCandidateOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = ReleaseCandidateOptions.Parse(args.Skip(1).ToArray());
            try
            {
                var result = ReleaseCandidate.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem(), ReleaseGateRemote.Production());
                Console.Out.WriteLine(result.ToJson());
                return result.ExitCode;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("release-candidate: " + e.Message);
                return 1;
            }
        }
    }
}
