// CARD-1015/CARD-1024: antiphon-scripts evidence-diff and evidence-deletion, the read-only Git object policy over
// .antiphon/ evidence (Antiphon.Nightly.EvidencePolicy). stdout carries only the EVIDENCE lines, UTF-8 without a BOM.
// Exit 0: compliant. 1: policy violations. 2: the input, the history or Git cannot be verified, or a malformed command
// line (usage on stderr). Never writes Git or source.
using System;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class EvidenceCommand
    {
        internal const string Diff = "evidence-diff";
        internal const string Deletion = "evidence-deletion";

        internal const string Usage =
            "evidence-diff [--repository <dir>] (--base <ref> [--head <ref>] | --event-path <json> --event-name <push|workflow_dispatch> [--manual-base <ref>] [--manual-head <ref>])"
            + " | evidence-deletion [--repository <dir>] --inventory-ref <ref> (--inventory-only | --inventory-path-sha256 <hex> --inventory-count <n> --inventory-bytes <n> [--head <ref>])"
            + " [--supplemental-cleanup CARD-nnnn]";

        internal static int Run(string[] args)
        {
            var history = args[0] == Diff;
            var flags = history
                ? Program.Flags(args, new[] { "--repository", "--base", "--head", "--event-path", "--event-name", "--manual-base", "--manual-head" }, new string[0])
                : Program.Flags(args, new[] { "--repository", "--inventory-ref", "--inventory-path-sha256", "--inventory-count", "--inventory-bytes", "--head", "--supplemental-cleanup" },
                    new[] { "--inventory-only" });
            if (flags == null)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + Usage);
                return Program.Usage;
            }
            string F(string name) => flags.TryGetValue(name, out var value) ? value : null;
            using var stdout = EvidencePolicy.OpenOutput(Console.OpenStandardOutput());
            if (history)
                return EvidencePolicy.History(new EvidenceHistoryRequest
                {
                    Repository = F("--repository") ?? ".",
                    BaseRef = F("--base"),
                    HeadRef = F("--head") ?? "HEAD",
                    EventPath = F("--event-path"),
                    EventName = F("--event-name"),
                    ManualBase = F("--manual-base"),
                    ManualHead = F("--manual-head"),
                }, stdout);
            return EvidencePolicy.Deletion(new EvidenceDeletionRequest
            {
                Repository = F("--repository") ?? ".",
                InventoryRef = F("--inventory-ref"),
                InventoryPathSha256 = F("--inventory-path-sha256"),
                InventoryCount = F("--inventory-count"),
                InventoryBytes = F("--inventory-bytes"),
                HeadRef = F("--head") ?? "HEAD",
                InventoryOnly = flags.ContainsKey("--inventory-only"),
                SupplementalCleanup = F("--supplemental-cleanup"),
            }, stdout);
        }
    }
}
