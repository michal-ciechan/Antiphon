// antiphon-scripts <command> [arguments]. Exit 0: handled (the command wrote its answer). Exit 10 (NotHandled):
// the input is outside what this command answers exactly, nothing the caller relies on was written, and the
// caller runs its PowerShell path. Exit 2: usage. The reason for NotHandled goes to stderr, one line.
using System;
using System.Linq;
using Antiphon.Checkpoints;

namespace Antiphon.Scripts
{
    public static class Program
    {
        public const int Handled = 0;
        public const int Usage = 2;
        public const int NotHandled = 10;

        public static int Main(string[] args)
        {
            // One process answers one request: the filter/textconv driver settings are asked of git once.
            CheckpointProcessRunner.DriverSettingsCacheMilliseconds = 60000;
            // The evidence scan answers every input itself (exit 0 or 2) and never hands back NotHandled.
            if ((args.Length == 2 || args.Length == 3) && args[0] == "live-lane-evidence"
                && Array.IndexOf(LiveLaneEvidence.Cases, args[1]) >= 0)
                return LiveLaneEvidence.RunCommand(args[1], args.Length == 3 ? args[2] : null);
            // The rc ledger commands answer every input themselves: exit 0 done, 1 refused (one stderr line), 2 usage.
            if (args.Length >= 1 && args[0] == "rc-ledger-stamp") return RcLedgerStamp(args);
            if (args.Length >= 1 && (args[0] == "rc-ledger-import" || args[0] == "rc-ledger-plan" || args[0] == "rc-ledger-status" || args[0] == "rc-input-census")) return RcLedgerCommand(args);
            try
            {
                if ((args.Length == 3 || args.Length == 4) && args[0] == "select-release") return ReleaseSelection.Run(args[1], args[2], args.Length == 4 ? args[3] : null);
                Console.Error.WriteLine("usage: antiphon-scripts select-release <request.json> <response.json> [<scripts-lib>]"
                    + " | live-lane-evidence <case> [<context.json>] | " + RcLedgerStampUsage);
                return Usage;
            }
            catch (NotHandledException e)
            {
                Console.Error.WriteLine("not-handled: " + e.Message);
                return NotHandled;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("not-handled: " + e.GetType().Name + ": " + e.Message);
                return NotHandled;
            }
        }

        public const int Refused = 1;

        private const string RcLedgerCommandsUsage =
            "rc-ledger-import --source-run-root <dir> --evidence-root <dir> --repository <dir> --expected-sha <sha> --expected-run-id <id> [--current-manifest <json>]"
            + " | rc-ledger-plan --request <json> --request-digest <sha256> --repository <dir> --expected-sha <sha> --plan <json>"
            + " | rc-ledger-status --evidence-root <dir> --plan-digest <sha256> [--json]"
            + " | rc-input-census [--repository <dir>] [--emit-rows] [--scan-only]";

        // --name value flags (and bare switches) into a map; a repeated, unknown or valueless flag is malformed.
        private static System.Collections.Generic.Dictionary<string, string> Flags(string[] args, string[] valued, string[] switches)
        {
            var flags = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var name = args[i];
                if (Array.IndexOf(switches, name) >= 0 && !flags.ContainsKey(name)) { flags[name] = "true"; continue; }
                if (Array.IndexOf(valued, name) < 0 || i + 1 >= args.Length || flags.ContainsKey(name)) return null;
                flags[name] = args[++i];
            }
            return flags;
        }

