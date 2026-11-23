// Set up projects and inspect their readiness from a shell, without composing HTTP by hand. It replaced scripts/project.ps1
// (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts project <verb> [...]` (scripts/antiphon project on sh, scripts\antiphon.cmd on Windows).
//
//   project new --dir <path> [--create-directory] [--name n] [--git-url u] [--base-branch b] [--board-name n]
//               [--orchestrator | --worker | --no-agent] [--agent-name n] [--profile <displayName|guid>]
//               [--level Frontier|High|Medium|Low] [--reply-style Normal|Terse|Caveman|Brief|Phone|Explanatory]
//               [--bundles a,b] [--prompt-file p] [--remote-control] [--start] [--json]
//   project readiness <project name|guid> [--json]
//   project catalog [--json]
//   project set <project name|guid> [--commit-on-settle On|Off|Inherit] [--default-worker-workspace Shared|Worktree|Inherit] [--json]
//
// --prompt-file is read as UTF-8 and sent as is, so newlines, quotes and shell metacharacters survive. A project is found by
// its guid or its exact name, then by its name ignoring case; anything else names the candidates. set re-sends the
// project's own fields with the one change (the PUT replaces the project). The server can answer 200 with notes when --start
// was refused after setup committed: those notes are printed loudly on stderr ("WARNING: SETUP NOTES ...") and the exit is
// still 0, so the project and agent are not lost behind an apparently successful launch. The task token
// (ANTIPHON_TASK_TOKEN) is sent when set, never printed. Exit 0 done; 1 an unresolvable project or profile, a missing
// prompt file or a failed request (one stderr line); usage errors are NightlyUsageException (the CLI's exit 2). Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The project command's options (project.ps1 &lt;verb&gt; [&lt;project&gt;] and its flags).</summary>
    public sealed class ProjectCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "project <new --dir <path> [--create-directory] [--name n] [--git-url u] [--base-branch b] [--board-name n]"
            + " [--orchestrator | --worker | --no-agent] [--agent-name n] [--profile <name|guid>] [--level <level>] [--reply-style <style>] [--bundles a,b]"
            + " [--prompt-file p] [--remote-control] [--start] [--json] | readiness <project> [--json] | catalog [--json]"
            + " | set <project> [--commit-on-settle On|Off|Inherit] [--default-worker-workspace Shared|Worktree|Inherit] [--json]>";

        private static readonly string[] Verbs = { "new", "readiness", "catalog", "set" };
        private static readonly string[] Valued =
        {
            "--commit-on-settle", "--default-worker-workspace", "--dir", "--name", "--git-url", "--base-branch", "--board-name", "--agent-name", "--profile",
            "--level", "--reply-style", "--bundles", "--prompt-file",
        };
        private static readonly string[] Switches = { "--create-directory", "--orchestrator", "--worker", "--no-agent", "--remote-control", "--start", "--json" };
        private static readonly string[] NewFlags =
        {
            "--dir", "--name", "--git-url", "--base-branch", "--board-name", "--agent-name", "--profile", "--level", "--reply-style", "--bundles", "--prompt-file",
            "--create-directory", "--orchestrator", "--worker", "--no-agent", "--remote-control", "--start", "--json",
        };

        private static readonly string[] CommitPolicies = { "On", "Off", "Inherit" };
        private static readonly string[] Workspaces = { "Shared", "Worktree", "Inherit" };
        private static readonly string[] Levels = { "Frontier", "High", "Medium", "Low" };
        private static readonly string[] ReplyStyles = { "Normal", "Terse", "Caveman", "Brief", "Phone", "Explanatory" };

        /// <summary>new, readiness, catalog or set.</summary>
        public string Verb { get; init; } = "catalog";

        /// <summary>readiness and set: the project's name or guid.</summary>
        public string Project { get; init; } = "";

        /// <summary>set: the commit policy (On, Off, Inherit); empty: unchanged.</summary>
        public string CommitOnSettle { get; init; } = "";

        /// <summary>set: the default worker workspace (Shared, Worktree, Inherit); empty: unchanged.</summary>
        public string DefaultWorkerWorkspace { get; init; } = "";

        /// <summary>new: the project directory.</summary>
        public string Dir { get; init; } = "";

        /// <summary>new: create the directory when it is missing.</summary>
        public bool CreateDirectory { get; init; }

        /// <summary>new: the project name; empty: the server's choice.</summary>
        public string Name { get; init; } = "";

        /// <summary>new: the git URL; empty: none.</summary>
        public string GitUrl { get; init; } = "";

        /// <summary>new: the base branch; empty: the server's default.</summary>
        public string BaseBranch { get; init; } = "";

        /// <summary>new: the board name; empty: the server's default.</summary>
        public string BoardName { get; init; } = "";

        /// <summary>new: the agent preset: orchestrator, worker, none (--no-agent) or empty.</summary>
        public string Preset { get; init; } = "";

        /// <summary>new: the agent's name.</summary>
        public string AgentName { get; init; } = "";

        /// <summary>new: the agent's profile, by display name or guid.</summary>
        public string Profile { get; init; } = "";

        /// <summary>new: the agent's model level.</summary>
        public string Level { get; init; } = "";

        /// <summary>new: the agent's reply style.</summary>
        public string ReplyStyle { get; init; } = "";

        /// <summary>new: the agent's bundle keys.</summary>
        public IReadOnlyList<string> Bundles { get; init; } = Array.Empty<string>();

        /// <summary>new: the file whose text is appended to the agent's system prompt.</summary>
        public string PromptFile { get; init; } = "";

        /// <summary>new: enable remote control on the agent.</summary>
        public bool RemoteControl { get; init; }

        /// <summary>new: start the agent.</summary>
        public bool Start { get; init; }

        /// <summary>Print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>True when new sets any agent option.</summary>
        public bool AgentOptions => AgentName.Length > 0 || Profile.Length > 0 || Level.Length > 0 || ReplyStyle.Length > 0 || Bundles.Count > 0 || PromptFile.Length > 0 || RemoteControl;

        /// <summary>
        /// The verb, the project (readiness and set) and the flags; the API and the task token come from the CLI. A missing
        /// verb, a value outside a flag's choices, a project given to new or catalog or missing from readiness or set, new
        /// without --dir, two presets, --no-agent with agent options, set with neither setting, or a flag the verb does not
        /// take is a NightlyUsageException.
        /// </summary>
        public static ProjectCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var project = BoardCli.Positional(rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var declared = Valued.Concat(Switches).ToArray();
            switch (verb)
            {
                case "set":
                    BoardCli.Only(flags, verb, declared, new[] { "--commit-on-settle", "--default-worker-workspace", "--json" });
                    if (string.IsNullOrWhiteSpace(project)) throw new NightlyUsageException("which project? Pass its name or guid after set");
                    if (string.IsNullOrWhiteSpace(flags.Value("--commit-on-settle")) && string.IsNullOrWhiteSpace(flags.Value("--default-worker-workspace")))
                        throw new NightlyUsageException("set requires --commit-on-settle On|Off|Inherit or --default-worker-workspace Shared|Worktree|Inherit");
                    break;
                case "readiness":
                    BoardCli.Only(flags, verb, declared, new[] { "--json" });
                    if (string.IsNullOrWhiteSpace(project)) throw new NightlyUsageException("which project? Pass its name or guid after readiness");
                    break;
                case "catalog":
                    BoardCli.Only(flags, verb, declared, new[] { "--json" });
                    if (project != null) throw new NightlyUsageException("catalog takes no project argument");
                    break;
                default:
                    BoardCli.Only(flags, verb, declared, NewFlags);
                    if (project != null) throw new NightlyUsageException("new does not take a positional project argument; use --dir");
                    if (string.IsNullOrWhiteSpace(flags.Value("--dir"))) throw new NightlyUsageException("new requires --dir <path>");
                    break;
            }
            var presets = new[] { "--orchestrator", "--worker", "--no-agent" }.Where(flags.Switch).ToArray();
            if (presets.Length > 1) throw new NightlyUsageException("pass only one of --orchestrator, --worker, or --no-agent");
            var options = new ProjectCliOptions
            {
                Verb = verb,
                Project = project ?? "",
                CommitOnSettle = BoardCli.Choice("--commit-on-settle", flags.Value("--commit-on-settle"), CommitPolicies) ?? "",
                DefaultWorkerWorkspace = BoardCli.Choice("--default-worker-workspace", flags.Value("--default-worker-workspace"), Workspaces) ?? "",
                Dir = flags.Value("--dir", ""),
                CreateDirectory = flags.Switch("--create-directory"),
                Name = flags.Value("--name", ""),
                GitUrl = flags.Value("--git-url", ""),
                BaseBranch = flags.Value("--base-branch", ""),
                BoardName = flags.Value("--board-name", ""),
                Preset = presets.Length == 0 ? "" : presets[0] == "--no-agent" ? "none" : presets[0].Substring(2),
                AgentName = flags.Value("--agent-name", ""),
                Profile = flags.Value("--profile", ""),
                Level = BoardCli.Choice("--level", flags.Value("--level"), Levels) ?? "",
                ReplyStyle = BoardCli.Choice("--reply-style", flags.Value("--reply-style"), ReplyStyles) ?? "",
                Bundles = flags.List("--bundles"),
                PromptFile = flags.Value("--prompt-file", ""),
                RemoteControl = flags.Switch("--remote-control"),
                Start = flags.Switch("--start"),
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
            if (options.Preset == "none" && options.AgentOptions) throw new NightlyUsageException("--no-agent cannot be combined with agent options");
            return options;
        }
    }

    /// <summary>The project setup, readiness, catalog and settings (project.ps1).</summary>
    public static class ProjectCli
    {
        /// <summary>Runs the verb: the answer on host.Out (setup notes on host.Error); exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(ProjectCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = BoardCli.Headers(options.TaskToken);
            JsonNode answer;
            string failure;
            switch (options.Verb)
            {
                case "set":
                {
                    var project = Resolve(options, host, http, headers);
                    if (project == null) return 1;
                    var body = new JsonObject
                    {
                        ["name"] = Copy(project, "name"),
                        ["gitRepositoryUrl"] = Copy(project, "gitRepositoryUrl"),
                        ["constitutionPath"] = Copy(project, "constitutionPath"),
                        ["gitHubIntegrationEnabled"] = Copy(project, "gitHubIntegrationEnabled"),
                        ["notificationsEnabled"] = Copy(project, "notificationsEnabled"),
                        ["baseBranch"] = Copy(project, "baseBranch"),
                    };
                    if (options.CommitOnSettle.Length > 0) body["commitOnSettle"] = options.CommitOnSettle;
                    if (options.DefaultWorkerWorkspace.Length > 0) body["defaultWorkerWorkspace"] = options.DefaultWorkerWorkspace;
                    if (Copy(project, "localRepositoryPath") != null) body["localRepositoryPath"] = Copy(project, "localRepositoryPath");
                    if (Copy(project, "repositoryVisibility") != null) body["repositoryVisibility"] = Copy(project, "repositoryVisibility");
                    var route = "/api/projects/" + Uri.EscapeDataString(BoardCli.Text(BoardCli.Get(project, "id")));
                    if (!BoardCli.Send(http, options.Api, "PUT", route, headers, OperatorApi.Json(body), out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(answer));
                        return 0;
                    }
                    host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "Project: {0} ({1}) commitOnSettle={2} effectiveCommitOnSettle={3} defaultWorkerWorkspace={4} configuredWorkerWorkspace={5} dispatchHonorsWorkspaceDefault={6}",
                        T(answer, "name"), T(answer, "id"), T(answer, "commitOnSettle"), T(answer, "effectiveCommitOnSettle"), T(answer, "defaultWorkerWorkspace"),
                        T(answer, "effectiveWorkerWorkspace"), T(answer, "dispatchHonorsWorkspaceDefault")));
                    return 0;
                }
                case "catalog":
                {
                    if (!BoardCli.Send(http, options.Api, "GET", "/api/projects/setup-catalog", headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json) host.Out.WriteLine(OperatorApi.Pretty(answer));
                    else WriteCatalog(host, answer);
                    return 0;
                }
                case "readiness":
                {
                    var project = Resolve(options, host, http, headers);
                    if (project == null) return 1;
                    var route = "/api/projects/" + Uri.EscapeDataString(BoardCli.Text(BoardCli.Get(project, "id"))) + "/readiness";
                    if (!BoardCli.Send(http, options.Api, "GET", route, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    if (options.Json)
                    {
                        host.Out.WriteLine(OperatorApi.Pretty(answer));
                        return 0;
                    }
                    host.Out.WriteLine("Project: " + T(project, "name") + " (" + T(project, "id") + ")");
                    WriteReadiness(host, answer);
                    return 0;
                }
                default:
                    return New(options, host, files, http, headers);
            }
        }

        private static int New(ProjectCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            var body = new JsonObject { ["directory"] = options.Dir };
            if (options.CreateDirectory) body["createDirectory"] = true;
            if (options.Name.Length > 0) body["name"] = options.Name;
            if (options.GitUrl.Length > 0) body["gitRepositoryUrl"] = options.GitUrl;
            if (options.BaseBranch.Length > 0) body["baseBranch"] = options.BaseBranch;
            if (options.BoardName.Length > 0) body["boardName"] = options.BoardName;
            if (options.Start) body["startAgent"] = true;
            if (options.Preset == "none") body["agent"] = null;
            else if (options.Preset.Length > 0 || options.AgentOptions)
            {
                var agent = new JsonObject { ["preset"] = options.Preset.Length > 0 ? options.Preset : null };
                if (options.AgentName.Length > 0) agent["name"] = options.AgentName;
                if (options.Profile.Length > 0)
                {
                    if (!BoardCli.Send(http, options.Api, "GET", "/api/projects/setup-catalog", headers, null, out var catalog, out var catalogFailure))
                        return BoardCli.Refuse(host, catalogFailure);
                    var profile = ResolveProfile(options.Profile, catalog, out var refusal);
                    if (profile == null) return BoardCli.Refuse(host, refusal);
                    agent["tuiProfileId"] = profile;
                }
                if (options.Level.Length > 0) agent["modelLevel"] = options.Level;
                if (options.ReplyStyle.Length > 0) agent["replyStyle"] = options.ReplyStyle;
                if (options.Bundles.Count > 0) agent["bundleKeys"] = new JsonArray(options.Bundles.Select(key => (JsonNode)JsonValue.Create(key)).ToArray());
                if (options.PromptFile.Length > 0)
                {
                    if (!files.File.Exists(options.PromptFile)) return BoardCli.Refuse(host, "--prompt-file '" + options.PromptFile + "' does not exist.");
                    agent["systemPromptAppend"] = files.File.ReadAllText(options.PromptFile, Encoding.UTF8);
                }
                if (options.RemoteControl) agent["remoteControlEnabled"] = true;
                body["agent"] = agent;
            }
            if (!BoardCli.Send(http, options.Api, "POST", "/api/projects/setup", headers, OperatorApi.Json(body), out var result, out var failure)) return BoardCli.Refuse(host, failure);
            if (options.Json)
            {
                host.Out.WriteLine(OperatorApi.Pretty(result));
                return 0;
            }
            host.Out.WriteLine("Project: " + T(result, "project", "name") + " (" + T(result, "project", "id") + ")");
            host.Out.WriteLine("Board:   " + T(result, "board", "name") + " (" + T(result, "board", "id") + ")");
            host.Out.WriteLine(BoardCli.Truthy(BoardCli.Get(result, "agent")) ? "Agent:   " + T(result, "agent", "name") + " (" + T(result, "agent", "id") + ")" : "Agent:   none");
            WriteReadiness(host, BoardCli.Get(result, "readiness"));
            var notes = BoardCli.Items(BoardCli.Get(result, "notes"));
            if (notes.Count > 0)
            {
                host.Error.WriteLine("WARNING: SETUP NOTES - AGENT START MAY HAVE BEEN REFUSED:");
                foreach (var note in notes) host.Error.WriteLine("WARNING:   " + BoardCli.Text(note));
            }
            return 0;
        }

        // Resolve-Project: the one project whose id equals the needle (ignoring case) or whose name equals it exactly, else the
        // one whose name equals it ignoring case; null after one stderr line naming the candidates otherwise.
        private static JsonNode Resolve(ProjectCliOptions options, NightlyHost host, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            if (!BoardCli.Send(http, options.Api, "GET", "/api/projects", headers, null, out var answer, out var failure))
            {
                host.Error.WriteLine(failure);
                return null;
            }
            var needle = options.Project;
            var all = BoardCli.Items(answer);
            var exact = all.Where(p => string.Equals(T(p, "id"), needle, StringComparison.OrdinalIgnoreCase) || string.Equals(T(p, "name"), needle, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1)
            {
                host.Error.WriteLine("'" + needle + "' has multiple exact project matches. Pass a project guid instead.");
                return null;
            }
            var hits = all.Where(p => T(p, "name").Length > 0 && string.Equals(T(p, "name"), needle, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 1) return hits[0];
            var candidates = hits.Count > 0 ? hits : all;
            host.Error.WriteLine("Project '" + needle + "' is not uniquely resolvable. Candidates: " + string.Join(", ", candidates.Select(p => T(p, "name") + " (" + T(p, "id") + ")")));
            return null;
        }

        // Resolve-ProfileId: a guid matches a profile's id, anything else a display name ignoring case (trimmed); exactly one.
        private static string ResolveProfile(string profile, JsonNode catalog, out string refusal)
        {
            var profiles = BoardCli.Items(BoardCli.Get(catalog, "profiles"));
            List<JsonNode> hits;
            if (Guid.TryParse(profile, out var id))
                hits = profiles.Where(p => string.Equals(T(p, "id"), id.ToString("D"), StringComparison.OrdinalIgnoreCase)).ToList();
            else
            {
                var needle = profile.Trim();
                hits = profiles.Where(p => T(p, "displayName").Length > 0 && string.Equals(T(p, "displayName"), needle, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            refusal = null;
            if (hits.Count == 1) return T(hits[0], "id");
            refusal = hits.Count == 0
                ? "No enabled profile matches '" + profile + "'. Run: scripts/antiphon project catalog"
                : "'" + profile + "' matches " + hits.Count.ToString(CultureInfo.InvariantCulture) + " enabled profiles. Pass the profile guid instead.";
            return null;
        }

        /// <summary>Write-Readiness: the verdict, then a table with the missing required checks first.</summary>
        public static void WriteReadiness(NightlyHost host, JsonNode readiness)
        {
            host.Out.WriteLine(BoardCli.Truthy(BoardCli.Get(readiness, "canDispatch")) ? "Ready to dispatch" : "Cannot dispatch yet");
            var checks = BoardCli.Items(BoardCli.Get(readiness, "checks"));
            bool Blocking(JsonNode check) => T(check, "level") == "Required" && T(check, "status") == "Missing";
            var rows = checks.Where(Blocking).Concat(checks.Where(check => !Blocking(check)));
            host.Out.WriteLine(Row("key", "level", "status", "summary", "fix"));
            foreach (var check in rows)
            {
                var fixes = BoardCli.Truthy(BoardCli.Get(check, "fixes")) ? BoardCli.Items(BoardCli.Get(check, "fixes"))
                    : BoardCli.Truthy(BoardCli.Get(check, "fix")) ? BoardCli.Items(BoardCli.Get(check, "fix")) : Array.Empty<JsonNode>();
                var fix = string.Join("; ", fixes.Select(item => T(item, "label") + (BoardCli.Truthy(BoardCli.Get(item, "route")) ? " (" + T(item, "route") + ")" : "")));
                host.Out.WriteLine(Row(T(check, "key"), T(check, "level"), T(check, "status"), T(check, "summary"), fix));
            }
        }

        private static string Row(string key, string level, string status, string summary, string fix) =>
            string.Format(CultureInfo.InvariantCulture, "{0,-22} {1,-12} {2,-15} {3,-48} {4}", key, level, status, summary, fix);

        /// <summary>Write-Catalog: model levels, reply styles, bundles, presets, profiles and the delegation settings.</summary>
        public static void WriteCatalog(NightlyHost host, JsonNode catalog)
        {
            host.Out.WriteLine("Model levels:");
            foreach (var tier in BoardCli.Items(BoardCli.Get(catalog, "modelLevels")))
            {
                var aliases = BoardCli.Get(tier, "aliasesByKind") is JsonObject byKind ? string.Join(", ", byKind.Select(kind => kind.Key + "=" + BoardCli.Text(kind.Value))) : "";
                host.Out.WriteLine("  " + T(tier, "key") + " (" + aliases + "): " + T(tier, "blurb"));
            }
            host.Out.WriteLine("Reply styles:");
            foreach (var style in BoardCli.Items(BoardCli.Get(catalog, "replyStyles"))) host.Out.WriteLine("  " + T(style, "key") + ": " + T(style, "description"));
            host.Out.WriteLine("Bundles:");
            foreach (var bundle in BoardCli.Items(BoardCli.Get(catalog, "bundles"))) host.Out.WriteLine("  " + T(bundle, "key") + ": " + T(bundle, "summary"));
            host.Out.WriteLine("Presets:");
            foreach (var preset in BoardCli.Items(BoardCli.Get(catalog, "presets")))
                host.Out.WriteLine("  " + T(preset, "key") + ": " + T(preset, "description") + "; level=" + T(preset, "modelLevel") + "; style=" + T(preset, "replyStyle")
                    + "; alwaysOn=" + T(preset, "alwaysOn") + "; bundles=" + BoardCli.Join(BoardCli.Get(preset, "bundleKeys")) + "; name=" + T(preset, "namePattern")
                    + "; prompt=" + T(preset, "systemPromptTemplate"));
            host.Out.WriteLine("Profiles:");
            foreach (var profile in BoardCli.Items(BoardCli.Get(catalog, "profiles"))) host.Out.WriteLine("  " + T(profile, "displayName") + " (" + T(profile, "kind") + ") " + T(profile, "id"));
            var d = BoardCli.Get(catalog, "delegation");
            host.Out.WriteLine("Delegation:");
            host.Out.WriteLine("  allowedRoots=" + BoardCli.Join(BoardCli.Get(d, "allowedRoots")) + "; allowedRootsIsEmpty=" + T(d, "allowedRootsIsEmpty") + "; maxConcurrentTasks="
                + T(d, "maxConcurrentTasks") + "; maxCostUsdPerRoot=" + T(d, "maxCostUsdPerRoot") + "; maxDepth=" + T(d, "maxDepth") + "; defaultLevel=" + T(d, "defaultLevel"));
        }

        private static string T(JsonNode node, params string[] path) => BoardCli.Text(BoardCli.Get(node, path));

        private static JsonNode Copy(JsonNode node, string name) => BoardCli.Get(node, name)?.DeepClone();
    }
}
