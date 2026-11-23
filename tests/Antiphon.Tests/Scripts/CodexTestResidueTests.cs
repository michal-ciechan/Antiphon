using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The Codex test residue cleanup (Antiphon.Nightly.CodexTestResidue, the body of scripts/cleanup-codex-test-residue.ps1 until unit
/// 21 of the 2026-11-22 plan) in process. T1-T13 are the rows of the deleted driver scripts/test-cleanup-codex-test-residue.ps1:
/// a saved threads dump (scripts/fixtures/codex-test-residue-threads-2026-08-26.json) through --input-json, which can never
/// delete, and a real read-only python read of a WAL database held open by a writer. The E rows drive --execute on recording
/// seams (no codex is started). The live codex command lines are always the seams' (none), and the Codex home is private, so
/// nothing reads this machine's ~/.codex.
/// </summary>
[Category("Integration")]
public sealed class CodexTestResidueTests
{
    private const string NowPin = "2026-08-26T14:00:00Z";
    private static readonly string Temp = Path.GetTempPath().TrimEnd('\\', '/');

    [Test]
    public async Task T1_to_T10_the_saved_dump_classifies_exactly_and_never_deletes()
    {
        using var work = new Work();
        var input = await work.FixtureAsync();
        var seams = new Seams();

        var result = Run(work, seams, "--input-json", input);
        result.ExitCode.ShouldBe(0, "T1 dry run exit 0");
        result.CandidateCount.ShouldBe(5, "T1 exact candidate count");
        result.UnattributedCount.ShouldBe(4, "T1 exact unattributed count");
        result.SkipCount.ShouldBe(4, "T1 exact skipped count");
        result.CandidateIds.ShouldBe([
            "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333",
            "44444444-4444-4444-4444-444444444444", "dddddddd-dddd-dddd-dddd-dddddddddddd"], ignoreOrder: true, "T1 candidate ids exact");
        result.CandidateIds.ShouldNotContain("99999999-9999-9999-9999-999999999999", "T2 vscode never candidate");
        result.CandidateIds.ShouldNotContain("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "T3 normal delegate cwd never candidate");
        result.CandidateIds.ShouldNotContain("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "T4 fresh row never candidate");
        result.CandidateIds.ShouldNotContain("cccccccc-cccc-cccc-cccc-cccccccccccc", "T5 archived path never candidate");
        result.CandidateIds.ShouldContain("dddddddd-dddd-dddd-dddd-dddddddddddd", "T6 row is a candidate before a writer lock is supplied");
        result.UnattributedIds.ShouldBe(["55555555-5555-5555-5555-555555555555", "66666666-6666-6666-6666-666666666666",
            "77777777-7777-7777-7777-777777777777", "88888888-8888-8888-8888-888888888888"], ignoreOrder: true);

        var locks = Path.Combine(work.Root, "locks.json");
        await File.WriteAllTextAsync(locks, """["dddddddd-dddd-dddd-dddd-dddddddddddd"]""");
        var withLock = Run(work, seams, "--input-json", input, "--lock-ids-json", locks);
        withLock.CandidateCount.ShouldBe(4, "T7 writer lock excludes exact id");
        withLock.CandidateIds.ShouldNotContain("dddddddd-dddd-dddd-dddd-dddddddddddd", "T7 locked row never candidate");

        // A single JSON string (what ConvertTo-Json made of a one-item array) is one cwd.
        var live = Path.Combine(work.Root, "live.json");
        await File.WriteAllTextAsync(live, JsonSerializer.Serialize(Temp + "\\antiphon-codex-canaryabc"));
        var withLive = Run(work, seams, "--input-json", input, "--live-cwds-json", live);
        withLive.CandidateCount.ShouldBe(4, "T8 live codex cwd excludes exact id");
        withLive.CandidateIds.ShouldNotContain("11111111-1111-1111-1111-111111111111", "T8 live cwd never candidate");

        var output = new StringWriter();
        var forced = Run(work, seams, output, "--input-json", input, "--execute");
        forced.ExitCode.ShouldBe(0, "T9 InputJson forces execute off");
        forced.Mutations.Count.ShouldBe(0, "T9 fixture run makes no mutation");
        output.ToString().ShouldContain("--input-json is set; --execute is ignored (dry run only).");
        output.ToString().ShouldContain("candidates=5 unattributed=4 skipped=4 execute=False");
        seams.Deleted.ShouldBeEmpty();

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(forced.ReportPath!));
        report.RootElement.GetProperty("unattributed").GetArrayLength().ShouldBe(4, "T10 report has a separate unattributed section");
        report.RootElement.GetProperty("candidates").GetArrayLength().ShouldBe(5, "T10 report has allow-listed candidates");
        report.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(0);
        report.RootElement.GetProperty("execute").GetBoolean().ShouldBeFalse();
        // The allow-list: a candidate row carries no first user message; a probe row does.
        report.RootElement.GetProperty("candidates")[0].TryGetProperty("firstUserMessage", out _).ShouldBeFalse();
        report.RootElement.GetProperty("unattributed")[0].TryGetProperty("firstUserMessage", out _).ShouldBeTrue();
        report.RootElement.GetProperty("skipped").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "cccccccc-cccc-cccc-cccc-cccccccccccc")
            .GetProperty("reason").GetString().ShouldBe("archived, protected-rollout-NEVER");
    }

    [Test]
    public async Task T11_to_T13_a_read_only_reader_sees_the_WAL_deletion_and_the_source_is_ascii()
    {
        using var work = new Work();
        // Keep the writer connected after deleting the row so the deletion remains in the WAL. A read-only SQLite connection
        // must observe the WAL rather than return the stale base database.
        var db = Path.Combine(work.Root, "state_5.sqlite");
        var ready = Path.Combine(work.Root, "sqlite-writer-ready");
        var release = Path.Combine(work.Root, "sqlite-writer-release");
        var writer = Path.Combine(work.Root, "hold-wal-writer.py");
        await File.WriteAllTextAsync(writer, """
            import pathlib, sqlite3, sys, time
            db, ready, release, cwd = sys.argv[1:]
            con = sqlite3.connect(db)
            try:
                con.execute("PRAGMA journal_mode=WAL")
                con.execute("CREATE TABLE threads (id TEXT, cwd TEXT, source TEXT, model_provider TEXT, created_at TEXT, rollout_path TEXT, archived INTEGER, is_pinned INTEGER, first_user_message TEXT)")
                con.execute("INSERT INTO threads VALUES (?, ?, 'cli', 'openai', '2026-08-01T00:00:00Z', NULL, 0, 0, NULL)", ('wal-deleted-thread', cwd))
                con.commit()
                con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
                con.execute("DELETE FROM threads WHERE id = 'wal-deleted-thread'")
                con.commit()
                pathlib.Path(ready).touch()
                while not pathlib.Path(release).exists():
                    time.sleep(0.05)
            finally:
                con.close()
            """.Replace("\r\n", "\n"));
        var start = new ProcessStartInfo("python") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-I", writer, db, ready, release, Temp + "\\antiphon-codex-canary-wal-regression" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("python did not start");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(ready) && DateTime.UtcNow < deadline && !process.HasExited) await Task.Delay(50);
            File.Exists(ready).ShouldBeTrue("T11 WAL fixture writer started");

            // The production reader (python on the host's launcher); the live command lines stay the seams' none.
            var host = NightlyHost.Production(TextWriter.Null, TextWriter.Null);
            var seams = new Seams { Reader = CodexTestResidue.Production(host) };
            var walRead = Run(work, seams, "--state-db-path", db);
            walRead.ExitCode.ShouldBe(0, "T12 WAL database read exits 0");
            walRead.CandidateCount.ShouldBe(0, "T12 WAL deletion is visible to read-only connection");
            walRead.SkipCount.ShouldBe(0, "T12 no stale base-database row is classified");
        }
        finally
        {
            File.WriteAllText(release, "");
            if (!process.WaitForExit(10000)) process.Kill(entireProcessTree: true);
        }

        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "CodexTestResidue.cs"))
            .Count(b => b > 127).ShouldBe(0, "T13 cleanup source is ASCII-only");
    }

    [Test]
    public async Task E1_execute_deletes_each_candidate_through_codex_and_rechecks_the_row()
    {
        using var work = new Work();
        var seams = new Seams();
        seams.Rows.Add(Row("e1-old-cli", "antiphon-codex-canary-e1", "cli", 1755000000));
        seams.Rows.Add(Row("e1-old-exec", "antiphon-codex-roundtrip-e1", "exec", 1755000000000));
        seams.Rows.Add(Row("e1-desktop", "antiphon-codex-canary-e1d", "vscode", 1755000000));
        seams.Answer = id => ["Deleted session " + id];

        var output = new StringWriter();
        var result = Run(work, seams, output, "--state-db-path", await work.DatabaseAsync(), "--execute", "--codex-exe", "codex-fixture");
        result.ExitCode.ShouldBe(0, output.ToString());
        seams.Deleted.ShouldBe(["codex-fixture delete e1-old-cli", "codex-fixture delete e1-old-exec"], ignoreOrder: true);
        result.Mutations.ShouldAllBe(mutation => mutation.Ok && mutation.Action == "delete" && mutation.DeletedLine.StartsWith("Deleted session "));
        output.ToString().ShouldContain("delete e1-old-cli ok");
        output.ToString().ShouldContain("execute=True");
    }

    [Test]
    public async Task E2_more_candidates_than_the_cap_refuses_without_deleting()
    {
        using var work = new Work();
        var seams = new Seams();
        seams.Rows.Add(Row("e2-a", "antiphon-codex-canary-e2a", "cli", 1755000000));
        seams.Rows.Add(Row("e2-b", "antiphon-codex-canary-e2b", "cli", 1755000000));
        var output = new StringWriter();
        var result = Run(work, seams, output, "--state-db-path", await work.DatabaseAsync(), "--execute", "--max-per-run", "1");
        result.ExitCode.ShouldBe(4);
        output.ToString().ShouldContain("Refusing to delete 2 candidates; --max-per-run is 1. The cap refuses, it does not truncate.");
        seams.Deleted.ShouldBeEmpty();
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReportPath!));
        report.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(4);
    }

    [Test]
    public async Task E3_a_delete_without_its_line_or_with_the_row_still_there_fails()
    {
        using var work = new Work();
        var seams = new Seams();
        seams.Rows.Add(Row("e3-no-line", "antiphon-codex-canary-e3a", "cli", 1755000000));
        seams.Rows.Add(Row("e3-row-stays", "antiphon-codex-canary-e3b", "cli", 1755000000));
        seams.Answer = id => id == "e3-no-line" ? ["error: no such session"] : ["Deleted session " + id];
        seams.KeepRows = true;
        var output = new StringWriter();
        var result = Run(work, seams, output, "--state-db-path", await work.DatabaseAsync(), "--execute");
        result.ExitCode.ShouldBe(5);
        output.ToString().ShouldContain("delete e3-no-line FAILED error: no such session");
        output.ToString().ShouldContain("delete e3-row-stays FAILED Deleted session line was present but threads row remained.");
        result.Mutations.ShouldAllBe(mutation => !mutation.Ok);
    }

    [Test]
    public void Usage_and_unreadable_threads()
    {
        Should.Throw<NightlyUsageException>(() => CodexTestResidueOptions.Parse(["--older-than-hours", "-1"], null, "C:\\profile", "C:\\repo", Temp));
        Should.Throw<NightlyUsageException>(() => CodexTestResidueOptions.Parse(["--max-per-run", "0"], null, "C:\\profile", "C:\\repo", Temp));
        Should.Throw<NightlyUsageException>(() => CodexTestResidueOptions.Parse(["--now", "not a time"], null, "C:\\profile", "C:\\repo", Temp));
        var defaults = CodexTestResidueOptions.Parse([], null, Path.Combine(Temp, "profile"), Path.Combine(Temp, "repo"), Temp);
        defaults.CodexHome.ShouldBe(Path.GetFullPath(Path.Combine(Temp, "profile", ".codex")));
        defaults.StateDbPath.ShouldBe(Path.Combine(defaults.CodexHome, "state_5.sqlite"));
        defaults.ReportPath.ShouldBe(Path.Combine(Temp, "repo", "logs", "codex-test-residue"));
        (defaults.OlderThanHours, defaults.MaxPerRun, defaults.CodexExe, defaults.Execute).ShouldBe((24, 10, "codex", false));

        using var work = new Work();
        var output = new StringWriter();
        var missing = Run(work, new Seams(), output, "--state-db-path", Path.Combine(work.Root, "absent.sqlite"));
        missing.ExitCode.ShouldBe(1);
        output.ToString().ShouldContain("state_5.sqlite not found: ");
        File.Exists(missing.ReportPath!).ShouldBeTrue("the report records the failed read");
    }

    private static JsonObjectRow Row(string id, string cwdUnderTemp, string source, long created) =>
        new(id, Temp + "\\" + cwdUnderTemp, source, created);

    private static CodexTestResidueResult Run(Work work, Seams seams, params string[] args) => Run(work, seams, new StringWriter(), args);

    private static CodexTestResidueResult Run(Work work, Seams seams, StringWriter output, params string[] args)
    {
        var options = CodexTestResidueOptions.Parse(["--now", NowPin, "--report-path", work.Reports, "--codex-home", work.CodexHome, .. args], null,
            work.Root, work.Root, Path.GetTempPath());
        return CodexTestResidue.Run(options, NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), seams);
    }

    private sealed record JsonObjectRow(string Id, string Cwd, string Source, long CreatedAt);

    /// <summary>The seams: rows answered from a list (or a real reader), no live codex, deletes recorded.</summary>
    private sealed class Seams : ICodexResidueSeams
    {
        public List<JsonObjectRow> Rows { get; } = [];
        public List<string> Deleted { get; } = [];
        public ICodexResidueSeams? Reader { get; init; }
        public Func<string, IReadOnlyList<string>> Answer { get; set; } = _ => throw new InvalidOperationException("no delete expected");
        public bool KeepRows { get; set; }

        public string ReadThreads(string stateDbPath) => Reader?.ReadThreads(stateDbPath) ?? JsonSerializer.Serialize(Rows.Select(row => new Dictionary<string, object?>
        {
            ["id"] = row.Id, ["cwd"] = row.Cwd, ["source"] = row.Source, ["model_provider"] = "openai", ["created_at"] = row.CreatedAt,
            ["rollout_path"] = null, ["archived"] = 0, ["is_pinned"] = 0, ["first_user_message"] = "fixture",
        }));

        public IReadOnlyList<string> CodexCommandLines() => [];

        public IReadOnlyList<string> Delete(string codexExe, string id)
        {
            Deleted.Add(codexExe + " delete " + id);
            var output = Answer(id);
            if (!KeepRows && output.Any(line => line.StartsWith("Deleted session", StringComparison.Ordinal))) Rows.RemoveAll(row => row.Id == id);
            return output;
        }
    }

    private sealed class Work : IDisposable
    {
        public Work()
        {
            Root = Directory.CreateTempSubdirectory("codex-residue-test-").FullName;
            Directory.CreateDirectory(CodexHome);
        }

        public string Root { get; }
        public string Reports => Path.Combine(Root, "reports");
        public string CodexHome => Path.Combine(Root, "codex-home");

        public async Task<string> FixtureAsync()
        {
            var raw = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "codex-test-residue-threads-2026-08-26.json"));
            var input = Path.Combine(Root, "threads.json");
            await File.WriteAllTextAsync(input, raw.Replace("__TEMP__", Temp.Replace("\\", "\\\\")));
            return input;
        }

        // A database file the seams stand in for (the reader is the seams', so its bytes are never read).
        public async Task<string> DatabaseAsync()
        {
            var path = Path.Combine(Root, "state_5.sqlite");
            await File.WriteAllTextAsync(path, "");
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
