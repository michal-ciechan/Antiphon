using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The stale Claude Code session cleanup (Antiphon.Nightly.ClaudeSessionCleanup, the body of scripts/cleanup-claude-sessions.ps1
/// until unit 22 of the 2026-11-22 plan) in process. T1-T12 are the rows of the deleted driver scripts/test-cleanup-claude-sessions.ps1:
/// the captured list (scripts/fixtures/claude-sessions-2026-08-22.json) and live bridges (claude-sessions-live-bridges.json) at the
/// pinned now, through --input-json or a recording HTTP seam. No request leaves the process, no credentials or session files of this
/// machine are read (the user profile is a temporary directory), and no process is looked up (the live-process seam is the test's).
/// </summary>
[Category("Unit")]
public sealed class ClaudeSessionCleanupTests
{
    private const string NowPin = "2026-08-22T14:14:00Z";
    private const string LeakToken = "tok_T12_LEAKTEST_9f3a1c7e_NOT_A_REAL_SECRET";
    private const string ListUri = "https://api.anthropic.com/v1/code/sessions?statuses=active&statuses=paused&limit=100";

    private static readonly string Fixture = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "claude-sessions-2026-08-22.json");
    private static readonly string Bridges = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "claude-sessions-live-bridges.json");

    private static readonly string[] ExpectedCandidates =
    [
        "cse_01A1bGtpG1reU5iCwWMwkwyj", "cse_01LtSACyrcManQGiQPbjRynd", "cse_01Ht9GkXxoRJdax9a3w5KTzx", "cse_012tkQGM6m8ZJf86yTmguEsb",
        "cse_019hZwrMDNbF142zVeiAMsZ7", "cse_01DWKb4PB87hjqG67iKxxYaP", "cse_01AYbKg5uH3Mk635MmvFekcq", "cse_01CwamtCHtqhpxnLkvYvuCTs",
        "cse_01C5ZCwnjajAL6U18ioBHMoe",
    ];

    private static readonly string[] C6Ids = ["cse_01VPMZ8VJbzp4D5EPA4v7Wxq", "cse_01U7SPMfVBcuuFmPq8sgQHPW"];

    [Test]
    public async Task T1_to_T3_the_captured_list_splits_nine_and_nine_and_C6_holds_without_the_disconnected_narrowing()
    {
        using var work = new Work();
        var t1 = Run(work, new CodeSessionsApi(), "--input-json", Fixture, "--live-bridges-json", Bridges);
        t1.ExitCode.ShouldBe(0, "T1 exit 0");
        t1.CandidateCount.ShouldBe(9, "T1 candidate count 9");
        t1.SkipCount.ShouldBe(9, "T1 skip count 9");
        t1.CandidateIds.ShouldBe(ExpectedCandidates, ignoreOrder: true, "T1 candidate ids match section 2.2, no extra candidates");

        var t2 = Run(work, new CodeSessionsApi(), "--input-json", Fixture, "--live-bridges-json", Bridges, "--require-disconnected", "false");
        t2.ExitCode.ShouldBe(0, "T2 exit 0");
        t2.CandidateCount.ShouldBe(9, "T2 still 9 candidates with RequireDisconnected false");
        C6Ids.ShouldAllBe(id => t2.SkipIds.Contains(id), "T2 C6 skips cse_01VPMZ8 and cse_01U7SPM");
        var t2Report = Report(t2);
        C6Ids.Select(id => Skipped(t2Report, id).GetProperty("reason").GetString()).ShouldAllBe(reason => reason!.Contains("LOCAL-LIVE"), "T2 skip reason is LOCAL-LIVE");
        t2Report.GetProperty("requireDisconnected").GetBoolean().ShouldBeFalse();
        t2Report.GetProperty("liveBridgeCount").GetInt32().ShouldBe(7);

        t1.SkipIds.ShouldContain("cse_0127JHMVHrqYtq7qW23b9osH", "T3 cse_0127JHMV skipped (fresh disconnected)");
        Skipped(Report(t1), "cse_0127JHMVHrqYtq7qW23b9osH").GetProperty("reason").GetString()!.Contains("fresh").ShouldBeTrue("T3 reason is fresh");
    }

    [Test]
    public void T4_T5_a_working_bucket_and_an_archived_row_are_never_candidates()
    {
        using var work = new Work();
        var t4 = Run(work, new CodeSessionsApi(), "--input-json", work.List(FixtureRows().Append(Row("cse_01SYNTHWORKING00000000001", title: "synthetic-working", bucket: "working"))),
            "--live-bridges-json", Bridges);
        t4.SkipIds.ShouldContain("cse_01SYNTHWORKING00000000001", "T4 working bucket skipped even when old+disconnected");
        t4.CandidateIds.ShouldNotContain("cse_01SYNTHWORKING00000000001", "T4 working row is not a candidate");

        var t5 = Run(work, new CodeSessionsApi(), "--input-json", work.List(FixtureRows().Append(Row("cse_01SYNTHARCHIVED0000000001", title: "synthetic-archived", status: "archived"))),
            "--live-bridges-json", Bridges);
        t5.CandidateIds.ShouldNotContain("cse_01SYNTHARCHIVED0000000001", "T5 archived row is not a candidate");
        t5.SkipIds.ShouldContain("cse_01SYNTHARCHIVED0000000001", "T5 archived row is skipped");
    }

    [Test]
    public void T6_a_dry_run_and_a_saved_list_send_nothing_even_with_execute()
    {
        using var work = new Work();
        var api = new CodeSessionsApi();
        var t6 = Run(work, api, "--input-json", Fixture, "--live-bridges-json", Bridges);
        t6.ExitCode.ShouldBe(0, "T6 dry-run exit 0");
        api.Calls.Count.ShouldBe(0, "T6 HttpShim records 0 requests (dry run / InputJson)");
        api.Mutating.Count().ShouldBe(0, "T6 zero mutating calls");
        t6.Mutations.Count.ShouldBe(0, "T6 PassThru mutations empty");

        var output = new StringWriter();
        var forced = Run(work, api, output, _ => null, "--input-json", Fixture, "--live-bridges-json", Bridges, "--execute");
        forced.ExitCode.ShouldBe(0);
        api.Calls.ShouldBeEmpty();
        output.ToString().ShouldContain("--input-json is set; --execute is ignored (dry run only).");
        output.ToString().ShouldContain("candidates=9 skipped=9 liveBridges=7 execute=False action=Archive requireDisconnected=True");
        Report(forced).GetProperty("execute").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public void T7_more_candidates_than_the_cap_refuses_without_a_mutation()
    {
        using var work = new Work();
        var api = new CodeSessionsApi { List = Body(FixtureRows().Append(Row("cse_01SYNTHCANDIDATE000000001", title: "extra-1")).Append(Row("cse_01SYNTHCANDIDATE000000002", title: "extra-2"))) };
        var output = new StringWriter();
        var t7 = Run(work, api, output, _ => null, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400), "--execute", "--max-per-run", "10",
            "--action", "Archive");
        t7.ExitCode.ShouldBe(4, "T7 exit 4 when 11 candidates exceed MaxPerRun 10");
        t7.CandidateCount.ShouldBe(11, "T7 classified 11 candidates");
        api.Mutating.Count().ShouldBe(0, "T7 zero mutating calls (cap refuses, does not truncate)");
        output.ToString().ShouldContain("Refusing to Archive 11 candidates; --max-per-run is 10. The cap refuses, it does not truncate.");
        Report(t7).GetProperty("exitCode").GetInt32().ShouldBe(4);
    }

    [Test]
    public void T8_archive_posts_once_per_candidate_and_delete_deletes_once_per_candidate()
    {
        using var work = new Work();
        var api = new CodeSessionsApi { List = Body(FixtureRows()) };
        var t8 = Run(work, api, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400, LeakToken), "--execute", "--action", "Archive");
        t8.ExitCode.ShouldBe(0, "T8 archive exit 0");
        var posts = api.Calls.Where(call => call.Method == "POST").ToArray();
        posts.Length.ShouldBe(9, "T8 exactly 9 POST archive calls");
        api.Calls.Count(call => call.Method == "DELETE").ShouldBe(0, "T8 never DELETE when Action=Archive");
        posts.ShouldAllBe(call => System.Text.RegularExpressions.Regex.IsMatch(call.Uri, "/v1/code/sessions/cse_[^/]+/archive$"), "T8 POST uri is /v1/code/sessions/{id}/archive");
        posts.Select(call => call.Uri.Split('/')[^2]).ShouldBe(ExpectedCandidates, ignoreOrder: true, "T8 one POST per candidate");
        t8.Mutations.ShouldAllBe(mutation => mutation.Ok && !mutation.AlreadyGone && mutation.Method == "POST" && mutation.Action == "Archive" && mutation.StatusCode == 200);

        // The list request and every mutation carry the token as the Authorization header (and nowhere else).
        api.Calls[0].Method.ShouldBe("GET");
        api.Calls[0].Uri.ShouldBe(ListUri);
        api.Calls.ShouldAllBe(call => call.Headers["Authorization"] == "Bearer " + LeakToken && call.Headers["anthropic-version"] == "2023-06-01"
            && call.Headers["Accept"] == "application/json");

        var deleteApi = new CodeSessionsApi { List = Body(FixtureRows()) };
        var t8d = Run(work, deleteApi, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400), "--execute", "--action", "Delete");
        t8d.ExitCode.ShouldBe(0, "T8 delete path exit 0");
        var deletes = deleteApi.Calls.Where(call => call.Method == "DELETE").ToArray();
        deletes.Length.ShouldBe(9, "T8 Delete issues exactly 9 DELETE calls");
        deleteApi.Calls.Count(call => call.Method == "POST").ShouldBe(0, "T8 Delete never POSTs archive");
        deletes.ShouldAllBe(call => System.Text.RegularExpressions.Regex.IsMatch(call.Uri, "/v1/code/sessions/cse_[^/]+$") && !call.Uri.Contains("/archive"),
            "T8 DELETE uri is /v1/code/sessions/{id}");
    }

    [Test]
    public void T9_already_gone_is_success_and_a_failure_does_not_abort_the_rest()
    {
        using var work = new Work();
        const string goneId = "cse_01A1bGtpG1reU5iCwWMwkwyj";
        var api = new CodeSessionsApi
        {
            List = Body(FixtureRows()),
            Mutation = uri => uri.Contains(goneId) ? new NightlyReportHttpResponse(404, """{"type":"not_found_error","resource_type":"session"}""") : null,
        };
        var output = new StringWriter();
        var t9 = Run(work, api, output, _ => null, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400), "--execute", "--action", "Archive");
        t9.ExitCode.ShouldBe(0, "T9 404 not_found_error treated as success (exit 0)");
        api.Mutating.Count().ShouldBe(9, "T9 still attempted every candidate after a 404");
        var gone = t9.Mutations.Single(mutation => mutation.Id == goneId);
        (gone.Ok && gone.AlreadyGone).ShouldBeTrue("T9 404 marked alreadyGone");
        output.ToString().ShouldContain("Archive " + goneId + " already gone (404 not_found_error) - treating as success");

        // A not_found_error body without a 404 is already gone too; a transport failure is a failure.
        const string failId = "cse_01LtSACyrcManQGiQPbjRynd";
        var failing = new CodeSessionsApi
        {
            List = Body(FixtureRows()),
            Mutation = uri =>
            {
                if (uri.Contains(failId)) return new NightlyReportHttpResponse(500, """{"type":"api_error"}""");
                if (uri.Contains(goneId)) return new NightlyReportHttpResponse(400, """{"type":"not_found_error"}""");
                if (uri.Contains("cse_01Ht9GkXxoRJdax9a3w5KTzx")) throw new HttpRequestException("connection reset");
                return null;
            },
        };
        var failedOutput = new StringWriter();
        var t9e = Run(work, failing, failedOutput, _ => null, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400), "--execute", "--action", "Archive");
        t9e.ExitCode.ShouldBe(5, "T9b non-404 failure yields exit 5");
        failing.Mutating.Count().ShouldBe(9, "T9b remaining candidates still attempted after a 500");
        t9e.Mutations.Single(mutation => mutation.Id == goneId).AlreadyGone.ShouldBeTrue();
        t9e.Mutations.Count(mutation => !mutation.Ok).ShouldBe(2);
        failedOutput.ToString().ShouldContain("Archive " + failId + " FAILED status=500 {\"type\":\"api_error\"}");
        failedOutput.ToString().ShouldContain("Archive cse_01Ht9GkXxoRJdax9a3w5KTzx FAILED status=0 connection reset");
        failedOutput.ToString().ShouldContain("2 mutation(s) failed.");
    }

    [Test]
    public void T10_expired_credentials_exit_2_without_a_request()
    {
        using var work = new Work();
        var api = new CodeSessionsApi();
        var output = new StringWriter();
        var t10 = Run(work, api, output, _ => null, "--credentials-path", work.Credentials(-300));
        t10.ExitCode.ShouldBe(2, "T10 expired expiresAt => exit 2");
        api.Calls.Count.ShouldBe(0, "T10 zero HTTP calls");
        output.ToString().ShouldContain("CLI credentials stale - run/refresh Claude Code on this machine.");
        Report(t10).GetProperty("exitCode").GetInt32().ShouldBe(2);

        // Within 60 s of expiry is stale; epoch seconds and a timestamp are read too.
        Run(work, api, "--credentials-path", work.Credentials(30)).ExitCode.ShouldBe(2);
        var seconds = work.CredentialsJson("""{"claudeAiOauth":{"accessToken":"t","expiresAt":1755872040}}""");
        Run(work, new CodeSessionsApi(), "--credentials-path", seconds).ExitCode.ShouldBe(2, "2025-08-22 in epoch seconds is stale at the pin");
        var future = work.CredentialsJson("""{"claudeAiOauth":{"accessToken":"t","expiresAt":"2026-08-23T00:00:00Z"}}""");
        Run(work, new CodeSessionsApi(), "--credentials-path", future, "--live-bridges-json", Bridges).ExitCode.ShouldBe(0, "a timestamp a day ahead is fresh");

        var missing = new StringWriter();
        Run(work, api, missing, _ => null, "--credentials-path", Path.Combine(work.Root, "absent.json")).ExitCode.ShouldBe(2);
        missing.ToString().ShouldContain("CLI credentials missing - run/refresh Claude Code on this machine.");
        var unreadable = new StringWriter();
        Run(work, api, unreadable, _ => null, "--credentials-path", work.CredentialsJson("{not json")).ExitCode.ShouldBe(2);
        unreadable.ToString().ShouldContain("CLI credentials unreadable - run/refresh Claude Code on this machine.");
        Run(work, api, "--credentials-path", work.CredentialsJson("""{"claudeAiOauth":{"expiresAt":1787443240000}}""")).ExitCode.ShouldBe(2, "no access token");
        api.Calls.ShouldBeEmpty();
    }

    [Test]
    public void T11_a_list_of_the_page_size_is_truncated_and_never_swept()
    {
        using var work = new Work();
        var api = new CodeSessionsApi();
        var rows = Enumerable.Range(0, 100).Select(i => Row("cse_01TRUNCATED00000000000" + i.ToString("000"), title: "truncated-" + i.ToString("000")));
        var t11 = Run(work, api, "--input-json", work.List(rows), "--live-bridges-json", Bridges);
        t11.ExitCode.ShouldBe(3, "T11 data.Count >= limit => exit 3");
        api.Calls.Count.ShouldBe(0, "T11 zero HTTP calls (InputJson truncated before mutate)");
        t11.Truncated.ShouldBeTrue("T11 truncated flag set");
        api.Mutating.Count().ShouldBe(0, "T11 zero mutating calls");

        // The fetched list is held to the same page size, even under --execute.
        var fetched = new CodeSessionsApi { List = Body(rows) };
        var output = new StringWriter();
        var live = Run(work, fetched, output, _ => null, "--credentials-path", work.Credentials(86400), "--live-bridges-json", Bridges, "--execute");
        live.ExitCode.ShouldBe(3);
        fetched.Mutating.ShouldBeEmpty();
        output.ToString().ShouldContain("WARNING: session list is truncated (data.Count=100 >= limit=100). Refusing to sweep a partial view.");
    }

    [Test]
    public void T12_the_report_never_holds_the_access_token()
    {
        using var work = new Work();
        var api = new CodeSessionsApi { List = Body(FixtureRows()) };
        var output = new StringWriter();
        var t12 = Run(work, api, output, _ => null, "--live-bridges-json", Bridges, "--credentials-path", work.Credentials(86400, LeakToken));
        t12.ExitCode.ShouldBe(0, "T12 report-only with live token in memory exits 0");
        var reports = Directory.GetFiles(Path.GetDirectoryName(t12.ReportPath.ShouldNotBeNull())!, "*.json");
        reports.Length.ShouldBeGreaterThanOrEqualTo(1, "T12 wrote a report file");
        reports.Where(file => File.ReadAllText(file).Contains(LeakToken)).ShouldBeEmpty("T12 report JSON contains no substring of the access token");
        output.ToString().ShouldNotContain(LeakToken);
        api.Mutating.ShouldBeEmpty();
    }

    [Test]
    public void C6_live_bridges_come_from_the_session_files_of_live_claude_and_node_processes()
    {
        using var work = new Work();
        var sessions = Directory.CreateDirectory(Path.Combine(work.Root, "sessions")).FullName;
        // A claude pid named in the file, a node pid named by the file name, a pwsh pid, a dead pid, a file without a bridge and a broken file.
        File.WriteAllText(Path.Combine(sessions, "a.json"), """{"pid":1006,"bridgeSessionId":"session_01VPMZ8VJbzp4D5EPA4v7Wxq"}""");
        File.WriteAllText(Path.Combine(sessions, "1007.json"), """{"bridgeSessionId":"session_01U7SPMfVBcuuFmPq8sgQHPW"}""");
        File.WriteAllText(Path.Combine(sessions, "b.json"), """{"pid":1008,"bridgeSessionId":"session_01A1bGtpG1reU5iCwWMwkwyj"}""");
        File.WriteAllText(Path.Combine(sessions, "c.json"), """{"pid":1009,"bridgeSessionId":"session_01LtSACyrcManQGiQPbjRynd"}""");
        File.WriteAllText(Path.Combine(sessions, "d.json"), """{"pid":1010}""");
        File.WriteAllText(Path.Combine(sessions, "e.json"), "{broken");
        var names = new Dictionary<int, string> { [1006] = "claude", [1007] = "node", [1008] = "pwsh", [1010] = "claude" };
        var asked = new List<int>();
        string? Name(int pid)
        {
            asked.Add(pid);
            return names.TryGetValue(pid, out var name) ? name : null;
        }

        var result = Run(work, new CodeSessionsApi(), new StringWriter(), Name, "--input-json", Fixture, "--sessions-dir", sessions, "--require-disconnected", "false");
        result.ExitCode.ShouldBe(0);
        C6Ids.ShouldAllBe(id => result.SkipIds.Contains(id));
        C6Ids.Select(id => Skipped(Report(result), id).GetProperty("reason").GetString()).ShouldAllBe(reason => reason!.Contains("LOCAL-LIVE"));
        result.CandidateIds.ShouldContain("cse_01A1bGtpG1reU5iCwWMwkwyj", "a pwsh pid is not a live Claude bridge");
        result.CandidateIds.ShouldContain("cse_01LtSACyrcManQGiQPbjRynd", "a dead pid is not a live bridge");
        Report(result).GetProperty("liveBridgeCount").GetInt32().ShouldBe(2);
        asked.ShouldBe([1006, 1007, 1008, 1009], ignoreOrder: true);

        // An absent sessions directory has no live bridges; a named bridges file that does not exist stops the run without a report.
        Run(work, new CodeSessionsApi(), "--input-json", Fixture, "--sessions-dir", Path.Combine(work.Root, "absent")).ExitCode.ShouldBe(0);
        var stopped = Run(work, new CodeSessionsApi(), "--input-json", Fixture, "--live-bridges-json", Path.Combine(work.Root, "absent.json"));
        stopped.ExitCode.ShouldBe(1);
        stopped.ReportPath.ShouldBeNull();
    }

    [Test]
    public void The_list_read_failing_exits_1_with_a_report()
    {
        using var work = new Work();
        var api = new CodeSessionsApi { ListAnswer = new NightlyReportHttpResponse(503, "unavailable") };
        var output = new StringWriter();
        var result = Run(work, api, output, _ => null, "--credentials-path", work.Credentials(86400), "--live-bridges-json", Bridges);
        result.ExitCode.ShouldBe(1);
        output.ToString().ShouldContain("GET /v1/code/sessions failed (status 503).");
        Report(result).GetProperty("exitCode").GetInt32().ShouldBe(1);

        var missing = new StringWriter();
        Run(work, new CodeSessionsApi(), missing, _ => null, "--input-json", Path.Combine(work.Root, "absent.json")).ExitCode.ShouldBe(1);
        missing.ToString().ShouldContain("--input-json not found: ");
    }

    [Test]
    public void Options_defaults_and_usage_errors()
    {
        var profile = Path.Combine(Path.GetTempPath(), "profile");
        var repo = Path.Combine(Path.GetTempPath(), "repo");
        var defaults = ClaudeSessionCleanupOptions.Parse([], profile, repo);
        (defaults.OlderThanDays, defaults.Action, defaults.Execute, defaults.MaxPerRun, defaults.SkipUnread, defaults.RequireDisconnected).ShouldBe((7, "Archive", false, 10, false, true));
        defaults.ReportPath.ShouldBe(Path.Combine(repo, "logs", "claude-session-cleanup"));
        defaults.SessionsDir.ShouldBe(Path.Combine(profile, ".claude", "sessions"));
        defaults.CredentialsPath.ShouldBe(Path.Combine(profile, ".claude", ".credentials.json"));
        defaults.Now.ShouldBeNull();
        ClaudeSessionCleanupOptions.Parse(["--action", "delete", "--require-disconnected", "False", "--skip-unread"], profile, repo).Action.ShouldBe("Delete");
        ClaudeSessionCleanupOptions.Parse(["--require-disconnected", "False"], profile, repo).RequireDisconnected.ShouldBeFalse();
        Should.Throw<NightlyUsageException>(() => ClaudeSessionCleanupOptions.Parse(["--action", "Purge"], profile, repo));
        Should.Throw<NightlyUsageException>(() => ClaudeSessionCleanupOptions.Parse(["--require-disconnected", "no"], profile, repo));
        Should.Throw<NightlyUsageException>(() => ClaudeSessionCleanupOptions.Parse(["--now", "not a time"], profile, repo));
        Should.Throw<NightlyUsageException>(() => ClaudeSessionCleanupOptions.Parse(["--older-than-days", "seven"], profile, repo));
        ClaudeSessionCleanup.Suffix("cse_01ABC").ShouldBe("01ABC");
        ClaudeSessionCleanup.Suffix("session_01ABC").ShouldBe("01ABC");
        ClaudeSessionCleanup.Suffix("cse_").ShouldBe("cse_");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "ClaudeSessionCleanup.cs")).Count(b => b > 127)
            .ShouldBe(0, "the cleanup source is ASCII-only");
    }

    private static ClaudeSessionCleanupResult Run(Work work, CodeSessionsApi api, params string[] args) => Run(work, api, new StringWriter(), _ => null, args);

    private static ClaudeSessionCleanupResult Run(Work work, CodeSessionsApi api, StringWriter output, Func<int, string?> processName, params string[] args)
    {
        var reports = Path.Combine(work.Reports, Guid.NewGuid().ToString("N"));
        var options = ClaudeSessionCleanupOptions.Parse(["--now", NowPin, "--report-path", reports, .. args], work.Profile, work.Root);
        var host = NightlyHost.Create(null, new RecordingLauncher(), output, output, null, null, null, null);
        return ClaudeSessionCleanup.Run(options, host, new System.IO.Abstractions.FileSystem(), api.Send, pid => processName(pid)!);
    }

    private static JsonElement Report(ClaudeSessionCleanupResult result)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(result.ReportPath.ShouldNotBeNull()));
        return document.RootElement.Clone();
    }

    private static JsonElement Skipped(JsonElement report, string id) =>
        report.GetProperty("skipped").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);

    private static IEnumerable<JsonNode> FixtureRows() => JsonNode.Parse(File.ReadAllText(Fixture))!["data"]!.AsArray().Select(row => row!.DeepClone());

    private static JsonObject Row(string id, string title = "synthetic", string status = "active", string connection = "disconnected", string bucket = "review_ready",
        string worker = "idle", string lastEvent = "2026-08-01T00:00:00Z", bool unread = false) => new()
    {
        ["id"] = id, ["title"] = title, ["status"] = status, ["connection_status"] = connection, ["status_bucket"] = bucket, ["worker_status"] = worker,
        ["created_at"] = "2026-07-01T00:00:00Z", ["last_event_at"] = lastEvent, ["unread"] = unread, ["tags"] = new JsonArray("remote-control-repl"),
    };

    private static JsonObject Body(IEnumerable<JsonNode> rows) => new() { ["data"] = new JsonArray(rows.Select(row => row.DeepClone()).ToArray()), ["resume_token"] = "synthetic-not-a-secret" };

    /// <summary>The code-sessions API: GET answers the list, every other request a mutation answer; each request is recorded.</summary>
    private sealed class CodeSessionsApi
    {
        public List<(string Method, string Uri, IReadOnlyDictionary<string, string> Headers)> Calls { get; } = [];
        public JsonObject? List { get; init; }
        public NightlyReportHttpResponse? ListAnswer { get; init; }
        public Func<string, NightlyReportHttpResponse?>? Mutation { get; init; }

        public IEnumerable<(string Method, string Uri, IReadOnlyDictionary<string, string> Headers)> Mutating =>
            Calls.Where(call => call.Method is "POST" or "DELETE" or "PUT" or "PATCH");

        public NightlyReportHttpResponse Send(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            Calls.Add((method, uri, new Dictionary<string, string>(headers)));
            if (method == "GET") return ListAnswer ?? new NightlyReportHttpResponse(200, (List ?? new JsonObject { ["data"] = new JsonArray() }).ToJsonString());
            return Mutation?.Invoke(uri) ?? new NightlyReportHttpResponse(200, """{"ok":true}""");
        }
    }

    private sealed class Work : IDisposable
    {
        public Work()
        {
            Root = Directory.CreateTempSubdirectory("claude-cleanup-test-").FullName;
            Directory.CreateDirectory(Profile);
        }

        public string Root { get; }
        public string Reports => Path.Combine(Root, "reports");
        public string Profile => Path.Combine(Root, "profile");

        /// <summary>New-TestCreds: an access token expiring offsetSeconds after the pinned now (epoch milliseconds).</summary>
        public string Credentials(int offsetSeconds, string token = "TEST_TOKEN_PLACEHOLDER_NOT_REAL")
        {
            var expires = DateTimeOffset.Parse(NowPin).ToUnixTimeMilliseconds() + offsetSeconds * 1000L;
            return CredentialsJson(new JsonObject
            {
                ["claudeAiOauth"] = new JsonObject
                {
                    ["accessToken"] = token, ["refreshToken"] = "refresh_unused_not_real", ["expiresAt"] = expires, ["refreshTokenExpiresAt"] = expires + 86400000,
                    ["subscriptionType"] = "pro",
                },
            }.ToJsonString());
        }

        public string CredentialsJson(string text)
        {
            var path = Path.Combine(Root, "credentials-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>Save-ListJson: a saved list body of the rows.</summary>
        public string List(IEnumerable<JsonNode> rows)
        {
            var path = Path.Combine(Root, "input-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, Body(rows).ToJsonString());
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
