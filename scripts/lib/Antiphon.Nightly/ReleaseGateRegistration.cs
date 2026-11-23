// CARD-0599 D-7: the idempotent registration front door for the three release-gate Windmill jobs,
// `antiphon-scripts register-release-gates --profile <profile.json> [--apply] [--enable-schedule nightly|readiness|rc]
// [--definition-root <dir>]`. It replaced scripts/register-release-gates.ps1 and release-gate.ps1's
// Invoke-ReleaseGateWindmill and Get-ReleaseGateTokenFromFile (deleted with unit 12 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the cases run in process (ReleaseGateRegistrationTests).
//
// Preview by default: it writes NOTHING, reads no token, and prints the content digest, tag, cron, timezone and the
// live-versus-desired difference of each definition. --apply registers or reconciles them; --enable-schedule turns on ONE
// named schedule and requires --apply. Registration uses the installed Windmill HTTP API (the transport), never a database
// write. The token is read from the operator-placed file the profile names and goes only into the Authorization header:
// never printed, logged or embedded in a payload; this command creates no token and holds no watchdog credential.
// Schedules are always CREATED disabled; enablement is explicit and never a consequence of a reapply, and an
// already-enabled matching schedule is preserved by an idempotent reapply. An apply reads every write back (the readback is
// the evidence, not the accepted request) and refuses content drift, and live cron or timezone drift refuses instead of
// being overwritten. The definitions are scripts/windmill/<job>.json and <job>.schedule.json of the checkout (or
// --definition-root). Running this against the live workspace is an operator S5/S6 step; a Code or Review delegate does
// not. Exit 0 done; 1 a write or readback failed; 2 usage; 3 refused before or instead of a write. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>The register-release-gates command's options.</summary>
    public sealed class ReleaseGateRegistrationOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "register-release-gates --profile <profile.json> [--apply] [--enable-schedule nightly|readiness|rc] [--definition-root <dir>]";

        private static readonly string[] Valued = { "--profile", "--enable-schedule", "--definition-root" };
        private static readonly string[] Switches = { "--apply" };

        /// <summary>The untracked profile: windmillBaseUrl, workspace (mc), tokenFile, repositoryPath, projectId. No inline token.</summary>
        public string ProfilePath { get; init; } = "";

        /// <summary>Perform the registration writes; otherwise a read-only preview.</summary>
        public bool Apply { get; init; }

        /// <summary>nightly, readiness or rc: the one schedule to enable (requires Apply); empty enables none.</summary>
        public string EnableSchedule { get; init; } = "";

        /// <summary>Where the definitions are read (the checkout's scripts/windmill).</summary>
        public string DefinitionRoot { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line or an unknown schedule name.</summary>
        public static ReleaseGateRegistrationOptions Parse(IReadOnlyList<string> arguments, string defaultDefinitionRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var enable = flags.Value("--enable-schedule", "");
            if (enable.Length > 0 && !ReleaseGateRegistration.Definitions.Any(d => string.Equals(d.Key, enable, StringComparison.OrdinalIgnoreCase)))
                throw new NightlyUsageException("--enable-schedule must be nightly, readiness or rc");
            var root = flags.Value("--definition-root", "");
            return new ReleaseGateRegistrationOptions
            {
                ProfilePath = flags.Required("--profile"), Apply = flags.Switch("--apply"), EnableSchedule = enable,
                DefinitionRoot = root.Length > 0 ? root : defaultDefinitionRoot ?? "",
            };
        }
    }

    /// <summary>One definition's row: the desired and the live state and what was (or would be) done.</summary>
    public sealed class ReleaseGateRegistrationRow
    {
        public string Key { get; set; } = "";
        public string ScriptPath { get; set; } = "";
        public string DesiredDigest { get; set; } = "";
        public string LiveHash { get; set; } = "";
        public bool ScriptExists { get; set; }
        public string Tag { get; set; } = "";
        public string Cron { get; set; } = "";
        public string Timezone { get; set; } = "";
        /// <summary>The schedule's args, as plain values.</summary>
        public object Args { get; set; }
        public bool DesiredEnabled { get; set; }
        /// <summary>The live schedule's enablement; null when there is no live schedule.</summary>
        public bool? LiveEnabled { get; set; }
        public string LiveCron { get; set; } = "";
        public bool ScheduleExists { get; set; }
        /// <summary>none, create-script or update-script, then +create-schedule or +schedule-unchanged; preview:&lt;action&gt; in a preview.</summary>
        public string Action { get; set; } = "none";
        /// <summary>The stored script's hash after a write; empty when nothing was written.</summary>
        public string ReadbackHash { get; set; } = "";
        /// <summary>SHA-256 of the stored script's content after a write.</summary>
        public string ReadbackContentDigest { get; set; } = "";
    }

    /// <summary>What a preview or an apply did (the script's -PassThru record).</summary>
    public sealed class ReleaseGateRegistrationResult
    {
        /// <summary>0 done, 1 a write or readback failed, 3 refused.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>preview or apply.</summary>
        public string Mode { get; set; } = "preview";

        /// <summary>The refusal; empty when done.</summary>
        public string Refusal { get; set; } = "";

        /// <summary>The writes performed.</summary>
        public int Writes { get; set; }

        /// <summary>The rows completed.</summary>
        public IReadOnlyList<ReleaseGateRegistrationRow> Rows { get; set; } = Array.Empty<ReleaseGateRegistrationRow>();

        /// <summary>The schedule enabled; empty when none.</summary>
        public string Enabled { get; set; } = "";
    }

    /// <summary>The release-gate Windmill registration (CARD-0599 D-7).</summary>
    public static class ReleaseGateRegistration
    {
        /// <summary>The three jobs: the selector, the script definition and the schedule definition.</summary>
        public static readonly IReadOnlyList<(string Key, string Script, string Schedule)> Definitions = new[]
        {
            ("nightly", "antiphon-nightly-tests.json", "antiphon-nightly-tests.schedule.json"),
            ("readiness", "antiphon-nightly-readiness.json", "antiphon-nightly-readiness.schedule.json"),
            ("rc", "antiphon-release-candidates.json", "antiphon-release-candidates.schedule.json"),
        };

        /// <summary>The profile fields that must be set.</summary>
        public static readonly IReadOnlyList<string> ProfileFields = new[] { "windmillBaseUrl", "workspace", "tokenFile", "repositoryPath", "projectId" };

        /// <summary>The profile fields that would carry a secret inline.</summary>
        public static readonly IReadOnlyList<string> ForbiddenProfileFields = new[] { "token", "password", "secret", "watchdogToken" };


        private sealed record WindmillAnswer(bool Ok, int Status, object Body);

        /// <summary>Runs the preview or apply; its lines go to the host's stdout. Never throws for a refusal: the result carries the exit code.</summary>
        public static ReleaseGateRegistrationResult Run(ReleaseGateRegistrationOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            void Line(string message) => host.Out.WriteLine("[register-release-gates] " + message);
            var result = new ReleaseGateRegistrationResult { Mode = options.Apply ? "apply" : "preview" };
            ReleaseGateRegistrationResult Refuse(string message, string refusal, int exitCode, List<ReleaseGateRegistrationRow> rows = null)
            {
                Line("REFUSED: " + message);
                result.Refusal = refusal;
                result.ExitCode = exitCode;
                if (rows != null) result.Rows = rows.ToArray();
                return result;
            }
            if (!string.IsNullOrWhiteSpace(options.EnableSchedule) && !options.Apply)
                return Refuse("--enable-schedule requires --apply.", "enable-requires-apply", 3);

            OrderedDictionary profile;
            try { profile = ReadProfile(options.ProfilePath); }
            catch (NightlyRefusal e) { return Refuse(e.Message, "profile-invalid", 3); }
            var workspace = Text(Field(profile, "workspace"));
            var baseUrl = Text(Field(profile, "windmillBaseUrl"));
            var token = "";
            if (options.Apply)
            {
                try { token = ReadToken(Text(Field(profile, "tokenFile"))); }
                catch (NightlyRefusal e) { return Refuse(e.Message, "token-unavailable", 3); }
            }
            WindmillAnswer Call(string method, string path, object body)
            {
                var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                // The token goes into this header only; a preview sends none.
                if (token.Length > 0) headers["Authorization"] = "Bearer " + token;
                try
                {
                    var answer = http(method, baseUrl.TrimEnd('/') + path, headers, body == null ? null : JsonText(body));
                    if (answer == null) return new WindmillAnswer(false, 0, null);
                    var ok = answer.Status >= 200 && answer.Status < 300;
                    return new WindmillAnswer(ok, answer.Status, ok ? BodyOf(answer.Body) : null);
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is OperationCanceledException || e is InvalidOperationException
                    || e is UriFormatException || e is IOException)
                {
                    return new WindmillAnswer(false, 0, null);
                }
            }

            var rows = new List<ReleaseGateRegistrationRow>();
            var writes = 0;
            foreach (var definition in Definitions)
            {
                var scriptFile = Path.Combine(options.DefinitionRoot ?? "", definition.Script);
                var scheduleFile = Path.Combine(options.DefinitionRoot ?? "", definition.Schedule);
                foreach (var needed in new[] { scriptFile, scheduleFile })
                    if (!File.Exists(needed)) return Refuse("missing definition " + needed + ".", "definition-missing", 3);
                var scriptDef = ReadDefinition(scriptFile);
                var scheduleDef = ReadDefinition(scheduleFile);
                var scriptPath = Text(Field(scriptDef, "path"));
                var schedulePath = Text(Field(scheduleDef, "path"));
                if (!string.Equals(Text(Field(scheduleDef, "script_path")), scriptPath, StringComparison.OrdinalIgnoreCase))
                    return Refuse("schedule " + Text(Field(scheduleDef, "script_path")) + " does not name script " + scriptPath + ".", "definition-path-mismatch", 3);
                // D-7: a future schedule payload must start disabled.
                if (PsValue.IsTrue(Field(scheduleDef, "enabled")) && definition.Key == "rc")
                    return Refuse("rc schedule payload must be created disabled.", "rc-schedule-preenabled", 3);

                var desired = DefinitionDigest(scriptDef);
                var live = Call("GET", "/api/w/" + workspace + "/scripts/get/p/" + scriptPath, null);
                var liveExists = live.Ok && PsValue.IsTrue(live.Body);
                var liveHash = liveExists ? Text(Field(live.Body, "hash")) : "";
                var liveSchedule = Call("GET", "/api/w/" + workspace + "/schedules/get/" + schedulePath, null);
                var scheduleExists = liveSchedule.Ok && PsValue.IsTrue(liveSchedule.Body);
                var row = new ReleaseGateRegistrationRow
                {
                    Key = definition.Key, ScriptPath = scriptPath, DesiredDigest = desired, LiveHash = liveHash, ScriptExists = liveExists,
                    Tag = Text(Field(scriptDef, "tag")), Cron = Text(Field(scheduleDef, "schedule")), Timezone = Text(Field(scheduleDef, "timezone")),
                    Args = Field(scheduleDef, "args"), DesiredEnabled = PsValue.IsTrue(Field(scheduleDef, "enabled")),
                    LiveEnabled = scheduleExists ? PsValue.IsTrue(Field(liveSchedule.Body, "enabled")) : (bool?)null,
                    LiveCron = scheduleExists ? Text(Field(liveSchedule.Body, "schedule")) : "", ScheduleExists = scheduleExists,
                };
                if (!liveExists) row.Action = "create-script";
                else if (!string.Equals(liveHash, desired, StringComparison.OrdinalIgnoreCase)) row.Action = "update-script";

                if (!options.Apply)
                {
                    row.Action = "preview:" + row.Action;
                    rows.Add(row);
                    continue;
                }

                if (row.Action == "create-script" || row.Action == "update-script")
                {
                    var payload = new OrderedDictionary
                    {
                        { "path", scriptPath }, { "summary", Text(Field(scriptDef, "summary")) }, { "description", Text(Field(scriptDef, "description")) },
                        { "content", Text(Field(scriptDef, "content")) }, { "language", Text(Field(scriptDef, "language")) }, { "tag", row.Tag },
                        { "schema", Field(scriptDef, "schema") },
                    };
                    if (liveExists) payload["parent_hash"] = liveHash;
                    var write = Call("POST", "/api/w/" + workspace + "/scripts/create", payload);
                    if (!write.Ok)
                        return Refuse("script write failed for " + scriptPath + " (status " + write.Status.ToString(CultureInfo.InvariantCulture) + ").", "script-write-failed", 1, rows);
                    writes++;
                    // Readback is the evidence, not the accepted request.
                    var back = Call("GET", "/api/w/" + workspace + "/scripts/get/p/" + scriptPath, null);
                    if (!back.Ok || back.Body == null) return Refuse("script readback failed for " + scriptPath + ".", "script-readback-failed", 1, rows);
                    row.ReadbackHash = Text(Field(back.Body, "hash"));
                    var stored = Text(Field(back.Body, "content"));
                    row.ReadbackContentDigest = Sha256(stored);
                    if (!string.Equals(stored, Text(Field(scriptDef, "content")), StringComparison.Ordinal))
                        return Refuse("stored content differs from the desired definition for " + scriptPath + ".", "script-content-drift", 1, rows);
                }

                if (!scheduleExists)
                {
                    var payload = new OrderedDictionary
                    {
                        { "path", schedulePath }, { "schedule", row.Cron }, { "timezone", row.Timezone }, { "script_path", Text(Field(scheduleDef, "script_path")) },
                        { "is_flow", PsValue.IsTrue(Field(scheduleDef, "is_flow")) }, { "args", Field(scheduleDef, "args") }, { "enabled", false },
                        { "tag", Text(Field(scheduleDef, "tag")) },
                    };
                    var write = Call("POST", "/api/w/" + workspace + "/schedules/create", payload);
                    if (!write.Ok)
                        return Refuse("schedule write failed for " + schedulePath + " (status " + write.Status.ToString(CultureInfo.InvariantCulture) + ").", "schedule-write-failed", 1, rows);
                    writes++;
                    row.Action += "+create-schedule";
                    var back = Call("GET", "/api/w/" + workspace + "/schedules/get/" + schedulePath, null);
                    if (!back.Ok || back.Body == null) return Refuse("schedule readback failed for " + schedulePath + ".", "schedule-readback-failed", 1, rows);
                    if (PsValue.IsTrue(Field(back.Body, "enabled"))) return Refuse("schedule " + schedulePath + " was created enabled.", "schedule-created-enabled", 1, rows);
                    row.LiveEnabled = false;
                }
                else
                {
                    // Idempotent reapply: an existing matching schedule keeps its enablement.
                    if (!string.Equals(row.LiveCron, row.Cron, StringComparison.Ordinal)
                        || !string.Equals(Text(Field(liveSchedule.Body, "timezone")), row.Timezone, StringComparison.Ordinal))
                        return Refuse("live schedule " + schedulePath + " differs (cron/timezone); resolve the drift explicitly.", "schedule-drift", 3, rows);
                    row.Action += "+schedule-unchanged";
                }
                rows.Add(row);
            }
            result.Rows = rows.ToArray();
            result.Writes = writes;

            if (options.Apply && !string.IsNullOrWhiteSpace(options.EnableSchedule))
            {
                var target = Definitions.Where(d => string.Equals(d.Key, options.EnableSchedule, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (target.Length != 1) return Refuse("unknown schedule selector " + options.EnableSchedule + ".", "unknown-schedule", 3);
                var schedulePath = Text(Field(ReadDefinition(Path.Combine(options.DefinitionRoot ?? "", target[0].Schedule)), "path"));
                var enable = Call("POST", "/api/w/" + workspace + "/schedules/setenabled/" + schedulePath, new OrderedDictionary { { "enabled", true } });
                if (!enable.Ok)
                    return Refuse("enable failed for " + schedulePath + " (status " + enable.Status.ToString(CultureInfo.InvariantCulture) + ").", "enable-failed", 1);
                var back = Call("GET", "/api/w/" + workspace + "/schedules/get/" + schedulePath, null);
                if (!back.Ok || !PsValue.IsTrue(Field(back.Body, "enabled"))) return Refuse("enable readback did not confirm " + schedulePath + ".", "enable-readback-failed", 1);
                result.Enabled = target[0].Key;
                result.Writes = result.Writes + 1;
            }

            foreach (var row in rows)
                Line(row.Key + ": " + row.Action + " cron=" + row.Cron + " tz=" + row.Timezone + " tag=" + row.Tag + " desiredDigest=" + row.DesiredDigest.Substring(0, 12)
                    + " liveHash=" + row.LiveHash + " liveEnabled=" + (row.LiveEnabled.HasValue ? (row.LiveEnabled.Value ? "True" : "False") : ""));
            result.ExitCode = 0;
            return result;
        }

        /// <summary>
        /// Read-ReleaseGateRegistrationProfile: every ProfileFields value set, workspace mc (this deployment), and none of the
        /// ForbiddenProfileFields (a token is read from tokenFile, never inline). Field names match ignoring case.
        /// </summary>
        public static OrderedDictionary ReadProfile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new NightlyRefusal("profile missing: " + path);
            OrderedDictionary profile;
            try { profile = ReleaseGateRemote.Plain(ParseDocument(File.ReadAllText(path))) as OrderedDictionary; }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException) { throw new NightlyRefusal("profile unreadable: " + path + ": " + e.Message); }
            if (profile == null) throw new NightlyRefusal("profile unreadable: " + path + ": not a JSON object");
            foreach (var field in ProfileFields)
                if (string.IsNullOrWhiteSpace(Text(Field(profile, field)))) throw new NightlyRefusal("profile field " + field + " is required");
            if (!string.Equals(Text(Field(profile, "workspace")), "mc", StringComparison.OrdinalIgnoreCase))
                throw new NightlyRefusal("workspace must be mc for this deployment, got " + Text(Field(profile, "workspace")));
            foreach (var forbidden in ForbiddenProfileFields)
                if (profile.Contains(forbidden)) throw new NightlyRefusal("profile must not carry an inline " + forbidden + "; use tokenFile");
            return profile;
        }

        /// <summary>
        /// Get-ReleaseGateDefinitionDigest: SHA-256 of a definition's canonical JSON (NightlyDocuments.CanonicalJson: keys
        /// sorted, only \ " CR LF TAB escaped), over its plain values (JSON objects as case-insensitive OrderedDictionary).
        /// </summary>
        public static string DefinitionDigest(object definition) => Sha256(NightlyDocuments.CanonicalJson(definition));

        /// <summary>DefinitionDigest of a JSON document's text.</summary>
        public static string DefinitionDigest(string json) => DefinitionDigest(ReleaseGateRemote.Plain(ParseDocument(json)));

        /// <summary>Get-ReleaseGateTokenFromFile: the trimmed token. It never reaches a log line, a script payload or a preview diff.</summary>
        public static string ReadToken(string tokenFile)
        {
            if (string.IsNullOrWhiteSpace(tokenFile)) throw new NightlyRefusal("tokenFile is required");
            if (!File.Exists(tokenFile)) throw new NightlyRefusal("token file missing: " + tokenFile);
            var text = File.ReadAllText(tokenFile).Trim();
            if (string.IsNullOrWhiteSpace(text)) throw new NightlyRefusal("token file empty: " + tokenFile);
            return text;
        }

        private static JsonElement ParseDocument(string text)
        {
            using var document = JsonDocument.Parse(text ?? "", new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.Clone();
        }

        // A checked-in definition as plain values (objects case-insensitive OrderedDictionary).
        private static OrderedDictionary ReadDefinition(string path) =>
            ReleaseGateRemote.Plain(ParseDocument(File.ReadAllText(path))) as OrderedDictionary ?? new OrderedDictionary(StringComparer.OrdinalIgnoreCase);

        // An answer's body: its JSON value (Invoke-RestMethod's object), the text when it is not JSON, null when empty.
        private static object BodyOf(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return ReleaseGateRemote.Plain(ParseDocument(text)); }
            catch (JsonException) { return text; }
        }

        private static object Field(object value, string name) => value is IDictionary map && map.Contains(name) ? map[name] : null;

        private static string Text(object value) => value == null ? "" : PsValue.Str(value);

        private static string Sha256(string text) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();

        private static string JsonText(object body)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) PlainJson.Write(writer, body);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
