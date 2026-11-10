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
            try
            {
                if ((args.Length == 3 || args.Length == 4) && args[0] == "select-release") return ReleaseSelection.Run(args[1], args[2], args.Length == 4 ? args[3] : null);
                Console.Error.WriteLine("usage: antiphon-scripts select-release <request.json> <response.json> [<scripts-lib>]"
                    + " | live-lane-evidence <case> [<context.json>]");
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
    }
}
