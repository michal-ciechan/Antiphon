// antiphon-scripts <command> [arguments]. Exit 0: handled (the command wrote its answer). Exit 10 (NotHandled):
// the input is outside what this command answers exactly, nothing the caller relies on was written, and the
// caller runs its PowerShell path. Exit 2: usage. The reason for NotHandled goes to stderr, one line.
using System;
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
            // The ledger stamp answers every input itself: exit 0 written, 1 refused (one stderr line), 2 usage.
            if (args.Length >= 1 && args[0] == "rc-ledger-stamp") return RcLedgerStamp(args);
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
