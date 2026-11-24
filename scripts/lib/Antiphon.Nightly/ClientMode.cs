// CARD-0216 S1: switch or inspect what port 17203 serves: the built bundle behind `vite preview`, or a live `vite` dev
// server for HMR. It replaced scripts/client-mode.ps1, deleted 2026-11-24 (unit 32 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
//
//   antiphon-scripts client-mode --mode built|dev [--timeout-sec <n>] [--port <n>] [--status] [--rebuild]
//   antiphon-scripts client-mode --status [--port <n>]
//   antiphon-scripts client-mode --rebuild
//
// (scripts/antiphon client-mode on sh, scripts\antiphon.cmd client-mode on Windows). Both modes run inside one process,
// client/scripts/serve.mjs, which Aspire starts with `npm run serve` (Antiphon.AppHost/Program.cs). This never restarts the
// AppHost: it writes the files the shim polls every ~1 s under the checkout's logs/ and, for --mode, waits for the swap.
//
// --rebuild touches logs/client.rebuild-requested (the UTC time, round-trip form): in built mode the shim runs one more clean
// `vite build` in place; in dev mode nothing reads it. --mode writes logs/client.mode (the mode alone, no line end), then GETs
// http://localhost:<port>/ once a second (5 s each) until the page shows the mode ('/@vite/client' in the HTML is dev,
// ignoring case) or --timeout-sec (default 60: built mode does a clean build on every swap into it) passes. --status prints
// logs/client.state.json (the shim's mode, status, pid, since and lastBuildAt, as written) and one live probe; it changes
// nothing. --port (default 17203, the only port the shim runs on) lets a test probe a port nothing listens on.
//
// Exit 0 done; 1 the port did not show the mode in time (without --status, which always ends 0) or the state file is not
// JSON (one host.Error line). Files are IFileSystem's (UTF-8, no byte-order mark), the probe the transport's, the clock and
// the wait the host's, the lines host.Out's. Usage errors (no action, an unknown mode) are NightlyUsageException (the CLI's
// exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The client-mode command's options (client-mode.ps1 -Mode -Status -Rebuild -TimeoutSec -Port).</summary>
    public sealed class ClientModeOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "client-mode --mode built|dev [--timeout-sec <n>] [--port <n>] [--status] [--rebuild] | client-mode --status [--port <n>] | client-mode --rebuild";

        /// <summary>The modes, in their written spelling.</summary>
        public static readonly IReadOnlyList<string> Modes = new[] { "built", "dev" };

        /// <summary>The mode to switch to (built or dev); empty: no switch.</summary>
        public string Mode { get; init; } = "";

        /// <summary>Print the state file and a live probe.</summary>
        public bool Status { get; init; }

        /// <summary>Request one more clean build in built mode.</summary>
        public bool Rebuild { get; init; }

        /// <summary>How long --mode waits for the port to show the mode, in seconds.</summary>
        public int TimeoutSec { get; init; } = 60;

        /// <summary>The port probed.</summary>
        public int Port { get; init; } = 17203;

        /// <summary>The checkout whose logs/ the shim reads.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI. One of --mode, --status or --rebuild is required.</summary>
        public static ClientModeOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--mode", "--timeout-sec", "--port" }, new[] { "--status", "--rebuild" });
            var mode = "";
            if (flags.Has("--mode"))
            {
                var given = flags.Value("--mode", "");
                mode = Modes.FirstOrDefault(m => string.Equals(m, given, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException("--mode expects built or dev, not '" + given + "'");
            }
            var options = new ClientModeOptions
            {
                Mode = mode,
                Status = flags.Switch("--status"),
                Rebuild = flags.Switch("--rebuild"),
                TimeoutSec = flags.Int("--timeout-sec", 60),
                Port = flags.Int("--port", 17203),
                RepoRoot = repoRoot ?? "",
            };
            if (options.Mode.Length == 0 && !options.Status && !options.Rebuild) throw new NightlyUsageException("specify --mode built|dev, --status, or --rebuild");
            return options;
        }
    }

    /// <summary>One look at the port: reachable with its HTTP status and the mode its page shows, or the failure.</summary>
    public sealed record ClientModeProbe(bool Reachable, int? StatusCode, string ModeObserved, string Error);

    /// <summary>The client mode switch and status (client-mode.ps1).</summary>
    public static class ClientMode
    {
        /// <summary>Each probe's deadline.</summary>
        public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>The wait between two probes while --mode waits.</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Writes the rebuild request and the mode, waits for the mode, prints the status; see the header for the exit codes.</summary>
        public static int Run(ClientModeOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var logs = ClientTools.Logs(files, options.RepoRoot);
            var modeFile = Path.Combine(logs, "client.mode");
            var stateFile = Path.Combine(logs, "client.state.json");
            var rebuildSentinel = Path.Combine(logs, "client.rebuild-requested");
            var port = ClientTools.Number(options.Port);
            var timeout = ClientTools.Number(options.TimeoutSec);

            if (options.Rebuild)
            {
                var stamp = host.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                // The shim reads the sentinel's modification time; the text is for a person.
                files.File.WriteAllText(rebuildSentinel, stamp + "\n", Utf8NoBom);
                host.Out.WriteLine("Rebuild requested at " + stamp + " (built mode only - a no-op right now if the shim is in dev mode).");
                if (options.Mode.Length == 0 && !options.Status) return 0;
            }

            if (options.Mode.Length > 0)
            {
                files.File.WriteAllText(modeFile, options.Mode, Utf8NoBom);
                host.Out.WriteLine("Wrote " + modeFile + " = " + options.Mode);
                host.Out.WriteLine("Waiting up to " + timeout + "s for :" + port + " to reflect '" + options.Mode + "' mode...");
                var deadline = host.UtcNow.AddSeconds(options.TimeoutSec);
                ClientModeProbe probe = null;
                while (host.UtcNow < deadline)
                {
                    probe = Probe(http, options.Port);
                    if (Shows(probe, options.Mode)) break;
                    host.Sleep(PollInterval);
                }
                if (Shows(probe, options.Mode))
                {
                    host.Out.WriteLine("OK: :" + port + " is serving '" + options.Mode + "' mode (HTTP " + ClientTools.Number(probe.StatusCode ?? 0) + ").");
                    if (!options.Status) return 0;
                }
                else if (probe != null && probe.Reachable)
                {
                    host.Out.WriteLine("TIMEOUT: :" + port + " is reachable but still reports '" + probe.ModeObserved + "' mode after " + timeout + "s.");
                    if (!options.Status) return 1;
                }
                else
                {
                    host.Out.WriteLine("TIMEOUT: :" + port + " did not become reachable within " + timeout + "s (" + probe?.Error + ").");
                    if (!options.Status) return 1;
                }
            }

            // Status: given alone, after --rebuild, or after --mode whatever its outcome.
            if (files.File.Exists(stateFile))
            {
                JsonObject state;
                try
                {
                    state = JsonNode.Parse(files.File.ReadAllText(stateFile)) as JsonObject;
                }
                catch (JsonException e)
                {
                    host.Error.WriteLine("client-mode: the state file " + stateFile + " is not JSON: " + e.Message);
                    return 1;
                }
                host.Out.WriteLine("State file (" + stateFile + "):");
                foreach (var field in new[] { "mode", "status", "pid", "since", "lastBuildAt" })
                    host.Out.WriteLine("  " + field.PadRight(12) + ": " + Text(state?[field]));
            }
            else
            {
                host.Out.WriteLine("No state file at " + stateFile + " yet (the shim writes this on its first poll).");
            }
            var live = Probe(http, options.Port);
            host.Out.WriteLine(live.Reachable
                ? "Live probe   : reachable, HTTP " + ClientTools.Number(live.StatusCode ?? 0) + ", observed mode = " + live.ModeObserved
                : "Live probe   : NOT reachable (" + live.Error + ")");
            return 0;
        }

        /// <summary>
        /// GET http://localhost:&lt;port&gt;/: a success status is reachable, dev when the page names /@vite/client (ignoring
        /// case), else built. Another status, or a transport failure, is not reachable, with the reason.
        /// </summary>
        public static ClientModeProbe Probe(NightlyReportHttp http, int port)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            try
            {
                var response = http("GET", "http://localhost:" + ClientTools.Number(port) + "/", new Dictionary<string, string>(StringComparer.Ordinal), null);
                if (response.Status < 200 || response.Status > 299)
                    return new ClientModeProbe(false, null, null, "Response status code does not indicate success: " + ClientTools.Number(response.Status) + ".");
                var dev = (response.Body ?? "").IndexOf("/@vite/client", StringComparison.OrdinalIgnoreCase) >= 0;
                return new ClientModeProbe(true, response.Status, dev ? "dev" : "built", null);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is OperationCanceledException || e is UriFormatException || e is IOException
                                      || e is InvalidOperationException)
            {
                return new ClientModeProbe(false, null, null, e.Message);
            }
        }

        private static bool Shows(ClientModeProbe probe, string mode) =>
            probe != null && probe.Reachable && string.Equals(probe.ModeObserved, mode, StringComparison.OrdinalIgnoreCase);

        // A state field as the shim wrote it: a string's text, any other value's JSON, nothing for null or absent.
        private static string Text(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return node.ToJsonString();
        }
    }
}
