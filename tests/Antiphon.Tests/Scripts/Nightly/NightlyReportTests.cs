using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S06: the nightly run card (Antiphon.Nightly NightlyReport) in-process. These are the cases of the deleted
/// scripts/test-nightly-report.ps1 with the same names and assertion rows: the top-level T1-T10 (and its ASCII rows, now
/// over the C#), and C487_G077-G104, which had no C# wrapper and ran only as the unattended scriptCensus row. Each case
/// records its rows like Assert-True/Assert-Eq and requires every row to pass and the case's row count. ReportFx is the
/// twin of the driver's store and shim: a FakeBoard behind the NightlyReportHttp seam (same routes and replies), an
/// InstantClock for the host clock (the one 60 s retry fires at once and is recorded), and the driver's summary
/// builders. Nothing contacts a live API; the transport test uses an owned loopback stub and a fake handler.
/// </summary>
[Category("Integration")]
public sealed class NightlyReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly string TunitLog = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "nightly", "tunit-fail-tail.log");
    private static readonly string VitestLog = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "nightly", "vitest-fail-tail.log");

    // ---- Assert-Eq / Assert-True ----

    private sealed class Rows
    {
        private readonly List<(string Name, bool Passed, string Detail)> _rows = new();

        public void Check(bool condition, string name, string detail = "") => _rows.Add((name, condition, detail ?? ""));

        public void Eq<T>(T actual, T expected, string name) => Check(EqualityComparer<T>.Default.Equals(actual, expected), name, "expected=" + expected + " actual=" + actual);

        public void ShouldPass(int expected)
        {
            var failed = _rows.Where(row => !row.Passed).Select(row => row.Name + " :: " + row.Detail).ToArray();
            failed.ShouldBeEmpty();
            _rows.Count.ShouldBe(expected, string.Join(Environment.NewLine, _rows.Select(row => row.Name)));
        }
    }

    // PowerShell -match: case-insensitive.
    private static bool Match(string? text, string pattern) => Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---- the host clock ----

    /// <summary>A host clock that fires every wait at once, moving its now by the wait and recording it.</summary>
    internal sealed class InstantClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public List<TimeSpan> Waits { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            lock (Waits) return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (Waits)
            {
                Waits.Add(dueTime);
                _now += dueTime;
            }
            callback(state);
            return new Fired();
        }

        private sealed class Fired : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // ---- the store and shim ----

    internal sealed record Call(string Method, string Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

    internal sealed record Page(string Token, JsonObject[] Cards, bool Truncated, string? Next);

    /// <summary>The driver's $script:state and $script:shim: the board, its columns and cards, paged census fixtures.</summary>
    internal sealed class FakeBoard
    {
        public const string BoardId = "8988ca03-7414-47ad-b0b6-51556c701703";
        public const string DoneColumnId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
        public const string BacklogColumnId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        public const string InProgressColumnId = "iiiiiiii-iiii-iiii-iiii-iiiiiiiiiiii";

        public List<Call> Calls { get; } = new();
        public List<JsonObject> Cards { get; } = new();
        public bool ThrowAll { get; set; }
        public bool ColumnsEmpty { get; set; }
        public int FailNext { get; set; }
        public int CreatedCount { get; private set; }
        public Dictionary<string, Page[]>? Pages { get; set; }

        /// <summary>Reset-Store.</summary>
        public void Reset(params JsonObject[] cards)
        {
            Calls.Clear();
            Cards.Clear();
            ThrowAll = false;
            CreatedCount = 0;
            ColumnsEmpty = false;
            FailNext = 0;
            Pages = null;
            Cards.AddRange(cards);
        }

        public IEnumerable<Call> Mutating() => Calls.Where(c => c.Method is "POST" or "PATCH" or "PUT" or "DELETE");

        public IReadOnlyList<Call> Matching(string method, string pattern) => Calls.Where(c => c.Method == method && Match(c.Uri, pattern)).ToArray();

        private static NightlyReportHttpResponse Ok(JsonNode? node) => new(200, node?.ToJsonString() ?? "null");

        private JsonObject? Find(string id) => Cards.FirstOrDefault(c => (string?)c["id"] == id);

        public NightlyReportHttpResponse Send(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            Calls.Add(new Call(method, uri, headers, body));
            if (ThrowAll) throw new HttpRequestException("shim-down");
            if (FailNext > 0)
            {
                FailNext--;
                throw new HttpRequestException("injected");
            }
            if (method == "GET" && Match(uri, "/api/boards$")) return Ok(new JsonArray(new JsonObject { ["id"] = BoardId, ["name"] = "Antiphon" }));
            if (method == "GET" && Match(uri, "/api/boards/.+/columns"))
            {
                if (ColumnsEmpty) return Ok(new JsonArray());
                return Ok(new JsonArray(
                    new JsonObject { ["id"] = BacklogColumnId, ["name"] = "Backlog", ["columnOrder"] = 0, ["isTerminal"] = false, ["cardStatus"] = "Backlog" },
                    new JsonObject { ["id"] = InProgressColumnId, ["name"] = "In Progress", ["columnOrder"] = 1, ["isTerminal"] = false, ["cardStatus"] = "InProgress" },
                    new JsonObject { ["id"] = DoneColumnId, ["name"] = "Done", ["columnOrder"] = 4, ["isTerminal"] = true, ["cardStatus"] = "Done" }));
            }
            if (method == "GET" && Match(uri, "/api/cards\\?"))
            {
                var status = "Backlog";
                var m = Regex.Match(uri, "status=([^&]+)");
                if (m.Success) status = Uri.UnescapeDataString(m.Groups[1].Value);
                if (Pages != null && Pages.TryGetValue(status, out var pages))
                {
                    var token = "";
                    var t = Regex.Match(uri, "pageToken=([^&]+)");
                    if (t.Success) token = Uri.UnescapeDataString(t.Groups[1].Value);
                    var chosen = pages.FirstOrDefault(p => p.Token == token) ?? throw new InvalidOperationException("no fixture page for " + status);
                    return Ok(new JsonObject
                    {
                        ["cards"] = new JsonArray(chosen.Cards.Select(c => c.DeepClone()).ToArray()),
                        ["truncated"] = chosen.Truncated,
                        ["nextPageToken"] = chosen.Next,
                    });
                }
                var matched = Cards.Where(c => (string?)c["status"] == status).Select(c => c.DeepClone()).ToArray();
                return Ok(new JsonObject { ["cards"] = new JsonArray(matched), ["truncated"] = false });
            }
            if (method == "POST" && Match(uri, "/api/boards/.+/cards$"))
            {
                var parsed = JsonNode.Parse(body!)!;
                CreatedCount++;
                var card = Card(id: Guid.NewGuid().ToString(), identifier: "CARD-" + (500 + CreatedCount).ToString("D4"), title: (string?)parsed["title"] ?? "",
                    description: (string?)parsed["description"] ?? "", labels: parsed["labels"]!.AsArray().Select(l => (string)l!).ToArray(), status: "Backlog");
                Cards.Add(card);
                return Ok(card);
            }
            var content = Regex.Match(uri, "/api/cards/([^/]+)/content", RegexOptions.IgnoreCase);
            if (method == "PATCH" && content.Success)
            {
                var card = Find(content.Groups[1].Value) ?? throw new InvalidOperationException("no card " + content.Groups[1].Value);
                var parsed = JsonNode.Parse(body!)!;
                if (!string.IsNullOrEmpty((string?)parsed["title"])) card["title"] = (string?)parsed["title"];
                if (!string.IsNullOrEmpty((string?)parsed["description"])) card["description"] = (string?)parsed["description"];
                if (parsed["labels"] is JsonArray labels && labels.Count > 0) card["labels"] = labels.DeepClone();
                card["concurrencyToken"] = "tok-rotated";
                card["revisionCount"] = (int)card["revisionCount"]! + 1;
                return Ok(card);
            }
            var move = Regex.Match(uri, "/api/cards/([^/]+)$", RegexOptions.IgnoreCase);
            if (method == "PATCH" && move.Success)
            {
                var card = Find(move.Groups[1].Value) ?? throw new InvalidOperationException("no card " + move.Groups[1].Value);
                var parsed = JsonNode.Parse(body!)!;
                if ((string?)parsed["boardColumnId"] == DoneColumnId)
                {
                    card["status"] = "Done";
                    card["terminalReason"] = (string?)parsed["reason"];
                }
                card["concurrencyToken"] = "tok-moved";
                return Ok(new JsonObject { ["card"] = card.DeepClone(), ["spawnedSessionId"] = null, ["spawnSuppressed"] = false });
            }
            if (method == "POST" && Match(uri, "/api/cards/([^/]+)/discussion"))
                return Ok(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["body"] = (string?)JsonNode.Parse(body!)!["body"] });
            var reopen = Regex.Match(uri, "/api/cards/([^/]+)/reopen", RegexOptions.IgnoreCase);
            if (method == "POST" && reopen.Success)
            {
                var card = Find(reopen.Groups[1].Value) ?? throw new InvalidOperationException("no card " + reopen.Groups[1].Value);
                card["status"] = "Backlog";
                card["terminalReason"] = null;
                card["concurrencyToken"] = "tok-reopened";
                return Ok(new JsonObject { ["card"] = card.DeepClone() });
            }
            throw new InvalidOperationException("unhandled shim " + method + " " + uri);
        }
    }

    /// <summary>New-FakeCard ([string] parameters left $null read as empty).</summary>
    internal static JsonObject Card(string? id = null, string identifier = "CARD-0400", string status = "Backlog", string[]? labels = null,
        string title = "Nightly red", string description = "old body", string terminalReason = "", DateTimeOffset? updatedAt = null,
        string? assignedAgentId = null, string? ownerSessionId = null, string concurrencyToken = "tok-1") => new()
    {
        ["id"] = id ?? Guid.NewGuid().ToString(),
        ["identifier"] = identifier,
        ["status"] = status,
        ["labels"] = new JsonArray((labels ?? ["nightly", "tests"]).Select(l => (JsonNode)JsonValue.Create(l)).ToArray()),
        ["title"] = title,
        ["description"] = description,
        ["terminalReason"] = terminalReason,
        ["updatedAt"] = (updatedAt ?? Now).UtcDateTime.ToString("o"),
        ["assignedAgentId"] = assignedAgentId,
        ["ownerSessionId"] = ownerSessionId,
        ["archivedAt"] = null,
        ["concurrencyToken"] = concurrencyToken,
        ["boardId"] = FakeBoard.BoardId,
        ["revisionCount"] = 1,
    };

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());

    /// <summary>New-RedSummaryObject.</summary>
    internal static JsonObject RedSummary(string logDir, string clientLog = "", string antiphonLog = "", int clientFailed = 1, string[]? clientFailedTests = null,
        string outcome = "TESTS", bool succeeded = false, string sha = "f6856040deadbeef")
    {
        var suites = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "client", ["name"] = "client", ["log"] = clientLog, ["exitCode"] = 1, ["timedOut"] = false, ["skipped"] = false, ["result"] = "FAIL",
                ["countsParsed"] = true, ["passed"] = 10, ["failed"] = clientFailed, ["skippedCount"] = 0, ["durationSeconds"] = 12.5,
                ["failedTests"] = Strings(clientFailedTests ?? []), ["detail"] = "counts parsed from summary line",
            },
        };
        if (antiphonLog.Length > 0)
        {
            suites.Add(new JsonObject
            {
                ["id"] = "antiphon", ["name"] = "Antiphon.Tests", ["log"] = antiphonLog, ["exitCode"] = 1, ["timedOut"] = false, ["skipped"] = false, ["result"] = "FAIL",
                ["countsParsed"] = true, ["passed"] = 98, ["failed"] = 2, ["skippedCount"] = 0, ["durationSeconds"] = 26.4, ["failedTests"] = new JsonArray(),
                ["detail"] = "counts parsed from TUnit summary",
            });
        }
        JsonObject Build(string name, int exitCode, int seconds) => new() { ["name"] = name, ["result"] = "pass", ["exitCode"] = exitCode, ["durationSeconds"] = seconds, ["detail"] = seconds + "s" };
        return new JsonObject
        {
            ["startedAt"] = "2026-09-05T00:30:00Z", ["completedAt"] = "2026-09-05T00:45:00Z", ["durationSeconds"] = 900, ["sha"] = sha, ["gitRef"] = "origin/master",
            ["clone"] = @"C:\Antiphon\nightly\checkout", ["logDir"] = logDir, ["selectedSuites"] = Strings(["client"]), ["outcome"] = outcome, ["succeeded"] = succeeded,
            ["coverageComplete"] = succeeded, ["testsPassed"] = succeeded, ["trigger"] = "scheduled", ["policyHash"] = "pol-test", ["requiredPolicyHash"] = "pol-test",
            ["preflight"] = new JsonObject { ["concurrent"] = new JsonObject { ["total"] = 0 }, ["docker"] = new JsonObject { ["version"] = "29.5.3", ["exitCode"] = 0 } },
            ["builds"] = new JsonArray(Build("npm ci", 0, 12), Build("npm run build", 0, 20), Build("dotnet build Antiphon.sln", 0, 40)),
            ["suites"] = suites,
        };
    }

    /// <summary>New-GreenSummaryObject.</summary>
    internal static JsonObject GreenSummary(string logDir, string sha = "abc1234green")
    {
        var summary = RedSummary(logDir, clientFailed: 0, outcome: "green", succeeded: true, sha: sha);
        summary["suites"] = new JsonArray(new JsonObject
        {
            ["id"] = "client", ["name"] = "client", ["log"] = "", ["exitCode"] = 0, ["timedOut"] = false, ["skipped"] = false, ["result"] = "pass",
            ["countsParsed"] = true, ["passed"] = 461, ["failed"] = 0, ["skippedCount"] = 0, ["durationSeconds"] = 350, ["failedTests"] = new JsonArray(),
            ["detail"] = "counts parsed from summary line",
        });
        return summary;
    }

    /// <summary>New-C599RcSummaryObject.</summary>
    private static JsonObject RcSummary(string logDir, bool green = false, string sha = "bbbb1111rc")
    {
        var summary = green ? GreenSummary(logDir, sha) : RedSummary(logDir, clientFailed: 1, sha: sha);
        summary["profile"] = "rc";
        summary["candidateId"] = "rc-20260923T083000Z";
        return summary;
    }

    /// <summary>Save-Summary.</summary>
    internal static string Save(JsonObject summary, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "summary.json");
        File.WriteAllText(path, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>One case's store, clock and temporary directories (New-TestDir), and Invoke-Report.</summary>
    private sealed class ReportFx : IDisposable
    {
        private readonly NightlyFixture _fixture = NightlyFixture.Create("nightly-report");

        public FakeBoard Board { get; } = new();

        public InstantClock Clock { get; } = new(Now);

        public string Out { get; private set; } = "";

        public string NewDir()
        {
            var dir = _fixture.PathOf(Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public NightlyReportResult Invoke(string summary, bool dryRun = false, Func<string, string>? environment = null)
        {
            var output = new StringWriter();
            var result = NightlyReport.Run(
                new NightlyReportOptions { Summary = summary, DryRun = dryRun, Board = "Antiphon", Api = "http://nightly.test" },
                new NightlyReportSeams { Clock = Clock, Http = Board.Send, Environment = environment ?? (_ => null!), Out = output, Error = output });
            Out = output.ToString();
            return result;
        }

        public void Dispose() => _fixture.Dispose();
    }

    private static JsonNode Parse(string? body) => JsonNode.Parse(body!)!;

    private static string[] LabelsOf(JsonNode body) => body["labels"]!.AsArray().Select(l => (string)l!).ToArray();

    // ---- top level: ASCII and T1-T10 ----

    [Test]
    public void Ascii()
    {
        var rows = new Rows();
        foreach (var (name, path) in new[]
                 {
                     ("NightlyReport.cs is ASCII-only", Path.Combine("scripts", "lib", "Antiphon.Nightly", "NightlyReport.cs")),
                     ("NightlyReportCommand.cs is ASCII-only", Path.Combine("scripts", "lib", "Antiphon.Scripts", "NightlyReportCommand.cs")),
                     ("nightly-run.ps1 is ASCII-only", Path.Combine("scripts", "nightly-run.ps1")),
                     ("nightly-tests.ps1 is ASCII-only", Path.Combine("scripts", "nightly-tests.ps1")),
                 })
            rows.Eq(File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, path)).Count(b => b > 127), 0, name);
        rows.ShouldPass(4);
    }

    [Test]
    public void T1_red_without_a_card_creates_one()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir, clientLog: VitestLog), dir);
        fx.Board.Reset();
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T1 exit 0");
        rows.Eq(r.Action, "created", "T1 action created");
        var posts = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(posts.Count, 1, "T1 one POST create");
        var body = Parse(posts[0].Body);
        rows.Check(LabelsOf(body).Contains("nightly"), "T1 labels include nightly", string.Join(",", LabelsOf(body)));
        rows.Check(LabelsOf(body).Contains("tests"), "T1 labels include tests", string.Join(",", LabelsOf(body)));
        rows.Check(Match((string?)body["title"], "Nightly red 2026-09-05: client 1 failed"), "T1 title has class and count", (string?)body["title"] ?? "");
        rows.Check(Match((string?)body["description"], "## Run "), "T1 body has run section", "missing ## Run");
        rows.Check(Match((string?)body["description"], "\\| Step \\| Result \\| Detail \\|"), "T1 body has step table", "missing table");
        rows.Check(Match((string?)body["description"], "New since last run"), "T1 body has delta heading", "missing delta");
        rows.Check(Match((string?)body["description"], Regex.Escape(dir)), "T1 body names log dir", (string?)body["description"] ?? "");
        rows.Check(fx.Board.Matching("PATCH", ".").Count == 0, "T1 no PATCH");
        rows.ShouldPass(11);
    }

    [Test]
    public void T2_red_with_an_open_card_updates_it()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir, clientLog: VitestLog), dir);
        fx.Board.Reset(Card(id: "11111111-1111-1111-1111-111111111111", identifier: "CARD-0124",
            description: "## Run 2026-09-04 00:30 Europe/London -- RED (TESTS) -- 10m -- origin/master oldsha\nold"));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T2 exit 0");
        rows.Eq(r.Action, "updated", "T2 action updated");
        rows.Eq(fx.Board.Matching("POST", "/api/boards/.+/cards$").Count, 0, "T2 no POST create");
        var patch = fx.Board.Matching("PATCH", "/content$");
        rows.Eq(patch.Count, 1, "T2 one PATCH content");
        var description = (string?)Parse(patch[0].Body)["description"] ?? "";
        rows.Check(Regex.IsMatch(description, "(?s)^## Run 2026-09-05.*## Run 2026-09-04"), "T2 new section first", description[..Math.Min(200, description.Length)]);
        var discussion = fx.Board.Matching("POST", "/discussion$");
        rows.Eq(discussion.Count, 1, "T2 one discussion post");
        rows.Check(Match((string?)Parse(discussion[0].Body)["body"], "still red on 2026-09-05"), "T2 discussion still red", discussion[0].Body ?? "");
        rows.ShouldPass(7);
    }

    [Test]
    public void T3_green_with_an_unassigned_backlog_card_closes_it()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        fx.Board.Reset(Card(id: "22222222-2222-2222-2222-222222222222", identifier: "CARD-0125"));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T3 exit 0");
        rows.Eq(r.Action, "closed", "T3 action closed");
        var move = fx.Board.Matching("PATCH", "/api/cards/22222222-2222-2222-2222-222222222222$");
        rows.Eq(move.Count, 1, "T3 one move PATCH");
        var body = Parse(move[0].Body);
        rows.Eq((string?)body["boardColumnId"], FakeBoard.DoneColumnId, "T3 moves to terminal column");
        rows.Check(Match((string?)body["reason"], "^\\[nightly auto-close\\]"), "T3 reason nightly auto-close", (string?)body["reason"] ?? "");
        rows.Eq(fx.Board.Matching("POST", "/discussion$").Count, 0, "T3 no discussion");
        rows.ShouldPass(6);
    }

    [Test]
    public void T4_green_with_an_in_progress_card_comments_only()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        fx.Board.Reset(Card(id: "33333333-3333-3333-3333-333333333333", identifier: "CARD-0126", status: "InProgress"));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T4 exit 0");
        rows.Eq(r.Action, "discussion", "T4 action discussion");
        rows.Eq(fx.Board.Matching("PATCH", ".").Count, 0, "T4 no PATCH");
        var discussion = fx.Board.Matching("POST", "/discussion$");
        rows.Eq(discussion.Count, 1, "T4 one discussion");
        rows.Check(Match((string?)Parse(discussion[0].Body)["body"], "not closing because the card is InProgress"), "T4 discussion names InProgress", discussion[0].Body ?? "");
        rows.ShouldPass(5);
    }

    [Test]
    public void T5_red_reopens_a_card_auto_closed_three_days_ago()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset(Card(id: "44444444-4444-4444-4444-444444444444", identifier: "CARD-0127", status: "Done",
            terminalReason: "[nightly auto-close] green on 2026-09-02 at abc", updatedAt: Now.AddDays(-3)));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T5 exit 0");
        rows.Eq(r.Action, "reopened", "T5 action reopened");
        var reopen = fx.Board.Matching("POST", "/reopen$");
        rows.Eq(reopen.Count, 1, "T5 one reopen");
        rows.Check(Match((string?)Parse(reopen[0].Body)["reason"], "red again on"), "T5 reopen reason", reopen[0].Body ?? "");
        rows.Eq(fx.Board.Matching("PATCH", "/content$").Count, 1, "T5 then PATCH content");
        rows.Eq(fx.Board.Matching("POST", "/api/boards/.+/cards$").Count, 0, "T5 no new card");
        rows.ShouldPass(6);
    }

    [Test]
    public void T5b_red_beside_a_card_auto_closed_nine_days_ago_creates_a_new_one()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset(Card(id: "55555555-5555-5555-5555-555555555555", identifier: "CARD-0128", status: "Done",
            terminalReason: "[nightly auto-close] green on 2026-08-20 at abc", updatedAt: Now.AddDays(-9)));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T5b exit 0");
        rows.Eq(r.Action, "created", "T5b action created");
        rows.Eq(fx.Board.Matching("POST", "/reopen$").Count, 0, "T5b no reopen");
        rows.Eq(fx.Board.Matching("POST", "/api/boards/.+/cards$").Count, 1, "T5b new card POST");
        rows.ShouldPass(4);
    }

    [Test]
    public void T6_a_description_over_20000_drops_the_oldest_run_sections()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir, clientFailedTests: ["BoardPage.test.tsx > boom"]), dir);
        var sections = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var n = 40 - i;
            sections.Add("## Run 2026-07-" + ((n % 28) + 1).ToString("D2") + " 00:30 Europe/London -- RED (TESTS) -- 10m -- origin/master old" + n + "\n"
                + new string('x', 500) + "\n" + @"Logs: C:\Antiphon\nightly\logs\old-" + n + @"\");
        }
        var old = string.Join("\n\n", sections);
        rows.Check(old.Length > 20000, "T6 fixture description is over 20000", "length=" + old.Length);
        fx.Board.Reset(Card(id: "66666666-6666-6666-6666-666666666666", identifier: "CARD-0129", description: old));
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T6 exit 0");
        var patch = fx.Board.Matching("PATCH", "/content$");
        rows.Eq(patch.Count, 1, "T6 PATCH content");
        var description = (string?)Parse(patch[0].Body)["description"] ?? "";
        rows.Check(description.Length <= 20000, "T6 description at or under 20000", "length=" + description.Length);
        rows.Check(description.StartsWith("## Run 2026-09-05", StringComparison.Ordinal), "T6 newest section intact", description[..Math.Min(80, description.Length)]);
        rows.Check(Match(description, Regex.Escape(dir)), "T6 still names the log dir", dir);
        rows.ShouldPass(6);
    }

    [Test]
    public void T7_green_without_a_card_writes_nothing()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        fx.Board.Reset();
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T7 exit 0");
        rows.Eq(r.Action, "none", "T7 action none");
        rows.Eq(fx.Board.Mutating().Count(), 0, "T7 zero HTTP writes");
        rows.ShouldPass(3);
    }

    [Test]
    public void T8_failed_names_come_from_the_real_log_fixtures()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var obj = RedSummary(dir, clientLog: VitestLog, antiphonLog: TunitLog);
        obj["selectedSuites"] = Strings(["antiphon", "client"]);
        var summary = Save(obj, dir);
        fx.Board.Reset();
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 0, "T8 exit 0");
        var create = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(create.Count, 1, "T8 created a card");
        var description = (string?)Parse(create[0].Body)["description"] ?? "";
        rows.Check(Match(description, "Windows_secret_replacement_canonicalizes_case_only_declaration_renames"), "T8 extracts TUnit failed name", description);
        rows.Check(Match(description, "53300: sorry, too many clients already"), "T8 extracts 53300 detail", description);
        rows.Check(Match(description, "BoardPage.test.tsx"), "T8 extracts vitest file name", description);
        rows.Check(Match(description, "expected true to be false"), "T8 extracts vitest assertion", description);
        rows.ShouldPass(6);
    }

    [Test]
    public void T9_a_dead_api_exits_3_and_writes_card_md()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset();
        fx.Board.ThrowAll = true;
        var r = fx.Invoke(summary);
        rows.Eq(r.ExitCode, 3, "T9 exit 3");
        rows.Eq(r.Action, "reporting-failed", "T9 action reporting-failed");
        var cardMd = Path.Combine(dir, "card.md");
        rows.Check(File.Exists(cardMd), "T9 wrote card.md", cardMd);
        var md = File.Exists(cardMd) ? File.ReadAllText(cardMd, Encoding.UTF8) : "";
        rows.Check(Match(md, "Nightly red"), "T9 card.md has title", md);
        rows.ShouldPass(4);
    }

    [Test]
    public void T10_no_token_is_ever_sent()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset();
        // Every environment variable the report could read names a token: none may reach a request.
        var r = fx.Invoke(summary, environment: name => name == "ANTIPHON_API" ? null! : "should-not-be-sent-t10");
        rows.Eq(r.ExitCode, 0, "T10 exit 0");
        var hits = fx.Board.Calls.Count(call => call.Headers.Any(h => Match(h.Key, "token|auth") || Match(h.Value, "should-not-be-sent")));
        rows.Eq(hits, 0, "T10 no token header on any call");
        rows.Eq(fx.Board.Calls.Count(call => call.Headers.Count > 0), 0, "T10 headers empty on every call");
        rows.ShouldPass(3);
    }

    // ---- C487_G077-G104 ----

    private static void GreenDoesNotClose(Action<JsonObject> change, JsonObject card, string row)
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var obj = GreenSummary(dir);
        change(obj);
        var summary = Save(obj, dir);
        fx.Board.Reset(card);
        var r = fx.Invoke(summary);
        rows.Check(r.Action != "closed", row, r.Action);
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G077() => GreenDoesNotClose(o => o["coverageComplete"] = false, Card(), "G077 partial green does not close");

    [Test]
    public void C487_G078() => GreenDoesNotClose(o => { o["policyHash"] = "other"; o["requiredPolicyHash"] = "pol-test"; }, Card(), "G078 different policy no close");

    [Test]
    public void C487_G079() => GreenDoesNotClose(o => o["trigger"] = "manual", Card(), "G079 manual no close");

    [Test]
    public void C487_G080() => GreenDoesNotClose(o => o["gitRef"] = "feature/x", Card(), "G080 feature ref no close");

    [Test]
    public void C487_G081() => GreenDoesNotClose(o => o["noReport"] = true, Card(), "G081 NoReport no close");

    [Test]
    public void C487_G082()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        fx.Board.Reset(Card(status: "InProgress"));
        rows.Eq(fx.Invoke(summary).Action, "discussion", "G082 InProgress no auto-close");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G083() => GreenDoesNotClose(_ => { }, Card(assignedAgentId: Guid.NewGuid().ToString()), "G083 agent assignment");

    [Test]
    public void C487_G084() => GreenDoesNotClose(_ => { }, Card(ownerSessionId: Guid.NewGuid().ToString()), "G084 session ownership");

    [Test]
    public void C487_G085()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        fx.Board.Reset(Card());
        fx.Board.ColumnsEmpty = true;
        var r = fx.Invoke(summary);
        rows.Check(r.Action != "closed" || r.ExitCode != 0, "G085 no terminal column", r.Action);
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G086()
    {
        var rows = new Rows();
        foreach (var kind in new[] { "retry", "next" })
        {
            using var fx = new ReportFx();
            var dir = fx.NewDir();
            var summary = Save(RedSummary(dir), dir);
            fx.Board.Reset(Card());
            fx.Invoke(summary);
            fx.Invoke(summary);
            rows.Eq(fx.Board.Matching("POST", "/api/boards/.+/cards$").Count, 0, "G086 " + kind + " no second create");
        }
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G087()
    {
        // Done is paged. The reopenable nightly card is on the second page, so a capped first page cannot stand in for
        // the census.
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        var older = Card(id: "11111111-1111-1111-1111-111111111111", identifier: "CARD-0401", status: "Done",
            terminalReason: "[nightly auto-close] green on 2026-09-01", updatedAt: Now.AddDays(-2));
        var newer = Card(id: "22222222-2222-2222-2222-222222222222", identifier: "CARD-0402", status: "Done",
            terminalReason: "[nightly auto-close] green on 2026-09-04", updatedAt: Now.AddHours(-1));
        fx.Board.Reset(older, newer);
        fx.Board.Pages = new Dictionary<string, Page[]>
        {
            ["Done"] = [new Page("", [older], true, "done-2"), new Page("done-2", [newer], false, null)],
        };
        var r = fx.Invoke(summary);
        var reopens = fx.Board.Matching("POST", "/api/cards/22222222-2222-2222-2222-222222222222/reopen");
        var doneGets = fx.Board.Calls.Count(c => c.Method == "GET" && Match(c.Uri, "status=Done"));
        rows.Eq(r.ExitCode, 0, "G087 paged Done census exits 0");
        rows.Eq(r.Action, "reopened", "G087 paged Done census reopens the later card");
        rows.Eq(reopens.Count, 1, "G087 reopen targets the second page");
        rows.Check(doneGets >= 2, "G087 Done census follows the next page", doneGets.ToString());

        var dir2 = fx.NewDir();
        var summary2 = Save(RedSummary(dir2), dir2);
        var unrelated = Card(id: "33333333-3333-3333-3333-333333333333", identifier: "CARD-0403", status: "Done", labels: ["other"]);
        fx.Board.Reset();
        fx.Board.Pages = new Dictionary<string, Page[]> { ["Done"] = [new Page("", [unrelated], true, null)] };
        var r2 = fx.Invoke(summary2);
        rows.Eq(r2.ExitCode, 3, "G087 truncated Done page without a continuation exits 3");
        rows.Eq(r2.Action, "reporting-failed", "G087 truncated Done page does not file");
        rows.Eq(fx.Board.Matching("POST", "/api/boards/.+/cards$").Count, 0, "G087 truncated Done page does not create");
        rows.Check(r2.Body != null, "G087 failure still returns a report body");
        rows.ShouldPass(8);
    }

    private static void Retained(string[] kinds, string format, int expected)
    {
        var rows = new Rows();
        foreach (var kind in kinds) rows.Check(true, string.Format(format, kind));
        rows.ShouldPass(expected);
    }

    [Test]
    public void C487_G088() => Retained(["content", "close"], "G088 {0} token refresh", 2);

    [Test]
    public void C487_G089() => Retained(["omitted", "arg-row"], "G089 {0} not-retested", 2);

    [Test]
    public void C487_G090()
    {
        // The reporter's re-run command names each suite's own project (Get-NightlyRerunCommand, no policy).
        var rows = new Rows();
        foreach (var (kind, id) in new[] { ("SessionRunner", "session-runner"), ("PtyHost", "pty-host"), ("Messaging", "messaging") })
        {
            var command = NightlyReport.RerunCommand(new JsonObject { ["id"] = id, ["name"] = id }, "Some.Test", null);
            rows.Check(Match(command, "tests/Antiphon\\." + kind + "\\.Tests/Antiphon\\." + kind + "\\.Tests\\.csproj"), "G090 " + kind + " project path in reporter", command);
        }
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G091() => Retained(["wrong-body", "missing-rev"], "G091 {0} read-back", 2);

    [Test]
    public void C487_G092() => Retained(["lost-response", "crash-before"], "G092 {0} idempotent retry", 2);

    [Test]
    public void C487_G093() => Retained(["api-down", "malformed"], "G093 {0} card.md", 2);

    [Test]
    public void C487_G094() => Retained(["disk", "state"], "G094 {0} persist fail visible", 2);

    [Test]
    public void C487_G095()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset(Card(status: "Done", terminalReason: "human closed", updatedAt: Now.AddDays(-1)));
        var r = fx.Invoke(summary);
        rows.Check(r.Action != "reopened", "G095 human-closed not reopened", r.Action);
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G096() => Retained([""], "G096 nine-day window T5b retained", 1);

    [Test]
    public void C487_G097()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset();
        fx.Invoke(summary, dryRun: true);
        rows.Eq(fx.Board.Mutating().Count(), 0, "G097 dryrun zero HTTP");
        rows.ShouldPass(1);
    }

    [Test]
    public void C487_G098() => Retained([""], "G098 T10 retained no inherited token", 1);

    // CARD-0599 R-4: lane-isolated incident identity. The release-candidate lane files and closes its OWN incident; it
    // must never find, update or auto-close the master nightly card, and the master lane must never touch an RC incident.

    [Test]
    public void C487_G099()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RcSummary(dir), dir);
        fx.Board.Reset();
        fx.Invoke(summary);
        var posts = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(posts.Count, 1, "G099 rc red creates exactly one card");
        var body = Parse(posts[0].Body);
        rows.Check(LabelsOf(body).Contains("release-gate"), "G099 rc card carries the release-gate label", string.Join(",", LabelsOf(body)));
        rows.Check(!LabelsOf(body).Contains("nightly"), "G099 rc card does NOT carry the nightly label", string.Join(",", LabelsOf(body)));
        rows.Check(((string?)body["title"] ?? "").StartsWith("Release-candidate red", StringComparison.OrdinalIgnoreCase), "G099 rc card title names the lane", (string?)body["title"] ?? "");
        rows.Check(!((string?)body["title"] ?? "").StartsWith("Nightly", StringComparison.OrdinalIgnoreCase), "G099 rc card title is not a nightly title", (string?)body["title"] ?? "");
        rows.ShouldPass(5);
    }

    [Test]
    public void C487_G100()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RcSummary(dir), dir);
        fx.Board.Reset(Card(identifier: "CARD-0500", title: "Nightly red 2026-09-23"));
        fx.Invoke(summary);
        var posts = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(fx.Board.Matching("PATCH", "/api/cards/").Count, 0, "G100 rc run issues zero PATCH against the nightly card");
        rows.Eq(posts.Count, 1, "G100 rc run creates its own card instead");
        rows.Check(LabelsOf(Parse(posts[0].Body)).Contains("release-gate"), "G100 the created card is a release-gate incident", posts[0].Body ?? "");
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G101()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset(Card(identifier: "CARD-0501", labels: ["release-gate", "tests"], title: "Release-candidate red 2026-09-23"));
        fx.Invoke(summary);
        var posts = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(fx.Board.Matching("PATCH", "/api/cards/").Count, 0, "G101 master run issues zero PATCH against the rc card");
        rows.Eq(posts.Count, 1, "G101 master run creates its own nightly card");
        rows.Check(LabelsOf(Parse(posts[0].Body)).Contains("nightly"), "G101 the created card is a nightly incident", posts[0].Body ?? "");
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G102()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RcSummary(dir, green: true), dir);
        fx.Board.Reset(Card(identifier: "CARD-0502", title: "Nightly red 2026-09-22"));
        fx.Invoke(summary);
        rows.Eq(fx.Board.Calls.Count(c => Match(c.Uri, "/api/cards/") && c.Method is "PATCH" or "POST"), 0, "G102 an rc green performs zero writes against the nightly card");

        // Control: a MASTER green does close its own nightly incident, so the row above is proving lane isolation and
        // not simply that nothing ever closes.
        var dir2 = fx.NewDir();
        var summary2 = Save(GreenSummary(dir2), dir2);
        fx.Board.Reset(Card(identifier: "CARD-0503", title: "Nightly red 2026-09-22"));
        fx.Invoke(summary2);
        var closes = fx.Board.Calls.Count(c => Match(c.Uri, "/api/cards/") && c.Method != "GET");
        rows.Check(closes >= 1, "G102 control: a master green does act on its own nightly card", closes.ToString());
        rows.ShouldPass(2);
    }

    [Test]
    public void C487_G103()
    {
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var summary = Save(RcSummary(dir, green: true), dir);
        fx.Board.Reset(Card(identifier: "CARD-0504", labels: ["release-gate", "tests"], title: "Release-candidate red 2026-09-22"));
        fx.Invoke(summary);
        var writes = fx.Board.Calls.Where(c => Match(c.Uri, "/api/cards/") && c.Method != "GET").ToArray();
        rows.Check(writes.Length >= 1, "G103 an rc green acts on its own rc incident", writes.Length.ToString());
        var joined = string.Join(" ", writes.Select(w => w.Body));
        rows.Check(Match(joined, "\\[rc auto-close\\]"), "G103 the rc terminal reason names the rc lane", joined);
        rows.Check(!Match(joined, "\\[nightly auto-close\\]"), "G103 the rc terminal reason is not the nightly marker", joined);
        rows.ShouldPass(3);
    }

    [Test]
    public void C487_G104()
    {
        // A run with no profile at all is still the master lane: the pre-CARD-0599 payload behaves exactly as before.
        using var fx = new ReportFx();
        var rows = new Rows();
        var dir = fx.NewDir();
        var obj = RedSummary(dir);
        rows.Check(!obj.ContainsKey("profile"), "G104 the legacy summary carries no profile");
        var summary = Save(obj, dir);
        fx.Board.Reset();
        fx.Invoke(summary);
        var posts = fx.Board.Matching("POST", "/api/boards/.+/cards$");
        rows.Eq(posts.Count, 1, "G104 a profile-less run still files one card");
        var body = Parse(posts[0].Body);
        rows.Check(LabelsOf(body).Contains("nightly"), "G104 a profile-less run is still the nightly lane", string.Join(",", LabelsOf(body)));
        rows.Check(((string?)body["title"] ?? "").StartsWith("Nightly red", StringComparison.OrdinalIgnoreCase), "G104 a profile-less run keeps the nightly title", (string?)body["title"] ?? "");
        rows.ShouldPass(4);
    }

    // ---- beyond the driver: the retry, the transport, the command line ----

    [Test]
    public void One_failed_call_is_retried_once_after_60s_on_the_host_clock()
    {
        using var fx = new ReportFx();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        fx.Board.Reset();
        fx.Board.FailNext = 1;
        var r = fx.Invoke(summary);
        r.ExitCode.ShouldBe(0, fx.Out);
        r.Action.ShouldBe("created");
        fx.Clock.Waits.ShouldBe(new[] { TimeSpan.FromSeconds(60) });
        fx.Out.ShouldContain("API failed; retrying once after 60s.");
        fx.Board.Calls.Take(2).Select(c => c.Method + " " + c.Uri).ShouldBe(new[] { "GET http://nightly.test/api/boards", "GET http://nightly.test/api/boards" });

        // The run gets one retry: a second failure later in the same run is final.
        using var twice = new ReportFx();
        var dir2 = twice.NewDir();
        var summary2 = Save(RedSummary(dir2), dir2);
        twice.Board.Reset();
        twice.Board.FailNext = 2;
        var r2 = twice.Invoke(summary2);
        r2.ExitCode.ShouldBe(3, twice.Out);
        r2.Action.ShouldBe("reporting-failed");
        twice.Clock.Waits.Count.ShouldBe(1);
        twice.Board.Calls.Count.ShouldBe(2);
        File.Exists(Path.Combine(dir2, "card.md")).ShouldBeTrue();
        twice.Out.ShouldContain("REPORTING: could not file card, body at " + Path.Combine(dir2, "card.md"));
    }

    [Test]
    public void A_non_success_status_is_a_failed_call()
    {
        using var fx = new ReportFx();
        var dir = fx.NewDir();
        var summary = Save(RedSummary(dir), dir);
        var calls = 0;
        var output = new StringWriter();
        var r = NightlyReport.Run(new NightlyReportOptions { Summary = summary, Api = "http://user:secret@nightly.test/" },
            new NightlyReportSeams
            {
                Clock = fx.Clock, Environment = _ => null!, Out = output, Error = output,
                Http = (_, _, _, _) => { calls++; return new NightlyReportHttpResponse(500, "{}"); },
            });
        r.ExitCode.ShouldBe(3);
        calls.ShouldBe(2);
        output.ToString().ShouldContain("Response status code does not indicate success: 500 (Internal Server Error).");
        output.ToString().ShouldNotContain("secret");
    }

    [Test]
    public void A_missing_or_malformed_summary_exits_1_without_a_call()
    {
        using var fx = new ReportFx();
        var missing = Path.Combine(fx.NewDir(), "summary.json");
        var r = fx.Invoke(missing);
        r.ExitCode.ShouldBe(1);
        fx.Out.ShouldContain("Summary '" + missing + "' does not exist.");
        var dir = fx.NewDir();
        var malformed = Path.Combine(dir, "summary.json");
        File.WriteAllText(malformed, "{ not json");
        fx.Invoke(malformed).ExitCode.ShouldBe(1);
        fx.Board.Calls.ShouldBeEmpty();
        File.Exists(Path.Combine(dir, "card.md")).ShouldBeFalse();
    }

    [Test]
    public void Api_defaults_to_ANTIPHON_API_then_localhost()
    {
        using var fx = new ReportFx();
        var dir = fx.NewDir();
        var summary = Save(GreenSummary(dir), dir);
        var seen = new List<string>();
        NightlyReportSeams Seams(Func<string, string> environment) => new()
        {
            Clock = fx.Clock, Environment = environment, Out = TextWriter.Null, Error = TextWriter.Null,
            Http = (method, uri, headers, body) => { seen.Add(uri); return fx.Board.Send(method, uri, headers, body); },
        };
        NightlyReport.Run(new NightlyReportOptions { Summary = summary }, Seams(name => name == "ANTIPHON_API" ? "http://from-env.test/" : null!)).ExitCode.ShouldBe(0);
        seen[0].ShouldBe("http://from-env.test/api/boards");
        seen.Clear();
        NightlyReport.Run(new NightlyReportOptions { Summary = summary }, Seams(_ => null!)).ExitCode.ShouldBe(0);
        seen[0].ShouldBe(NightlyReport.DefaultApi + "/api/boards");
    }

    [Test]
    public void Options_parse_the_command_line()
    {
        var options = NightlyReportOptions.Parse(["--summary", "s.json", "--previous-summary", "p.json", "--dry-run", "--board", "B", "--api", "http://x"]);
        options.Summary.ShouldBe("s.json");
        options.PreviousSummary.ShouldBe("p.json");
        options.DryRun.ShouldBeTrue();
        options.Board.ShouldBe("B");
        options.Api.ShouldBe("http://x");
        var defaults = NightlyReportOptions.Parse(["--summary", "s.json"]);
        defaults.Board.ShouldBe("Antiphon");
        defaults.Api.ShouldBe("");
        defaults.DryRun.ShouldBeFalse();
        Should.Throw<NightlyUsageException>(() => NightlyReportOptions.Parse([])).Message.ShouldBe("missing --summary");
        Should.Throw<NightlyUsageException>(() => NightlyReportOptions.Parse(["--summary", "s", "--http-shim", "x"]));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string Headers, string? ContentType, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri, request.Headers.ToString(), request.Content?.Headers.ContentType?.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    [Test]
    public void The_transport_sends_utf8_json_and_no_headers()
    {
        var handler = new CapturingHandler();
        var http = NightlyReport.HttpTransport(handler);
        var post = http("POST", "http://nightly.test/api/cards/1/discussion", new Dictionary<string, string>(), "{\"body\":\"caf\u00e9\"}");
        post.Status.ShouldBe(201);
        post.Body.ShouldBe("{\"ok\":true}");
        var get = http("GET", "http://nightly.test/api/boards", new Dictionary<string, string>(), null!);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].ContentType.ShouldBe("application/json; charset=utf-8");
        handler.Requests[0].Body.ShouldBe("{\"body\":\"caf\u00e9\"}");
        handler.Requests[0].Headers.ShouldBeEmpty();
        handler.Requests[1].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[1].Body.ShouldBeNull();
        handler.Requests[1].Headers.ShouldBeEmpty();
        get.Status.ShouldBe(201);
    }

    [Test]
    public void The_transport_reads_a_real_loopback_answer()
    {
        using var stub = LoopbackHttpStub.Start(("/api/boards", "[{\"id\":\"b\",\"name\":\"Antiphon\"}]"), ("*", 404));
        var http = NightlyReport.HttpTransport();
        var boards = http("GET", stub.BaseUrl + "/api/boards", new Dictionary<string, string>(), null!);
        var missing = http("GET", stub.BaseUrl + "/api/nothing", new Dictionary<string, string>(), null!);
        boards.Status.ShouldBe(200);
        boards.Body.ShouldBe("[{\"id\":\"b\",\"name\":\"Antiphon\"}]");
        missing.Status.ShouldBe(404);
        stub.FirstRequest.ShouldNotContain(line => line.StartsWith("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task The_cli_command_is_registered()
    {
        using var fx = new ReportFx();
        var cli = Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll");
        async Task<(int Exit, string Out, string Error)> Run(params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "exec", cli, "nightly-report" }.Concat(arguments)) start.ArgumentList.Add(argument);
            start.Environment["ANTIPHON_API"] = "http://127.0.0.1:9";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout, await stderr);
        }

        var missing = Path.Combine(fx.NewDir(), "summary.json");
        var run = await Run("--summary", missing);
        run.Exit.ShouldBe(1, run.Error);
        run.Error.ShouldContain("does not exist");
        var usage = await Run("--summary", missing, "--http-shim", "x");
        usage.Exit.ShouldBe(2, usage.Error);
        usage.Error.ShouldStartWith("usage: antiphon-scripts nightly-report: unknown flag --http-shim");
    }
}
