// CARD-0604: warn when an adopted daemon was built before a source change in its project closure (unit 30 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced the body of scripts/check-daemon-build.ps1,
// which stays as a thin launcher because restart-apphost.ps1 and dev-aspire.ps1 run it by path. The CLI runs it as
//
//   antiphon-scripts check-daemon-build [--fail-on-stale]
//
// For each daemon (the session runner on 17204, the fake gateway on 17208) it reads the build identity from
// http://localhost:<port>/capabilities (3 s), and when the reported commit is an ancestor of the checkout's HEAD, lists the
// commits since it that touch the daemon's project closure. A daemon that is down, predates /capabilities, reports no
// commit, or reports one outside HEAD's history is named and skipped. Advisory: exit 0, or 1 with --fail-on-stale when a
// daemon is stale. HTTP is the transport's, git the host's (in the checkout), the lines host.Out's. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>One daemon the check reads: its name, port, project closure (repository-relative) and the fix it names.</summary>
    public sealed record DaemonBuildTarget(string Name, int Port, IReadOnlyList<string> Paths, string Fix);

    /// <summary>The check-daemon-build command's options (check-daemon-build.ps1 -FailOnStale).</summary>
    public sealed class DaemonBuildCheckOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "check-daemon-build [--fail-on-stale]";

        /// <summary>Exit 1 when a daemon is stale.</summary>
        public bool FailOnStale { get; init; }

        /// <summary>The checkout whose HEAD and history are compared.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DaemonBuildCheckOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--fail-on-stale" });
            return new DaemonBuildCheckOptions { FailOnStale = flags.Switch("--fail-on-stale"), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>The daemon build staleness check (check-daemon-build.ps1).</summary>
    public static class DaemonBuildCheck
    {
        /// <summary>Each /capabilities read's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

        /// <summary>The daemons, in the order they are read. The runner's paths are its project reference closure.</summary>
        public static readonly IReadOnlyList<DaemonBuildTarget> Daemons = new[]
        {
            new DaemonBuildTarget("session-runner", 17204, new[]
            {
                "src/Antiphon.SessionRunner",
                "src/Antiphon.SessionRunner.Contracts",
                "src/Antiphon.Observability",
                "src/Antiphon.GitInProcess",
                "src/Antiphon.Agents.Pty",
                "src/Antiphon.PtyHost",
                "src/Antiphon.PtyHost.Client",
                "src/Antiphon.PtyHost.Protocol",
            }, "scripts/antiphon restart-session-runner"),
            new DaemonBuildTarget("fake-gateway", 17208, new[] { "src/Antiphon.Messaging.FakeGateway", "src/Antiphon.Messaging.Gateway", "src/Antiphon.Messaging" },
                "restart the fake-gateway daemon through its AppHost supervisor"),
        };

        /// <summary>Checks every daemon; the lines on host.Out. Returns 1 when --fail-on-stale finds a stale daemon, else 0.</summary>
        public static int Run(DaemonBuildCheckOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var stale = false;
            foreach (var daemon in Daemons)
            {
                var build = Build(http, daemon.Port, out var reachable);
                if (!reachable)
                {
                    host.Out.WriteLine("  " + daemon.Name + ": build identity unavailable (daemon is down or predates /capabilities)");
                    continue;
                }
                var sha = Text(build?["commitSha"]);
                if (string.IsNullOrWhiteSpace(sha))
                {
                    host.Out.WriteLine("  " + daemon.Name + ": build identity unavailable (daemon did not report a commit SHA)");
                    continue;
                }
                if (host.RunGit(options.RepoRoot, "merge-base", "--is-ancestor", sha, "HEAD").ExitCode != 0)
                {
                    host.Out.WriteLine("  " + daemon.Name + ": build " + sha + " is not an ancestor of HEAD; not comparing unrelated history");
                    continue;
                }
                var log = new List<string> { "log", "--oneline", sha + "..HEAD", "--" };
                log.AddRange(daemon.Paths);
                var history = host.RunGit(options.RepoRoot, log.ToArray());
                if (history.ExitCode != 0)
                {
                    host.Out.WriteLine("  " + daemon.Name + ": could not compare build " + sha + " to HEAD");
                    continue;
                }
                var changes = history.Output.Replace("\r\n", "\n").Split('\n').Where(line => line.Length > 0).ToArray();
                if (changes.Length == 0)
                {
                    host.Out.WriteLine("  " + daemon.Name + ": build " + sha.Substring(0, Math.Min(7, sha.Length)) + " has no newer source changes in its project closure");
                    continue;
                }
                stale = true;
                var built = Text(build["assemblyWriteTimeUtc"]);
                var startedAt = Text(build["processStartUtc"]);
                host.Out.WriteLine("WARNING: " + daemon.Name + " is stale for " + changes.Length.ToString(CultureInfo.InvariantCulture) + " source change(s).");
                host.Out.WriteLine("  build " + sha + "; built " + (built ?? "unknown build time") + "; running since " + (startedAt ?? "unknown start time"));
                foreach (var change in changes.Take(5)) host.Out.WriteLine("  " + change);
                host.Out.WriteLine("  Fix when it is safe to restart: " + daemon.Fix);
            }
            return options.FailOnStale && stale ? 1 : 0;
        }

        // GET /capabilities: the build object (null when the answer has none); reachable false when the daemon did not
        // answer 2xx in time, as Invoke-RestMethod threw.
        private static JsonObject Build(NightlyReportHttp http, int port, out bool reachable)
        {
            NightlyReportHttpResponse answer;
            try
            {
                answer = http("GET", "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/capabilities", null, null);
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is SocketException || e is InvalidOperationException)
            {
                reachable = false;
                return null;
            }
            reachable = answer.Status >= 200 && answer.Status < 300;
            if (!reachable) return null;
            try
            {
                return (JsonNode.Parse(answer.Body ?? "") as JsonObject)?["build"] as JsonObject;
            }
            catch (JsonException)
            {
                // Invoke-RestMethod returned a non-JSON body as text, which has no build.
                return null;
            }
        }

        // [string] of a JSON value: the string itself, a number's or a literal's text; null for an absent or null value.
        private static string Text(JsonNode node)
        {
            if (node is null) return null;
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return node.ToJsonString();
        }
    }
}
