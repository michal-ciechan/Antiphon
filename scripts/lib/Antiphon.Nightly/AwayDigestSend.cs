// CARD-0036: send the server-composed away digest now, `antiphon-scripts digest` (POST /api/digest/send, which runs the
// digest for one channel or for every digest-enabled one). It replaced scripts/digest.ps1 (deleted 2026-11-23, unit 5 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). A --channel that is not a GUID is looked up by title
// (GET /api/channels, ignoring case) and must name exactly one channel. --since is read in the invariant culture; a time
// with no zone is the host's local time; it is sent as UTC round-trip text. The server's answer (one result per channel)
// is printed indented. The HTTP transport is the board client's (NightlyReport.HttpTransport). Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The digest command's options.</summary>
    public sealed class AwayDigestSendOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "digest [--channel <guid|title>] [--since <time>] [--base-url http://localhost:17202]";

        private static readonly string[] Valued = { "--channel", "--since", "--base-url" };

        /// <summary>The channel's GUID or title; empty sends every digest-enabled channel's digest.</summary>
        public string Channel { get; init; } = "";

        /// <summary>The digest window's start as given; empty lets the server choose.</summary>
        public string Since { get; init; } = "";

        /// <summary>The server's base URL.</summary>
        public string BaseUrl { get; init; } = NightlyReport.DefaultApi;

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or a --since that is not a time.</summary>
        public static AwayDigestSendOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            var since = flags.Value("--since", "");
            if (since.Length > 0 && !DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new NightlyUsageException("--since expects a time, not '" + since + "'");
            var baseUrl = flags.Value("--base-url", "");
            return new AwayDigestSendOptions
            {
                Channel = flags.Value("--channel", ""), Since = since, BaseUrl = baseUrl.Length > 0 ? baseUrl : NightlyReport.DefaultApi,
            };
        }
    }

    /// <summary>The digest send.</summary>
    public static class AwayDigestSend
    {
        private static readonly JsonSerializerOptions Printed = new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// The send: resolves the channel, posts {channelId?, since?} and prints the answer on host.Out. Exit 0 sent (or
        /// nothing due); 1 the channel title is not unique, the server refused or could not be reached, or a channel's
        /// result is send_failed (one line on host.Error; the answer is still printed when there is one).
        /// </summary>
        public static int Run(AwayDigestSendOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var api = options.BaseUrl.TrimEnd('/');
            try
            {
                var body = new JsonObject();
                if (options.Channel.Length > 0)
                {
                    if (!Guid.TryParse(options.Channel, out var channelId))
                    {
                        var listed = Call(http, "GET", api + "/api/channels", null);
                        var matches = (JsonNode.Parse(listed) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                            .Where(channel => Text(channel, "title") is { Length: > 0 } title
                                && string.Equals(title, options.Channel, StringComparison.InvariantCultureIgnoreCase))
                            .ToList();
                        if (matches.Count != 1 || !Guid.TryParse(Text(matches[0], "id"), out channelId))
                        {
                            host.Error.WriteLine("digest: Channel '" + options.Channel + "' was not found uniquely by title. Pass its GUID instead.");
                            return 1;
                        }
                    }
                    body["channelId"] = channelId.ToString("D");
                }
                if (options.Since.Length > 0) body["since"] = Utc(options.Since, host.Clock.LocalTimeZone).ToString("O", CultureInfo.InvariantCulture);

                var answer = Call(http, "POST", api + "/api/digest/send", body.ToJsonString());
                JsonNode results;
                try { results = JsonNode.Parse(answer); }
                catch (JsonException) { results = null; }
                host.Out.WriteLine(results == null ? answer : results.ToJsonString(Printed).Replace("\r\n", "\n").Replace("\n", host.Out.NewLine));
                var failed = (results as JsonArray ?? new JsonArray(results?.DeepClone())).OfType<JsonObject>().Count(result => Text(result, "reason") == "send_failed");
                if (failed > 0)
                {
                    host.Error.WriteLine("digest: " + failed + " channel(s) send_failed");
                    return 1;
                }
                return 0;
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is JsonException || e is InvalidOperationException
                || e is ArgumentException)
            {
                host.Error.WriteLine("digest: " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return 1;
            }
        }

        // One call; InvalidOperationException for an answer that is not a success.
        private static string Call(NightlyReportHttp http, string method, string uri, string body)
        {
            var response = http(method, uri, new Dictionary<string, string>(), body);
            if (response.Status < 200 || response.Status > 299)
                throw new InvalidOperationException(method + " " + uri + " answered " + response.Status + (string.IsNullOrWhiteSpace(response.Body) ? "" : ": " + response.Body));
            return response.Body ?? "";
        }

        // [datetime]$Since then .ToUniversalTime(): a zone in the text is honoured; no zone is the local time zone.
        private static DateTime Utc(string text, TimeZoneInfo local)
        {
            var parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Local => parsed.ToUniversalTime(),
                _ => TimeZoneInfo.ConvertTimeToUtc(parsed, local),
            };
        }

        private static string Text(JsonObject item, string name) =>
            item[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
