using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0408/0409: the card command's private notes and card-file status (Antiphon.Nightly.CardCli, which replaced
/// scripts/card.ps1 in unit 27 of the 2026-11-22 plan) in process against a stub transport. Notes are file-only, sent
/// verbatim and never printed; new/edit report eligibility, target and pending cleanup without claiming a file was written.
/// Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class CardFilePrivacyScriptTests
{
    private const string Board = "22222222-2222-2222-2222-222222222222";
    [Test]
    public async Task Paired_disposition_edit_sends_one_pinned_patch()
    {
        var api = new Api();
        var descriptionFile = Path.GetTempFileName();
        var notesFile = Path.GetTempFileName();
        const string description = "Generalized incident\r\nTrigger, failure, fix and validation.\n雪 😀 ` $()";
        const string notes = "  Previous note\r\n雪 😀 ` $()\n  \n\nC409_PRIVATE_EXCERPT\r\n ";
        try
        {
            await File.WriteAllBytesAsync(descriptionFile, Encoding.UTF8.GetBytes(description));
            await File.WriteAllBytesAsync(notesFile, Encoding.UTF8.GetBytes(notes));
            var run = Run(api, "edit", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "--board", Board,
                "--title", "Review existing card privacy dispositions", "--description-file", descriptionFile,
                "--private-notes-file", notesFile, "--card-file-visibility", "Inherit", "--token", "pinned-token",
                "--reason", "CARD-0409 privacy disposition applied; technical meaning preserved", "--json");
            run.Code.ShouldBe(0, run.Text);
            api.Writes.ShouldBe(1);
            api.Body.GetProperty("description").GetString().ShouldBe(description);
            api.Body.GetProperty("privateNotes").GetString().ShouldBe(notes);
            api.Body.GetProperty("title").GetString().ShouldBe("Review existing card privacy dispositions");
            api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("pinned-token");
            api.Body.GetProperty("cardFileVisibility").GetString().ShouldBe("Inherit");
            api.Body.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[] {
                "title", "description", "privateNotes", "cardFileVisibility", "concurrencyToken", "reason" }.Order());
            run.Text.ShouldNotContain("C409_PRIVATE_EXCERPT");
        }
        finally { File.Delete(descriptionFile); File.Delete(notesFile); }
    }
    [Test]
    [Arguments("Unknown", false, "board_not_opted_in")]
    [Arguments("Public", false, "board_not_opted_in")]
    [Arguments("Private", true, null)]
    public void New_reports_eligibility_target_and_visibility_without_claiming_written(string visibility, bool eligible, string? reason)
    {
        var api = new Api(visibility, eligible, reason);
        var run = Run(api, "new", "--board", Board, "--title", "public");
        run.Code.ShouldBe(0, run.Text);
        api.Writes.ShouldBe(1);
        run.Text.ShouldContain(eligible ? "card files  ELIGIBLE: next sync (60s)" : "card files  NOT WRITTEN: " + reason);
        run.Text.ShouldContain("target      C:\\src\\example\\docs\\cards\\example");
        run.Text.ShouldContain("policy      Inherit; board sync " + (eligible ? "on" : "off") + "; AutoCommit off");
        run.Text.ShouldContain("private     notes stored in Antiphon; excluded from card files");
        if (visibility == "Unknown") run.Text.ShouldContain("UNKNOWN REPOSITORY VISIBILITY: sync blocked");
        if (visibility == "Public") run.Text.ShouldContain("PUBLIC REPOSITORY: public card fields will be written on sync");
        run.Text.ShouldNotContain("card files  WRITTEN");
    }

    [Test]
    [Arguments("new")]
    [Arguments("edit")]
    public async Task Notes_are_file_only_verbatim_and_JSON_is_one_note_free_response(string verb)
    {
        var api = new Api();
        var file = Path.GetTempFileName();
        const string notes = "  C408_SCRIPT_PRIVATE\n雪 `$() <img>  ";
        try
        {
            await File.WriteAllTextAsync(file, notes, new UTF8Encoding(false));
            var args = verb == "new" ? new[] { "new", "--board", Board, "--title", "public" }
                : new[] { "edit", "CARD-0001", "--reason", "correction" };
            var run = Run(api, args.Concat(["--private-notes-file", file, "--card-file-visibility", "Private", "--json"]).ToArray());
            run.Code.ShouldBe(0, run.Text);
            api.Body.GetProperty("privateNotes").GetString().ShouldBe(notes);
            api.Body.GetProperty("cardFileVisibility").GetString().ShouldBe("Private");
            run.Text.ShouldNotContain("C408_SCRIPT_PRIVATE");
            var result = JsonDocument.Parse(run.Text).RootElement;
            result.ValueKind.ShouldBe(JsonValueKind.Object);
            result.TryGetProperty("privateNotes", out _).ShouldBeFalse();
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void Clear_notes_and_omission_preserve_token_contract()
    {
        var api = new Api();
        Run(api, "edit", "CARD-0001", "--reason", "clear", "--clear-private-notes", "--token", "strict-token").Code.ShouldBe(0);
        api.Body.GetProperty("privateNotes").GetString().ShouldBeEmpty();
        api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("strict-token");
        Run(api, "edit", "CARD-0001", "--reason", "title", "--title", "updated").Code.ShouldBe(0);
        api.Body.TryGetProperty("privateNotes", out _).ShouldBeFalse();
        api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("fresh-token");
    }

    [Test]
    public async Task Oversized_notes_fail_locally_without_echo_and_mixed_case_server_errors_survive()
    {
        var api = new Api();
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "C408_PRIVATE" + new string('x', 20001));
            var result = Run(api, "new", "--board", Board, "--title", "public", "--private-notes-file", file);
            result.Code.ShouldNotBe(0);
            result.Text.ShouldContain("limit 20000");
            result.Text.ShouldNotContain("C408_PRIVATE");
            api.Writes.ShouldBe(0);
            api.Reject = true;
            result = Run(api, "edit", "CARD-0001", "--title", "updated", "--reason", "correction");
            result.Code.ShouldNotBe(0);
            result.Text.ShouldContain("privateNotes: too long");
            result.Text.ShouldContain("ConcurrencyToken: required");
        }
        finally { File.Delete(file); }
    }

    [Test]
    [Arguments("working", "removal     pending: reconcile previously exported working files")]
    [Arguments("git", "removal     pending: working files removed; Git index/HEAD cleanup required")]
    [Arguments("unknown", "removal     pending: cleanup state unavailable; erasure not confirmed")]
    [Arguments("staged", "removal     pending: staged private export; reconcile to unstage")]
    public void Pending_residue_has_distinct_truthful_output(string pending, string line)
    {
        var api = new Api { Pending = pending };
        var run = Run(api, "new", "--board", Board, "--title", "public");
        run.Code.ShouldBe(0);
        run.Text.ShouldContain(line);
    }

    [Test]
    [Arguments("new-clear")]
    [Arguments("file-clear")]
    [Arguments("missing-file")]
    [Arguments("inline")]
    [Arguments("enum")]
    public void Invalid_note_arguments_refuse_before_mutation(string variant)
    {
        var api = new Api();
        var args = variant switch {
            "new-clear" => new[] { "new", "--board", Board, "--title", "public", "--clear-private-notes" },
            "file-clear" => new[] { "edit", "CARD-0001", "--reason", "synthetic", "--private-notes-file", "missing", "--clear-private-notes" },
            "missing-file" => new[] { "new", "--board", Board, "--title", "public", "--private-notes-file", "c408-does-not-exist" },
            "inline" => new[] { "new", "--board", Board, "--title", "public", "--private-notes", "C408_INLINE_NOTE" },
            _ => new[] { "new", "--board", Board, "--title", "public", "--card-file-visibility", "Invalid" }
        };
        Run(api, args).Code.ShouldNotBe(0); api.Writes.ShouldBe(0);
    }

    [Test]
    public void Empty_file_clears_and_stale_explicit_token_is_not_retried()
    {
        var api = new Api(); var file = Path.GetTempFileName();
        try
        {
            var empty = Run(api, "edit", "CARD-0001", "--reason", "empty file", "--private-notes-file", file);
            empty.Code.ShouldBe(0); api.Body.GetProperty("privateNotes").GetString().ShouldBeEmpty();
            api.Conflict = true;
            var failed = Run(api, "edit", "CARD-0001", "--reason", "stale", "--token", "stale-token", "--title", "changed");
            failed.Code.ShouldNotBe(0); api.Writes.ShouldBe(2);
            api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("stale-token");
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void Old_server_status_is_unavailable_and_zero_interval_means_manual_sync()
    {
        var old = new Api { OmitStatus = true };
        var legacy = Run(old, "new", "--board", Board, "--title", "public");
        legacy.Code.ShouldBe(0); legacy.Text.ShouldContain("status unavailable; export safety not confirmed"); old.Writes.ShouldBe(1);
        var manual = new Api("Private", true, null) { IntervalSeconds = 0 };
        var current = Run(manual, "new", "--board", Board, "--title", "public");
        current.Code.ShouldBe(0); current.Text.ShouldContain("ELIGIBLE: manual sync"); manual.Writes.ShouldBe(1);
    }

    private static (int Code, string Text) Run(Api api, params string[] args)
    {
        var run = CardCommandRun.Run(api.Transport, args, taskToken: "");
        return (run.Exit, run.Output + run.Error);
    }

    private sealed class Api(string visibility = "Private", bool eligible = false, string? reason = "board_not_opted_in")
    {
        public int Writes { get; private set; }
        public JsonElement Body { get; private set; }
        public bool Reject { get; set; }
        public bool Conflict { get; set; }
        public bool OmitStatus { get; set; }
        public int IntervalSeconds { get; set; } = 60;
        public string? Pending { get; set; }

        public NightlyReportHttp Transport => (method, uri, _, body) =>
        {
            object response;
            var status = 200;
            if (new Uri(uri).AbsolutePath.EndsWith("/limits")) response = new { maxTitleLength = 300, maxDescriptionLength = 20000, maxReasonLength = 4000, maxActorLength = 200, maxPrivateNotesLength = 20000 };
            else
            {
                if (method is "POST" or "PATCH")
                {
                    Writes++;
                    Body = JsonDocument.Parse(body!).RootElement.Clone();
                }
                response = new { id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", boardId = Board, title = "public", identifier = "CARD-0001", status = "Backlog", importance = "Normal", urgency = "Normal", rank = 10, concurrencyToken = "fresh-token", revisionCount = 1,
                    cardFileStatus = new { eligible, reason, repositoryVisibility = visibility, repositoryPath = "C:\\src\\example", directory = "docs/cards/example", relativeFile = eligible ? "docs/cards/example/CARD-0001-public.md" : null, enabled = true, syncCardFiles = eligible, autoCommit = false, intervalSeconds = IntervalSeconds, cardFileVisibility = "Inherit", workingTreeRemovalPending = Pending == "unknown" ? (bool?)null : Pending == "working", gitRemovalPending = Pending == "unknown" ? (bool?)null : Pending is "git" or "staged" or "working", warnings = Pending == "staged" ? new[] { "card_file_staged_private_residue" } : [] } };
                if (Reject && method == "PATCH") { status = 422; response = new { detail = "Validation failed", code = "validation_failed", errors = new Dictionary<string, string[]> { ["privateNotes"] = ["too long"], ["ConcurrencyToken"] = ["required"] } }; }
            }
            if (Conflict && method == "PATCH") { status = 409; response = new { detail = "Stale token", code = "conflict" }; }
            if (OmitStatus) { var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(response))!.AsObject(); node.Remove("cardFileStatus"); response = node; }
            return new NightlyReportHttpResponse(status, JsonSerializer.Serialize(response));
        };
    }
}
