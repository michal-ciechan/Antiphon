// CARD-0461: explicit best-effort herdr pane disposal through the server's routes. It replaced scripts/herdr-pane.ps1 (deleted
// 2026-11-23, unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts herdr-pane <inspect|dispose|status>`. Ordinary Stop still detaches attached panes. External changes after
// the final Antiphon check can race an unconditional pane.close, so dispose is a dry run (the request it would send, printed)
// unless --execute is given, and it disposes only a reviewed preview.
//
//   herdr-pane inspect --pane-id <w..:p..> (--expected-session-id <uuid> | --expected-native-session-id <uuid>) [--json]
//   herdr-pane dispose --preview-id <uuid> --operation-id <uuid> (--reason <text> | --reason-file <path>) [--execute] [--json]
//   herdr-pane status --operation-id <uuid> [--json]
//
// The task token (ANTIPHON_TASK_TOKEN) is sent when set and never printed; neither is a request header or a transport
// exception. A failed request prints the server's JSON error body, or {code: herdr_disposal_transport_failed} (with a detail
// telling the operator to read the status under the same OperationId when the body was not JSON), and exits 1. Usage errors
// are NightlyUsageException (the CLI's exit 2) and reach no route. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The herdr-pane command's options (herdr-pane.ps1 &lt;verb&gt; -PaneId -ExpectedSessionId -ExpectedNativeSessionId -PreviewId -OperationId -Reason -ReasonFile -Execute -Json).</summary>
    public sealed class HerdrPaneOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "herdr-pane <inspect|dispose|status> [--pane-id <w..:p..>] [--expected-session-id <uuid>] [--expected-native-session-id <uuid>]"
            + " [--preview-id <uuid>] [--operation-id <uuid>] [--reason <text> | --reason-file <path>] [--execute] [--json]";

        private static readonly Regex PanePattern = new Regex(@"\Aw[0-9A-Za-z]+:p[0-9A-Za-z]+\z", RegexOptions.CultureInvariant);

        /// <summary>inspect, dispose or status.</summary>
        public string Verb { get; init; } = "inspect";

        /// <summary>inspect: the exact workspace-qualified pane.</summary>
        public string PaneId { get; init; } = "";

        /// <summary>inspect: the session the pane must belong to (normalised), or empty.</summary>
        public string ExpectedSessionId { get; init; } = "";

        /// <summary>inspect: the native session the pane must belong to (normalised), or empty.</summary>
        public string ExpectedNativeSessionId { get; init; } = "";

        /// <summary>dispose: the reviewed preview (normalised).</summary>
        public string PreviewId { get; init; } = "";

        /// <summary>dispose and status: the operation (normalised).</summary>
        public string OperationId { get; init; } = "";

        /// <summary>dispose: the reason, or empty when ReasonFile names it.</summary>
        public string Reason { get; init; } = "";

        /// <summary>dispose: a file holding the reason, read whole at run time.</summary>
        public string ReasonFile { get; init; } = "";

        /// <summary>dispose: send it (absent: print the request instead).</summary>
        public bool Execute { get; init; }

        /// <summary>Print the answer as JSON (inspect otherwise prints four lines).</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: no token header.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The verb and the flags after the command name; the API and the token come from the CLI.</summary>
        public static HerdrPaneOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = arguments != null && arguments.Count > 0 ? arguments[0] ?? "" : "";
            if (verb != "inspect" && verb != "dispose" && verb != "status") throw new NightlyUsageException("the first argument is the verb: inspect, dispose or status, not '" + verb + "'");
            var flags = NightlyArgs.Parse(arguments.Skip(1).ToArray(),
                new[] { "--pane-id", "--expected-session-id", "--expected-native-session-id", "--preview-id", "--operation-id", "--reason", "--reason-file" },
                new[] { "--execute", "--json" });
            var pane = flags.Value("--pane-id", "");
            var expected = flags.Value("--expected-session-id", "");
            var native = flags.Value("--expected-native-session-id", "");
            var preview = flags.Value("--preview-id", "");
            var operation = flags.Value("--operation-id", "");
            var reason = flags.Value("--reason", "");
            var reasonFile = flags.Value("--reason-file", "");
            switch (verb)
            {
                case "inspect":
                    if (pane.Length > 128 || !PanePattern.IsMatch(pane)) throw new NightlyUsageException("--pane-id requires one exact workspace-qualified pane ID.");
                    if (expected.Length == 0 && native.Length == 0) throw new NightlyUsageException("--expected-session-id or --expected-native-session-id is required.");
                    if (expected.Length > 0) expected = Id(expected, "--expected-session-id");
                    if (native.Length > 0) native = Id(native, "--expected-native-session-id");
                    break;
                case "dispose":
                    preview = Id(preview, "--preview-id");
                    operation = Id(operation, "--operation-id");
                    if (reasonFile.Length > 0 && reason.Length > 0) throw new NightlyUsageException("Use --reason or --reason-file, not both.");
                    if (reasonFile.Length == 0 && Blank(reason)) throw new NightlyUsageException("A nonblank --reason or --reason-file of at most 4096 characters is required.");
                    break;
                default:
                    operation = Id(operation, "--operation-id");
                    break;
            }
            return new HerdrPaneOptions
            {
                Verb = verb,
                PaneId = pane,
                ExpectedSessionId = expected,
                ExpectedNativeSessionId = native,
                PreviewId = preview,
                OperationId = operation,
                Reason = reason,
                ReasonFile = reasonFile,
                Execute = flags.Switch("--execute"),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }

        /// <summary>A reason the route accepts: not blank and at most 4096 characters.</summary>
        internal static bool Blank(string reason) => string.IsNullOrWhiteSpace(reason) || reason.Length > 4096;

        // Require-Id: a full, nonempty UUID in the D form, normalised.
        private static string Id(string value, string name)
        {
            if (!Guid.TryParseExact(value ?? "", "D", out var parsed) || parsed == Guid.Empty) throw new NightlyUsageException(name + " requires a full nonempty UUID.");
            return parsed.ToString("D");
        }
    }

    /// <summary>The herdr pane disposal front door (herdr-pane.ps1).</summary>
    public static class HerdrPane
    {
        /// <summary>The disposal routes' prefix.</summary>
        public const string RoutePrefix = "/api/herdr/pane-disposals";

        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

        /// <summary>The answer when a request failed without a readable body.</summary>
        public const string TransportFailedCode = "herdr_disposal_transport_failed";

        /// <summary>
        /// Runs the verb: the answer on host.Out (JSON, or inspect's four lines). Exit 0; 1 when the request failed (its error
        /// printed); 2 when the reason file cannot be read or holds a reason the route refuses (one stderr line, nothing sent).
        /// </summary>
        public static int Run(HerdrPaneOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var path = RoutePrefix;
            var method = "POST";
            JsonObject body = null;
            switch (options.Verb)
            {
                case "inspect":
                    body = new JsonObject { ["paneId"] = options.PaneId };
                    if (options.ExpectedSessionId.Length > 0) body["expectedSessionId"] = options.ExpectedSessionId;
                    if (options.ExpectedNativeSessionId.Length > 0) body["expectedNativeSessionId"] = options.ExpectedNativeSessionId;
                    path += "/preview";
                    break;
                case "dispose":
                    var reason = options.Reason;
                    if (options.ReasonFile.Length > 0)
                    {
                        var file = files.Path.GetFullPath(options.ReasonFile);
                        if (!files.File.Exists(file))
                        {
                            host.Error.WriteLine("herdr-pane: --reason-file " + options.ReasonFile + " does not exist.");
                            return 2;
                        }
                        reason = files.File.ReadAllText(file);
                    }
                    if (HerdrPaneOptions.Blank(reason))
                    {
                        host.Error.WriteLine("herdr-pane: A nonblank --reason or --reason-file of at most 4096 characters is required.");
                        return 2;
                    }
                    body = new JsonObject { ["previewId"] = options.PreviewId, ["operationId"] = options.OperationId, ["reason"] = reason };
                    if (!options.Execute)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(new JsonObject { ["dryRun"] = true, ["method"] = method, ["path"] = path, ["body"] = body }));
                        return 0;
                    }
                    break;
                default:
                    path += "/" + options.OperationId;
                    method = "GET";
                    break;
            }

            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            JsonNode result;
            var failed = false;
            try
            {
                var answer = http(method, options.Api + path, headers, body == null ? null : OperatorApi.Json(body));
                if (OperatorApi.Succeeded(answer)) result = JsonNode.Parse(answer.Body ?? "");
                else
                {
                    failed = true;
                    result = ErrorBody(answer.Body);
                }
            }
            catch (Exception)
            {
                // Never print the request, its headers or an arbitrary transport exception.
                failed = true;
                result = ErrorBody(null);
            }
            if (options.Json || options.Verb != "inspect" || failed)
            {
                host.Out.WriteLine(OperatorApi.Pretty(result));
            }
            else
            {
                var fields = result as JsonObject ?? new JsonObject();
                host.Out.WriteLine("Pane " + Text(fields["paneId"]) + ", tab " + Text(fields["tabId"]) + ", workspace " + Text(fields["workspaceId"]) + "; eligible="
                    + Text(fields["eligible"]) + "; guardAvailable=" + Text(fields["guardAvailable"]));
                var blockers = fields["blockers"];
                host.Out.WriteLine("Preview " + Text(fields["previewId"]) + ", expires " + Text(fields["expiresAtUtc"]) + "; blockers: "
                    + (blockers is JsonArray list ? string.Join(", ", list.Select(Text)) : Text(blockers)));
                host.Out.WriteLine("Guard mode: " + Text(fields["guardMode"]) + "; atomic close: " + Text(fields["atomicClose"]) + "; process inventory complete: "
                    + Text(fields["processInventoryComplete"]));
                host.Out.WriteLine("External changes after the final check can race close. Execute only the reviewed preview.");
            }
            return failed ? 1 : 0;
        }

        // The failed answer: the server's JSON error body; a body that is not JSON names the transport failure and how to recover;
        // no body names it alone.
        private static JsonNode ErrorBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return new JsonObject { ["code"] = TransportFailedCode };
            try
            {
                return JsonNode.Parse(body) ?? new JsonObject { ["code"] = TransportFailedCode };
            }
            catch (JsonException)
            {
                return new JsonObject
                {
                    ["code"] = TransportFailedCode,
                    ["detail"] = "Request failed. Read status using the same OperationId; do not automatically retry disposal.",
                };
            }
        }

        // A value as the script's -f formatted it: a string as is, a boolean True or False, anything else its JSON text.
        private static string Text(JsonNode node)
        {
            if (node == null) return "";
            if (node is JsonValue value)
            {
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: return value.GetValue<string>();
                    case JsonValueKind.True: return "True";
                    case JsonValueKind.False: return "False";
                }
            }
            return node.ToJsonString();
        }
    }
}
