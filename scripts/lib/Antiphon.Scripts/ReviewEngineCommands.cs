// The review engine's operator commands (unit 42 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; feature 012):
// review-standalone, review-github-setup and review-live-github. Each replaced the scripts/<name>.ps1 of the same name (deleted
// 2026-11-24; nothing started them by path), with the same parameters in kebab case. The behaviour is Antiphon.Nightly's
// (ReviewStandalone, ReviewGitHubSetup, ReviewLiveGitHub, ReviewAppVault) on the production host, the real file system and
// ReviewEngineOs. This file passes in the checkout the CLI was built in (else the current directory's), the home directory (~ for
// .bw-session) and TEMP (else the system temp path), and binds review-live-github's build slot with Ctrl+C and SIGTERM as its
// cancellation (the test run's tree is killed and the lease released, exit 130), as the build-slot command does. ASCII only.
using System;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class ReviewEngineCommands
    {
        internal const string Standalone = "review-standalone";
        internal const string GitHubSetup = "review-github-setup";
        internal const string LiveGitHub = "review-live-github";
        internal const string ReviewRun = "review-run";

        private const string ReviewRunUsage = "review-run close <id> (--reason <text> | --reason-file <path>) [--token <number>] [--cookie-file <path>]";
        internal const string Usage = ReviewStandaloneOptions.Usage + " | " + ReviewGitHubSetupOptions.Usage + " | " + ReviewLiveGitHubOptions.Usage
            + " | " + ReviewRunUsage;

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

        internal static int RunReviewRun(string[] args)
        {
            if (args.Length < 3 || args[1] != "close" || !Guid.TryParse(args[2], out var id))
                throw new NightlyUsageException(ReviewRunUsage);
            var options = NightlyArgs.Parse(args.Skip(3).ToArray(), ["--reason", "--reason-file", "--token", "--cookie-file"], []);
            if (options.Has("--reason") == options.Has("--reason-file"))
                throw new NightlyUsageException("Provide exactly one of --reason and --reason-file.");
            var reason = options.Has("--reason-file") ? File.ReadAllText(options.Value("--reason-file")) : options.Value("--reason");
            if (string.IsNullOrWhiteSpace(reason))
                throw new NightlyUsageException("A close reason is required.");

            var api = OperatorApi.Base(Environment.GetEnvironmentVariable("ANTIPHON_API"));
            var headers = BoardCli.Headers(Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN"));
            if (options.Has("--cookie-file"))
            {
                if (!Uri.TryCreate(api, UriKind.Absolute, out var endpoint)
                    || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
                    throw new NightlyUsageException("--cookie-file requires HTTPS or loopback HTTP.");
                var cookie = File.ReadAllText(options.Value("--cookie-file")).Trim();
                if (cookie.Length == 0 || cookie.Any(char.IsControl))
                    throw new NightlyUsageException("--cookie-file must contain one Cookie header value.");
                headers["Cookie"] = cookie;
            }

            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            var http = NightlyReport.HttpTransport(handler, BoardCli.Deadline);
            var path = $"/api/review-runs/{id:D}";
            uint token;
            if (options.Has("--token"))
            {
                if (!uint.TryParse(options.Value("--token"), NumberStyles.None, CultureInfo.InvariantCulture, out token) || token == 0)
                    throw new NightlyUsageException("--token must be a positive concurrency token from the run detail.");
            }
            else
            {
                if (!BoardCli.Send(http, api, "GET", path, headers, null, out var run, out var readError))
                {
                    Console.Error.WriteLine(readError);
                    return 1;
                }
                if (run?["concurrencyToken"] is not JsonValue value || !value.TryGetValue(out token) || token == 0)
                {
                    Console.Error.WriteLine("The server returned no review run concurrency token.");
                    return 1;
                }
            }

            var payload = OperatorApi.Json(new JsonObject { ["concurrencyToken"] = token, ["reason"] = reason });
            if (!BoardCli.Send(http, api, "POST", path + "/close", headers, payload, out var closed, out var writeError))
            {
                Console.Error.WriteLine(writeError);
                return 1;
            }
            Console.WriteLine(OperatorApi.Pretty(closed));
            return 0;
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
