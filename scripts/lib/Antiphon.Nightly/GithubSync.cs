// CARD-0166 S7 and CARD-0171: trigger Antiphon's bidirectional GitHub Issues <-> cards sync. It replaced the body of
// scripts/github-sync.ps1 (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts github-sync` (scripts/antiphon on sh, scripts\antiphon.cmd on Windows). scripts/github-sync.ps1 stays
// as a thin launcher: Windmill's u/lndcobra/antiphon_github_sync (every 3 hours, desktop tag, SSH bridge) starts it by
// path with -Notify.
//
//   github-sync [--board-id <guid>] [--base-url http://localhost:17202] [--notify]
//
// It calls the Antiphon API only (the server holds the GitHub token): POST /api/boards/{board}/tracker/sync, or without
// --board-id POST /api/tracker-sync/run (every board whose tracker is not Internal); --notify forwards ?notify=true, and
// the server then announces what changed on each changed board's tracker.notify_channel. --base-url defaults to the
// AppHost port, as the script's -BaseUrl did (ANTIPHON_API is not read). It prints "POST <url>", "HTTP <status>", the
// answer, and for each board "  <name>: pulled=.. commentsIn=.. commentsOut=.. labels=.. state=.. creates=.. reopens=..
// changes=<n> skips=[..]" (and " error=<error>"), "  (concurrent run skipped for at least one board)", and each
// notification's outcome ("  notified board <id> -> channel <id>" or "  NOT notified board <id>: <reason>"; a notification
// never fails the sync, which has already committed). Exit 0 every board synced cleanly; 1 the request failed ("HTTP
// <status> from <url>" and the answer, or "Request failed: <reason>", on stderr) or a board reported an error ("<n>
// board(s) reported an error."). A failed sync is never a green Windmill job (CARD-0171). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The github-sync command's options (github-sync.ps1 -BoardId -BaseUrl -Notify).</summary>
    public sealed class GithubSyncOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "github-sync [--board-id <guid>] [--base-url <url>] [--notify]";

        /// <summary>One board; null: every board with a tracker.</summary>
        public Guid? BoardId { get; init; }

        /// <summary>The API base, without a trailing slash.</summary>
        public string BaseUrl { get; init; } = OperatorApi.DefaultApi;

        /// <summary>Ask the server to announce what this run changed.</summary>
        public bool Notify { get; init; }

        /// <summary>The arguments after the command name. A --board-id that is not a GUID is a NightlyUsageException.</summary>
        public static GithubSyncOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--board-id", "--base-url" }, new[] { "--notify" });
            var board = flags.Value("--board-id", "");
            Guid? id = null;
            if (board.Length > 0)
            {
                if (!Guid.TryParse(board, out var parsed)) throw new NightlyUsageException("--board-id expects a GUID, not '" + board + "'");
                id = parsed;
            }
            return new GithubSyncOptions { BoardId = id, BaseUrl = OperatorApi.Base(flags.Value("--base-url", "")), Notify = flags.Switch("--notify") };
        }
    }

    /// <summary>The tracker sync trigger (github-sync.ps1).</summary>
    public static class GithubSync
    {
        /// <summary>The request's deadline: the server syncs every board before it answers.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(15);

        /// <summary>Runs the sync; 0 when every board synced cleanly, 1 otherwise.</summary>
        public static int Run(GithubSyncOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var url = options.BoardId.HasValue
                ? options.BaseUrl + "/api/boards/" + options.BoardId.Value.ToString("D") + "/tracker/sync"
                : options.BaseUrl + "/api/tracker-sync/run";
            if (options.Notify) url += "?notify=true";

            host.Out.WriteLine("POST " + url);
            NightlyReportHttpResponse response;
            try
            {
                response = http("POST", url, new Dictionary<string, string>(), null);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Threading.Tasks.TaskCanceledException || e is InvalidOperationException || e is IOException)
            {
                host.Error.WriteLine("Request failed: " + e.Message);
                return 1;
            }
            var body = response.Body ?? "";
            if (!OperatorApi.Succeeded(response))
            {
                host.Error.WriteLine("HTTP " + response.Status + " from " + url + Environment.NewLine + body);
                return 1;
            }
            host.Out.WriteLine("HTTP " + response.Status);
            host.Out.WriteLine(body);

            JsonNode summary;
            try
            {
                summary = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                // Not JSON: the raw answer is already printed.
                summary = null;
            }
            var boardErrors = 0;
            if (BoardCli.Truthy(BoardCli.Get(summary, "boards")))
            {
                foreach (var board in BoardCli.Items(BoardCli.Get(summary, "boards")))
                {
                    var error = BoardCli.Get(board, "error");
                    if (BoardCli.Truthy(error)) boardErrors++;
                    host.Out.WriteLine("  " + Text(board, "boardName") + ": pulled=" + Text(board, "issuesPulled") + " commentsIn=" + Text(board, "commentsIn")
                        + " commentsOut=" + Text(board, "commentsOut") + " labels=" + Text(board, "labelsChanged") + " state=" + Text(board, "stateChanges")
                        + " creates=" + Text(board, "creates") + " reopens=" + Text(board, "externalReopens")
                        + " changes=" + BoardCli.Items(BoardCli.Get(board, "changes")).Count.ToString(CultureInfo.InvariantCulture) + " skips=[" + BoardCli.Join(BoardCli.Get(board, "skips")) + "]"
                        + (BoardCli.Truthy(error) ? " error=" + BoardCli.Text(error) : ""));
                }
            }
            if (BoardCli.Truthy(BoardCli.Get(summary, "concurrentRunSkipped"))) host.Out.WriteLine("  (concurrent run skipped for at least one board)");
            // CARD-0171: a board that changed but could not be announced says why (notify_channel_unset, channel_not_found,
            // channel_ambiguous, channel_disabled, send_failed); it never fails the sync, which has already committed.
            if (BoardCli.Truthy(BoardCli.Get(summary, "notifications")))
            {
                foreach (var notification in BoardCli.Items(BoardCli.Get(summary, "notifications")))
                {
                    host.Out.WriteLine(BoardCli.Truthy(BoardCli.Get(notification, "sent"))
                        ? "  notified board " + Text(notification, "boardId") + " -> channel " + Text(notification, "channelId")
                        : "  NOT notified board " + Text(notification, "boardId") + ": " + Text(notification, "reason"));
                }
            }
            if (boardErrors > 0)
            {
                host.Out.WriteLine(boardErrors.ToString(CultureInfo.InvariantCulture) + " board(s) reported an error.");
                return 1;
            }
            return 0;
        }

        private static string Text(JsonNode node, string name) => BoardCli.Text(BoardCli.Get(node, name));
    }
}
