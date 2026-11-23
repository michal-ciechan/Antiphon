// CARD-0658: open the Hangfire dashboard in the default browser, already logged in. It replaced
// scripts/hangfire-dashboard.ps1 (unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it
// as `antiphon-scripts hangfire-dashboard` (scripts\antiphon.cmd hangfire-dashboard on Windows).
//
// The dashboard needs the operator credential; a loopback address is not one. The command reads the operator token from the
// owner-only file the server creates at startup (OperatorApi.ReadToken; the CLI passes ANTIPHON_OPERATOR_TOKEN_FILE, else
// the server's default path, and ANTIPHON_API in), asks POST /api/operator/dashboard-sessions for a one-time login link
// (single use, two minutes) and hands it to the browser seam, which receives a 12-hour dashboard cookie scoped to
// /hangfire. It prints neither the token, nor the link, nor its nonce: the link is a credential until it is redeemed, and
// terminals here are captured into transcripts. A refusal is reported by its problem code only.
//
// The browser is the caller's seam (openBrowser: the CLI's starts the default browser on Windows; a test records the
// address). Without one (off Windows) it prints how to send the header with curl instead and answers 2, before reading
// anything. Exit 0 opened; 1 no or empty token file (stderr), a refused login ("The server refused the dashboard login:
// <code>"), an answer without a usable link, or a browser that could not be started (each a fixed stdout line); 2 no browser
// seam. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The hangfire-dashboard command's options.</summary>
    public sealed class HangfireDashboardOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "hangfire-dashboard";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The operator token file the CLI resolved.</summary>
        public string OperatorTokenFile { get; init; } = "";

        /// <summary>The arguments after the command name (none); api and operatorTokenFile come from the CLI.</summary>
        public static HangfireDashboardOptions Parse(IReadOnlyList<string> arguments, string api, string operatorTokenFile)
        {
            NightlyArgs.Parse(arguments, Array.Empty<string>(), Array.Empty<string>());
            return new HangfireDashboardOptions { Api = OperatorApi.Base(api), OperatorTokenFile = operatorTokenFile ?? "" };
        }
    }

    /// <summary>The Hangfire dashboard login (hangfire-dashboard.ps1).</summary>
    public static class HangfireDashboard
    {
        /// <summary>The login request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets a login link and opens it with openBrowser (null: no browser here, exit 2). 0 opened; 1 refused or failed. Every
        /// line but the token file's refusal is on host.Out.
        /// </summary>
        public static int Run(HangfireDashboardOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, Action<string> openBrowser)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (openBrowser == null)
            {
                host.Out.WriteLine("Opening a browser is only supported on Windows. From a script, send the X-Antiphon-Operator-Token header (read from the operator token file) with curl to /hangfire instead.");
                return 2;
            }
            var token = OperatorApi.ReadToken(files, options.OperatorTokenFile, out var refusal);
            if (token == null)
            {
                host.Error.WriteLine(refusal);
                return 1;
            }
            var headers = new Dictionary<string, string>(StringComparer.Ordinal) { [OperatorApi.OperatorTokenHeader] = token };
            NightlyReportHttpResponse response;
            try
            {
                response = http("POST", options.Api + "/api/operator/dashboard-sessions", headers, null);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Threading.Tasks.TaskCanceledException || e is InvalidOperationException || e is IOException)
            {
                // The exception text could name the address only; the refusal is reported by code, as for an answer.
                host.Out.WriteLine("The server refused the dashboard login: request_failed");
                return 1;
            }
            if (!OperatorApi.Succeeded(response))
            {
                var code = BoardCli.Text(BoardCli.Get(Parse(response.Body), "code"));
                host.Out.WriteLine("The server refused the dashboard login: " + (string.IsNullOrWhiteSpace(code) ? "request_failed" : code));
                return 1;
            }
            var loginPath = BoardCli.Text(BoardCli.Get(Parse(response.Body), "loginPath"));
            if (string.IsNullOrWhiteSpace(loginPath) || !loginPath.StartsWith("/", StringComparison.Ordinal))
            {
                host.Out.WriteLine("The server returned no usable login link.");
                return 1;
            }
            try
            {
                openBrowser(options.Api + loginPath);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is PlatformNotSupportedException || e is FileNotFoundException)
            {
                // The error text would contain the link; a fixed message instead.
                host.Out.WriteLine("The default browser could not be started.");
                return 1;
            }
            host.Out.WriteLine("Opened the Hangfire dashboard in the default browser (session lasts 12 hours).");
            return 0;
        }

        private static JsonNode Parse(string body)
        {
            try
            {
                return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
