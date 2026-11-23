// CARD-0051: read and correct board cards from a shell, without composing HTTP by hand. It replaced scripts/card.ps1
// (deleted 2026-11-23, unit 27 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts card <verb> [...]` (scripts/antiphon card on sh, scripts\antiphon.cmd card on Windows). This header is
// the reference the board-api bundle points agents at.
//
// A card is addressed the way it is NAMED: CARD-0051, card-51, '#51', 51, or its guid. Every verb takes any of those
// (CARD-0051 slice 1); there is no id to look up first.
//
// When two boards name a card alike (CARD-0011 on Antiphon and Gym Stat), the checkout and the delegation token
// (X-Antiphon-Task-Token) decide which one answers; --board <name|guid> overrides. The checkout, sent as cwd, is git's top
// level of the working directory, else the checkout an antiphon.workspace.json marker there names (CARD-0251), else the
// working directory. A collision that survives all of that is a 409 that lists every candidate (board, guid, status,
// title). --board on a card that board does not hold is a 404 naming where it does live.
//
// ALL LONG TEXT COMES FROM A FILE. --description-file and --reason-file are read as UTF-8 and sent as is, so backticks,
// $(...), quotes, newlines and everything else survive untouched. --description / --reason exist for short one-liners;
// anything with shell metacharacters in it goes in a file (cmd.exe and both shells mangle it silently).
//
// CONCURRENCY TOKEN: every card write carries a concurrency token, and the server rotates it on every write. By default the
// card is read immediately before each write and that token is sent, so the window in which someone else's write could be
// clobbered is milliseconds, not the minutes of a human round trip. It is not zero: two genuinely concurrent writers still
// collide at the database (unique CardId, RevisionNumber) and one gets a 409, and every content write is revision-logged
// (CARD-0019), so a clobber is readable and reversible from the card's history. For true compare-and-swap pass --token
// <guid>: it is sent verbatim, and a stale one is the server's 409.
//
// Verbs (scripts/antiphon card <verb> ...; every verb also takes --board to scope the identifier):
//   list      (--board <name|guid> | --status <status> | --updated-since <UTC>) [--all] [--limit <page size>] [--json]
//   search    '<phrase>' [--board <name|guid>] [--status <status>] [--all] [--limit <page size>] [--json]
//   get       CARD-0051 [--json]
//   history   CARD-0051 [--json]
//   block     CARD-0051 --blocked-by CARD-0049 [--related-board <name|guid>] [--reason r | --reason-file p] [--by name]
//             [--token g] [--json]
//   unblock   CARD-0051 --blocked-by CARD-0049 [--reason r | --reason-file p] [--by name] [--token g] [--json]
//   new       --board <name|guid> --title <t> [--description-file p | --description s] [--alias a]
//             [--importance Low|Normal|High|Critical] [--urgency Normal|Soon|Now] [--due-at iso] [--labels a,b]
//             [--platform Any|Windows|Linux] [--card-file-visibility Inherit|Private|Public] [--private-notes-file p] [--json]
//   edit      CARD-0051 --reason <r> | --reason-file <p> [--title t] [--description-file p | --description s]
//             [--alias a | --clear-alias] [--importance Low|Normal|High|Critical] [--importance-provenance Auto|Human]
//             [--urgency Normal|Soon|Now] [--due-at iso | --clear-due-at] [--labels a,b]
//             [--code-verification-policy FullOnly|AllowInterim] [--review-verification-policy FullOnly|AllowInterim]
//             [--platform Any|Windows|Linux] [--card-file-visibility Inherit|Private|Public]
//             [--private-notes-file p | --clear-private-notes] [--by name] [--token g] [--json]
//   move      CARD-0051 --to <column name|state key|status|guid> [--reason r | --reason-file p] [--spawn] [--token g]
//   close     CARD-0051 --reason r | --reason-file p [--token g]
//   reopen    CARD-0051 --reason r | --reason-file p [--to column] [--by name] [--token g]
//   archive   CARD-0051 --reason r | --reason-file p [--by name] [--token g]
//   unarchive CARD-0051 --reason r | --reason-file p [--by name] [--token g]
//   diagnose  CARD-0051 [--no-wait] [--json]
//   reorder   CARD-0051 (--before CARD-nnnn | --after CARD-nnnn | --top | --bottom) [--reason r | --reason-file p]
//             [--by name] [--token g]
//   order     --board <b> --order-file <p> --reason r | --reason-file p [--by name] [--override-human-ratings]
//   limits
//
// list/search follow every continuation and buffer the results until complete. --limit is a page-size hint capped by the
// server, not a total result cap; --all includes archived cards and does not control paging. Search matches stored full
// text; returned text is still previewed. For duplicate checks, search '<distinctive phrase>' --board <name> --all, then
// get each candidate. A capped list, preview, failed page or changed-enumeration 409 cannot prove absence.
//
// A move into an ACTIVE column does NOT start an agent unless you pass --spawn (CARD-0051 slice 3). The tick will not pick
// that card up either (CARD-0087); --spawn or POST /spawn starts it, and the command says so when it would have. A reopen
// never starts an agent, even into an active column. close moves into the board's first terminal column.
// new/edit: --card-file-visibility Inherit|Private|Public and --private-notes-file <UTF-8 file>; edit --clear-private-notes
// clears the notes (not with --private-notes-file). Notes are never printed. Public title, description, outcome and archive
// fields may publish on sync; saving a card never syncs, and new/edit report eligibility and cleanup from the returned status.
// edit --code-verification-policy / --review-verification-policy FullOnly|AllowInterim (CARD-0544): each role is independent
// and permits only an explicitly REQUESTED Interim round; omitted keeps the stored value. Interim stays unavailable while
// the deployment readiness gate is disabled.
// --platform (CARD-0710): the default task platform for new tasks bound to the card; omitted leaves it, Any resets it.
// --alias (CARD-0350): trimmed, single line, at most the server's word count; never generated. edit --clear-alias (or
// --alias '') clears it.
// --priority is gone: it refuses and names --importance, --urgency and --due-at / --clear-due-at.
//
// Every request sends the task token (ANTIPHON_TASK_TOKEN) when set, never printed, and the registered caller label
// card.ps1 (AntiphonCaller; the server meters card GET polls by it, AgentTaskGetPollMetrics, so the script's label stays).
// The CLI passes ANTIPHON_API (default http://localhost:17202), the task token and its working directory in. Exit 0 done;
// 1 a refused value, a missing file or a failed request (the server's detail on stderr); usage errors are
// NightlyUsageException (the CLI's exit 2). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The card command's options (card.ps1 &lt;verb&gt; [&lt;card&gt;] and its parameters, in kebab case).</summary>
    public sealed class CardCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "card <list|search|get|history|block|unblock|new|edit|move|close|reopen|archive|unarchive|diagnose|reorder|order|limits>"
            + " [<card> | '<phrase>'] [--board <name|guid>] [flags: scripts/lib/Antiphon.Nightly/CardCli.cs lists them per verb]";

        /// <summary>The verbs, in their listed spelling.</summary>
        public static readonly IReadOnlyList<string> Verbs = new[]
        {
            "list", "search", "get", "history", "block", "unblock", "new", "edit", "move", "close", "reopen", "archive", "unarchive", "diagnose", "reorder",
            "order", "limits",
        };

        /// <summary>The card statuses --status takes.</summary>
        public static readonly IReadOnlyList<string> Statuses = new[] { "Backlog", "InProgress", "Review", "Done", "NeedsDecision", "Canceled" };

        /// <summary>The importances --importance takes.</summary>
        public static readonly IReadOnlyList<string> Importances = new[] { "Low", "Normal", "High", "Critical" };

        /// <summary>The urgencies --urgency takes.</summary>
        public static readonly IReadOnlyList<string> Urgencies = new[] { "Normal", "Soon", "Now" };

        /// <summary>The provenances --importance-provenance takes.</summary>
        public static readonly IReadOnlyList<string> Provenances = new[] { "Auto", "Human" };

        /// <summary>The policies --code-verification-policy and --review-verification-policy take.</summary>
        public static readonly IReadOnlyList<string> VerificationPolicies = new[] { "FullOnly", "AllowInterim" };

        /// <summary>The platforms --platform takes.</summary>
        public static readonly IReadOnlyList<string> Platforms = new[] { "Any", "Windows", "Linux" };

        /// <summary>The visibilities --card-file-visibility takes.</summary>
        public static readonly IReadOnlyList<string> Visibilities = new[] { "Inherit", "Private", "Public" };

        private static readonly string[] Valued =
        {
            "--board", "--blocked-by", "--related-board", "--status", "--updated-since", "--limit", "--title", "--alias", "--description", "--description-file",
            "--card-file-visibility", "--private-notes-file", "--reason", "--reason-file", "--to", "--priority", "--importance", "--importance-provenance",
            "--code-verification-policy", "--review-verification-policy", "--platform", "--urgency", "--due-at", "--labels", "--by", "--token", "--before",
            "--after", "--order-file",
        };

        private static readonly string[] Switches =
        {
            "--all", "--clear-alias", "--clear-private-notes", "--clear-due-at", "--spawn", "--json", "--no-wait", "--top", "--bottom", "--override-human-ratings",
        };

        private static readonly string[] CollectionFlags = { "--status", "--updated-since", "--all", "--limit" };
        private static readonly string[] NoteFlags = { "--private-notes-file", "--clear-private-notes", "--card-file-visibility" };
        private static readonly string[] Reasons = { "--reason", "--reason-file" };
        private static readonly string[] Writer = { "--by", "--token" };

        // The flags each verb reads (--board is every verb's but limits').
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["list"] = new[] { "--board", "--status", "--updated-since", "--all", "--limit", "--json" },
            ["search"] = new[] { "--board", "--status", "--all", "--limit", "--json" },
            ["get"] = new[] { "--board", "--json" },
            ["history"] = new[] { "--board", "--json" },
            ["block"] = new[] { "--board", "--blocked-by", "--related-board", "--json" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["unblock"] = new[] { "--board", "--blocked-by", "--json" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["new"] = new[]
            {
                "--board", "--title", "--alias", "--description", "--description-file", "--importance", "--urgency", "--due-at", "--labels", "--platform",
                "--card-file-visibility", "--private-notes-file", "--json",
            },
            ["edit"] = new[]
            {
                "--board", "--title", "--alias", "--clear-alias", "--description", "--description-file", "--importance", "--importance-provenance", "--urgency",
                "--due-at", "--clear-due-at", "--labels", "--code-verification-policy", "--review-verification-policy", "--platform", "--card-file-visibility",
                "--private-notes-file", "--clear-private-notes", "--json",
            }.Concat(Reasons).Concat(Writer).ToArray(),
            ["move"] = new[] { "--board", "--to", "--spawn", "--token" }.Concat(Reasons).ToArray(),
            ["close"] = new[] { "--board", "--token" }.Concat(Reasons).ToArray(),
            ["reopen"] = new[] { "--board", "--to" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["archive"] = new[] { "--board" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["unarchive"] = new[] { "--board" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["diagnose"] = new[] { "--board", "--no-wait", "--json" },
            ["reorder"] = new[] { "--board", "--before", "--after", "--top", "--bottom" }.Concat(Reasons).Concat(Writer).ToArray(),
            ["order"] = new[] { "--board", "--order-file", "--by", "--override-human-ratings" }.Concat(Reasons).ToArray(),
            ["limits"] = Array.Empty<string>(),
        };

        // The verbs that take a card after the verb, and those whose reason is required.
        private static readonly string[] CardVerbs = { "get", "history", "block", "unblock", "edit", "move", "close", "reopen", "archive", "unarchive", "diagnose", "reorder" };
        private static readonly string[] ReasonVerbs = { "edit", "close", "reopen", "archive", "unarchive", "order" };

        /// <summary>The verb, in its listed spelling.</summary>
        public string Verb { get; init; } = "get";

        /// <summary>The card as written (CARD-0051, card-51, #51, 51 or a guid); for search, the phrase.</summary>
        public string Card { get; init; } = "";

        /// <summary>--board: the board's name or guid; empty: none.</summary>
        public string Board { get; init; } = "";

        /// <summary>block/unblock: the blocker.</summary>
        public string BlockedBy { get; init; } = "";

        /// <summary>block: the blocker's board, when its identifier exists on several boards.</summary>
        public string RelatedBoard { get; init; } = "";

        /// <summary>list/search: the status, in its listed spelling; empty: any.</summary>
        public string Status { get; init; } = "";

        /// <summary>list: the lower bound on the update time, as given; empty: none.</summary>
        public string UpdatedSince { get; init; } = "";

        /// <summary>list/search: include archived cards.</summary>
        public bool All { get; init; }

        /// <summary>list/search: the page-size hint; null: the server's.</summary>
        public int? Limit { get; init; }

        /// <summary>new/edit: the title; empty: none (edit: unchanged).</summary>
        public string Title { get; init; } = "";

        /// <summary>new/edit: the alias as given (empty clears it on edit); null when --alias was not given.</summary>
        public string Alias { get; init; }

        /// <summary>new/edit: the inline description.</summary>
        public string Description { get; init; } = "";

        /// <summary>new/edit: the file the description is read from (UTF-8, sent as is).</summary>
        public string DescriptionFile { get; init; } = "";

        /// <summary>new/edit: the card-file visibility, in its listed spelling; null: not sent.</summary>
        public string CardFileVisibility { get; init; }

        /// <summary>new/edit: the private notes file (UTF-8, never printed); null when --private-notes-file was not given.</summary>
        public string PrivateNotesFile { get; init; }

        /// <summary>edit: clear the private notes.</summary>
        public bool ClearPrivateNotes { get; init; }

        /// <summary>The inline reason.</summary>
        public string Reason { get; init; } = "";

        /// <summary>The file the reason is read from (UTF-8, sent as is).</summary>
        public string ReasonFile { get; init; } = "";

        /// <summary>move/reopen: the target column's name, state key, card status (move only) or guid.</summary>
        public string To { get; init; } = "";

        /// <summary>new/edit: the importance; null: not sent.</summary>
        public string Importance { get; init; }

        /// <summary>edit: the importance provenance; null: not sent.</summary>
        public string ImportanceProvenance { get; init; }

        /// <summary>edit: the Code role's verification policy; null: unchanged.</summary>
        public string CodeVerificationPolicy { get; init; }

        /// <summary>edit: the Review role's verification policy; null: unchanged.</summary>
        public string ReviewVerificationPolicy { get; init; }

        /// <summary>new/edit: the default task platform; null: unchanged.</summary>
        public string Platform { get; init; }

        /// <summary>new/edit: the urgency; null: not sent.</summary>
        public string Urgency { get; init; }

        /// <summary>new/edit: the due time, as given; empty: none.</summary>
        public string DueAt { get; init; } = "";

        /// <summary>edit: clear the due time (wins over --due-at).</summary>
        public bool ClearDueAt { get; init; }

        /// <summary>new/edit: the labels (comma separated on the command line); empty: unchanged.</summary>
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

        /// <summary>The self-reported author of the revision (free text); empty: none.</summary>
        public string By { get; init; } = "";

        /// <summary>The concurrency token sent verbatim (strict compare-and-swap); empty: the token read just before the write.</summary>
        public string Token { get; init; } = "";

        /// <summary>move: let a move into an active column start an agent session.</summary>
        public bool Spawn { get; init; }

        /// <summary>Print the answer as JSON.</summary>
        public bool Json { get; init; }

        /// <summary>diagnose: return after the 202 instead of polling GET /api/diagnoses.</summary>
        public bool NoWait { get; init; }

        /// <summary>reorder: the neighbour this card goes before.</summary>
        public string Before { get; init; } = "";

        /// <summary>reorder: the neighbour this card goes after.</summary>
        public string After { get; init; } = "";

        /// <summary>reorder: the top of the card's rank cell.</summary>
        public bool Top { get; init; }

        /// <summary>reorder: the bottom of the card's rank cell.</summary>
        public bool Bottom { get; init; }

        /// <summary>order: the file of card refs, one per line, optionally followed by an importance and an urgency.</summary>
        public string OrderFile { get; init; } = "";

        /// <summary>order: re-rate human-rated cards too.</summary>
        public bool OverrideHumanRatings { get; init; }

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The task token (ANTIPHON_TASK_TOKEN); empty: none.</summary>
        public string TaskToken { get; init; } = "";

        /// <summary>The directory the checkout is resolved from and relative files are read against (the CLI's working directory).</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>True for list and search.</summary>
        public bool Collection => Verb == "list" || Verb == "search";

        /// <summary>
        /// The verb, the card (or the search phrase) and the flags; the API, the task token and the working directory come from
        /// the CLI. A malformed or inapplicable flag, a missing card, phrase, --blocked-by, --board or --title, a missing
        /// required reason, two of a text and its file, or a contradictory placement is a NightlyUsageException; the server and
        /// the files are never touched here.
        /// </summary>
        public static CardCliOptions Parse(IReadOnlyList<string> arguments, string api, string taskToken, string workingDirectory)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var card = BoardCli.Positional(rest);
            var flags = NightlyArgs.Parse(rest, Valued, Switches);
            string Value(string flag) => flags.Value(flag, "");
            bool Blank(string flag) => string.IsNullOrWhiteSpace(flags.Value(flag, ""));

            if (flags.Has("--priority"))
                throw new NightlyUsageException("--priority is gone. Use --importance (Low|Normal|High|Critical), --urgency (Normal|Soon|Now), and --due-at / --clear-due-at.");
            var collection = verb == "list" || verb == "search";
            if (!collection && CollectionFlags.Any(flags.Has)) throw new NightlyUsageException("--status, --updated-since, --all and --limit apply only to list/search.");
            if (verb != "new" && verb != "edit" && NoteFlags.Any(flags.Has))
                throw new NightlyUsageException("Private-note and card-file visibility options apply only to new/edit.");
            if (flags.Switch("--clear-private-notes") && (verb != "edit" || flags.Has("--private-notes-file")))
                throw new NightlyUsageException("--clear-private-notes applies only to edit and cannot be combined with --private-notes-file.");
            if (verb == "unblock" && flags.Has("--related-board")) throw new NightlyUsageException("--related-board applies to block.");
            if (verb == "search" && flags.Has("--updated-since")) throw new NightlyUsageException("--updated-since applies only to list.");
            BoardCli.Only(flags, verb, Valued.Concat(Switches), Allowed[verb]);

            int? limit = null;
            if (flags.Has("--limit"))
            {
                limit = flags.Int("--limit", 0);
                if (limit <= 0) throw new NightlyUsageException("--limit must be positive.");
            }
            switch (verb)
            {
                case "list":
                    if (!string.IsNullOrWhiteSpace(card)) throw new NightlyUsageException("list takes no card argument. Use --board, --status or --updated-since.");
                    if (Blank("--board") && Blank("--status") && Blank("--updated-since"))
                        throw new NightlyUsageException("list requires --board, --status or --updated-since; --all is not a scope.");
                    if (flags.Has("--updated-since")
                        && (Blank("--updated-since") || !DateTimeOffset.TryParse(Value("--updated-since"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                        throw new NightlyUsageException("--updated-since must be a valid timestamp.");
                    break;
                case "search":
                    if (string.IsNullOrWhiteSpace(card)) throw new NightlyUsageException("search requires a nonblank phrase after the verb.");
                    break;
                case "new":
                case "order":
                case "limits":
                    if (card != null) throw new NightlyUsageException(verb + " takes no card argument.");
                    break;
                default:
                    if (CardVerbs.Contains(verb) && string.IsNullOrWhiteSpace(card))
                        throw new NightlyUsageException("which card? Pass it after the verb: scripts/antiphon card " + verb + " CARD-0051 ...");
                    break;
            }
            if ((verb == "block" || verb == "unblock") && Blank("--blocked-by")) throw new NightlyUsageException("Pass --blocked-by <card> for '" + verb + "'.");
            if (verb == "new" && Blank("--board")) throw new NightlyUsageException("A --board (name or guid) is required for new.");
            if (verb == "new" && Blank("--title")) throw new NightlyUsageException("A --title is required for new.");
            if (!Blank("--description") && !Blank("--description-file")) throw new NightlyUsageException("Pass --description or --description-file, not both.");
            if (!Blank("--reason") && !Blank("--reason-file")) throw new NightlyUsageException("Pass --reason or --reason-file, not both.");
            if (ReasonVerbs.Contains(verb) && Blank("--reason") && Blank("--reason-file"))
                throw new NightlyUsageException("A --reason (or --reason-file) is required for '" + verb + "'. Say why, not what.");
            if (flags.Has("--alias") && flags.Switch("--clear-alias")) throw new NightlyUsageException("Pass --alias or --clear-alias, not both.");
            if (verb == "reorder")
            {
                var top = flags.Switch("--top");
                var bottom = flags.Switch("--bottom");
                var neighbour = !Blank("--before") || !Blank("--after");
                if (top && bottom) throw new NightlyUsageException("Pass only one of --top or --bottom.");
                if ((top || bottom) && neighbour) throw new NightlyUsageException("--top / --bottom cannot be combined with --before / --after.");
                if (!top && !bottom && !neighbour) throw new NightlyUsageException("reorder needs --before, --after, --top or --bottom.");
            }
            if (verb == "order" && Blank("--board")) throw new NightlyUsageException("order needs --board <name|guid>.");
            if (verb == "order" && Blank("--order-file"))
                throw new NightlyUsageException("order needs --order-file <path> (one card ref per line, optionally \"CARD-nnnn High\" or \"CARD-nnnn High Soon\").");

            return new CardCliOptions
            {
                Verb = verb,
                Card = card ?? "",
                Board = Value("--board"),
                BlockedBy = Value("--blocked-by"),
                RelatedBoard = Value("--related-board"),
                Status = BoardCli.Choice("--status", Value("--status"), Statuses) ?? "",
                UpdatedSince = Value("--updated-since"),
                All = flags.Switch("--all"),
                Limit = limit,
                Title = Value("--title"),
                Alias = flags.Switch("--clear-alias") ? "" : flags.Value("--alias"),
                Description = Value("--description"),
                DescriptionFile = Value("--description-file"),
                CardFileVisibility = BoardCli.Choice("--card-file-visibility", Value("--card-file-visibility"), Visibilities),
                PrivateNotesFile = flags.Value("--private-notes-file"),
                ClearPrivateNotes = flags.Switch("--clear-private-notes"),
                Reason = Value("--reason"),
                ReasonFile = Value("--reason-file"),
                To = Value("--to"),
                Importance = BoardCli.Choice("--importance", Value("--importance"), Importances),
                ImportanceProvenance = BoardCli.Choice("--importance-provenance", Value("--importance-provenance"), Provenances),
                CodeVerificationPolicy = BoardCli.Choice("--code-verification-policy", Value("--code-verification-policy"), VerificationPolicies),
                ReviewVerificationPolicy = BoardCli.Choice("--review-verification-policy", Value("--review-verification-policy"), VerificationPolicies),
                Platform = BoardCli.Choice("--platform", Value("--platform"), Platforms),
                Urgency = BoardCli.Choice("--urgency", Value("--urgency"), Urgencies),
                DueAt = Value("--due-at"),
                ClearDueAt = flags.Switch("--clear-due-at"),
                Labels = flags.List("--labels"),
                By = Value("--by"),
                Token = Value("--token"),
                Spawn = flags.Switch("--spawn"),
                Json = flags.Switch("--json"),
                NoWait = flags.Switch("--no-wait"),
                Before = Value("--before"),
                After = Value("--after"),
                Top = flags.Switch("--top"),
                Bottom = flags.Switch("--bottom"),
                OrderFile = Value("--order-file"),
                OverrideHumanRatings = flags.Switch("--override-human-ratings"),
                Api = OperatorApi.Base(api),
                TaskToken = taskToken ?? "",
                WorkingDirectory = workingDirectory ?? "",
            };
        }
    }

    /// <summary>The board card command (card.ps1).</summary>
    public static class CardCli
    {
        /// <summary>The registered caller label every request sends (unchanged from the script: the server's metric key).</summary>
        public const string CallerLabel = "card.ps1";

        /// <summary>How long diagnose polls for the ledger row.</summary>
        public static readonly TimeSpan DiagnoseWait = TimeSpan.FromSeconds(120);

        /// <summary>The pause between diagnose's polls.</summary>
        public static readonly TimeSpan DiagnosePoll = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Runs the verb: the answer on host.Out (list/search page progress and the relationships warning on host.Error); exit 0,
        /// or 1 after the refusal or the request's failure on host.Error. git (the checkout) runs on the host's seam, files are
        /// read through files, and every request goes through http (production: NightlyReport.HttpTransport, BoardCli.Deadline).
        /// </summary>
        public static int Run(CardCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            try
            {
                return new CardRun(options, host, files, http).Execute();
            }
            catch (CardRefusal refusal)
            {
                host.Error.WriteLine(refusal.Message);
                return 1;
            }
        }

        // A refused value or a failed request: its line goes to stderr and the run exits 1.
        private sealed class CardRefusal : Exception
        {
            public CardRefusal(string message) : base(message) { }
        }

        private sealed class CardRun
        {
            private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant);

            private readonly CardCliOptions _o;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly NightlyReportHttp _http;
            private readonly Dictionary<string, string> _headers;
            private JsonNode _limits;
            private string _checkout;
            private string _boardId;

            public CardRun(CardCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http)
            {
                _o = options;
                _host = host;
                _files = files;
                _http = http;
                _headers = BoardCli.Headers(options.TaskToken);
                _headers[AntiphonCaller.Header] = AntiphonCaller.Label(CallerLabel);
            }

            private CardLines Out => new CardLines(_host);

            public int Execute()
            {
                if (_o.Verb == "limits") return Limits();

                // Notes are read before anything is resolved, and never echoed: not the text, not the path's content.
                var notesText = "";
                if (_o.PrivateNotesFile != null)
                {
                    try
                    {
                        notesText = _files.File.ReadAllText(Resolve(_o.PrivateNotesFile), Encoding.UTF8);
                    }
                    catch (Exception)
                    {
                        throw new CardRefusal("Private notes file could not be read.");
                    }
                    var notesLimit = LimitOf("maxPrivateNotesLength");
                    if (notesLimit == 0) notesLimit = 20000;
                    if (notesText.Length > notesLimit)
                        throw new CardRefusal("Private notes length " + Number(notesText.Length) + "; limit " + Number(notesLimit) + ".");
                }
                if (!string.IsNullOrWhiteSpace(_o.Board)) _boardId = ResolveBoard(_o.Board, _o.Collection && _o.All);

                switch (_o.Verb)
                {
                    case "list":
                    case "search":
                        return Collection();
                    case "get":
                        return Get();
                    case "history":
                        return History();
                    case "block":
                    case "unblock":
                        return Relationship();
                    case "new":
                        return New(notesText);
                    case "edit":
                        return Edit(notesText);
                    case "move":
                    case "close":
                        return Move();
                    case "reopen":
                        return Reopen();
                    case "archive":
                    case "unarchive":
                        return Archive();
                    case "reorder":
                        return Reorder();
                    case "order":
                        return Order();
                    default:
                        return Diagnose();
                }
            }

            // ---- verbs ----

            private int Limits()
            {
                var l = CardLimits();
                var o = _host.Out;
                o.WriteLine("title       " + T(l, "maxTitleLength"));
                o.WriteLine("description " + T(l, "maxDescriptionLength"));
                o.WriteLine("privateNotes " + T(l, "maxPrivateNotesLength"));
                o.WriteLine("reason      " + T(l, "maxReasonLength"));
                o.WriteLine("actor       " + T(l, "maxActorLength"));
                o.WriteLine("alias       " + T(l, "maxAliasLength") + " (" + T(l, "maxAliasWords") + " words)");
                o.WriteLine("importance  " + BoardCli.Join(BoardCli.Get(l, "importanceValues")));
                o.WriteLine("urgency     " + BoardCli.Join(BoardCli.Get(l, "urgencyValues")));
                return 0;
            }

            private int Collection()
            {
                var search = _o.Verb == "search";
                var endpoint = search ? "/api/cards/search" : "/api/cards";
                var cards = new List<JsonNode>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tokens = new HashSet<string>(StringComparer.Ordinal);
                string token = null;
                long? total = null;
                for (var pageNumber = 0; pageNumber < 100000; pageNumber++)
                {
                    var page = Request("GET", endpoint + CollectionQuery(token), null);
                    var cardsNode = BoardCli.Get(page, "cards");
                    if (!(page is JsonObject pageObject) || IsNull(cardsNode)) throw new CardRefusal("Card enumeration response has no cards array.");
                    if (!pageObject.TryGetPropertyValue("truncated", out var truncatedNode) || truncatedNode == null
                        || (truncatedNode.GetValueKind() != JsonValueKind.True && truncatedNode.GetValueKind() != JsonValueKind.False)
                        || !pageObject.ContainsKey("nextPageToken"))
                        throw new CardRefusal("Card enumeration response has no valid pagination fields; server may be too old.");
                    var truncated = truncatedNode.GetValueKind() == JsonValueKind.True;
                    if (search)
                    {
                        var pageTotal = Long(BoardCli.Get(page, "total"));
                        if (pageTotal == null || pageTotal < 0) throw new CardRefusal("Card search response has no valid total.");
                        if (total == null) total = pageTotal;
                        else if (total != pageTotal) throw new CardRefusal("Card search total changed during pagination; restart.");
                    }
                    var batch = BoardCli.Items(cardsNode);
                    if (truncated && batch.Count == 0) throw new CardRefusal("Card enumeration returned an empty continuing page.");
                    foreach (var item in batch)
                    {
                        var id = T(item, "id");
                        if (IsNull(item) || string.IsNullOrWhiteSpace(id)) throw new CardRefusal("Card enumeration returned a card without an id.");
                        if (!ids.Add(id)) throw new CardRefusal("Card enumeration repeated a card id; restart.");
                        cards.Add(item);
                    }
                    var nextNode = BoardCli.Get(page, "nextPageToken");
                    var next = BoardCli.Text(nextNode);
                    if ((truncated && string.IsNullOrWhiteSpace(next)) || (!truncated && !IsNull(nextNode)))
                        throw new CardRefusal("Card enumeration returned contradictory truncated and nextPageToken fields; server may be too old.");
                    if (!truncated)
                    {
                        if (search && cards.Count != total) throw new CardRefusal("Card search final count differs from total; restart.");
                        if (!search) total = cards.Count;
                        if (_o.Json)
                        {
                            _host.Out.WriteLine(OperatorApi.Pretty(new JsonObject
                            {
                                ["cards"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()),
                                ["total"] = total.Value,
                                ["truncated"] = false,
                                ["nextPageToken"] = null,
                            }));
                            return 0;
                        }
                        var scope = string.IsNullOrEmpty(_boardId) ? "all boards" : "board " + _boardId;
                        _host.Out.WriteLine(Number(cards.Count) + " cards in " + scope + ". Text below is previewed; use scripts/antiphon card get <hit> for full text.");
                        foreach (var item in cards)
                        {
                            Out.Card(item);
                            if (string.IsNullOrEmpty(_boardId)) _host.Out.WriteLine("board       " + T(item, "boardId"));
                        }
                        return 0;
                    }
                    if (!tokens.Add(next)) throw new CardRefusal("Card enumeration repeated a page token; restart.");
                    _host.Error.WriteLine("Card page " + Number(pageNumber + 1) + ": " + Number(cards.Count) + " returned; continuing for complete enumeration.");
                    token = next;
                }
                throw new CardRefusal("Card enumeration exceeded the page safety limit; restart.");
            }

            private int Get()
            {
                var card = GetCard();
                if (_o.Json)
                {
                    _host.Out.WriteLine(OperatorApi.Pretty(card));
                    return 0;
                }
                // This display-only read is optional on older or unavailable servers: a 404 is quiet, anything else one
                // warning. The card fetch and every write stay on the fatal path.
                JsonNode relationships = null;
                string failure = null;
                try
                {
                    var answer = _http("GET", _o.Api + "/api/cards/" + T(card, "id") + "/relationships", _headers, null);
                    if (OperatorApi.Succeeded(answer))
                    {
                        try
                        {
                            relationships = string.IsNullOrWhiteSpace(answer.Body) ? new JsonObject() : JsonNode.Parse(answer.Body) ?? new JsonObject();
                        }
                        catch (JsonException)
                        {
                            failure = "invalid JSON";
                        }
                    }
                    else if (answer == null || answer.Status != 404) failure = answer == null ? "transport error" : "HTTP " + Number(answer.Status);
                }
                catch (Exception)
                {
                    failure = "transport error";
                }
                if (failure != null) _host.Error.WriteLine("WARNING: relationships lookup failed (" + failure + ").");

                var o = _host.Out;
                Out.Card(card);
                o.WriteLine("id          " + T(card, "id"));
                o.WriteLine("board       " + T(card, "boardId"));
                o.WriteLine("column      " + T(card, "boardColumnId"));
                o.WriteLine("importance  " + T(card, "importance"));
                o.WriteLine("urgency     " + T(card, "urgency"));
                if (!string.IsNullOrWhiteSpace(T(card, "alias"))) o.WriteLine("alias       " + T(card, "alias"));
                o.WriteLine("rank        " + T(card, "rank"));
                if (!IsNull(BoardCli.Get(card, "position"))) o.WriteLine("pos         " + T(card, "position"));
                if (BoardCli.Truthy(BoardCli.Get(card, "dueAt"))) o.WriteLine("due         " + T(card, "dueAt"));
                o.WriteLine("token       " + T(card, "concurrencyToken"));
                o.WriteLine("revisions   " + T(card, "revisionCount"));
                if (relationships == null) o.WriteLine("relationships: unavailable");
                else
                {
                    foreach (var blocker in BoardCli.Items(BoardCli.Get(relationships, "blockedBy"))) Out.Blocker(blocker);
                    foreach (var blocked in BoardCli.Items(BoardCli.Get(relationships, "blocks"))) o.WriteLine("blocks      " + T(blocked, "relatedIdentifier"));
                }
                if (BoardCli.Truthy(BoardCli.Get(card, "assignedAgentName"))) o.WriteLine("agent       " + T(card, "assignedAgentName"));
                if (BoardCli.Truthy(BoardCli.Get(card, "ownerSessionId"))) o.WriteLine("session     " + T(card, "ownerSessionId"));
                var external = BoardCli.Get(card, "externalIssue");
                if (BoardCli.Truthy(external))
                {
                    var text = T(external, "key");
                    if (BoardCli.Truthy(BoardCli.Get(external, "needsHumanReview")))
                    {
                        var raised = T(external, "author");
                        if (string.IsNullOrWhiteSpace(raised)) raised = "unknown";
                        text += " [needs human review: raised by " + raised + "]";
                    }
                    o.WriteLine("external    " + text);
                }
                if (BoardCli.Truthy(BoardCli.Get(card, "archivedAt"))) o.WriteLine("ARCHIVED    " + T(card, "archivedReason"));
                if (BoardCli.Truthy(BoardCli.Get(card, "terminalReason"))) o.WriteLine("closed      " + T(card, "terminalReason"));
                if (!string.IsNullOrWhiteSpace(T(card, "description")))
                {
                    o.WriteLine("");
                    o.WriteLine(T(card, "description"));
                }
                return 0;
            }

            private int History()
            {
                var revisions = Request("GET", "/api/cards/" + Uri.EscapeDataString(_o.Card.Trim()) + "/revisions" + ScopeQuery(), null);
                if (_o.Json)
                {
                    _host.Out.WriteLine(OperatorApi.Pretty(revisions));
                    return 0;
                }
                var rows = BoardCli.Items(revisions);
                if (rows.Count == 0)
                {
                    _host.Out.WriteLine("No history.");
                    return 0;
                }
                foreach (var revision in rows)
                {
                    var related = BoardCli.Truthy(BoardCli.Get(revision, "relatedCardIdentifier")) ? " -> " + T(revision, "relatedCardIdentifier") : "";
                    _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,3}  {1,-12}{4} {2}  {3}",
                        T(revision, "revisionNumber"), T(revision, "kind"), T(revision, "createdAt"), T(revision, "reason"), related));
                    if (BoardCli.Truthy(BoardCli.Get(revision, "editedBy"))) _host.Out.WriteLine("     by " + T(revision, "editedBy"));
                }
                return 0;
            }

            private int Relationship()
            {
                var reason = ReadText("reason", _o.Reason, _o.ReasonFile);
                if (!string.IsNullOrWhiteSpace(reason) || !string.IsNullOrWhiteSpace(_o.By))
                {
                    WithinLimit("Reason", reason, LimitOf("maxReasonLength"));
                    WithinLimit("By", _o.By, LimitOf("maxActorLength"));
                }
                var relatedBoardId = string.IsNullOrWhiteSpace(_o.RelatedBoard) ? null : ResolveBoard(_o.RelatedBoard, false);
                var card = GetCard();
                var body = new JsonObject { ["concurrencyToken"] = TokenFor(card) };
                if (!string.IsNullOrWhiteSpace(reason)) body["reason"] = reason;
                var path = "/api/cards/" + T(card, "id") + "/relationships";
                if (_o.Verb == "block")
                {
                    body["kind"] = "BlockedBy";
                    body["relatedCard"] = _o.BlockedBy.Trim();
                    if (!string.IsNullOrEmpty(relatedBoardId)) body["relatedBoardId"] = relatedBoardId;
                    if (!string.IsNullOrWhiteSpace(_o.By)) body["createdBy"] = _o.By;
                    var created = Request("POST", path, body);
                    if (_o.Json) _host.Out.WriteLine(OperatorApi.Pretty(created));
                    else Out.Blocker(created);
                    return 0;
                }
                var relationships = Request("GET", path, null);
                var blockers = BoardCli.Items(BoardCli.Get(relationships, "blockedBy"));
                var matches = blockers.Where(r => string.Equals(T(r, "relatedIdentifier"), _o.BlockedBy.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1)
                {
                    var current = string.Join(", ", blockers.Select(r => T(r, "relatedIdentifier")));
                    if (string.IsNullOrWhiteSpace(current)) current = "(none)";
                    throw new CardRefusal("Expected one blocker matching '" + _o.BlockedBy + "'; found " + Number(matches.Count) + ". Current blockers: " + current);
                }
                if (!string.IsNullOrWhiteSpace(_o.By)) body["removedBy"] = _o.By;
                var updated = Request("POST", path + "/" + T(matches[0], "id") + "/remove", body);
                if (_o.Json) _host.Out.WriteLine(OperatorApi.Pretty(updated));
                else _host.Out.WriteLine("unblocked   " + T(card, "identifier") + " (removed " + T(matches[0], "relatedIdentifier") + ")");
                return 0;
            }

            private int New(string notesText)
            {
                var description = ReadText("description", _o.Description, _o.DescriptionFile);
                WithinLimit("Title", _o.Title, LimitOf("maxTitleLength"));
                WithinLimit("Description", description, LimitOf("maxDescriptionLength"));
                if (_o.Alias != null) AssertAlias(_o.Alias);

                var body = new JsonObject { ["title"] = _o.Title };
                if (_o.PrivateNotesFile != null) body["privateNotes"] = notesText;
                if (_o.CardFileVisibility != null) body["cardFileVisibility"] = _o.CardFileVisibility;
                if (!string.IsNullOrEmpty(description)) body["description"] = description;
                if (!string.IsNullOrWhiteSpace(_o.Alias)) body["alias"] = _o.Alias;
                if (_o.Importance != null) body["importance"] = _o.Importance;
                if (_o.Urgency != null) body["urgency"] = _o.Urgency;
                if (!string.IsNullOrWhiteSpace(_o.DueAt)) body["dueAt"] = _o.DueAt;
                if (_o.Labels.Count > 0) body["labels"] = Strings(_o.Labels);
                if (_o.Platform != null) body["requiredPlatform"] = _o.Platform;

                var created = Request("POST", "/api/boards/" + _boardId + "/cards", body);
                if (_o.Json)
                {
                    _host.Out.WriteLine(OperatorApi.Pretty(created));
                    return 0;
                }
                Out.Card(created);
                _host.Out.WriteLine("id          " + T(created, "id"));
                Out.CardFiles(created);
                return 0;
            }

            private int Edit(string notesText)
            {
                var card = GetCard();
                var reason = RequiredReason();
                var description = ReadText("description", _o.Description, _o.DescriptionFile);
                WithinLimit("Title", _o.Title, LimitOf("maxTitleLength"));
                WithinLimit("Description", description, LimitOf("maxDescriptionLength"));
                WithinLimit("By", _o.By, LimitOf("maxActorLength"));
                if (_o.Alias != null) AssertAlias(_o.Alias);

                // Null means UNCHANGED for every content field, so only what actually changed is sent.
                var body = new JsonObject { ["concurrencyToken"] = TokenFor(card), ["reason"] = reason };
                if (!string.IsNullOrWhiteSpace(_o.Title)) body["title"] = _o.Title;
                if (_o.PrivateNotesFile != null || _o.ClearPrivateNotes) body["privateNotes"] = notesText;
                if (_o.CardFileVisibility != null) body["cardFileVisibility"] = _o.CardFileVisibility;
                if (!string.IsNullOrEmpty(description)) body["description"] = description;
                if (_o.Importance != null) body["importance"] = _o.Importance;
                if (_o.ImportanceProvenance != null) body["importanceProvenance"] = _o.ImportanceProvenance;
                if (_o.Urgency != null) body["urgency"] = _o.Urgency;
                if (_o.ClearDueAt) body["clearDueAt"] = true;
                else if (!string.IsNullOrWhiteSpace(_o.DueAt)) body["dueAt"] = _o.DueAt;
                if (_o.Labels.Count > 0) body["labels"] = Strings(_o.Labels);
                if (_o.Alias != null) body["alias"] = _o.Alias;
                if (_o.CodeVerificationPolicy != null) body["codeVerificationPolicy"] = _o.CodeVerificationPolicy;
                if (_o.ReviewVerificationPolicy != null) body["reviewVerificationPolicy"] = _o.ReviewVerificationPolicy;
                if (_o.Platform != null) body["requiredPlatform"] = _o.Platform;
                if (!string.IsNullOrWhiteSpace(_o.By)) body["editedBy"] = _o.By;
                if (body.Count <= 2)
                    throw new CardRefusal("Nothing to change. Pass at least one of --title, --description/--description-file, --alias, --importance, --importance-provenance,"
                        + " --urgency, --due-at, --clear-due-at, --labels, --code-verification-policy, --review-verification-policy, --platform.");

                var updated = Request("PATCH", "/api/cards/" + T(card, "id") + "/content", body);
                if (_o.Json)
                {
                    _host.Out.WriteLine(OperatorApi.Pretty(updated));
                    return 0;
                }
                Out.Card(updated);
                _host.Out.WriteLine("revision    " + T(updated, "revisionCount"));
                Out.CardFiles(updated);
                return 0;
            }

            private int Move()
            {
                var card = GetCard();
                var columns = BoardCli.Items(Request("GET", "/api/boards/" + T(card, "boardId") + "/columns", null));
                string reason;
                JsonNode target;
                if (_o.Verb == "close")
                {
                    reason = RequiredReason();
                    // The board's own idea of "finished": the first terminal column in column order.
                    target = columns.Where(c => BoardCli.Truthy(BoardCli.Get(c, "isTerminal"))).OrderBy(c => Double(BoardCli.Get(c, "columnOrder"))).FirstOrDefault();
                    if (target == null) throw new CardRefusal("Board " + T(card, "boardId") + " has no terminal column to close into.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_o.To)) throw new CardRefusal("A --to column is required for move. This board has: " + Names(columns));
                    reason = ReadText("reason", _o.Reason, _o.ReasonFile);
                    if (!string.IsNullOrWhiteSpace(reason)) WithinLimit("Reason", reason, LimitOf("maxReasonLength"));
                    var needle = _o.To.Trim();
                    target = columns.FirstOrDefault(c => Same(T(c, "id"), _o.To) || Same(T(c, "name"), needle) || Same(T(c, "stateKey"), needle) || Same(T(c, "cardStatus"), needle));
                    if (target == null) throw new CardRefusal("No column '" + _o.To + "' on this board. It has: " + Names(columns));
                }

                var body = new JsonObject { ["boardColumnId"] = BoardCli.Get(target, "id")?.DeepClone(), ["concurrencyToken"] = TokenFor(card) };
                if (!string.IsNullOrWhiteSpace(reason)) body["reason"] = reason;
                if (_o.Spawn) body["spawn"] = true;

                var result = Request("PATCH", "/api/cards/" + T(card, "id"), body);
                Out.Card(BoardCli.Get(result, "card"));
                _host.Out.WriteLine("moved to    " + T(target, "name"));
                Out.TrackerPush(BoardCli.Get(result, "trackerPush"));
                Out.TaskSettlement(BoardCli.Get(result, "taskSettlement"));
                if (BoardCli.Truthy(BoardCli.Get(target, "isTerminal"))) Out.DeploymentPrompt();
                if (BoardCli.Truthy(BoardCli.Get(result, "spawnedSessionId"))) _host.Out.WriteLine("started     session " + T(result, "spawnedSessionId"));
                else if (BoardCli.Truthy(BoardCli.Get(result, "spawnSuppressed")))
                    _host.Out.WriteLine("moved into an active column; NO agent was started - the tick will not pick it up either; re-run with --spawn (or POST /spawn) to start one");
                return 0;
            }

            private int Reopen()
            {
                var card = GetCard();
                var reason = RequiredReason();
                WithinLimit("By", _o.By, LimitOf("maxActorLength"));

                // --to is optional: omitted, the server picks Backlog (then the lowest-order live column). That fallback is not
                // re-implemented here; only a name the caller DID give is resolved.
                JsonNode target = null;
                if (!string.IsNullOrWhiteSpace(_o.To))
                {
                    var columns = BoardCli.Items(Request("GET", "/api/boards/" + T(card, "boardId") + "/columns", null));
                    var needle = _o.To.Trim();
                    target = columns.FirstOrDefault(c => Same(T(c, "id"), _o.To) || Same(T(c, "name"), needle) || Same(T(c, "stateKey"), needle));
                    if (target == null) throw new CardRefusal("No column '" + _o.To + "' on this board. It has: " + Names(columns));
                }

                var body = new JsonObject { ["concurrencyToken"] = TokenFor(card), ["reason"] = reason };
                if (target != null) body["boardColumnId"] = BoardCli.Get(target, "id")?.DeepClone();
                if (!string.IsNullOrWhiteSpace(_o.By)) body["reopenedBy"] = _o.By;

                var updated = Request("POST", "/api/cards/" + T(card, "id") + "/reopen", body);
                Out.Card(BoardCli.Get(updated, "card"));
                Out.TrackerPush(BoardCli.Get(updated, "trackerPush"));
                _host.Out.WriteLine("reopened to " + (target != null ? T(target, "name") : T(updated, "card", "status")));
                return 0;
            }

            private int Archive()
            {
                var card = GetCard();
                var reason = RequiredReason();
                WithinLimit("By", _o.By, LimitOf("maxActorLength"));
                var body = new JsonObject { ["concurrencyToken"] = TokenFor(card), ["reason"] = reason };
                if (!string.IsNullOrWhiteSpace(_o.By)) body[_o.Verb == "archive" ? "archivedBy" : "unarchivedBy"] = _o.By;

                var updated = Request("POST", "/api/cards/" + T(card, "id") + "/" + _o.Verb, body);
                Out.Card(updated);
                // Archive is not deletion: the row stays, so every reference to the identifier keeps resolving and the allocator
                // never hands the number out again.
                _host.Out.WriteLine(_o.Verb == "archive" ? "archived (reversible: scripts/antiphon card unarchive)" : "back on the board");
                return 0;
            }

            private int Reorder()
            {
                var card = GetCard();
                var reason = ReadText("reason", _o.Reason, _o.ReasonFile);
                if (!string.IsNullOrWhiteSpace(reason)) WithinLimit("Reason", reason, LimitOf("maxReasonLength"));
                WithinLimit("By", _o.By, LimitOf("maxActorLength"));

                var body = new JsonObject { ["concurrencyToken"] = TokenFor(card) };
                if (!string.IsNullOrWhiteSpace(_o.Before)) body["before"] = _o.Before.Trim();
                if (!string.IsNullOrWhiteSpace(_o.After)) body["after"] = _o.After.Trim();
                if (_o.Top) body["placement"] = "Top";
                if (_o.Bottom) body["placement"] = "Bottom";
                if (!string.IsNullOrWhiteSpace(reason)) body["reason"] = reason;
                if (!string.IsNullOrWhiteSpace(_o.By)) body["editedBy"] = _o.By;

                var updated = Request("PATCH", "/api/cards/" + T(card, "id") + "/position", body);
                Out.Card(updated);
                if (!IsNull(BoardCli.Get(updated, "position"))) _host.Out.WriteLine("pos         " + T(updated, "position"));
                return 0;
            }

            private int Order()
            {
                var path = Resolve(_o.OrderFile);
                if (!_files.File.Exists(path)) throw new CardRefusal("Order file not found: " + _o.OrderFile);
                var reason = RequiredReason();
                WithinLimit("By", _o.By, LimitOf("maxActorLength"));

                var entries = new JsonArray();
                foreach (var line in _files.File.ReadAllLines(path, Encoding.UTF8))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    var parts = Whitespace.Split(trimmed, 3);
                    var entry = new JsonObject { ["id"] = parts[0] };
                    if (parts.Length >= 2) entry["importance"] = parts[1];
                    if (parts.Length >= 3) entry["urgency"] = parts[2];
                    entries.Add(entry);
                }
                if (entries.Count == 0) throw new CardRefusal("Order file has no card refs.");

                var body = new JsonObject { ["cards"] = entries, ["reason"] = reason };
                if (!string.IsNullOrWhiteSpace(_o.By)) body["editedBy"] = _o.By;
                if (_o.OverrideHumanRatings) body["overrideHumanRatings"] = true;

                var result = Request("POST", "/api/boards/" + _boardId + "/card-order", body);
                foreach (var card in BoardCli.Items(BoardCli.Get(result, "cards"))) Out.Card(card);
                foreach (var skipped in BoardCli.Items(BoardCli.Get(result, "skippedHumanRated")))
                    _host.Out.WriteLine("skipped     " + T(skipped, "identifier") + "  human-rated " + T(skipped, "importance"));
                return 0;
            }

            private int Diagnose()
            {
                var card = GetCard();
                var id = T(card, "id");
                var queued = Request("POST", "/api/cards/" + id + "/diagnose", new JsonObject());
                if (_o.NoWait)
                {
                    if (_o.Json) _host.Out.WriteLine(OperatorApi.Pretty(queued));
                    else _host.Out.WriteLine("queued      " + T(card, "identifier") + "  POST /api/cards/" + id + "/diagnose -> 202");
                    return 0;
                }

                var updatedAt = Time(BoardCli.Get(card, "updatedAt"));
                var deadline = _host.UtcNow + DiagnoseWait;
                JsonNode row = null;
                while (_host.UtcNow < deadline)
                {
                    var rows = BoardCli.Items(Request("GET", "/api/diagnoses?cardId=" + id + "&kind=Labels&limit=1", null));
                    if (rows.Count > 0)
                    {
                        // A row written before the card's last update is an earlier diagnosis; wait for this one.
                        var created = BoardCli.Truthy(BoardCli.Get(rows[0], "createdAt")) ? Time(BoardCli.Get(rows[0], "createdAt")) : null;
                        if (created == null || updatedAt == null || created >= updatedAt)
                        {
                            row = rows[0];
                            break;
                        }
                    }
                    _host.Sleep(DiagnosePoll);
                }
                if (row == null)
                    throw new CardRefusal("Timed out waiting for a diagnosis of " + T(card, "identifier") + ". Pass --no-wait and poll GET /api/diagnoses?cardId=" + id + ".");
                if (_o.Json)
                {
                    _host.Out.WriteLine(OperatorApi.Pretty(row));
                    return 0;
                }
                _host.Out.WriteLine(T(card, "identifier") + "  " + T(row, "outcome") + "  " + T(row, "applied"));
                if (BoardCli.Truthy(BoardCli.Get(row, "reason"))) _host.Out.WriteLine("reason      " + T(row, "reason"));
                if (BoardCli.Truthy(BoardCli.Get(row, "answer"))) _host.Out.WriteLine("answer      " + T(row, "answer"));
                _host.Out.WriteLine("cost        " + T(row, "costUsd") + "  wait " + T(row, "waitMs") + "ms");
                return 0;
            }

            // ---- requests, limits and lookups ----

            // One request on the fatal path: the parsed answer, or a refusal carrying the server's detail.
            private JsonNode Request(string method, string path, JsonNode body)
            {
                if (!BoardCli.Send(_http, _o.Api, method, path, _headers, body == null ? null : OperatorApi.Json(body), out var answer, out var failure))
                    throw new CardRefusal(failure);
                return answer;
            }

            private JsonNode CardLimits() => _limits ??= Request("GET", "/api/cards/limits", null) ?? new JsonObject();

            // A ceiling from GET /api/cards/limits; 0 when the server does not state it (as [int]$null was).
            private int LimitOf(string name)
            {
                var value = BoardCli.Get(CardLimits(), name);
                if (IsNull(value)) return 0;
                if (value.GetValueKind() == JsonValueKind.Number) return (int)Math.Round(value.GetValue<double>(), MidpointRounding.ToEven);
                return int.TryParse(BoardCli.Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            }

            // Fails LOCALLY, naming the ceiling, so an over-long body never becomes a round trip that ends in a 422.
            private static void WithinLimit(string field, string value, int limit)
            {
                if (string.IsNullOrEmpty(value) || value.Length <= limit) return;
                throw new CardRefusal(field + " is " + Number(value.Length) + " characters; the limit is " + Number(limit) + ". Trim it, or split the detail across a comment.");
            }

            // CARD-0350: refused here as CardService.TryNormalizeAlias would. Blank is "no alias" (new omits it; edit sends empty
            // to clear) and is not an error.
            private void AssertAlias(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new CardRefusal("Alias must be a single line.");
                var words = Whitespace.Split(value.Trim());
                var maxWords = LimitOf("maxAliasWords");
                if (words.Length > maxWords) throw new CardRefusal("Alias must be at most " + Number(maxWords) + " words; got " + Number(words.Length) + ".");
                WithinLimit("Alias", string.Join(" ", words), LimitOf("maxAliasLength"));
            }

            // The text of --<name> or --<name>-file (both is a usage error); a file is read as UTF-8 and sent as is.
            private string ReadText(string name, string inline, string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return inline ?? "";
                var resolved = Resolve(path);
                if (!_files.File.Exists(resolved)) throw new CardRefusal("--" + name + "-file '" + path + "' does not exist.");
                return _files.File.ReadAllText(resolved, Encoding.UTF8);
            }

            private string RequiredReason()
            {
                var text = ReadText("reason", _o.Reason, _o.ReasonFile);
                if (string.IsNullOrWhiteSpace(text)) throw new CardRefusal("A --reason (or --reason-file) is required for '" + _o.Verb + "'. Say why, not what.");
                WithinLimit("Reason", text, LimitOf("maxReasonLength"));
                return text;
            }

            // A path the caller gave, relative to the working directory.
            private string Resolve(string path)
            {
                if (string.IsNullOrEmpty(_o.WorkingDirectory) || string.IsNullOrEmpty(path)) return path;
                return _files.Path.Combine(_o.WorkingDirectory, path);
            }

            // A board guid as given, else the one board whose name matches ignoring case.
            private string ResolveBoard(string nameOrGuid, bool includeArchived)
            {
                if (Guid.TryParse(nameOrGuid, out _)) return nameOrGuid;
                var all = BoardCli.Items(Request("GET", includeArchived ? "/api/boards?includeArchived=true" : "/api/boards", null));
                var hits = all.Where(b => T(b, "name").Length > 0 && string.Equals(T(b, "name").ToLowerInvariant(), nameOrGuid.ToLowerInvariant(), StringComparison.Ordinal)).ToList();
                if (hits.Count == 0) throw new CardRefusal("No board named '" + nameOrGuid + "'. Known boards: " + string.Join(", ", all.Select(b => T(b, "name"))));
                if (hits.Count > 1) throw new CardRefusal("'" + nameOrGuid + "' names " + Number(hits.Count) + " boards - pass the guid instead.");
                return T(hits[0], "id");
            }

            private JsonNode GetCard() => Request("GET", "/api/cards/" + Uri.EscapeDataString(_o.Card.Trim()) + ScopeQuery(), null);

            // By default the token read just before the write; --token is sent verbatim.
            private JsonNode TokenFor(JsonNode card) =>
                !string.IsNullOrWhiteSpace(_o.Token) ? JsonValue.Create(_o.Token) : BoardCli.Get(card, "concurrencyToken")?.DeepClone();

            // ?boardId=<resolved board>&cwd=<checkout>: the scope a card identifier resolves in (CARD-0218).
            private string ScopeQuery()
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(_boardId)) parts.Add("boardId=" + _boardId);
                var checkout = Checkout();
                if (!string.IsNullOrWhiteSpace(checkout)) parts.Add("cwd=" + Uri.EscapeDataString(checkout));
                return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
            }

            private string CollectionQuery(string pageToken)
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(_boardId)) parts.Add("boardId=" + Uri.EscapeDataString(_boardId));
                if (!string.IsNullOrEmpty(_o.Status)) parts.Add("status=" + Uri.EscapeDataString(_o.Status));
                if (_o.Verb == "list" && !string.IsNullOrEmpty(_o.UpdatedSince)) parts.Add("updatedSince=" + Uri.EscapeDataString(_o.UpdatedSince));
                if (_o.Verb == "search") parts.Add("q=" + Uri.EscapeDataString(_o.Card));
                if (_o.All) parts.Add("includeArchived=true");
                if (_o.Limit != null) parts.Add("limit=" + Uri.EscapeDataString(Number(_o.Limit.Value)));
                if (!string.IsNullOrEmpty(pageToken)) parts.Add("pageToken=" + Uri.EscapeDataString(pageToken));
                return "?" + string.Join("&", parts);
            }

            // git's top level of the working directory, else the checkout a workspace marker there names, else the working
            // directory itself (outside a repository, or no git).
            private string Checkout()
            {
                if (_checkout != null) return _checkout;
                var directory = _o.WorkingDirectory ?? "";
                if (directory.Trim().Length == 0) return _checkout = "";
                try
                {
                    var result = _host.RunGit(directory, "rev-parse", "--show-toplevel");
                    var top = (result.Output ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
                    if (result.ExitCode == 0 && !string.IsNullOrEmpty(top)) return _checkout = top;
                }
                catch (Exception)
                {
                    // Outside a repository, or git missing: the marker, then the working directory.
                }
                return _checkout = WorkspaceCheckout(directory) ?? directory;
            }

            // CARD-0251: a dedicated sibling workspace carries antiphon.workspace.json pointing at the checkout.
            private string WorkspaceCheckout(string directory)
            {
                try
                {
                    var marker = _files.Path.Combine(directory, OrchestratorWorkspace.MarkerFileName);
                    if (!_files.File.Exists(marker)) return null;
                    var checkout = T(JsonNode.Parse(_files.File.ReadAllText(marker)), "checkout");
                    if (string.IsNullOrWhiteSpace(checkout)) return null;
                    var resolved = _files.Path.GetFullPath(_files.Path.Combine(directory, checkout));
                    return _files.Directory.Exists(resolved) || _files.File.Exists(resolved) ? resolved : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // ---- values ----

            private static string T(JsonNode node, params string[] path) => BoardCli.Text(BoardCli.Get(node, path));

            private static bool IsNull(JsonNode node) => node == null || node.GetValueKind() == JsonValueKind.Null;

            private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Names(IEnumerable<JsonNode> columns) => string.Join(", ", columns.Select(c => T(c, "name")));

            private static JsonArray Strings(IEnumerable<string> values) => new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());

            private static double Double(JsonNode node)
            {
                if (IsNull(node)) return double.MinValue;
                if (node.GetValueKind() == JsonValueKind.Number) return node.GetValue<double>();
                return double.TryParse(BoardCli.Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.MinValue;
            }

            private static long? Long(JsonNode node)
            {
                if (IsNull(node)) return null;
                if (node.GetValueKind() == JsonValueKind.Number) return (long)Math.Round(node.GetValue<double>(), MidpointRounding.ToEven);
                return long.TryParse(BoardCli.Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (long?)null;
            }

            private static DateTimeOffset? Time(JsonNode node) =>
                DateTimeOffset.TryParse(BoardCli.Text(node), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTimeOffset?)null;
        }

        // The lines the verbs share.
        private readonly struct CardLines
        {
            private readonly NightlyHost _host;

            public CardLines(NightlyHost host) => _host = host;

            private static string T(JsonNode node, params string[] path) => BoardCli.Text(BoardCli.Get(node, path));

            private static bool IsNull(JsonNode node) => node == null || node.GetValueKind() == JsonValueKind.Null;

            private static bool Is(JsonNode node, string value) => string.Equals(BoardCli.Text(node), value, StringComparison.OrdinalIgnoreCase);

            // "<identifier>  <status>  <importance>/<urgency>  rank <r> [pos <p>] (auto|human-rated)  <title> [labels]", then the
            // platform when the card pins one.
            public void Card(JsonNode card)
            {
                var labels = BoardCli.Items(BoardCli.Get(card, "labels"));
                var labelText = labels.Count > 0 ? " [" + string.Join(", ", labels.Select(BoardCli.Text)) + "]" : "";
                var provenance = Is(BoardCli.Get(card, "importanceProvenance"), "Human") ? "human-rated" : "auto";
                var rank = "rank " + T(card, "rank");
                if (!IsNull(BoardCli.Get(card, "position"))) rank += " pos " + T(card, "position");
                _host.Out.WriteLine(T(card, "identifier") + "  " + T(card, "status") + "  " + T(card, "importance") + "/" + T(card, "urgency") + "  " + rank
                    + " (" + provenance + ")  " + T(card, "title") + labelText);
                if (BoardCli.Truthy(BoardCli.Get(card, "requiredPlatform"))) _host.Out.WriteLine("platform    " + T(card, "requiredPlatform"));
            }

            public void Blocker(JsonNode relationship)
            {
                var state = T(relationship, "relatedStatus");
                if (BoardCli.Truthy(BoardCli.Get(relationship, "resolved"))) state = "resolved: " + state;
                _host.Out.WriteLine("blocked by  " + T(relationship, "relatedIdentifier") + " (" + state + ")");
            }

            // CARD-0408: eligibility, target, policy and pending cleanup from the saved card's cardFileStatus. It never claims a
            // file was written: saving a card does not sync.
            public void CardFiles(JsonNode card)
            {
                var o = _host.Out;
                var s = BoardCli.Get(card, "cardFileStatus");
                if (IsNull(s))
                {
                    o.WriteLine("status unavailable; export safety not confirmed");
                    return;
                }
                var eligible = BoardCli.Truthy(BoardCli.Get(s, "eligible"));
                if (eligible)
                {
                    var interval = BoardCli.Get(s, "intervalSeconds");
                    var when = !IsNull(interval) && interval.GetValueKind() == JsonValueKind.Number && interval.GetValue<double>() == 0
                        ? "manual sync" : "next sync (" + T(s, "intervalSeconds") + "s)";
                    o.WriteLine("card files  ELIGIBLE: " + when);
                }
                else o.WriteLine("card files  NOT WRITTEN: " + T(s, "reason"));
                var visibility = BoardCli.Get(s, "repositoryVisibility");
                if (BoardCli.Truthy(BoardCli.Get(s, "repositoryPath")) && BoardCli.Truthy(BoardCli.Get(s, "directory")))
                {
                    var relative = eligible && BoardCli.Truthy(BoardCli.Get(s, "relativeFile")) ? T(s, "relativeFile") : T(s, "directory");
                    var described = Is(visibility, "Unknown") ? "Unknown repository visibility; not checked" : "configured " + BoardCli.Text(visibility) + " repository";
                    o.WriteLine("target      " + Target(T(s, "repositoryPath"), relative) + " (" + described + ")");
                }
                else o.WriteLine("target      unavailable (no safe resolved card-file target)");
                o.WriteLine("policy      " + T(s, "cardFileVisibility") + "; board sync " + (BoardCli.Truthy(BoardCli.Get(s, "syncCardFiles")) ? "on" : "off")
                    + "; AutoCommit " + (BoardCli.Truthy(BoardCli.Get(s, "autoCommit")) ? "on" : "off"));
                o.WriteLine("private     notes stored in Antiphon; excluded from card files");
                if (Is(visibility, "Public")) o.WriteLine("PUBLIC REPOSITORY: public card fields will be written on sync");
                if (Is(visibility, "Unknown")) o.WriteLine("UNKNOWN REPOSITORY VISIBILITY: sync blocked");
                if (IsNull(BoardCli.Get(s, "workingTreeRemovalPending")) || IsNull(BoardCli.Get(s, "gitRemovalPending")))
                    o.WriteLine("removal     pending: cleanup state unavailable; erasure not confirmed");
                else if (BoardCli.Items(BoardCli.Get(s, "warnings")).Any(w => Is(w, "card_file_staged_private_residue")))
                    o.WriteLine("removal     pending: staged private export; reconcile to unstage");
                else if (BoardCli.Truthy(BoardCli.Get(s, "workingTreeRemovalPending"))) o.WriteLine("removal     pending: reconcile previously exported working files");
                else if (BoardCli.Truthy(BoardCli.Get(s, "gitRemovalPending"))) o.WriteLine("removal     pending: working files removed; Git index/HEAD cleanup required");
                if (!BoardCli.Truthy(BoardCli.Get(s, "enabled"))) o.WriteLine("card-file sync disabled; existing exports are not erased");
            }

            // The server's repository path joined with the card-file path for display. It is the server's path, not a local one:
            // a drive-letter root keeps backslashes off Windows too.
            private static string Target(string repositoryPath, string relative)
            {
                if (System.IO.Path.DirectorySeparatorChar == '\\' || !Regex.IsMatch(repositoryPath, "^[A-Za-z]:"))
                    return Join(repositoryPath, relative.Replace('/', System.IO.Path.DirectorySeparatorChar), System.IO.Path.DirectorySeparatorChar);
                return Join(repositoryPath, relative.Replace('/', '\\'), '\\');
            }

            private static string Join(string root, string leaf, char separator)
            {
                var trimmedRoot = root.TrimEnd('\\', '/');
                var trimmedLeaf = leaf.TrimStart('\\', '/');
                if (trimmedLeaf.Length == 0) return trimmedRoot.Length == 0 ? root : trimmedRoot;
                return trimmedRoot + separator + trimmedLeaf;
            }

            public void TaskSettlement(JsonNode settlement)
            {
                if (IsNull(settlement)) return;
                foreach (var id in BoardCli.Items(BoardCli.Get(settlement, "canceled")).Select(BoardCli.Text).Where(id => !string.IsNullOrWhiteSpace(id)))
                    _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-11} canceled {1}", "tasks", id));
                foreach (var id in BoardCli.Items(BoardCli.Get(settlement, "leftOpen")).Select(BoardCli.Text).Where(id => !string.IsNullOrWhiteSpace(id)))
                    _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-11} still working {1} (attention row; cancel or reopen)", "tasks", id));
            }

            public void DeploymentPrompt()
            {
                _host.Out.WriteLine("deployment not verified: card move and task settlement succeeded; service activation is still unverified.");
                _host.Out.WriteLine("From the updated canonical checkout, compare the running GET /api/version SHA with the intended landed commit (canonical HEAD or origin/master), then probe the changed feature.");
                _host.Out.WriteLine("For runner/fake-gateway changes, run pwsh -File scripts/check-daemon-build.ps1 there; restart or redeploy only the affected service using docs/apphost-runbook.md, then recheck its build.");
            }

            public void TrackerPush(JsonNode push)
            {
                if (IsNull(push)) return;
                var name = Is(BoardCli.Get(push, "trackerKind"), "GitHubIssues") ? "GitHub" : T(push, "trackerKind");
                var outcome = T(push, "outcome");
                string line;
                switch (outcome.ToLowerInvariant())
                {
                    case "closed": line = "closed " + T(push, "externalKey") + " (" + T(push, "url") + ")"; break;
                    case "reopened": line = "reopened " + T(push, "externalKey") + " (" + T(push, "url") + ")"; break;
                    case "insync": line = "already in sync (" + T(push, "externalKey") + ")"; break;
                    case "skipped": line = "skipped: " + T(push, "reason"); break;
                    case "failed": line = "push FAILED: " + T(push, "reason") + " - the next scheduled sync will retry"; break;
                    default: line = outcome + " (" + T(push, "externalKey") + ")"; break;
                }
                _host.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-11} {1}", name, line));
            }
        }
    }
}
