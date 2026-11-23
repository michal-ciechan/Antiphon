// Inspect and select the shared, versioned pipeline definitions. It replaced scripts/pipeline-definition.ps1 (deleted
// 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts pipeline-definition <verb> [...]` (scripts/antiphon pipeline-definition on sh).
//
//   pipeline-definition list [--include-archived]
//   pipeline-definition get --id <guid>
//   pipeline-definition clone --id <guid> --name <n>
//   pipeline-definition revise --id <guid> --stages-file <json array> [--note <text>]
//   pipeline-definition set-board --board <guid> (--id <guid> | --inherit)
//   pipeline-definition set-project --project <guid> (--id <guid> | --inherit)
//
// The answer is printed as indented JSON (the script printed PowerShell's object view, or JSON with -Json; there is one
// form now). The stages file is read as UTF-8 and must hold a JSON array. The task token (ANTIPHON_TASK_TOKEN) is sent when
// set, never printed. Exit 0 done; 1 a missing or malformed stages file or a failed request (one stderr line); usage errors
// are NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The pipeline-definition command's options (pipeline-definition.ps1 &lt;verb&gt; -Id -Name -StagesFile -Note -Board -Project -Inherit -IncludeArchived).</summary>
    public sealed class PipelineDefinitionCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "pipeline-definition <list [--include-archived] | get --id <guid> | clone --id <guid> --name <n>"
            + " | revise --id <guid> --stages-file <json> [--note <text>] | set-board --board <guid> (--id <guid> | --inherit)"
            + " | set-project --project <guid> (--id <guid> | --inherit)>";

        private static readonly string[] Verbs = { "list", "get", "clone", "revise", "set-board", "set-project" };
        private static readonly string[] Valued = { "--id", "--name", "--stages-file", "--note", "--board", "--project" };
        private static readonly string[] Switches = { "--inherit", "--include-archived" };

        /// <summary>The verb.</summary>
        public string Verb { get; init; } = "list";

        /// <summary>The definition (get, clone, revise; set-board and set-project without --inherit).</summary>
        public string Id { get; init; } = "";

        /// <summary>clone: the new definition's name.</summary>
        public string Name { get; init; } = "";

        /// <summary>revise: the file holding the stages array.</summary>
        public string StagesFile { get; init; } = "";

        /// <summary>revise: the change note; null: none.</summary>
        public string Note { get; init; }

        /// <summary>set-board: the board.</summary>
        public string Board { get; init; } = "";

        /// <summary>set-project: the project.</summary>
        public string Project { get; init; } = "";

        /// <summary>set-board and set-project: clear the pointer so the scope inherits.</summary>
        public bool Inherit { get; init; }

        /// <summary>list: include archived definitions.</summary>
        public bool IncludeArchived { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>
        /// The verb and the flags; the API and the task token come from the CLI. A missing verb, a required flag left out
        /// ("--id is required for get"), --id together with --inherit, or a flag the verb does not take is a NightlyUsageException.
        /// </summary>
        public static PipelineDefinitionCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            var declared = new[] { "--id", "--name", "--stages-file", "--note", "--board", "--project", "--inherit", "--include-archived" };
            string[] required;
            switch (verb)
            {
                case "list":
                    BoardCli.Only(flags, verb, declared, new[] { "--include-archived" });
                    required = Array.Empty<string>();
                    break;
                case "get":
                    BoardCli.Only(flags, verb, declared, new[] { "--id" });
                    required = new[] { "--id" };
                    break;
                case "clone":
                    BoardCli.Only(flags, verb, declared, new[] { "--id", "--name" });
                    required = new[] { "--id", "--name" };
                    break;
                case "revise":
                    BoardCli.Only(flags, verb, declared, new[] { "--id", "--stages-file", "--note" });
                    required = new[] { "--id", "--stages-file" };
                    break;
                default:
                    var scope = verb == "set-board" ? "--board" : "--project";
                    BoardCli.Only(flags, verb, declared, new[] { scope, "--id", "--inherit" });
                    if (flags.Switch("--inherit") && flags.Has("--id")) throw new NightlyUsageException("give --id or --inherit, not both");
                    required = flags.Switch("--inherit") ? new[] { scope } : new[] { scope, "--id" };
                    break;
            }
            foreach (var flag in required)
                if (string.IsNullOrWhiteSpace(flags.Value(flag))) throw new NightlyUsageException(flag + " is required for " + verb);
            return new PipelineDefinitionCliOptions
            {
                Verb = verb,
                Id = flags.Value("--id", ""),
                Name = flags.Value("--name", ""),
                StagesFile = flags.Value("--stages-file", ""),
                Note = flags.Value("--note"),
                Board = flags.Value("--board", ""),
                Project = flags.Value("--project", ""),
                Inherit = flags.Switch("--inherit"),
                IncludeArchived = flags.Switch("--include-archived"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
            };
        }
    }

    /// <summary>The pipeline definition list, get, clone, revise and board/project selection (pipeline-definition.ps1).</summary>
    public static class PipelineDefinitionCli
    {
        /// <summary>Runs the verb: the answer on host.Out as indented JSON; exit 0, or 1 after one line on host.Error.</summary>
        public static int Run(PipelineDefinitionCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var definition = "/api/pipeline-definitions/" + Uri.EscapeDataString(options.Id);
            string method = "GET", path;
            JsonObject body = null;
            switch (options.Verb)
            {
                case "list":
                    path = "/api/pipeline-definitions" + (options.IncludeArchived ? "?includeArchived=true" : "");
                    break;
                case "get":
                    path = definition;
                    break;
                case "clone":
                    method = "POST";
                    path = definition + "/clone";
                    body = new JsonObject { ["name"] = options.Name };
                    break;
                case "revise":
                    if (!files.File.Exists(options.StagesFile)) return BoardCli.Refuse(host, "Stages file does not exist: " + options.StagesFile);
                    JsonNode stages;
                    try
                    {
                        stages = JsonNode.Parse(files.File.ReadAllText(options.StagesFile, Encoding.UTF8));
                    }
                    catch (JsonException e)
                    {
                        return BoardCli.Refuse(host, "Stages file is not valid JSON: " + e.Message);
                    }
                    if (!(stages is JsonArray)) return BoardCli.Refuse(host, "Stages file must contain a JSON array.");
                    method = "POST";
                    path = definition + "/revisions";
                    body = new JsonObject { ["stages"] = stages, ["changeNote"] = options.Note };
                    break;
                case "set-board":
                    method = "PUT";
                    path = "/api/boards/" + Uri.EscapeDataString(options.Board) + "/pipeline";
                    body = new JsonObject { ["pipelineDefinitionId"] = options.Inherit ? null : options.Id };
                    break;
                case "set-project":
                    method = "PUT";
                    path = "/api/projects/" + Uri.EscapeDataString(options.Project) + "/pipeline";
                    body = new JsonObject { ["pipelineDefinitionId"] = options.Inherit ? null : options.Id };
                    break;
                default:
                    throw new NightlyUsageException("unknown verb '" + options.Verb + "'");
            }
            if (!BoardCli.Send(http, options.Api, method, path, BoardCli.Headers(options.TaskToken), body == null ? null : OperatorApi.Json(body), out var answer, out var failure))
                return BoardCli.Refuse(host, failure);
            host.Out.WriteLine(OperatorApi.Pretty(answer));
            return 0;
        }
    }
}
