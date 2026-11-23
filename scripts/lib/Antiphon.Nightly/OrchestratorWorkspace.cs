// CARD-0251: inspect, preview and create a dedicated sibling orchestrator workspace. It replaced
// scripts/orchestrator-workspace.ps1 (deleted 2026-11-23, unit 26 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md);
// the CLI runs it as `antiphon-scripts orchestrator-workspace <verb> <target> [...]` (scripts/antiphon orchestrator-workspace on
// sh, scripts\antiphon.cmd on Windows).
//
// Sibling layout: C:\src\<checkout>-orchestrator beside C:\src\<checkout>. Nested is unsafe for Claude Code (the parent
// CLAUDE.md leaks into every Shared delegate).
//
//   orchestrator-workspace inspect <dir|project> [--json]
//   orchestrator-workspace plan    <project> [--cli claude|codex|grok] [--path <sibling>] [--json]
//   orchestrator-workspace setup   <project> [--cli claude|codex|grok] [--path <sibling>] [--verify]
//   orchestrator-workspace acknowledge <project> [--json]
//
// plan never writes. setup writes the marker (antiphon.workspace.json), the context file and, for Claude, .claude/settings.json
// with the orchestrator hook, a junction .claude/skills to the checkout's skills, and the external-import flags of the
// sibling's project key in ~/.claude.json (the CLI passes the user profile in); it does not PATCH the agent's
// WorkingDirectory (that is the operator's S5 step). The Codex and Grok arms write files and print "hooks unverified". Every
// file write goes through IFileSystem and the junction through the caller's seam (WorkspaceJunction: the CLI binds mklink /J
// on Windows only; without one, setup warns and leaves the skills link out), so a test runs on an in-memory file system.
// A project is found by its guid or exact name, then by its name ignoring case. The task token (ANTIPHON_TASK_TOKEN) is sent
// when set, never printed. Exit 0 done; 1 an unresolvable project, a project without a checkout, an unreadable marker or
// ~/.claude.json, or a failed request (one stderr line); usage errors are NightlyUsageException (the CLI's exit 2). Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>Creates the directory junction link pointing at target; null when it was created, else why not.</summary>
    public delegate string WorkspaceJunction(string link, string target);

    /// <summary>The orchestrator-workspace command's options (orchestrator-workspace.ps1 &lt;verb&gt; &lt;target&gt; and its flags).</summary>
    public sealed class OrchestratorWorkspaceOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "orchestrator-workspace <inspect <dir|project> [--json] | plan <project> [--cli claude|codex|grok] [--path <sibling>] [--json]"
            + " | setup <project> [--cli claude|codex|grok] [--path <sibling>] [--verify] | acknowledge <project> [--json]>";

        private static readonly string[] Verbs = { "inspect", "plan", "setup", "acknowledge" };
        private static readonly string[] Valued = { "--cli", "--path" };
        private static readonly string[] Switches = { "--verify", "--json" };
        private static readonly string[] Clis = { "claude", "codex", "grok" };

        /// <summary>inspect, plan, setup or acknowledge.</summary>
        public string Verb { get; init; } = "inspect";

        /// <summary>The project's name or guid (inspect: or a directory).</summary>
        public string Target { get; init; } = "";

        /// <summary>plan and setup: claude, codex or grok.</summary>
        public string Cli { get; init; } = "claude";

        /// <summary>plan and setup: the sibling's path; empty: beside the checkout.</summary>
        public string Path { get; init; } = "";

        /// <summary>setup: print how to verify the CLI picks the workspace up.</summary>
        public bool Verify { get; init; }

        /// <summary>inspect, plan and acknowledge: print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The user profile folder whose .claude.json setup writes (USERPROFILE).</summary>
        public string UserProfile { get; init; } = "";

        /// <summary>
        /// The verb, the target and the flags; the API, the task token and the user profile come from the CLI. A missing verb or
        /// target, a --cli outside its choices, or a flag the verb does not take is a NightlyUsageException.
        /// </summary>
        public static OrchestratorWorkspaceOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string userProfile)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var target = BoardCli.Positional(rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var declared = Valued.Concat(Switches).ToArray();
            switch (verb)
            {
                case "inspect":
                case "acknowledge":
                    BoardCli.Only(flags, verb, declared, new[] { "--json" });
                    break;
                case "plan":
                    BoardCli.Only(flags, verb, declared, new[] { "--cli", "--path", "--json" });
                    break;
                default:
                    BoardCli.Only(flags, verb, declared, new[] { "--cli", "--path", "--verify" });
                    break;
            }
            if (string.IsNullOrWhiteSpace(target)) throw new NightlyUsageException("pass a project name or guid after " + verb + (verb == "inspect" ? ", or a directory" : ""));
            return new OrchestratorWorkspaceOptions
            {
                Verb = verb,
                Target = target,
                Cli = BoardCli.Choice("--cli", flags.Value("--cli"), Clis) ?? "claude",
                Path = flags.Value("--path", ""),
                Verify = flags.Switch("--verify"),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                UserProfile = userProfile ?? "",
            };
        }
    }

    /// <summary>The dedicated sibling orchestrator workspace (orchestrator-workspace.ps1).</summary>
    public static class OrchestratorWorkspace
    {
        /// <summary>The marker that points a sibling workspace at its checkout.</summary>
        public const string MarkerFileName = "antiphon.workspace.json";

        /// <summary>The readiness check this command reads.</summary>
        public const string ReadinessKey = "orchestrator-workspace";

        /// <summary>The live-settings rule both context files carry (CARD-0822).</summary>
        public const string LiveSettingsRule = "Live operating settings: read the file at $env:ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (or GET /api/orchestrator-instructions)"
            + " at session start, after compaction and when a settings-changed note arrives; it outranks the numbers in AGENTS.md.";

        private const string HooksUnverified = "hooks unverified - see CARD-0251 plan 1.2/1.3";

        private sealed class Failure : Exception
        {
            public Failure(string message) : base(message) { }
        }

        /// <summary>
        /// Runs the verb: the answer on host.Out; exit 0, or 1 after one line on host.Error. junction creates setup's skills link
        /// (null: none here; setup warns and goes on).
        /// </summary>
        public static int Run(OrchestratorWorkspaceOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, WorkspaceJunction junction)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            try
            {
                switch (options.Verb)
                {
                    case "inspect": Inspect(options, host, files, http, headers); break;
                    case "plan": Plan(options, host, files, http, headers); break;
                    case "setup": Setup(options, host, files, http, headers, junction); break;
                    default: Acknowledge(options, host, http, headers); break;
                }
                return 0;
            }
            catch (Failure e)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static void Inspect(OrchestratorWorkspaceOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            if (files.File.Exists(options.Target) || files.Directory.Exists(options.Target))
            {
                var directory = files.Path.GetFullPath(options.Target);
                var marker = files.Path.Combine(directory, MarkerFileName);
                var facts = new JsonObject
                {
                    ["directory"] = directory,
                    ["markerExists"] = files.File.Exists(marker) || files.Directory.Exists(marker),
                    ["claudeMd"] = Exists(files, files.Path.Combine(directory, "CLAUDE.md")),
                    ["agentsMd"] = Exists(files, files.Path.Combine(directory, "AGENTS.md")),
                };
                if (BoardCli.Truthy(BoardCli.Get(facts, "markerExists"))) facts["marker"] = ReadJson(files, marker);
                if (options.Json)
                {
                    host.Out.WriteLine(OperatorApi.Pretty(facts));
                    return;
                }
                host.Out.WriteLine("directory: " + directory);
                host.Out.WriteLine("marker:    " + T(facts, "markerExists"));
                if (BoardCli.Truthy(BoardCli.Get(facts, "marker"))) host.Out.WriteLine("checkout:  " + BoardCli.Text(BoardCli.Get(facts, "marker", "checkout")));
                host.Out.WriteLine("CLAUDE.md: " + T(facts, "claudeMd"));
                host.Out.WriteLine("AGENTS.md: " + T(facts, "agentsMd"));
                host.Out.WriteLine("For the server classification, pass a project name: inspect <project>.");
                return;
            }
            var project = Resolve(options, http, headers);
            var check = WorkspaceCheck(options, http, headers, T(project, "id"));
            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(check));
                return;
            }
            host.Out.WriteLine("Project: " + T(project, "name") + " (" + T(project, "id") + ")");
            if (check == null) throw new Failure("readiness did not return an orchestrator-workspace row.");
            host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-12} {1}", T(check, "status"), T(check, "summary")));
            if (BoardCli.Truthy(BoardCli.Get(check, "detail"))) host.Out.WriteLine(T(check, "detail"));
        }

        private static void Plan(OrchestratorWorkspaceOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var project = Resolve(options, http, headers);
            var checkout = Checkout(files, project);
            var sibling = Sibling(files, options, checkout);
            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(new JsonObject { ["checkout"] = checkout, ["sibling"] = sibling, ["cli"] = options.Cli, ["projectId"] = T(project, "id") }));
                return;
            }
            var check = WorkspaceCheck(options, http, headers, T(project, "id"));
            if (check != null) host.Out.WriteLine("current: " + T(check, "status") + " - " + T(check, "summary"));
            host.Out.WriteLine("checkout: " + checkout);
            host.Out.WriteLine("sibling:  " + sibling);
            host.Out.WriteLine("cli:      " + options.Cli);
            host.Out.WriteLine("would write:");
            host.Out.WriteLine("  " + files.Path.Combine(sibling, MarkerFileName));
            if (options.Cli == "claude")
            {
                host.Out.WriteLine("  " + files.Path.Combine(sibling, "CLAUDE.md"));
                host.Out.WriteLine("  " + files.Path.Combine(sibling, ".claude", "settings.json"));
                host.Out.WriteLine("  junction " + files.Path.Combine(sibling, ".claude", "skills") + " -> " + files.Path.Combine(checkout, ".claude", "skills"));
                host.Out.WriteLine("  ~/.claude.json projects[\"" + ForwardSlashKey(files, sibling) + "\"].hasClaudeMdExternalIncludesApproved = true");
            }
            else
            {
                host.Out.WriteLine("  " + files.Path.Combine(sibling, "AGENTS.md"));
                host.Out.WriteLine("  " + HooksUnverified);
            }
            host.Out.WriteLine("would NOT write: the agent WorkingDirectory PATCH (operator / S5).");
            host.Out.WriteLine("project: " + T(project, "id"));
        }

        private static void Setup(OrchestratorWorkspaceOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers,
            WorkspaceJunction junction)
        {
            var project = Resolve(options, http, headers);
            var checkout = Checkout(files, project);
            if (!Exists(files, checkout)) throw new Failure("Checkout '" + checkout + "' does not exist.");
            var sibling = Sibling(files, options, checkout);
            files.Directory.CreateDirectory(sibling);
            var marker = new JsonObject
            {
                ["version"] = 1,
                ["checkout"] = Relative(files, sibling, checkout),
                ["project"] = T(project, "id"),
                ["cli"] = options.Cli,
            };
            Write(files, files.Path.Combine(sibling, MarkerFileName), OperatorApi.Json(marker));
            if (options.Cli == "claude")
            {
                Write(files, files.Path.Combine(sibling, "CLAUDE.md"), ClaudeContext(Relative(files, sibling, files.Path.Combine(checkout, "AGENTS.md"))));
                var settings = files.Path.Combine(sibling, ".claude");
                files.Directory.CreateDirectory(settings);
                Write(files, files.Path.Combine(settings, "settings.json"), OperatorApi.Pretty(ClaudeSettings(files.Path.Combine(checkout, "scripts", "hooks", "orchestrator-investigation-hook.mjs"))));
                var skills = files.Path.Combine(checkout, ".claude", "skills");
                var link = files.Path.Combine(settings, "skills");
                if (Exists(files, skills) && !Exists(files, link))
                {
                    var refused = junction == null ? "directory junctions are created on Windows only" : junction(link, skills);
                    if (refused != null) host.Error.WriteLine("WARNING: the skills junction " + link + " -> " + skills + " was not created: " + refused);
                }
                ApproveExternalIncludes(options, files, sibling);
            }
            else
            {
                Write(files, files.Path.Combine(sibling, "AGENTS.md"), AgentsContext(Relative(files, sibling, files.Path.Combine(checkout, "AGENTS.md"))));
                host.Out.WriteLine(HooksUnverified);
            }
            host.Out.WriteLine("wrote sibling workspace: " + sibling);
            host.Out.WriteLine("Next: PATCH the standing orchestrator WorkingDirectory to that path, then restart it.");
            host.Out.WriteLine("  scripts/antiphon orchestrator-workspace plan " + T(project, "name"));
            if (!options.Verify) return;
            if (options.Cli == "claude") host.Out.WriteLine("Claude has no offline inspect; run the S0 codeword canary for proof.");
            else if (options.Cli == "codex") host.Out.WriteLine("Verify with: codex debug prompt-input  (hooks unverified)");
            else host.Out.WriteLine("Verify with: grok inspect --json  (hooks unverified)");
        }

        private static void Acknowledge(OrchestratorWorkspaceOptions options, NightlyHost host, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var project = Resolve(options, http, headers);
            var route = "/api/projects/" + Uri.EscapeDataString(T(project, "id")) + "/acknowledge-orchestrator-workspace";
            var result = Send(options, http, headers, "POST", route, "{}");
            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(result));
                return;
            }
            var check = Row(result);
            host.Out.WriteLine(check != null ? T(check, "status") + ": " + T(check, "summary") : "acknowledged.");
        }

        /// <summary>The CLAUDE.md of a Claude sibling: the orchestrator rule, the live-settings rule and the @ import of the checkout's AGENTS.md (relative).</summary>
        public static string ClaudeContext(string agentsImport) => string.Join("\n", new[]
        {
            "You are an orchestrator. You do not do the work - you decompose it, delegate every piece,",
            "and integrate what comes back. Read server/Bundles/orchestrator.md in the checkout for the",
            "standing rules.",
            LiveSettingsRule,
            "",
            "@" + agentsImport,
        });

        /// <summary>The AGENTS.md of a Codex or Grok sibling: the orchestrator rule, the live-settings rule and where the checkout's AGENTS.md is (relative).</summary>
        public static string AgentsContext(string agentsPath) => string.Join("\n", new[]
        {
            "You are an orchestrator. You do not do the work - you decompose it, delegate every piece,",
            "and integrate what comes back.",
            LiveSettingsRule,
            "",
            "At session start, read " + agentsPath + " in full before acting.",
        });

        /// <summary>
        /// The .claude/settings.json of a Claude sibling: the checkout's orchestrator hook (node "&lt;hook with forward slashes&gt;", 5 s) on
        /// PreToolUse for Read|Grep|Glob|Bash|PowerShell and on SessionStart for startup|resume|compact.
        /// </summary>
        public static JsonObject ClaudeSettings(string hookPath)
        {
            JsonArray Hook() => new JsonArray(new JsonObject { ["type"] = "command", ["command"] = "node \"" + hookPath.Replace('\\', '/') + "\"", ["timeout"] = 5 });
            return new JsonObject
            {
                ["hooks"] = new JsonObject
                {
                    ["PreToolUse"] = new JsonArray(new JsonObject { ["matcher"] = "Read|Grep|Glob|Bash|PowerShell", ["hooks"] = Hook() }),
                    ["SessionStart"] = new JsonArray(new JsonObject { ["matcher"] = "startup|resume|compact", ["hooks"] = Hook() }),
                },
            };
        }

        /// <summary>
        /// Get-SiblingPath: &lt;parent&gt;/&lt;name&gt;-orchestrator beside the checkout; &lt;checkout&gt;/orchestrator for a root that has no
        /// parent or name.
        /// </summary>
        public static string SiblingPath(IFileSystem files, string checkout)
        {
            var root = files.Path.GetFullPath(checkout);
            var full = root.TrimEnd('\\', '/');
            var parent = files.Path.GetDirectoryName(full);
            var name = files.Path.GetFileName(full);
            // A drive or file-system root keeps its separator (Join-Path's "C:\orchestrator", not the drive-relative "C:orchestrator").
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name)) return files.Path.Combine(root, "orchestrator");
            return files.Path.Combine(parent, name + "-orchestrator");
        }

        /// <summary>Get-ForwardSlashKey: the full path without a trailing separator, with forward slashes (Claude's project key).</summary>
        public static string ForwardSlashKey(IFileSystem files, string directory) => files.Path.GetFullPath(directory).TrimEnd('\\', '/').Replace('\\', '/');

        // Set-ClaudeExternalIncludesApproved: the sibling's project key in ~/.claude.json gets both external-import flags; the
        // rest of the file is kept.
        private static void ApproveExternalIncludes(OrchestratorWorkspaceOptions options, IFileSystem files, string sibling)
        {
            if (string.IsNullOrWhiteSpace(options.UserProfile)) throw new Failure("No user profile folder to find .claude.json in.");
            var path = files.Path.Combine(options.UserProfile, ".claude.json");
            var key = ForwardSlashKey(files, sibling);
            JsonObject root = new JsonObject { ["projects"] = new JsonObject() };
            if (Exists(files, path))
            {
                try
                {
                    root = JsonNode.Parse(files.File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? throw new Failure("Could not parse " + path + ": not a JSON object");
                }
                catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException)
                {
                    throw new Failure("Could not parse " + path + ": " + e.Message);
                }
            }
            if (!root.TryGetPropertyValue("projects", out var projectsNode) || projectsNode == null)
            {
                projectsNode = new JsonObject();
                root["projects"] = projectsNode;
            }
            if (!(projectsNode is JsonObject projects)) throw new Failure("Could not parse " + path + ": projects is not an object");
            if (!projects.TryGetPropertyValue(key, out var entryNode) || entryNode == null)
            {
                entryNode = new JsonObject();
                projects[key] = entryNode;
            }
            if (!(entryNode is JsonObject entry)) throw new Failure("Could not parse " + path + ": projects[\"" + key + "\"] is not an object");
            entry["hasClaudeMdExternalIncludesApproved"] = true;
            entry["hasClaudeMdExternalIncludesWarningShown"] = true;
            Write(files, path, OperatorApi.Pretty(root));
        }

        /// <summary>
        /// The production junction (Windows): cmd.exe /c mklink /J &lt;link&gt; &lt;target&gt; on the host's launcher, its output captured;
        /// null when it exited 0, else its output (or the exit code).
        /// </summary>
        public static WorkspaceJunction Mklink(NightlyHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return (link, target) =>
            {
                var result = host.Launch(new NightlyLaunch("cmd.exe", new[] { "/c", "mklink", "/J", link, target }, "", TimeoutMilliseconds: 60000, CaptureOutput: true));
                if (result.ExitCode == 0) return null;
                var text = (result.LogText ?? "").Trim();
                return text.Length > 0 ? text : "mklink exited " + result.ExitCode.ToString(CultureInfo.InvariantCulture);
            };
        }

        // Resolve-Project: the one project whose id equals the needle (ignoring case) or whose name equals it exactly, else the one
        // whose name equals it ignoring case.
        private static JsonNode Resolve(OrchestratorWorkspaceOptions options, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var needle = options.Target;
            var all = BoardCli.Items(Send(options, http, headers, "GET", "/api/projects", null));
            var exact = all.Where(p => string.Equals(T(p, "id"), needle, StringComparison.OrdinalIgnoreCase) || string.Equals(T(p, "name"), needle, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) throw new Failure("'" + needle + "' has multiple exact project matches. Pass a project guid instead.");
            var hits = all.Where(p => T(p, "name").Length > 0 && string.Equals(T(p, "name"), needle, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) throw new Failure("'" + needle + "' matches " + hits.Count.ToString(CultureInfo.InvariantCulture) + " projects. Pass a project guid instead.");
            throw new Failure("No project matches '" + needle + "'.");
        }

        // Get-WorkspaceCheck: the readiness row of the orchestrator workspace; null when readiness has none.
        private static JsonNode WorkspaceCheck(OrchestratorWorkspaceOptions options, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers, string projectId) =>
            Row(Send(options, http, headers, "GET", "/api/projects/" + Uri.EscapeDataString(projectId) + "/readiness", null));

        private static JsonNode Row(JsonNode readiness) =>
            BoardCli.Items(BoardCli.Get(readiness, "checks")).FirstOrDefault(check => string.Equals(T(check, "key"), ReadinessKey, StringComparison.OrdinalIgnoreCase));

        private static string Checkout(IFileSystem files, JsonNode project)
        {
            var local = T(project, "localRepositoryPath");
            if (string.IsNullOrWhiteSpace(local)) throw new Failure("Project has no localRepositoryPath.");
            return files.Path.GetFullPath(local);
        }

        private static string Sibling(IFileSystem files, OrchestratorWorkspaceOptions options, string checkout) =>
            string.IsNullOrEmpty(options.Path) ? SiblingPath(files, checkout) : files.Path.GetFullPath(options.Path);

        private static JsonNode Send(OrchestratorWorkspaceOptions options, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers, string method, string path, string body)
        {
            if (!BoardCli.Send(http, options.Api, method, path, headers, body, out var answer, out var failure)) throw new Failure(failure);
            return answer;
        }

        private static JsonNode ReadJson(IFileSystem files, string path)
        {
            try
            {
                return JsonNode.Parse(files.File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e) when (e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                throw new Failure("Could not parse " + path + ": " + e.Message);
            }
        }

        // Set-Content -Encoding UTF8 (pwsh): UTF-8 without a byte-order mark, then a line end.
        private static void Write(IFileSystem files, string path, string text) => files.File.WriteAllText(path, text + "\n", new UTF8Encoding(false));

        private static string Relative(IFileSystem files, string from, string to) => files.Path.GetRelativePath(from, to).Replace('\\', '/');

        private static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        private static string T(JsonNode node, string name) => BoardCli.Text(BoardCli.Get(node, name));
    }
}
