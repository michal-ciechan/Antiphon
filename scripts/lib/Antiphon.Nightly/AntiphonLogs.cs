// The operator inventory of logs and transcript evidence (docs/logs.md). It replaced scripts/logs.ps1 (unit 25 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as `antiphon-scripts logs` (scripts/antiphon
// logs on sh, scripts\antiphon.cmd logs on Windows).
//
//   logs --source <name> [--tail 100]
//
// Read-only. The file sources resolve git's primary worktree (the first `git worktree list --porcelain` record of the
// checkout the CLI was built in), never a linked worktree, and print the last --tail lines (1 to 10000):
//   desktop-server   the newest <main>/server/logs/antiphon-*.log, after "--- <file> ---"
//   apphost          <main>/logs/apphost.log
//   session-runner   <main>/logs/session-runner.log, then the newest <temp>/antiphon-logs/session-runner-*.log when there is one
//   pty-host         the newest C:\logs\antiphon\session-runner\pty-hosts\logs\*.log
//   fake-gateway     <main>/logs/fake-gateway.log
// The HTTP sources use ANTIPHON_API (the CLI passes it, the task token and the operator token file in; no value is printed):
//   hangfire         GET /hangfire with the operator token; prints the status and how to open the dashboard
//                    (scripts/antiphon hangfire-dashboard). Its in-memory history is not a log file.
//   transcripts      GET /api/agents, then each live session's GET /api/sessions/{id}/transcript?since=0 (the task token
//                    when set); prints "--- <agent> <session> ---" and the last --tail entries, one compact JSON line each.
// The server2 sources run ssh (BatchMode, 15 s connect timeout) to mc@server2 on the host's launcher, sharing this console:
//   server2-runner   the runner container's docker logs and its persisted runner and nested-Docker log tails
//   server2-deploy   the deployment root's inventory (never secrets/), then a deliberate failure: there is no durable
//                    server2 deployment-evidence location (a gap check, not a tail).
// Files are read with read and write sharing (a live Serilog file stays open). Exit 0 printed; 1 a missing file, no
// matching log, no live session, a failed request or ssh (one stderr line); usage errors are NightlyUsageException (the
// CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The logs command's options (logs.ps1 -Source -Tail).</summary>
    public sealed class AntiphonLogsOptions
    {
        /// <summary>The sources, in their listed spelling.</summary>
        public static readonly IReadOnlyList<string> Sources = new[]
        {
            "desktop-server", "apphost", "session-runner", "pty-host", "hangfire", "fake-gateway", "server2-runner", "server2-deploy", "transcripts",
        };

        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "logs --source <desktop-server|apphost|session-runner|pty-host|hangfire|fake-gateway|server2-runner|server2-deploy|transcripts> [--tail 100]";

        /// <summary>The source, in its listed spelling.</summary>
        public string Source { get; init; } = "";

        /// <summary>Lines (entries) to print, 1 to 10000.</summary>
        public int Tail { get; init; } = 100;

        /// <summary>The checkout git starts from (the CLI's: the one it was built in).</summary>
        public string Checkout { get; init; } = "";

        /// <summary>The temp directory whose antiphon-logs holds the runner's own Serilog files.</summary>
        public string TempRoot { get; init; } = "";

        /// <summary>The pty hosts' log directory.</summary>
        public string PtyHostLogs { get; init; } = AntiphonLogs.PtyHostLogDirectory;

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The operator token file the CLI resolved.</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>
        /// The arguments after the command name; the checkout, temp root, API, task token and operator token file come from the
        /// CLI. --source is required and matches ignoring case; --tail out of range is a NightlyUsageException.
        /// </summary>
        public static AntiphonLogsOptions Parse(IReadOnlyList<string> arguments, string checkout, string tempRoot, string api, string taskToken, string operatorTokenFile)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--source", "--tail" }, Array.Empty<string>());
            return new AntiphonLogsOptions
            {
                Source = BoardCli.Choice("--source", flags.Required("--source"), Sources),
                Tail = WatchMasterOptions.Ranged(flags, "--tail", 100, 1, 10000),
                Checkout = checkout ?? "",
                TempRoot = tempRoot ?? "",
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                OperatorTokenFile = operatorTokenFile ?? "",
            };
        }
    }

    /// <summary>The log sources (logs.ps1).</summary>
    public static class AntiphonLogs
    {
        /// <summary>Where the desktop runner's pty hosts log.</summary>
        public const string PtyHostLogDirectory = @"C:\logs\antiphon\session-runner\pty-hosts\logs";

        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

        private static readonly Regex WorktreeRecord = new Regex(@"^worktree\s+(.+)$", RegexOptions.CultureInvariant);

        /// <summary>Prints the source on host.Out; 0, or 1 after one host.Error line.</summary>
        public static int Run(AntiphonLogsOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            try
            {
                return Show(options, host, files, http);
            }
            catch (LogsRefusal refusal)
            {
                host.Error.WriteLine(refusal.Message);
                return 1;
            }
        }

        private static int Show(AntiphonLogsOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            switch (options.Source)
            {
                case "desktop-server":
                    ShowNewest(host, files, files.Path.Combine(MainCheckout(options, host, files), "server", "logs"), "antiphon-*.log", options.Tail);
                    return 0;
                case "apphost":
                    WriteTail(host, files, files.Path.Combine(MainCheckout(options, host, files), "logs", "apphost.log"), options.Tail);
                    return 0;
                case "session-runner":
                {
                    WriteTail(host, files, files.Path.Combine(MainCheckout(options, host, files), "logs", "session-runner.log"), options.Tail);
                    var serilog = files.Path.Combine(options.TempRoot, "antiphon-logs");
                    if (Matching(files, serilog, "session-runner-*.log").Count > 0) ShowNewest(host, files, serilog, "session-runner-*.log", options.Tail);
                    return 0;
                }
                case "pty-host":
                    ShowNewest(host, files, options.PtyHostLogs, "*.log", options.Tail);
                    return 0;
                case "fake-gateway":
                    WriteTail(host, files, files.Path.Combine(MainCheckout(options, host, files), "logs", "fake-gateway.log"), options.Tail);
                    return 0;
                case "hangfire":
                    return Hangfire(options, host, files, http);
                case "transcripts":
                    return Transcripts(options, host, http);
                case "server2-runner":
                {
                    var tail = options.Tail.ToString(CultureInfo.InvariantCulture);
                    var exit = Ssh(host, "docker logs --tail " + tail + " antiphon-runner-session-runner-1; docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n " + tail
                        + " /state/runner-logs/session-runner-*.log /state/logs/dockerd.log 2>&1'");
                    if (exit != 0) throw new LogsRefusal("server2 runner log retrieval failed (ssh/docker exit " + exit.ToString(CultureInfo.InvariantCulture) + ").");
                    return 0;
                }
                case "server2-deploy":
                {
                    var exit = Ssh(host, @"find /home/mc/antiphon-server2 -path /home/mc/antiphon-server2/secrets -prune -o -maxdepth 2 -type f \( -name '*.log' -o -name '*.json' -o -name '*.txt' \) -print");
                    if (exit != 0) throw new LogsRefusal("server2 deployment-evidence inventory failed (ssh exit " + exit.ToString(CultureInfo.InvariantCulture) + ").");
                    throw new LogsRefusal("No durable server2 deployment-evidence location is configured; see docs/logs.md.");
                }
                default:
                    throw new NightlyUsageException("--source must be one of " + string.Join(", ", AntiphonLogsOptions.Sources) + ", not '" + options.Source + "'");
            }
        }

        // The primary worktree of the checkout's repository: the first record git lists.
        private static string MainCheckout(AntiphonLogsOptions options, NightlyHost host, IFileSystem files)
        {
            if (string.IsNullOrEmpty(options.Checkout)) throw new LogsRefusal("No Antiphon checkout found: run the command from a checkout.");
            var top = host.RunGit(options.Checkout, "rev-parse", "--show-toplevel");
            var root = top.Output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
            if (top.ExitCode != 0 || string.IsNullOrEmpty(root)) throw new LogsRefusal("Could not resolve the Git checkout containing " + options.Checkout + ".");
            var list = host.RunGit(root, "worktree", "list", "--porcelain");
            if (list.ExitCode != 0) throw new LogsRefusal("Could not list Git worktrees for " + root + ".");
            var record = list.Output.Split('\n').Select(line => WorktreeRecord.Match(line.TrimEnd('\r'))).FirstOrDefault(match => match.Success);
            if (record == null) throw new LogsRefusal("Git did not report a primary worktree for " + root + ".");
            var main = files.Path.GetFullPath(record.Groups[1].Value.Trim());
            if (!files.Directory.Exists(main)) throw new LogsRefusal("Git primary worktree does not exist: " + main + ".");
            return main;
        }

        private static IReadOnlyList<IFileInfo> Matching(IFileSystem files, string directory, string pattern) =>
            files.Directory.Exists(directory)
                ? files.DirectoryInfo.New(directory).EnumerateFiles(pattern).OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray()
                : Array.Empty<IFileInfo>();

        // Show-Tail: the newest matching file's tail, after "--- <file> ---".
        private static void ShowNewest(NightlyHost host, IFileSystem files, string directory, string pattern, int tail)
        {
            var matching = Matching(files, directory, pattern);
            if (matching.Count == 0) throw new LogsRefusal("No log files found at " + files.Path.Combine(directory, pattern) + ".");
            var newest = matching[matching.Count - 1];
            host.Out.WriteLine("--- " + newest.FullName + " ---");
            WriteTail(host, files, newest.FullName, tail);
        }

        // Get-Content -Tail: the last lines, read with the writer's sharing.
        private static void WriteTail(NightlyHost host, IFileSystem files, string path, int tail)
        {
            if (!files.File.Exists(path)) throw new LogsRefusal("Log file not found: " + path + ".");
            var last = new Queue<string>();
            try
            {
                using var stream = files.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    last.Enqueue(line);
                    if (last.Count > tail) last.Dequeue();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new LogsRefusal("Cannot read " + path + ": " + e.Message);
            }
            foreach (var line in last) host.Out.WriteLine(line);
        }

        // CARD-0658: the dashboard needs the operator token, whatever the address; the value is never printed.
        private static int Hangfire(AntiphonLogsOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            var token = OperatorApi.ReadToken(files, options.OperatorTokenFile, out var refusal);
            if (token == null) throw new LogsRefusal(refusal);
            var headers = new Dictionary<string, string>(StringComparer.Ordinal) { [OperatorApi.OperatorTokenHeader] = token };
            NightlyReportHttpResponse response;
            try
            {
                response = http("GET", options.Api + "/hangfire", headers, null);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Threading.Tasks.TaskCanceledException || e is InvalidOperationException || e is IOException)
            {
                throw new LogsRefusal("Antiphon GET /hangfire failed: " + e.Message);
            }
            host.Out.WriteLine("Hangfire dashboard: HTTP " + response.Status.ToString(CultureInfo.InvariantCulture)
                + ". Open it with scripts/antiphon hangfire-dashboard and inspect Failed and Recurring Jobs; its in-memory history is not exposed as a log file.");
            return 0;
        }

        private static int Transcripts(AntiphonLogsOptions options, NightlyHost host, NightlyReportHttp http)
        {
            var headers = BoardCli.Headers(options.TaskToken);
            if (!BoardCli.Send(http, options.Api, "GET", "/api/agents", headers, null, out var agents, out var failure)) throw new LogsRefusal(failure);
            var live = BoardCli.Items(agents).Where(agent => BoardCli.Truthy(BoardCli.Get(agent, "liveSession", "id"))).ToArray();
            if (live.Length == 0) throw new LogsRefusal("No live agent sessions were returned; use the session id from an agent/task record for a historical transcript.");
            foreach (var agent in live)
            {
                var session = BoardCli.Text(BoardCli.Get(agent, "liveSession", "id"));
                host.Out.WriteLine("--- " + BoardCli.Text(BoardCli.Get(agent, "name")) + " " + session + " ---");
                if (!BoardCli.Send(http, options.Api, "GET", "/api/sessions/" + Uri.EscapeDataString(session) + "/transcript?since=0", headers, null, out var transcript, out var refused))
                    throw new LogsRefusal(refused);
                var entries = BoardCli.Items(BoardCli.Get(transcript, "entries"));
                foreach (var entry in entries.Skip(Math.Max(0, entries.Count - options.Tail))) host.Out.WriteLine(OperatorApi.Json(entry));
            }
            return 0;
        }

        // ssh to server2 on this console: its exit code.
        private static int Ssh(NightlyHost host, string remoteCommand)
        {
            var arguments = new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2", remoteCommand };
            try
            {
                return host.Launch(new NightlyLaunch("ssh", arguments, "")).ExitCode;
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new LogsRefusal("ssh could not be started: " + e.Message);
            }
        }

        // One refusal line; the command answers 1.
        private sealed class LogsRefusal : Exception
        {
            public LogsRefusal(string message) : base(message) { }
        }
    }
}
