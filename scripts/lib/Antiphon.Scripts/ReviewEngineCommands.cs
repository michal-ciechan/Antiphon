// The review engine's operator commands (unit 42 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; feature 012):
// review-standalone, review-github-setup and review-live-github. Each replaced the scripts/<name>.ps1 of the same name (deleted
// 2026-11-24; nothing started them by path), with the same parameters in kebab case. The behaviour is Antiphon.Nightly's
// (ReviewStandalone, ReviewGitHubSetup, ReviewLiveGitHub, ReviewAppVault) on the production host, the real file system and
// ReviewEngineOs. This file passes in the checkout the CLI was built in (else the current directory's), the home directory (~ for
// .bw-session) and TEMP (else the system temp path), and binds review-live-github's build slot with Ctrl+C and SIGTERM as its
// cancellation (the test run's tree is killed and the lease released, exit 130), as the build-slot command does. ASCII only.
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ReviewEngineCommands
    {
        internal const string Standalone = "review-standalone";
        internal const string GitHubSetup = "review-github-setup";
        internal const string LiveGitHub = "review-live-github";

        internal const string Usage = ReviewStandaloneOptions.Usage + " | " + ReviewGitHubSetupOptions.Usage + " | " + ReviewLiveGitHubOptions.Usage;

        private static string Checkout => SourceCheckout.Root() ?? Environment.CurrentDirectory;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string Temp => Environment.GetEnvironmentVariable("TEMP") is { Length: > 0 } temp ? temp : Path.GetTempPath();

        // Exit 0 started or stopped; 1 refused or failed (one stderr line).
        internal static int RunStandalone(string[] args)
        {
            var options = ReviewStandaloneOptions.Parse(args.Skip(1).ToArray(), Checkout, Home, Temp);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return ReviewStandalone.Run(options, host, new FileSystem(), Seams(host));
        }

        // Exit 0 written or a dry run; 1 refused or failed (one stderr line).
        internal static int RunGitHubSetup(string[] args)
        {
            var options = ReviewGitHubSetupOptions.Parse(args.Skip(1).ToArray(), Home);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return ReviewGitHubSetup.Run(options, host, new FileSystem(), Seams(host));
        }

        // The test run's exit code (4 no slot, 130 interrupted); 1 refused or failed (one stderr line).
        internal static int RunLiveGitHub(string[] args)
        {
            var options = ReviewLiveGitHubOptions.Parse(args.Skip(1).ToArray(), Checkout, Home);
            var host = NightlyHost.Production(Console.Out, Console.Error);
            return ReviewLiveGitHub.Run(options, host, new FileSystem(), Seams(host));
        }

        private static ReviewEngineOs Seams(NightlyHost host) => new ReviewEngineOs(host.Qualification, Slot, host.Variables);

        // The in-process build slot on this console; the signals cancel it only while it runs.
        private static int Slot(BuildSlotOptions options, TextWriter capture)
        {
            using var cancellation = new CancellationTokenSource();
            using var interrupt = BuildSlotCommands.Register(PosixSignal.SIGINT, cancellation);
            using var terminate = BuildSlotCommands.Register(PosixSignal.SIGTERM, cancellation);
            return BuildSlot.Runner(BuildSlotCommands.Platform, Console.Out, Console.Error, cancellation.Token)(options, capture);
        }
    }
}
