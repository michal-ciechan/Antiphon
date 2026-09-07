using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardFilePrivacyScriptTests
{
    private const string Board = "22222222-2222-2222-2222-222222222222";
    [Test]
    [Arguments("Unknown", false, "board_not_opted_in")]
    [Arguments("Public", false, "board_not_opted_in")]
    [Arguments("Private", true, null)]
    public async Task New_reports_eligibility_target_and_visibility_without_claiming_written(string visibility, bool eligible, string? reason)
    {
        using var api = new Api(visibility, eligible, reason);
        var run = await Run(api, "new", "-Board", Board, "-Title", "public");
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
        using var api = new Api();
        var file = Path.GetTempFileName();
        const string notes = "  C408_SCRIPT_PRIVATE\n雪 `$() <img>  ";
        try
        {
            await File.WriteAllTextAsync(file, notes, new UTF8Encoding(false));
            var args = verb == "new" ? new[] { "new", "-Board", Board, "-Title", "public" }
                : new[] { "edit", "CARD-0001", "-Reason", "correction" };
            var run = await Run(api, args.Concat(["-PrivateNotesFile", file, "-CardFileVisibility", "Private", "-Json"]).ToArray());
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
    public async Task Clear_notes_and_omission_preserve_token_contract()
    {
        using var api = new Api();
        (await Run(api, "edit", "CARD-0001", "-Reason", "clear", "-ClearPrivateNotes", "-Token", "strict-token")).Code.ShouldBe(0);
        api.Body.GetProperty("privateNotes").GetString().ShouldBeEmpty();
        api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("strict-token");
        (await Run(api, "edit", "CARD-0001", "-Reason", "title", "-Title", "updated")).Code.ShouldBe(0);
        api.Body.TryGetProperty("privateNotes", out _).ShouldBeFalse();
        api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("fresh-token");
    }

    [Test]
    public async Task Oversized_notes_fail_locally_without_echo_and_mixed_case_server_errors_survive()
    {
        using var api = new Api();
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "C408_PRIVATE" + new string('x', 20001));
            var result = await Run(api, "new", "-Board", Board, "-Title", "public", "-PrivateNotesFile", file);
            result.Code.ShouldNotBe(0);
            result.Text.ShouldContain("limit 20000");
            result.Text.ShouldNotContain("C408_PRIVATE");
            api.Writes.ShouldBe(0);
            api.Reject = true;
            result = await Run(api, "edit", "CARD-0001", "-Title", "updated", "-Reason", "correction");
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
    public async Task Pending_residue_has_distinct_truthful_output(string pending, string line)
    {
        using var api = new Api { Pending = pending };
        var run = await Run(api, "new", "-Board", Board, "-Title", "public");
        run.Code.ShouldBe(0);
        run.Text.ShouldContain(line);
    }

    [Test]
    [Arguments("new-clear")]
    [Arguments("file-clear")]
    [Arguments("missing-file")]
    [Arguments("inline")]
    [Arguments("enum")]
    public async Task Invalid_note_arguments_refuse_before_mutation(string variant)
    {
        using var api = new Api();
        var args = variant switch {
            "new-clear" => new[] { "new", "-Board", Board, "-Title", "public", "-ClearPrivateNotes" },
            "file-clear" => new[] { "edit", "CARD-0001", "-Reason", "synthetic", "-PrivateNotesFile", "missing", "-ClearPrivateNotes" },
            "missing-file" => new[] { "new", "-Board", Board, "-Title", "public", "-PrivateNotesFile", "c408-does-not-exist" },
            "inline" => new[] { "new", "-Board", Board, "-Title", "public", "-PrivateNotes", "C408_INLINE_NOTE" },
            _ => new[] { "new", "-Board", Board, "-Title", "public", "-CardFileVisibility", "Invalid" }
        };
        (await Run(api, args)).Code.ShouldNotBe(0); api.Writes.ShouldBe(0);
    }

    [Test]
    public async Task Empty_file_clears_and_stale_explicit_token_is_not_retried()
    {
        using var api = new Api(); var file = Path.GetTempFileName();
        try
        {
            var empty = await Run(api, "edit", "CARD-0001", "-Reason", "empty file", "-PrivateNotesFile", file);
            empty.Code.ShouldBe(0); api.Body.GetProperty("privateNotes").GetString().ShouldBeEmpty();
            api.Conflict = true;
            var failed = await Run(api, "edit", "CARD-0001", "-Reason", "stale", "-Token", "stale-token", "-Title", "changed");
            failed.Code.ShouldNotBe(0); api.Writes.ShouldBe(2);
            api.Body.GetProperty("concurrencyToken").GetString().ShouldBe("stale-token");
        }
        finally { File.Delete(file); }
    }

    [Test]
    public async Task Old_server_status_is_unavailable_and_zero_interval_means_manual_sync()
    {
        using var old = new Api { OmitStatus = true };
        var legacy = await Run(old, "new", "-Board", Board, "-Title", "public");
        legacy.Code.ShouldBe(0); legacy.Text.ShouldContain("status unavailable; export safety not confirmed"); old.Writes.ShouldBe(1);
        using var manual = new Api("Private", true, null) { IntervalSeconds = 0 };
        var current = await Run(manual, "new", "-Board", Board, "-Title", "public");
        current.Code.ShouldBe(0); current.Text.ShouldContain("ELIGIBLE: manual sync"); manual.Writes.ShouldBe(1);
    }

    private static async Task<(int Code, string Text)> Run(Api api, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/card.ps1") }.Concat(args)) start.ArgumentList.Add(a);
        start.Environment["ANTIPHON_API"] = api.Url.TrimEnd('/');
        start.Environment["ANTIPHON_TASK_TOKEN"] = "";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class Api(string visibility = "Private", bool eligible = false, string? reason = "board_not_opted_in") : IDisposable
    {
        private readonly HttpListener _listener = new();
        private Task? _pump;
        private string? _url;
        public string Url { get { if (_url is null) { _url = EphemeralHttpListener.BindLoopback(_listener); _pump = Pump(); } return _url; } }
        public int Writes { get; private set; }
        public JsonElement Body { get; private set; }
        public bool Reject { get; set; }
        public bool Conflict { get; set; }
        public bool OmitStatus { get; set; }
        public int IntervalSeconds { get; set; } = 60;
        public string? Pending { get; set; }
        private async Task Pump()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await _listener.GetContextAsync(); } catch { return; }
                object response;
                if (c.Request.Url!.AbsolutePath.EndsWith("/limits")) response = new { maxTitleLength = 300, maxDescriptionLength = 20000, maxReasonLength = 4000, maxActorLength = 200, maxPrivateNotesLength = 20000 };
                else
                {
                    if (c.Request.HttpMethod is "POST" or "PATCH")
                    {
                        Writes++;
                        using var reader = new StreamReader(c.Request.InputStream, Encoding.UTF8);
                        Body = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement.Clone();
                    }
                    response = new { id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", boardId = Board, title = "public", identifier = "CARD-0001", status = "Backlog", importance = "Normal", urgency = "Normal", rank = 10, concurrencyToken = "fresh-token", revisionCount = 1,
                        cardFileStatus = new { eligible, reason, repositoryVisibility = visibility, repositoryPath = "C:\\src\\example", directory = "docs/cards/example", relativeFile = eligible ? "docs/cards/example/CARD-0001-public.md" : null, enabled = true, syncCardFiles = eligible, autoCommit = false, intervalSeconds = IntervalSeconds, cardFileVisibility = "Inherit", workingTreeRemovalPending = Pending == "unknown" ? (bool?)null : Pending == "working", gitRemovalPending = Pending == "unknown" ? (bool?)null : Pending is "git" or "staged" or "working", warnings = Pending == "staged" ? new[] { "card_file_staged_private_residue" } : [] } };
                    if (Reject && c.Request.HttpMethod == "PATCH") { c.Response.StatusCode = 422; response = new { detail = "Validation failed", code = "validation_failed", errors = new Dictionary<string, string[]> { ["privateNotes"] = ["too long"], ["ConcurrencyToken"] = ["required"] } }; }
                }
                if (Conflict && c.Request.HttpMethod == "PATCH") { c.Response.StatusCode = 409; response = new { detail = "Stale token", code = "conflict" }; }
                if (OmitStatus) { var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(response))!.AsObject(); node.Remove("cardFileStatus"); response = node; }
                c.Response.ContentType = "application/json";
                await c.Response.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(response));
                c.Response.Close();
            }
        }
        public void Dispose() { _listener.Close(); _pump?.GetAwaiter().GetResult(); }
    }
}
