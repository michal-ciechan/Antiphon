// The parts the operator API commands share (unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: runner-drain,
// workspace-slots, herdr-pane, worktree-residue): the API base, the owner-only operator token file the server creates at
// startup (read, trimmed, never printed), a compact JSON body and the HTTP failure text. The CLI resolves the token file's
// path (ANTIPHON_OPERATOR_TOKEN_FILE, else the server's default) and passes it in. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>What the operator API commands share.</summary>
    public static class OperatorApi
    {
        /// <summary>The API when ANTIPHON_API is unset.</summary>
        public const string DefaultApi = "http://localhost:17202";

        /// <summary>The operator token's header.</summary>
        public const string OperatorTokenHeader = "X-Antiphon-Operator-Token";

        /// <summary>The task token's header.</summary>
        public const string TaskTokenHeader = "X-Antiphon-Task-Token";

        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>ANTIPHON_API's value without a trailing slash; DefaultApi when it is blank.</summary>
        public static string Base(string api) => (string.IsNullOrWhiteSpace(api) ? DefaultApi : api).TrimEnd('/');

        /// <summary>
        /// Reads the operator token file: the trimmed token, or null and the reason (it names the path, never the content):
        /// "Operator token file not found at &lt;path&gt;. ..." or "Operator token file at &lt;path&gt; is empty.".
        /// </summary>
        public static string ReadToken(IFileSystem files, string path, out string refusal)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (string.IsNullOrEmpty(path) || !files.File.Exists(path))
            {
                refusal = "Operator token file not found at " + path + ". The server creates it at startup; run this as the account the server runs under.";
                return null;
            }
            var token = files.File.ReadAllText(path).Trim();
            if (token.Length == 0)
            {
                refusal = "Operator token file at " + path + " is empty.";
                return null;
            }
            refusal = null;
            return token;
        }

        /// <summary>A body as compact JSON (non-ASCII and quotes unescaped, as ConvertTo-Json -Compress wrote them).</summary>
        public static string Json(JsonNode body) => body == null ? "null" : body.ToJsonString(Compact);

        /// <summary>A value as indented JSON (ConvertTo-Json).</summary>
        public static string Pretty(JsonNode node) => node == null ? "null" : node.ToJsonString(Indented);

        /// <summary>A failed answer's text: "HTTP &lt;status&gt;", then the body when it has one.</summary>
        public static string Failure(NightlyReportHttpResponse answer)
        {
            if (answer == null) return "HTTP unavailable";
            var body = (answer.Body ?? "").Trim();
            return "HTTP " + answer.Status + (body.Length == 0 ? "" : " " + body);
        }

        /// <summary>True for a 2xx status.</summary>
        public static bool Succeeded(NightlyReportHttpResponse answer) => answer != null && answer.Status >= 200 && answer.Status <= 299;
    }
}
