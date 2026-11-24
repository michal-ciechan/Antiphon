// The sanitized smoke of an AI Agent TUI profile: REST configuration, agent selection and, unless skipped, one exact reply
// from a fresh live session. It replaced scripts/verify-agent-tui-profile.ps1, deleted 2026-11-24 (unit 33 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
//
//   antiphon-scripts verify-agent-tui-profile [--base-url <url>] [--agent-name <name>] [--profile-name <name>] [--model-id <id>]
//       [--expected-reply <text>] [--canary <secret>] [--ocg-path <file>] [--session-runner-url <url>] [--skip-live-message]
//       [--evidence-dir <dir>]
//
// (scripts\antiphon.cmd verify-agent-tui-profile on Windows, scripts/antiphon verify-agent-tui-profile on sh). It GETs
// <base>/health, creates the named OpenCode profile (pwsh.exe running --ocg-path with --auto --mini, wrapper-managed auth)
// or PATCHes it at its revision, refreshes its models, validates it (the startup stage must pass and the profile must be
// interactive), PATCHes the named agent onto the profile and model (no --model-id: the runner default) and reads
// <base>/metrics/agent-tui. With --skip-live-message it stops there. Otherwise it stops the agent (ignoring a failure),
// starts it fresh without remote control and, on Windows, waits up to 30 s for the SessionRunner to expose the TUI process
// and checks its command line carries "--model" "<id>" as separate arguments (and no --model for the default). It waits up
// to 60 s for the OpenCode composer ("Ask anything..." on the runner's rendered screen), queues "Reply with exactly: <reply>"
// with mode WhenIdle, polls the buffer and the rendered screen every 3 s for up to 120 s for a line that is exactly the reply
// (ignoring case), and requires the agent's live selection to name the profile's revision and the model.
//
// This starts a real agent TUI and spends a real model turn unless --skip-live-message is given: run it only on the
// operator's stack. Every answer is saved under --evidence-dir (default <temp>/antiphon-agent-tui-smoke-<guid>); with
// --canary, a saved file or the metrics text that contains the canary fails the run at once. Exit 0 verified; 1 failed (one
// host.Error line). Usage errors are NightlyUsageException (the CLI's exit 2). HTTP is the seams' transport, the TUI's command
// line the seams' process reader, files IFileSystem's, the clock and the waits the host's, the lines host.Out's. Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The verify-agent-tui-profile command's options (verify-agent-tui-profile.ps1's parameters).</summary>
    public sealed class AgentTuiProfileSmokeOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "verify-agent-tui-profile [--base-url <url>] [--agent-name <name>] [--profile-name <name>] [--model-id <id>] [--expected-reply <text>]"
            + " [--canary <secret>] [--ocg-path <file>] [--session-runner-url <url>] [--skip-live-message] [--evidence-dir <dir>]";

        public string BaseUrl { get; init; } = "http://localhost:17282";
        public string AgentName { get; init; } = "Atlas-Orchestrator";
        public string ProfileName { get; init; } = "OpenCode Gateway";

        /// <summary>The exact model; empty: the runner default (no --model).</summary>
        public string ModelId { get; init; } = "";
        public string ExpectedReply { get; init; } = "Atlas OpenCode default verified.";

        /// <summary>A secret that must not appear in any retained evidence; empty: none.</summary>
        public string Canary { get; init; } = "";
        public string OcgPath { get; init; } = @"C:\Users\mike.ciechan\.local\bin\ocg.ps1";
        public string SessionRunnerUrl { get; init; } = "http://localhost:17204";
        public bool SkipLiveMessage { get; init; }
        public string EvidenceDir { get; init; } = "";

        /// <summary>The flags after the command name; tempPath is the default evidence directory's parent.</summary>
        public static AgentTuiProfileSmokeOptions Parse(IReadOnlyList<string> arguments, string tempPath)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--base-url", "--agent-name", "--profile-name", "--model-id", "--expected-reply", "--canary", "--ocg-path",
                "--session-runner-url", "--evidence-dir" }, new[] { "--skip-live-message" });
            string Text(string flag, string fallback) => flags.Value(flag, "") is { Length: > 0 } given ? given : fallback;
            var defaults = new AgentTuiProfileSmokeOptions();
            return new AgentTuiProfileSmokeOptions
            {
                BaseUrl = Text("--base-url", defaults.BaseUrl).TrimEnd('/'),
                AgentName = Text("--agent-name", defaults.AgentName),
                ProfileName = Text("--profile-name", defaults.ProfileName),
                ModelId = flags.Value("--model-id", ""),
                ExpectedReply = Text("--expected-reply", defaults.ExpectedReply),
                Canary = flags.Value("--canary", ""),
                OcgPath = Text("--ocg-path", defaults.OcgPath),
                SessionRunnerUrl = Text("--session-runner-url", defaults.SessionRunnerUrl).TrimEnd('/'),
                SkipLiveMessage = flags.Switch("--skip-live-message"),
                EvidenceDir = Text("--evidence-dir", Path.Combine(tempPath ?? "", "antiphon-agent-tui-smoke-" + Guid.NewGuid().ToString("N"))),
            };
        }
    }

    /// <summary>The smoke's outside reads.</summary>
    public sealed class AgentTuiProfileSmokeSeams
    {
        /// <summary>The API and SessionRunner transport.</summary>
        public NightlyReportHttp Http { get; init; }

        /// <summary>A live process's command line; null when there is no such process or it cannot be read.</summary>
        public Func<int, string> CommandLine { get; init; }

        /// <summary>True on Windows, where the TUI's launch arguments are checked.</summary>
        public bool IsWindows { get; init; }
    }

    /// <summary>The smoke failed: one host.Error line, exit 1.</summary>
    public sealed class AgentTuiProfileSmokeFailure : Exception
    {
        public AgentTuiProfileSmokeFailure(string message) : base(message) { }
    }

    /// <summary>The AI Agent TUI profile smoke (verify-agent-tui-profile.ps1).</summary>
    public static class AgentTuiProfileSmoke
    {
        /// <summary>The OpenCode composer's placeholder, shown once the TUI takes input.</summary>
        public const string ComposerReady = "Ask anything...";

        public static readonly TimeSpan RunnerProcessDeadline = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ComposerDeadline = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan ReadinessPoll = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan ReplyPoll = TimeSpan.FromSeconds(3);

        private static readonly JsonNodeOptions IgnoreCase = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private sealed class Smoke
        {
            public AgentTuiProfileSmokeOptions Options;
            public NightlyHost Host;
            public IFileSystem Files;
            public AgentTuiProfileSmokeSeams Seams;
        }

        /// <summary>Runs the smoke; see the header for the exit codes.</summary>
        public static int Run(AgentTuiProfileSmokeOptions options, NightlyHost host, IFileSystem files, AgentTuiProfileSmokeSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            try
            {
                return Verify(new Smoke { Options = options, Host = host, Files = files, Seams = seams });
            }
            catch (Exception e) when (e is AgentTuiProfileSmokeFailure || e is IOException || e is UnauthorizedAccessException || e is JsonException
                || e is System.Net.Http.HttpRequestException || e is OperationCanceledException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static AgentTuiProfileSmokeFailure Fail(string message) => new AgentTuiProfileSmokeFailure(message);

        private static JsonNode Get(JsonNode node, string key) => node is JsonObject o && o.TryGetPropertyValue(key, out var value) ? value : null;

        private static IEnumerable<JsonNode> Items(JsonNode node) => node is JsonArray a ? a : node == null ? Enumerable.Empty<JsonNode>() : new[] { node };

        private static string Str(JsonNode node)
        {
            if (node is not JsonValue v) return node == null ? "" : node.ToJsonString();
            switch (v.GetValueKind())
            {
                case JsonValueKind.String: return v.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Null: return "";
                default: return v.ToJsonString();
            }
        }

        private static bool Truthy(JsonNode node)
        {
            if (node == null) return false;
            if (node is JsonArray a) return a.Count > 0;
            if (node is JsonObject) return true;
            switch (node.GetValueKind())
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.String: return Str(node).Length > 0;
                case JsonValueKind.Number: return Str(node) != "0";
                default: return false;
            }
        }

        // a -eq b: null equals null, else the scalar texts ignoring case.
        private static bool Same(JsonNode a, JsonNode b) =>
            (a == null || a.GetValueKind() == JsonValueKind.Null) ? (b == null || b.GetValueKind() == JsonValueKind.Null)
                : b != null && string.Equals(Str(a), Str(b), StringComparison.OrdinalIgnoreCase);

        // Invoke-WebRequest: the answer's text; a status outside 2xx fails.
        private static string Request(Smoke s, string method, string uri, string body = null)
        {
            var response = s.Seams.Http(method, uri, new Dictionary<string, string>(StringComparer.Ordinal), body);
            if (response.Status < 200 || response.Status > 299)
                throw Fail(method + " " + uri + " failed (HTTP " + response.Status.ToString(CultureInfo.InvariantCulture) + ").");
            return response.Body ?? "";
        }

        // Invoke-Json: the JSON answer of <base><path>, null for an empty one; a body is sent as compact JSON.
        private static JsonNode Json(Smoke s, string method, string path, JsonNode body = null)
        {
            var text = Request(s, method, s.Options.BaseUrl + path, body?.ToJsonString());
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text, IgnoreCase);
        }

        private static JsonNode RunnerJson(Smoke s, string path)
        {
            var text = Request(s, "GET", s.Options.SessionRunnerUrl + path);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text, IgnoreCase);
        }

        // Save-Evidence: text as given, anything else as indented JSON; a saved canary fails at once.
        private static void Save(Smoke s, string name, JsonNode value) => Save(s, name, value == null ? "null" : value.ToJsonString(Indented));

        private static void Save(Smoke s, string name, string text)
        {
            var path = Path.Combine(s.Options.EvidenceDir, name);
            s.Files.File.WriteAllText(path, (text ?? "") + Environment.NewLine);
            if (s.Options.Canary.Length > 0 && s.Files.File.ReadAllText(path).Contains(s.Options.Canary, StringComparison.Ordinal))
                throw Fail("Canary secret leaked into evidence file " + name);
        }

        private static JsonArray Strings(params string[] values) => new JsonArray(values.Select(v => (JsonNode)v).ToArray());

        private static int Verify(Smoke s)
        {
            var o = s.Options;
            var output = s.Host.Out;
            s.Files.Directory.CreateDirectory(o.EvidenceDir);
            output.WriteLine("Evidence: " + o.EvidenceDir);
            output.WriteLine("Checking health...");
            Save(s, "health.txt", Request(s, "GET", o.BaseUrl + "/health"));

            var profiles = Json(s, "GET", "/api/agent-tui/profiles");
            Save(s, "profiles-before.json", profiles);
            var profile = Items(profiles).FirstOrDefault(p => string.Equals(Str(Get(p, "displayName")), o.ProfileName, StringComparison.OrdinalIgnoreCase));
            var writeBody = new JsonObject
            {
                ["displayName"] = o.ProfileName,
                ["kind"] = "OpenCode",
                ["isEnabled"] = true,
                ["isDefault"] = false,
                ["executable"] = "pwsh.exe",
                ["arguments"] = Strings("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", o.OcgPath, "--auto", "--mini"),
                ["discoveryArguments"] = Strings("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", o.OcgPath, "models"),
                ["versionArguments"] = Strings("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", o.OcgPath, "--version"),
                ["workingDirectory"] = null,
                ["authenticationMode"] = "WrapperManaged",
                ["nonSecretEnvironment"] = new JsonObject(),
                ["secretEnvironmentNames"] = new JsonArray(),
                ["modelArgumentName"] = "--model",
                ["guidance"] = "Local OpenCode gateway via ocg.ps1 (wrapper-managed auth).",
                ["models"] = new JsonArray(),
            };
            if (profile == null)
            {
                output.WriteLine("Creating profile '" + o.ProfileName + "'...");
                profile = Json(s, "POST", "/api/agent-tui/profiles", writeBody);
            }
            else
            {
                output.WriteLine("Updating profile '" + o.ProfileName + "' revision " + Str(Get(profile, "revision")) + "...");
                writeBody["expectedRevision"] = Get(profile, "revision")?.DeepClone();
                profile = Json(s, "PATCH", "/api/agent-tui/profiles/" + Str(Get(profile, "id")), writeBody);
            }
            Save(s, "profile.json", profile);
            var profileId = Str(Get(profile, "id"));

            output.WriteLine("Refreshing models...");
            Save(s, "models-refresh.json", Json(s, "POST", "/api/agent-tui/profiles/" + profileId + "/models/refresh"));

            output.WriteLine("Validating...");
            var validation = Json(s, "POST", "/api/agent-tui/profiles/" + profileId + "/validate");
            Save(s, "validation.json", validation);
            var startup = Items(Get(validation, "stages")).FirstOrDefault(stage => string.Equals(Str(Get(stage, "name")), "startup", StringComparison.OrdinalIgnoreCase));
            if (startup == null || !string.Equals(Str(Get(startup, "status")), "Passed", StringComparison.OrdinalIgnoreCase) || !Truthy(Get(Get(validation, "suitability"), "interactive")))
                throw Fail("Profile validation did not establish interactive startup readiness.");

            var agents = Json(s, "GET", "/api/agents");
            var agent = Items(agents).FirstOrDefault(a => string.Equals(Str(Get(a, "name")), o.AgentName, StringComparison.OrdinalIgnoreCase))
                ?? throw Fail("Agent '" + o.AgentName + "' not found");
            var agentId = Str(Get(agent, "id"));
            var modelValue = string.IsNullOrWhiteSpace(o.ModelId) ? null : o.ModelId;
            output.WriteLine("Patching agent '" + o.AgentName + "' -> profile " + profileId + ", model=" + modelValue);
            var patch = new JsonObject();
            foreach (var field in new[] { "name", "workingDirectory", "details", "defaultWorkflowTemplateId", "assignmentPolicy", "boardId", "alwaysOn",
                         "remoteControlEnabled", "systemPromptAppend" })
                patch[field] = Get(agent, field)?.DeepClone();
            patch["tuiProfileId"] = Get(profile, "id")?.DeepClone();
            patch["modelId"] = modelValue;
            Save(s, "agent.json", Json(s, "PATCH", "/api/agents/" + agentId, patch));

            var metrics = Request(s, "GET", o.BaseUrl + "/metrics/agent-tui");
            Save(s, "metrics.txt", metrics);
            if (o.Canary.Length > 0 && metrics.Contains(o.Canary, StringComparison.Ordinal)) throw Fail("Canary found in metrics output");

            if (o.SkipLiveMessage)
            {
                output.WriteLine("SkipLiveMessage set - configuration smoke complete.");
                return 0;
            }

            output.WriteLine("Stopping agent (if running)...");
            try { Json(s, "POST", "/api/agents/" + agentId + "/stop"); }
            catch (Exception e) when (e is AgentTuiProfileSmokeFailure || e is JsonException || e is System.Net.Http.HttpRequestException || e is OperationCanceledException) { }

            output.WriteLine("Starting agent (fresh)...");
            var started = Json(s, "POST", "/api/agents/" + agentId + "/start", new JsonObject { ["fresh"] = true, ["remoteControl"] = false });
            Save(s, "agent-started.json", started);
            var sessionId = Str(Get(Get(started, "liveSession"), "id"));
            if (sessionId.Length == 0) sessionId = Str(Get(started, "persistentSessionId"));
            if (sessionId.Length == 0) throw Fail("No live session id after start");

            if (s.Seams.IsWindows) CheckLaunchArguments(s, sessionId);

            Save(s, "ready-snapshot.json", WaitForComposer(s, sessionId));

            output.WriteLine("Queueing prompt to session " + sessionId + "...");
            Json(s, "POST", "/api/sessions/" + sessionId + "/messages", new JsonObject { ["body"] = "Reply with exactly: " + o.ExpectedReply, ["mode"] = "WhenIdle" });
            if (!WaitForReply(s, sessionId)) throw Fail("Expected reply not observed within timeout: " + o.ExpectedReply);

            // Soft check: live selection metadata.
            var agentAfter = Json(s, "GET", "/api/agents/" + agentId);
            Save(s, "agent-after.json", agentAfter);
            var selection = Get(agentAfter, "liveSessionSelection");
            if (Truthy(selection))
                output.WriteLine("Live selection: revision=" + Str(Get(selection, "tuiProfileRevisionId")) + " model=" + Str(Get(selection, "effectiveModelId"))
                    + " pendingRestart=" + Str(Get(selection, "pendingRestart")));
            if (selection == null || selection.GetValueKind() == JsonValueKind.Null || !Same(Get(selection, "tuiProfileRevisionId"), Get(profile, "revisionId"))
                || !Same(Get(selection, "effectiveModelId"), modelValue == null ? null : JsonValue.Create(modelValue)))
                throw Fail("Live session selection does not match the requested profile revision and model.");

            output.WriteLine("SMOKE OK - profile='" + o.ProfileName + "' model='" + o.ModelId + "' reply verified.");
            output.WriteLine("Evidence retained at " + o.EvidenceDir);
            return 0;
        }

        // Wait-RunnerProcess, then the launch-argument check: the default model has no --model; an explicit one is the
        // separate arguments "--model" "<id>".
        private static void CheckLaunchArguments(Smoke s, string sessionId)
        {
            var deadline = s.Host.UtcNow + RunnerProcessDeadline;
            string pid = null;
            string commandLine = null;
            while (s.Host.UtcNow < deadline)
            {
                try
                {
                    var runnerSession = RunnerJson(s, "/sessions/" + sessionId);
                    if (int.TryParse(Str(Get(runnerSession, "pid")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId) && processId > 0)
                    {
                        commandLine = s.Seams.CommandLine(processId);
                        if (commandLine != null)
                        {
                            pid = processId.ToString(CultureInfo.InvariantCulture);
                            break;
                        }
                    }
                }
                catch (Exception e) when (e is AgentTuiProfileSmokeFailure || e is JsonException || e is System.Net.Http.HttpRequestException || e is OperationCanceledException)
                {
                    // The session runner may not have launched the TUI process yet.
                }
                s.Host.Sleep(ReadinessPoll);
            }
            if (pid == null) throw Fail("The runner did not expose a live TUI process within 30 seconds.");
            var o = s.Options;
            var hasModelArgument = commandLine.Contains("\"--model\"", StringComparison.Ordinal);
            var hasExpectedModelPair = !string.IsNullOrWhiteSpace(o.ModelId) && commandLine.Contains("\"--model\" \"" + o.ModelId + "\"", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(o.ModelId) && hasModelArgument) throw Fail("The default-model launch unexpectedly contained a model argument.");
            if (!string.IsNullOrWhiteSpace(o.ModelId) && !hasExpectedModelPair) throw Fail("The explicit-model launch did not contain the expected separate model arguments.");
            Save(s, "launch-arguments.json", new JsonObject
            {
                ["processId"] = int.Parse(pid, CultureInfo.InvariantCulture), ["modelArgumentPresent"] = hasModelArgument, ["expectedModelPairPresent"] = hasExpectedModelPair,
            });
        }

        // Wait-ForOpenCodeComposer: the runner's snapshot once its rendered screen shows the composer.
        private static JsonNode WaitForComposer(Smoke s, string sessionId)
        {
            var deadline = s.Host.UtcNow + ComposerDeadline;
            while (s.Host.UtcNow < deadline)
            {
                try
                {
                    var snapshot = RunnerJson(s, "/sessions/" + sessionId + "/snapshot");
                    if (Str(Get(snapshot, "renderedScreen")).Contains(ComposerReady, StringComparison.Ordinal)) return snapshot;
                }
                catch (Exception e) when (e is AgentTuiProfileSmokeFailure || e is JsonException || e is System.Net.Http.HttpRequestException || e is OperationCanceledException)
                {
                    // The runner may still be establishing the terminal snapshot.
                }
                s.Host.Sleep(ReadinessPoll);
            }
            throw Fail("The OpenCode composer did not become ready within 60 seconds.");
        }

        // The reply poll: every 3 s the buffer and the runner's rendered screen are saved; a screen line that is exactly the
        // reply (trimmed, ignoring case) ends it. A read that fails keeps polling; a leaked canary does not.
        private static bool WaitForReply(Smoke s, string sessionId)
        {
            var deadline = s.Host.UtcNow + ReplyDeadline;
            while (s.Host.UtcNow < deadline)
            {
                s.Host.Sleep(ReplyPoll);
                JsonNode buffer, snapshot;
                try
                {
                    buffer = Json(s, "GET", "/api/sessions/" + sessionId + "/buffer");
                    snapshot = RunnerJson(s, "/sessions/" + sessionId + "/snapshot");
                }
                catch (Exception e) when (e is AgentTuiProfileSmokeFailure || e is JsonException || e is System.Net.Http.HttpRequestException || e is OperationCanceledException)
                {
                    continue;
                }
                Save(s, "buffer-latest.json", buffer);
                Save(s, "snapshot-latest.json", snapshot);
                var lines = Str(Get(snapshot, "renderedScreen")).Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
                if (lines.Contains(s.Options.ExpectedReply, StringComparer.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
