// N0 (unit 57): the launcher's own entry, for a checkout with nothing published yet. scripts/antiphon, scripts/antiphon.cmd
// and scripts/lib/antiphon-cli.ps1 build this project into a fresh boot-<n> folder and run
//   Antiphon.CliLauncher publish <checkout>
// which publishes the CLI for the checkout's current inputs under the build lock (CliPublisher.Ensure) and prints nothing
// on stdout. Exit 0 published; 9 not (one stderr line); 2 usage. ASCII only.
#nullable disable
using System;

namespace Antiphon.CliLauncher
{
    /// <summary>The bootstrap command line.</summary>
    public static class Program
    {
        /// <summary>The usage line.</summary>
        public const string Usage = "usage: Antiphon.CliLauncher publish <checkout>";

        /// <summary>publish &lt;checkout&gt;: exit 0 published, 9 not (one stderr line), 2 usage.</summary>
        public static int Main(string[] args)
        {
            if (args.Length != 2 || args[0] != "publish" || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            string checkout;
            try { checkout = CliGate.Checkout(args[1]); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is System.IO.PathTooLongException)
            {
                Console.Error.WriteLine("antiphon-cli: no checkout at " + args[1]);
                return CliPublisher.Unavailable;
            }
            return CliPublisher.Ensure(checkout, new CliPublishOptions()) == null ? CliPublisher.Unavailable : 0;
        }
    }
}
