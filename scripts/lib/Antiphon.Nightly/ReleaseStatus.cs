// CARD-0599 D-9: whether a running build is a published release, `antiphon-scripts release-status [--release-root <dir>]
// [--version-url <url>] [--sha <sha>]`. It replaced scripts/release-status.ps1 (deleted with unit 12 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the cases run in process (ReleaseStatusTests).
//
// It reads GET <version-url> (the service under test's /api/version, 10 s) unless --sha names the SHA, then looks that SHA
// up by EXACT match (ignoring case) among the published rows of <release-root>\publications.json. The answer is the
// running SHA and capabilities plus either the matched release's tag and URL ('released') or 'unreleased integration
// build'. Missing data is 'unknown', never the latest release: no version answer, a version answer without a version, or
// no publication journal. An ancestor tag, or simply the newest release, is not the identity of a newer running SHA. It
// performs no restart, deploy, checkout or kill: it starts no process and runs no git. One compact JSON line on the host's
// stdout; exit 0. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The release-status command's options.</summary>
    public sealed class ReleaseStatusOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "release-status [--release-root <dir>] [--version-url <url>] [--sha <sha>]";

        /// <summary>The producer's release root.</summary>
        public const string DefaultReleaseRoot = @"C:\Antiphon\releases";

        /// <summary>The local service's version endpoint.</summary>
        public const string DefaultVersionUrl = "http://127.0.0.1:17202/api/version";

        private static readonly string[] Valued = { "--release-root", "--version-url", "--sha" };

        /// <summary>Holds publications.json.</summary>
        public string ReleaseRoot { get; init; } = DefaultReleaseRoot;

        /// <summary>The running service's /api/version.</summary>
        public string VersionUrl { get; init; } = DefaultVersionUrl;

        /// <summary>The SHA to look up; empty reads it from VersionUrl.</summary>
        public string Sha { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static ReleaseStatusOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Array.Empty<string>());
            return new ReleaseStatusOptions
            {
                ReleaseRoot = flags.Value("--release-root", DefaultReleaseRoot),
                VersionUrl = flags.Value("--version-url", DefaultVersionUrl),
                Sha = flags.Value("--sha", ""),
            };
        }
    }

    /// <summary>One status answer (the script's -PassThru record).</summary>
    public sealed class ReleaseStatusResult
    {
        /// <summary>The state when the SHA is a published release.</summary>
        public const string Released = "released";

        /// <summary>The state when no published release names the SHA.</summary>
        public const string Unreleased = "unreleased integration build";

        /// <summary>The state when the answer cannot be known.</summary>
        public const string Unknown = "unknown";

        /// <summary>The running (or given) SHA; empty when unknown.</summary>
        public string Sha { get; set; } = "";

        /// <summary>The running build's capabilities, as /api/version lists them.</summary>
        public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();

        /// <summary>released, unreleased integration build or unknown.</summary>
        public string State { get; set; } = Unknown;

        /// <summary>The release tag when released.</summary>
        public string Tag { get; set; } = "";

        /// <summary>The release's URL when released.</summary>
        public string ReleaseUrl { get; set; } = "";

        /// <summary>Why the state is unknown or unreleased.</summary>
        public string Detail { get; set; } = "";

        /// <summary>The command's line: sha, capabilities, state, tag, releaseUrl, detail.</summary>
        public string ToJson()
        {
            var capabilities = new JsonArray();
            foreach (var capability in Capabilities) capabilities.Add(capability);
            return new JsonObject
            {
                ["sha"] = Sha, ["capabilities"] = capabilities, ["state"] = State, ["tag"] = Tag, ["releaseUrl"] = ReleaseUrl, ["detail"] = Detail,
            }.ToJsonString();
        }
    }

    /// <summary>CARD-0599 D-9's release status.</summary>
    public static class ReleaseStatus
    {
        /// <summary>The version read's bound (Invoke-RestMethod -TimeoutSec 10).</summary>
        public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Observes the status and writes its JSON line to the host's stdout. Always exit 0 (in the result's state).</summary>
        public static ReleaseStatusResult Run(ReleaseStatusOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var status = Observe(options, http);
            host.Out.WriteLine(status.ToJson());
            return status;
        }

        /// <summary>The status, read only: the version endpoint (unless a SHA is given) and the publication journal.</summary>
        public static ReleaseStatusResult Observe(ReleaseStatusOptions options, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var status = new ReleaseStatusResult();
            if (string.IsNullOrWhiteSpace(options.Sha))
            {
                JsonObject version = null;
                try
                {
                    var answer = http("GET", options.VersionUrl, new Dictionary<string, string>(), null);
                    if (answer != null && answer.Status >= 200 && answer.Status < 300) version = ReleaseGateJournal.Parse(answer.Body) as JsonObject;
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is OperationCanceledException || e is JsonException
                    || e is InvalidOperationException || e is UriFormatException || e is IOException)
                {
                    version = null;
                }
                if (version == null)
                {
                    status.Detail = "version endpoint unavailable";
                    return status;
                }
                status.Sha = ReleaseGateJournal.Str(version["version"]);
                status.Capabilities = Capabilities(version["capabilities"]);
                if (string.IsNullOrWhiteSpace(status.Sha))
                {
                    status.Detail = "version endpoint named no version";
                    return status;
                }
            }
            else status.Sha = options.Sha;

            var journalPath = Path.Combine(options.ReleaseRoot ?? "", "publications.json");
            if (!File.Exists(journalPath))
            {
                status.Detail = "no publication journal";
                return status;
            }
            foreach (var row in ReleaseGateJournal.Rows(ReleaseGateJournal.Read(journalPath)))
            {
                if (!ReleaseGateJournal.Truthy(row["published"])) continue;
                // EXACT sha match only. An ancestor tag is not this build's identity.
                if (!string.Equals(ReleaseGateJournal.Str(row["sha"]), status.Sha, StringComparison.OrdinalIgnoreCase)) continue;
                status.State = ReleaseStatusResult.Released;
                status.Tag = ReleaseGateJournal.Str(row["tag"]);
                status.ReleaseUrl = ReleaseGateJournal.Str(row["releaseUrl"]);
                return status;
            }
            status.State = ReleaseStatusResult.Unreleased;
            status.Detail = "no published release manifest names this sha";
            return status;
        }

        // @($body.capabilities) when it is set: an array's entries, or the one value.
        private static IReadOnlyList<string> Capabilities(JsonNode value)
        {
            var list = new List<string>();
            if (!ReleaseGateJournal.Truthy(value)) return list;
            if (value is JsonArray array)
            {
                foreach (var item in array) list.Add(ReleaseGateJournal.Str(item));
                return list;
            }
            list.Add(ReleaseGateJournal.Str(value));
            return list;
        }
    }
}