        // CARD-1836: the S5b import, the S5c diagnostic plan and its status. None launches, activates or grants credit.
        private static int RcLedgerCommand(string[] args)
        {
            System.Collections.Generic.Dictionary<string, string> flags;
            string F(string name) => flags.TryGetValue(name, out var v) ? v : null;
            try
            {
                switch (args[0])
                {
                    case "rc-ledger-import":
                        flags = Flags(args, new[] { "--source-run-root", "--evidence-root", "--repository", "--expected-sha", "--expected-run-id", "--current-manifest" }, new string[0]);
                        if (flags == null || F("--source-run-root") == null || F("--evidence-root") == null || F("--repository") == null || F("--expected-sha") == null || F("--expected-run-id") == null) break;
                        var report = Antiphon.Nightly.RcLedgerImport.Run(F("--source-run-root"), F("--evidence-root"), F("--repository"), F("--expected-sha"), F("--expected-run-id"),
                            F("--current-manifest"), RepositoryCheckout(F("--repository")));
                        Console.WriteLine("RC IMPORT admitted=0 dirty=" + report["dirty"] + " unproven=" + report["unproven"] + " Full=true");
                        return Handled;
                    case "rc-ledger-plan":
                        flags = Flags(args, new[] { "--request", "--request-digest", "--repository", "--expected-sha", "--plan" }, new string[0]);
                        if (flags == null || new[] { "--request", "--request-digest", "--repository", "--expected-sha", "--plan" }.Any(n => F(n) == null)) break;
                        var request = System.Text.Json.Nodes.JsonNode.Parse(Antiphon.Nightly.RcAdmission.ReadJson(F("--request"), F("--request-digest")).GetRawText());
                        var plan = Antiphon.Nightly.RcIncrementalPlan.NewPlan(request, F("--repository"), F("--expected-sha"));
                        Antiphon.Nightly.RcIncrementalPlan.WritePlan(F("--plan"), plan, RepositoryCheckout(F("--repository")));
                        Console.WriteLine("RC PLAN diagnostic=true effective=Full required=" + plan["partition"]["required"].AsArray().Count + " carried=0 digest=" + plan["digest"]);
                        return Handled;
                    case "rc-input-census":
                        // CARD-1820 S3: exit 1 names every under-selected pair, unanalysable listing and no-op conflict.
                        flags = Flags(args, new[] { "--repository" }, new[] { "--emit-rows", "--scan-only" });
                        if (flags == null) break;
                        return Antiphon.Nightly.RcInputCensus.Run(F("--repository") ?? Environment.CurrentDirectory, F("--emit-rows") != null, F("--scan-only") != null, Console.Out);
                    case "rc-ledger-status":
                        flags = Flags(args, new[] { "--evidence-root", "--plan-digest" }, new[] { "--json" });
                        if (flags == null || F("--evidence-root") == null || F("--plan-digest") == null) break;
                        var status = Antiphon.Nightly.RcIncrementalPlan.ReadStatus(F("--evidence-root"), F("--plan-digest"));
                        if (F("--json") != null) { Console.WriteLine(status.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return Handled; }
                        Console.WriteLine("RC STATUS effective=" + status["effectiveMode"] + " planned=" + status["planned"] + " executed=" + status["executed"] + " carried=" + status["carried"]
                            + " sampled=" + status["sampled"] + " failed=" + status["failed"] + " unproven=" + status["unproven"] + " not-run=" + status["notRun"]);
                        Console.WriteLine("coverageComplete=false gateCredit=false revision=" + status["sequence"] + " reasons="
                            + string.Join(",", status["forcedReasons"].AsArray().Select(r => (string)r)));
                        var proposed = status["proposed"];
                        Console.WriteLine("UNVERIFIED PROPOSAL carried=" + proposed["carried"] + " unique-origin-chunks=" + proposed["uniqueOriginChunks"]
                            + " historical-potential-wall-seconds=" + proposed["historicalPotentialWallSeconds"]);
                        foreach (var origin in proposed["origins"].AsArray())
                            Console.WriteLine("ORIGIN envelope=" + origin["envelopeDigest"] + " run=" + origin["runId"] + " source=" + origin["sourceSha"] + " suite=" + origin["suite"]
                                + " chunk=" + origin["chunkId"]);
                        return Handled;
                }
            }
            catch (Exception e) when (e is Antiphon.Nightly.RcAdmissionRefusal || e is Antiphon.Nightly.RcLedgerRefusal || e is Antiphon.Nightly.RcChunkDigestRefusal
                || e is Antiphon.Nightly.RcInputRefusal || e is Antiphon.Nightly.RcBoundaryRefusal || e is InvalidOperationException || e is System.IO.IOException)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
            Console.Error.WriteLine("usage: antiphon-scripts " + RcLedgerCommandsUsage);
            return Usage;
        }

        // The checkout outputs must stay outside: the repository's own working tree.
        private static string RepositoryCheckout(string repository) => System.IO.Path.GetFullPath(repository);

        private const string RcLedgerStampUsage =
            "rc-ledger-stamp <ledger.json> <output.json> [--timing <sidecar>]... [--trx <trx>]... [--include-host --host-id <id> --os <windows|linux>]";

        // CARD-1842 S6: copy a ledger with durations from timing sidecars and TRX; --include-host writes v2 with the
        // given chunk host (never the machine name). Refuses an existing output.
        private static int RcLedgerStamp(string[] args)
        {
            var paths = new System.Collections.Generic.List<string>();
            var timing = new System.Collections.Generic.List<string>();
            var trx = new System.Collections.Generic.List<string>();
            var includeHost = false;
            string hostId = null, os = null;
            var malformed = false;
            for (var i = 1; i < args.Length && !malformed; i++)
            {
                var arg = args[i];
                var hasValue = i + 1 < args.Length;
                if (arg == "--include-host") includeHost = true;
                else if (arg == "--timing" && hasValue) timing.Add(args[++i]);
                else if (arg == "--trx" && hasValue) trx.Add(args[++i]);
                else if (arg == "--host-id" && hasValue) hostId = args[++i];
                else if (arg == "--os" && hasValue) os = args[++i];
                else if (arg.StartsWith("--", StringComparison.Ordinal)) malformed = true;
                else paths.Add(arg);
            }
            if (malformed || paths.Count != 2)
            {
                Console.Error.WriteLine("usage: antiphon-scripts " + RcLedgerStampUsage);
                return Usage;
            }
            try
            {
                Antiphon.Nightly.RcLedger.Stamp(paths[0], paths[1], timing, trx, includeHost, hostId, os);
                return Handled;
            }
            catch (Antiphon.Nightly.RcLedgerRefusal e)
            {
                Console.Error.WriteLine(e.Message);
                return Refused;
            }
        }
    }
}
