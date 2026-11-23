// What the board API commands share (unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: capability,
// model-availability, specialist-routing, pipeline-definition, project, schedule, dispatch-concurrency, routing-pin). Each
// replaced the scripts/<name>.ps1 of the same name. This file holds the verb and flag reading they have in common, one
// request on the host's HTTP transport with the failure line the scripts printed ("Antiphon <METHOD> <path> failed: ..."),
// and the PowerShell-like reading of an answer's values (a JSON boolean prints True or False, a missing value prints empty).
// The API base and the task token come from the CLI. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>What the board API commands share.</summary>
    public static class BoardCli
    {
        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

        private static readonly Regex UtcOffset = new Regex("(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The verb: the first argument when it is not a flag, matched to verbs ignoring case (as ValidateSet did) and answered
        /// in its listed spelling; fallback when the first argument is a flag or there is none. rest is what follows the verb.
        /// A verb outside the list, or no verb where fallback is null, is a NightlyUsageException.
        /// </summary>
        public static string Verb(IReadOnlyList<string> arguments, IReadOnlyList<string> verbs, string fallback, out List<string> rest)
        {
            var all = (arguments ?? Array.Empty<string>()).Select(argument => argument ?? "").ToList();
            var first = all.Count > 0 ? all[0] : "";
            var list = string.Join(", ", verbs);
            if (first.Length > 0 && !first.StartsWith("--", StringComparison.Ordinal))
            {
                var verb = verbs.FirstOrDefault(candidate => string.Equals(candidate, first, StringComparison.OrdinalIgnoreCase));
                if (verb == null) throw new NightlyUsageException("the first argument is the verb (" + list + "), not '" + first + "'");
                rest = all.Skip(1).ToList();
                return verb;
            }
            if (fallback == null) throw new NightlyUsageException("the first argument is the verb: " + list);
            rest = all;
            return fallback;
        }

        /// <summary>The positional value after the verb (a schedule id, a project name), taken out of rest; null when the next argument is a flag or there is none.</summary>
        public static string Positional(List<string> rest)
        {
            if (rest == null || rest.Count == 0 || rest[0].StartsWith("--", StringComparison.Ordinal)) return null;
            var value = rest[0];
            rest.RemoveAt(0);
            return value;
        }

        /// <summary>Refuses (NightlyUsageException) each given flag of declared that is not in allowed: "--x does not apply to &lt;verb&gt;".</summary>
        public static void Only(NightlyArgs flags, string verb, IEnumerable<string> declared, IEnumerable<string> allowed)
        {
            var permitted = new HashSet<string>(allowed ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var flag in declared)
                if (!permitted.Contains(flag) && flags.Has(flag)) throw new NightlyUsageException(flag + " does not apply to " + verb);
        }

        /// <summary>
        /// A ValidateSet value: null when value is empty; otherwise its listed spelling when it matches one ignoring case, else a
        /// NightlyUsageException naming the flag and the choices.
        /// </summary>
        public static string Choice(string flag, string value, IReadOnlyList<string> choices)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var match = choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase));
            if (match == null) throw new NightlyUsageException(flag + " must be one of " + string.Join(", ", choices) + ", not '" + value + "'");
            return match;
        }

        /// <summary>True for an ISO-8601 text ending in Z or a numeric offset (the scripts' Assert-UtcOffset).</summary>
        public static bool HasUtcOffset(string value) => UtcOffset.IsMatch(value ?? "");

        /// <summary>The request headers: X-Antiphon-Task-Token when the task token is set (never printed).</summary>
        public static Dictionary<string, string> Headers(string taskToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(taskToken)) headers[OperatorApi.TaskTokenHeader] = taskToken;
            return headers;
        }

        /// <summary>
        /// One request: api + path with the headers and the body text (null: none). True with the parsed answer (null for an
        /// empty body) on a 2xx status; false with one failure line otherwise: "Antiphon &lt;METHOD&gt; &lt;path&gt; failed: " and the
        /// transport's message or Detail of the answer, or a 2xx answer that is not JSON.
        /// </summary>
        public static bool Send(NightlyReportHttp http, string api, string method, string path, IReadOnlyDictionary<string, string> headers, string body,
            out JsonNode answer, out string failure)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            answer = null;
            failure = null;
            NightlyReportHttpResponse response;
            try
            {
                response = http(method, api + path, headers ?? new Dictionary<string, string>(), body);
            }
            catch (Exception e)
            {
                // A transport failure: no status. The exception text names the address, never a header.
                failure = "Antiphon " + method + " " + path + " failed: " + e.Message;
                return false;
            }
            if (!OperatorApi.Succeeded(response))
            {
                failure = "Antiphon " + method + " " + path + " failed: " + Detail(response);
                return false;
            }
            try
            {
                answer = string.IsNullOrWhiteSpace(response.Body) ? null : JsonNode.Parse(response.Body);
            }
            catch (JsonException e)
            {
                failure = "Antiphon " + method + " " + path + " answered with a body that is not JSON: " + e.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// A failed answer's detail. A problem body with a detail is read as the scripts read it: the detail, then "code &lt;code&gt;",
        /// "  &lt;field&gt;: &lt;message&gt;" for each validation error and "  &lt;board&gt;  &lt;id&gt;  &lt;status&gt;  &lt;title&gt;" for each candidate card, one per
        /// line. Any other answer is "HTTP &lt;status&gt;" and its body.
        /// </summary>
        public static string Detail(NightlyReportHttpResponse response)
        {
            var raw = (response?.Body ?? "").Trim();
            JsonObject problem = null;
            try
            {
                problem = raw.Length == 0 ? null : JsonNode.Parse(raw) as JsonObject;
            }
            catch (JsonException)
            {
                problem = null;
            }
            if (problem == null || !Truthy(Get(problem, "detail"))) return OperatorApi.Failure(response);
            var lines = new List<string> { Text(Get(problem, "detail")) };
            if (Truthy(Get(problem, "code"))) lines.Add("code " + Text(Get(problem, "code")));
            if (Get(problem, "errors") is JsonObject errors)
                foreach (var field in errors)
                    foreach (var message in Items(field.Value)) lines.Add("  " + field.Key + ": " + Text(message));
            foreach (var candidate in Items(Get(problem, "candidates")))
                lines.Add("  " + Text(Get(candidate, "boardName")) + "  " + Text(Get(candidate, "id")) + "  " + Text(Get(candidate, "status")) + "  " + Text(Get(candidate, "title")));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>A property along a path of objects; null when a step is missing or not an object (PowerShell's $null).</summary>
        public static JsonNode Get(JsonNode node, params string[] path)
        {
            var current = node;
            foreach (var name in path)
            {
                if (!(current is JsonObject obj) || !obj.TryGetPropertyValue(name, out var next)) return null;
                current = next;
            }
            return current;
        }

        /// <summary>
        /// A value as the scripts' -f printed it: empty for null, a string as is, True or False for a boolean, a number's JSON
        /// text, an array's items joined with spaces, an object as compact JSON.
        /// </summary>
        public static string Text(JsonNode node)
        {
            if (node == null) return "";
            switch (node.GetValueKind())
            {
                case JsonValueKind.String: return node.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "";
                case JsonValueKind.Number: return node.ToJsonString();
                case JsonValueKind.Array: return string.Join(" ", node.AsArray().Select(Text));
                default: return OperatorApi.Json(node);
            }
        }

        /// <summary>PowerShell's truth of a value: null, an empty string, false, zero and an empty array are false.</summary>
        public static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            switch (node.GetValueKind())
            {
                case JsonValueKind.String: return node.GetValue<string>().Length > 0;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return false;
                case JsonValueKind.Number: return node.GetValue<double>() != 0;
                case JsonValueKind.Array: return node.AsArray().Count > 0;
                default: return true;
            }
        }

        /// <summary>@(value): an array's items, one item for any other value, none for null.</summary>
        public static IReadOnlyList<JsonNode> Items(JsonNode node)
        {
            if (node == null || node.GetValueKind() == JsonValueKind.Null) return Array.Empty<JsonNode>();
            if (node is JsonArray array) return array.ToList();
            return new[] { node };
        }

        /// <summary>The items' texts joined with ", " (the scripts' @(...) -join ', ').</summary>
        public static string Join(JsonNode node) => string.Join(", ", Items(node).Select(Text));

        /// <summary>Writes the line and answers 1 (a refused value or a failed request).</summary>
        public static int Refuse(NightlyHost host, string message)
        {
            host.Error.WriteLine(message);
            return 1;
        }
    }
}
