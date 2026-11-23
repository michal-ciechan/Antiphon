// CARD-1483 S5: antiphon-scripts land-graph-audit [--check | --update] [--repo <dir>] [--tool-path <dll>]
// [--evaluation-budget-seconds <n>] [--land-refresh], and land-graph-precheck [--check | --update] [--repo <dir>]
// [--manifest-path <file>] [--tool-path <dll>]. The land verification build graph's author-time audit and its emulation
// pre-check (Antiphon.Nightly.LandGraphAuditRun on the production host and the real file system): they resolve the
// built tools/Antiphon.LandGraphAudit DLL (rebuilding a stale default) and run it. They replaced
// scripts/audit-land-verification-graph.ps1 and scripts/audit-land-verification-precheck.ps1 (deleted 2026-11-23, unit
// 11 of 2026-11-22-remaining-ps1-to-csharp.md). The repository defaults to the checkout this executable was built in
// (else the current directory's); the audit's children get this process's environment without MSBuild* names.
// land-graph-audit: exit 0 approved, 1 refused, 2 usage. land-graph-precheck: exit 0 matched or written, 1 mismatch,
// refusal or a failed tool, 2 a git failure or usage. See docs/orchestration-loop.md. ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class LandGraphAuditCommand
    {
        internal const string Name = "land-graph-audit";

        internal const string PrecheckName = "land-graph-precheck";

        internal const string Usage = LandGraphAuditRunOptions.Usage;

        internal const string PrecheckUsage = LandGraphPrecheckRunOptions.Usage;

        internal static int Run(string[] args)
        {
            var options = LandGraphAuditRunOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? Environment.CurrentDirectory, EnvironmentSnapshot());
            return LandGraphAuditRun.Run(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem()).ExitCode;
        }

        internal static int RunPrecheck(string[] args)
        {
            var options = LandGraphPrecheckRunOptions.Parse(args.Skip(1).ToArray(), SourceCheckout.Root() ?? Environment.CurrentDirectory);
            return LandGraphAuditRun.Precheck(options, NightlyHost.Production(Console.Out, Console.Error), new FileSystem());
        }

        // This process's environment, which the land-graph children start from.
        internal static IReadOnlyDictionary<string, string> EnvironmentSnapshot()
        {
            var snapshot = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) snapshot[(string)entry.Key] = (string)entry.Value;
            return snapshot;
        }
    }
}
