// CARD-0217 S9: report (default) or archive the boards and projects whose names match test-residue shapes and that have no
// agent, live session or non-terminal task attached. It replaced scripts/prune-test-data.ps1, deleted 2026-11-24 (unit 35 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
//
//   antiphon-scripts prune-test-data [--execute] [--match <regex>]... [--reason <text>] [--json]
//
// (scripts/antiphon prune-test-data on sh, scripts\antiphon.cmd prune-test-data on Windows).
//
// The default is a dry run: four GETs (/api/projects?includeArchived=true, /api/boards?includeArchived=true, /api/agents,
// /api/agent-tasks, the last read as its { scope, items, excluded } envelope, CARD-0515) and one GET /api/cards/{id} per card
// of an open task; nothing is written. --execute is opt-in and archives, never deletes: POST /api/boards/{id}/archive for each
// qualifying board, then POST /api/projects/{id}/archive for each qualifying project, with { reason, archivedBy:
// "prune-test-data" }. A row qualifies only when its name matches a pattern (each --match replaces the defaults; .NET regular
// expressions matched ignoring case, as -match did) and it is not already archived, has no agent on it (a project: on any of
// its boards), no agent with a live session and no Queued, Dispatched, Working or Blocked task on a card of it. A row whose
// name matches nothing is never touched.
//
// Output (host.Out): the text report, or with --json one indented JSON object (Mode, Patterns, ProjectQualify, ProjectSkip,
// BoardQualify, BoardSkip, Projects, Boards, Mutations, Errors, TouchedNothing). Exit 0 done; 1 a read failed (one line on
// host.Error, nothing archived) or an archive failed (each one listed; the rest still run). An invalid --match is a usage
// error. X-Antiphon-Task-Token is sent from ANTIPHON_TASK_TOKEN when the CLI passes one in; it is never printed. Usage errors
// are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The prune-test-data command's options (prune-test-data.ps1 -Execute -Match -Reason -Json).</summary>
    public sealed class TestDataPruneOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "prune-test-data [--execute] [--match <regex>]... [--reason <text>] [--json]";

        /// <summary>The archive reason when --reason is not given.</summary>
        public const string DefaultReason = "Test residue (prune-test-data)";

        /// <summary>
        /// The default name shapes, checked against the live database on 2026-08-28. The plan listed card-task-*, card\d+-*,
        /// Catalog Test*, PwshCreateTest, TUI Probe and CARD-\d+ Repro *; live names needed four more: task-{8 hex} (the board
        /// of a card-task worktree), card-\d+-* (card-0142-verify, card-0164-*), Card0007 * (board display names) and
        /// CARD-\d+ Verify * (the sibling of Repro).
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultPatterns = Array.AsReadOnly(new[]
        {
            "^card-task-",
            "^task-[0-9a-fA-F]{8}$",
            @"^card\d+-",
            @"^card-\d+-",
            "^Card0007 ",
            "^Catalog Test",
            "^PwshCreateTest$",
            "^TUI Probe$",
            @"^CARD-\d+ Repro ",
            @"^CARD-\d+ Verify ",
        });

        /// <summary>Archive the qualifying rows; false reports only.</summary>
        public bool Execute { get; init; }

        /// <summary>The name patterns in order (the first that matches names the row).</summary>
        public IReadOnlyList<string> Patterns { get; init; } = DefaultPatterns;

        /// <summary>The archive reason.</summary>
        public string Reason { get; init; } = DefaultReason;

        /// <summary>One JSON object instead of the text report.</summary>
        public bool Json { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The flags after the command name; the API and the task token come from the CLI. --match may repeat (a pattern can hold a
        /// comma); any --match replaces the defaults. A blank or invalid pattern, or an empty --reason, is a NightlyUsageException.
        /// </summary>
        public static TestDataPruneOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var patterns = new List<string>();
            var rest = new List<string>();
            var given = arguments ?? Array.Empty<string>();
            for (var i = 0; i < given.Count; i++)
            {
                if (given[i] != "--match") { rest.Add(given[i]); continue; }
                if (i + 1 >= given.Count || given[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new NightlyUsageException("--match needs a value");
                var pattern = given[++i];
                if (string.IsNullOrWhiteSpace(pattern)) throw new NightlyUsageException("--match needs a non-empty pattern");
                try
                {
                    _ = new Regex(pattern, TestDataPrune.PatternOptions);
                }
                catch (ArgumentException e)
                {
                    throw new NightlyUsageException("--match '" + pattern + "' is not a valid regular expression: " + e.Message);
                }
                patterns.Add(pattern);
            }
            var flags = NightlyArgs.Parse(rest, new[] { "--reason" }, new[] { "--execute", "--json" });
            var reason = flags.Value("--reason", DefaultReason);
            if (string.IsNullOrWhiteSpace(reason)) throw new NightlyUsageException("--reason needs a non-empty value");
            return new TestDataPruneOptions
            {
                Execute = flags.Switch("--execute"),
                Patterns = patterns.Count > 0 ? patterns.AsReadOnly() : DefaultPatterns,
                Reason = reason,
                Json = flags.Switch("--json"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The test-residue report and archive (prune-test-data.ps1).</summary>
    public static class TestDataPrune
    {
        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

        /// <summary>How a name pattern is matched (-match: ignoring case).</summary>
        public const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>The task statuses that hold a board or project.</summary>
        public static readonly IReadOnlyList<string> OpenStatuses = Array.AsReadOnly(new[] { "Queued", "Dispatched", "Working", "Blocked" });

        private const string ArchivedBy = "prune-test-data";

        /// <summary>A request that failed: a read ends the run before anything is archived; an archive is listed and the rest still run.</summary>
        private sealed class RequestFailedException : Exception
        {
            public RequestFailedException(string message) : base(message) { }
        }

        /// <summary>One name-matched board or project and why it qualifies or is skipped.</summary>
        private sealed class Row
        {
            public string Kind;
            public string Id;
            public string Name;
            public string Pattern;
            public string Skip;
            public string Why;
            public int Agents;
            public bool Live;
            public bool OpenTask;
            public bool Archived;

            public bool Qualify => Skip == null;
        }

        /// <summary>Reads, classifies and (with --execute) archives; see the header for the output and the exit codes.</summary>
        public static int Run(TestDataPruneOptions options, NightlyHost host, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.TaskToken)) headers[OperatorApi.TaskTokenHeader] = options.TaskToken;
            var patterns = options.Patterns.Select(pattern => (Text: pattern, Regex: new Regex(pattern, PatternOptions))).ToList();

            List<JsonNode> projects, boards, agents, tasks;
            var cardBoards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var boardById = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
            var agentsByBoard = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var agentsByProject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var liveBoards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var liveProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var openBoards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var openProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                projects = List(http, options.Api, headers, "/api/projects?includeArchived=true");
                boards = List(http, options.Api, headers, "/api/boards?includeArchived=true");
                agents = List(http, options.Api, headers, "/api/agents");
                tasks = List(http, options.Api, headers, "/api/agent-tasks");

                foreach (var board in boards)
                {
                    var id = Id(board);
                    if (!string.IsNullOrEmpty(id)) boardById[id] = board;
                }
                string ProjectOf(string boardId) => boardById.TryGetValue(boardId, out var board) ? Text(Field(board, "projectId")) : null;

                foreach (var agent in agents)
                {
                    var boardId = Truthy(Field(agent, "boardId")) ? Text(Field(agent, "boardId")) : null;
                    var agentName = Name(agent);
                    if (boardId != null)
                    {
                        Add(agentsByBoard, boardId, agentName);
                        var projectId = ProjectOf(boardId);
                        if (projectId != null) Add(agentsByProject, projectId, agentName);
                    }
                    var live = Field(agent, "liveSession") != null;
                    if (live && boardId != null)
                    {
                        liveBoards.Add(boardId);
                        var projectId = ProjectOf(boardId);
                        if (projectId != null) liveProjects.Add(projectId);
                    }
                }

                foreach (var task in tasks)
                {
                    var status = Text(Field(task, "status"));
                    if (!OpenStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) continue;
                    var cardId = Truthy(Field(task, "cardId")) ? Text(Field(task, "cardId")) : null;
                    string boardId = null;
                    if (cardId != null)
                    {
                        if (!cardBoards.TryGetValue(cardId, out boardId))
                        {
                            var card = Get(http, options.Api, headers, "/api/cards/" + cardId);
                            boardId = Text(Field(card, "boardId"));
                            cardBoards[cardId] = boardId;
                        }
                    }
                    if (!string.IsNullOrEmpty(boardId))
                    {
                        openBoards.Add(boardId);
                        var projectId = ProjectOf(boardId);
                        if (projectId != null) openProjects.Add(projectId);
                    }
                }
            }
            catch (RequestFailedException e)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }

            Row NewRow(JsonNode item, string kind, string pattern)
            {
                var id = Id(item) ?? "";
                var archivedAt = Scalar(First(Field(item, "archivedAt")));
                var already = !string.IsNullOrEmpty(archivedAt);
                var byKind = kind == "project" ? agentsByProject : agentsByBoard;
                var agentNames = byKind.TryGetValue(id, out var names) ? names : new List<string>();
                var live = kind == "project" ? liveProjects.Contains(id) : liveBoards.Contains(id);
                var open = kind == "project" ? openProjects.Contains(id) : openBoards.Contains(id);
                string skip = null;
                if (already) skip = "already archived";
                else if (agentNames.Count > 0) skip = "agent attached (" + string.Join(", ", agentNames) + ")";
                else if (live) skip = "live session attached";
                else if (open) skip = "non-terminal task attached";
                return new Row
                {
                    Kind = kind,
                    Id = id,
                    Name = Name(item),
                    Pattern = pattern,
                    Skip = skip,
                    Why = skip == null ? "matched " + pattern + "; agents=0 liveSessions=0 openTasks=0" : "matched " + pattern + "; skip=" + skip,
                    Agents = agentNames.Count,
                    Live = live,
                    OpenTask = open,
                    Archived = already,
                };
            }

            string Matching(string name) => patterns.Where(pattern => pattern.Regex.IsMatch(name)).Select(pattern => pattern.Text).FirstOrDefault();
            var projectRows = projects.Select(item => (Item: item, Pattern: Matching(Name(item)))).Where(row => row.Pattern != null)
                .Select(row => NewRow(row.Item, "project", row.Pattern)).ToList();
            var boardRows = boards.Select(item => (Item: item, Pattern: Matching(Name(item)))).Where(row => row.Pattern != null)
                .Select(row => NewRow(row.Item, "board", row.Pattern)).ToList();
            var qualifyProjects = projectRows.Where(row => row.Qualify).ToList();
            var skipProjects = projectRows.Where(row => !row.Qualify).ToList();
            var qualifyBoards = boardRows.Where(row => row.Qualify).ToList();
            var skipBoards = boardRows.Where(row => !row.Qualify).ToList();

            var mutations = new JsonArray();
            var errors = new List<(string Kind, string Id, string Name, string Error)>();
            if (options.Execute)
            {
                var body = OperatorApi.Json(new JsonObject { ["reason"] = options.Reason, ["archivedBy"] = ArchivedBy });
                foreach (var row in qualifyBoards.Concat(qualifyProjects))
                {
                    try
                    {
                        var updated = Send(http, "POST", options.Api, headers, "/api/" + (row.Kind == "board" ? "boards" : "projects") + "/" + row.Id + "/archive", body);
                        mutations.Add(new JsonObject { ["Kind"] = row.Kind, ["Id"] = row.Id, ["Name"] = row.Name, ["ArchivedAt"] = Field(updated, "archivedAt")?.DeepClone() });
                    }
                    catch (RequestFailedException e)
                    {
                        errors.Add((row.Kind, row.Id, row.Name, e.Message));
                    }
                }
            }

            if (options.Json)
            {
                JsonArray Rows(IEnumerable<Row> rows) => new JsonArray(rows.Select(row => (JsonNode)new JsonObject
                {
                    ["Kind"] = row.Kind, ["Id"] = row.Id, ["Name"] = row.Name, ["Pattern"] = row.Pattern, ["Qualify"] = row.Qualify,
                    ["Skip"] = row.Skip, ["Why"] = row.Why, ["Agents"] = row.Agents, ["Live"] = row.Live, ["OpenTask"] = row.OpenTask,
                    ["Archived"] = row.Archived,
                }).ToArray());
                var result = new JsonObject
                {
                    ["Mode"] = options.Execute ? "execute" : "dry-run",
                    ["Patterns"] = new JsonArray(options.Patterns.Select(pattern => (JsonNode)JsonValue.Create(pattern)).ToArray()),
                    ["ProjectQualify"] = qualifyProjects.Count,
                    ["ProjectSkip"] = skipProjects.Count,
                    ["BoardQualify"] = qualifyBoards.Count,
                    ["BoardSkip"] = skipBoards.Count,
                    ["Projects"] = Rows(projectRows),
                    ["Boards"] = Rows(boardRows),
                    ["Mutations"] = mutations,
                    ["Errors"] = new JsonArray(errors.Select(error => (JsonNode)new JsonObject
                    {
                        ["Kind"] = error.Kind, ["Id"] = error.Id, ["Name"] = error.Name, ["Error"] = error.Error,
                    }).ToArray()),
                    ["TouchedNothing"] = !options.Execute,
                };
                host.Out.WriteLine(OperatorApi.Pretty(result));
            }
            else
            {
                var output = host.Out;
                output.WriteLine(options.Execute ? "EXECUTE - archiving qualifying rows (never deleting)" : "DRY RUN - listing what would be archived; nothing was written");
                output.WriteLine("API " + options.Api);
                output.WriteLine("Patterns: " + string.Join(", ", options.Patterns));
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Projects: {0} qualify, {1} skipped of {2} name-matched ({3} total)",
                    qualifyProjects.Count, skipProjects.Count, projectRows.Count, projects.Count));
                output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Boards:   {0} qualify, {1} skipped of {2} name-matched ({3} total)",
                    qualifyBoards.Count, skipBoards.Count, boardRows.Count, boards.Count));
                void Section(string title, List<Row> rows)
                {
                    output.WriteLine("");
                    output.WriteLine(title);
                    if (rows.Count == 0) output.WriteLine("  none");
                    foreach (var row in rows) output.WriteLine("  " + row.Kind + "  " + row.Name + "  " + row.Id + "  " + row.Why);
                }
                Section("PROJECTS TO ARCHIVE", qualifyProjects);
                Section("PROJECTS SKIPPED", skipProjects);
                Section("BOARDS TO ARCHIVE", qualifyBoards);
                Section("BOARDS SKIPPED", skipBoards);
                if (options.Execute)
                {
                    output.WriteLine("");
                    output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Archived: {0}; errors: {1}", mutations.Count, errors.Count));
                    foreach (var error in errors) output.WriteLine("  FAIL " + error.Kind + " " + error.Name + "  " + error.Error);
                }
            }
            return errors.Count > 0 ? 1 : 0;
        }

        // Get-AntiphonList: an { items } envelope is unwrapped; an array is its elements; one object is one row; nothing is none.
        private static List<JsonNode> List(NightlyReportHttp http, string api, IReadOnlyDictionary<string, string> headers, string path)
        {
            var raw = Get(http, api, headers, path);
            if (raw is JsonObject envelope && envelope.TryGetPropertyValue("items", out var items)) raw = items;
            if (raw == null) return new List<JsonNode>();
            if (raw is JsonArray array) return array.Where(item => item != null).ToList();
            return new List<JsonNode> { raw };
        }

        private static JsonNode Get(NightlyReportHttp http, string api, IReadOnlyDictionary<string, string> headers, string path) =>
            Send(http, "GET", api, headers, path, null);

        // Invoke-Antiphon: a transport failure, a non-2xx answer or a body that is not JSON is "Antiphon <METHOD> <path> failed: <detail>".
        private static JsonNode Send(NightlyReportHttp http, string method, string api, IReadOnlyDictionary<string, string> headers, string path, string body)
        {
            NightlyReportHttpResponse answer;
            try
            {
                answer = http(method, api + path, headers, body);
            }
            catch (Exception e)
            {
                throw new RequestFailedException("Antiphon " + method + " " + path + " failed: " + e.Message);
            }
            if (!OperatorApi.Succeeded(answer)) throw new RequestFailedException("Antiphon " + method + " " + path + " failed: " + OperatorApi.Failure(answer));
            if (string.IsNullOrWhiteSpace(answer.Body)) return null;
            try
            {
                return JsonNode.Parse(answer.Body);
            }
            catch (JsonException e)
            {
                throw new RequestFailedException("Antiphon " + method + " " + path + " failed: the answer is not JSON: " + e.Message);
            }
        }

        private static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
            list.Add(value);
        }

        private static JsonNode Field(JsonNode item, string name) =>
            item is JsonObject value && value.TryGetPropertyValue(name, out var node) ? node : null;

        // Get-Scalar: an array stands for its first element.
        private static JsonNode First(JsonNode node) => node is JsonArray array ? (array.Count == 0 ? null : array[0]) : node;

        private static string Scalar(JsonNode node)
        {
            if (node is not JsonValue value) return null;
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number: return value.GetValue<JsonElement>().GetRawText();
                default: return null;
            }
        }

        private static string Text(JsonNode node) => Scalar(node);

        // PowerShell truthiness: a non-empty string, true or a non-zero number.
        private static bool Truthy(JsonNode node)
        {
            if (node is not JsonValue value) return node != null;
            switch (value.GetValueKind())
            {
                case JsonValueKind.String: return value.GetValue<string>().Length > 0;
                case JsonValueKind.True: return true;
                case JsonValueKind.Number: return value.GetValue<JsonElement>().GetDouble() != 0;
                default: return false;
            }
        }

        private static string Id(JsonNode item) => Scalar(First(Field(item, "id")));

        private static string Name(JsonNode item) => Scalar(First(Field(item, "name"))) ?? "";
    }
}
