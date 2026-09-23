using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// CARD-0599 R-4, delivery half (DL-2). The production <c>scripts/nightly-report.ps1</c> is run
/// against a REAL isolated Program and database - not a fake HTTP shim - and the verdict is a
/// recipient-side GET that finds the whole persisted card. A request the API accepted is not a
/// receipt; only the stored row read back through the API is.
///
/// <para>The decisive behaviour: a release-candidate run files its own release-gate incident and
/// leaves an open master nightly card untouched, and vice versa. Restarting the same intent after
/// an unavailable recipient delivers exactly one logical report, not two.</para>
/// </summary>
[NotInParallel]
[Category("OptIn")]
public class ReleaseGateReportDeliveryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AntiphonAppFixture _app = new();
    private string _root = null!;
    private Guid _boardId;
    private string _boardName = null!;

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _app.InitializeAsync();
        _root = Directory.CreateTempSubdirectory("antiphon-c599-report").FullName;

        using var client = _app.CreateClient();
        var project = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "c599 report " + Guid.NewGuid().ToString("N")[..8],
            gitRepositoryUrl = "https://github.com/example/c599-report.git",
            localRepositoryPath = (string?)null,
            baseBranch = "main",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
        }, JsonOptions);
        project.EnsureSuccessStatusCode();
        var projectId = (await project.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        _boardName = "c599 lane " + Guid.NewGuid().ToString("N")[..8];
        var board = await client.PostAsJsonAsync("/api/boards", new { projectId, name = _boardName }, JsonOptions);
        board.EnsureSuccessStatusCode();
        _boardId = (await board.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _app.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// A red run of each lane files its own incident, and the stored card read back through the
    /// API carries that lane's label and title. Neither lane can see the other's card.
    /// </summary>
    [Test]
    public async Task C599_ReportReadback()
    {
        var rc = await RunReportAsync(WriteSummary("rc-red", succeeded: false, profile: "rc"));
        rc.ExitCode.ShouldBe(0, $"C599 G-readback: the rc report must succeed\n{rc.Output}");

        var afterRc = await ListCardsAsync();
        var rcCards = afterRc.Where(c => Labels(c).Contains("release-gate")).ToList();
        rcCards.Count.ShouldBe(1, $"C599 G-readback: exactly one persisted release-gate card\n{rc.Output}");
        Title(rcCards[0]).ShouldStartWith("Release-candidate red", Case.Sensitive, "C599 G-readback: the stored rc title names the lane");
        Labels(rcCards[0]).ShouldNotContain("nightly", "C599 G-readback: the stored rc card is not labelled nightly");
        afterRc.Count(c => Labels(c).Contains("nightly")).ShouldBe(0, "C599 G-readback: the rc run filed no nightly card");

        var master = await RunReportAsync(WriteSummary("master-red", succeeded: false, profile: null));
        master.ExitCode.ShouldBe(0, $"C599 G-readback: the master report must succeed\n{master.Output}");

        var afterMaster = await ListCardsAsync();
        var nightlyCards = afterMaster.Where(c => Labels(c).Contains("nightly")).ToList();
        nightlyCards.Count.ShouldBe(1, $"C599 G-readback: exactly one persisted nightly card\n{master.Output}");
        Title(nightlyCards[0]).ShouldStartWith("Nightly red", Case.Sensitive, "C599 G-readback: the stored master title names the lane");

        // Both incidents coexist: the master run did not adopt, retitle or close the rc card.
        afterMaster.Count(c => Labels(c).Contains("release-gate")).ShouldBe(1,
            "C599 G-readback: the master run left the rc incident intact");
        var rcAfter = afterMaster.Single(c => Labels(c).Contains("release-gate"));
        Title(rcAfter).ShouldStartWith("Release-candidate red", Case.Sensitive, "C599 G-readback: the rc card kept its own title");
        Id(rcAfter).ShouldBe(Id(rcCards[0]), "C599 G-readback: it is the same stored card, not a replacement");
    }

    /// <summary>
    /// Recovery: an unavailable recipient produces no card and no green, and re-running the same
    /// intent once the API is reachable delivers exactly ONE logical report, not a duplicate.
    /// </summary>
    [Test]
    public async Task C599_ReportRecovery()
    {
        var summary = WriteSummary("rc-red-recovery", succeeded: false, profile: "rc");

        // Recipient unavailable: point the script at a dead port on this host.
        var dead = await RunReportAsync(summary, apiOverride: "http://127.0.0.1:9");
        dead.ExitCode.ShouldNotBe(0, $"C599 G-recovery: a dead API is not a delivered report\n{dead.Output}");
        (await ListCardsAsync()).Count.ShouldBe(0, "C599 G-recovery: a failed delivery persisted nothing");

        // The durable local record survives so the operator can see the intended write.
        var cardMd = Path.Combine(Path.GetDirectoryName(summary)!, "card.md");
        File.Exists(cardMd).ShouldBeTrue($"C599 G-recovery: a dead API still writes the durable local record\n{dead.Output}");

        // Restart the same intent against the live recipient.
        var first = await RunReportAsync(summary);
        first.ExitCode.ShouldBe(0, $"C599 G-recovery: the retry delivers\n{first.Output}");
        var afterFirst = await ListCardsAsync();
        afterFirst.Count(c => Labels(c).Contains("release-gate")).ShouldBe(1,
            $"C599 G-recovery: exactly one card after recovery\n{first.Output}");

        // Running it again must update that one card, never create a second incident.
        var second = await RunReportAsync(summary);
        second.ExitCode.ShouldBe(0, $"C599 G-recovery: the repeat run succeeds\n{second.Output}");
        var afterSecond = await ListCardsAsync();
        afterSecond.Count(c => Labels(c).Contains("release-gate")).ShouldBe(1,
            $"C599 G-recovery: still exactly one logical report after a repeat\n{second.Output}");
        Id(afterSecond.Single(c => Labels(c).Contains("release-gate")))
            .ShouldBe(Id(afterFirst.Single(c => Labels(c).Contains("release-gate"))),
                "C599 G-recovery: the repeat updated the same stored card");
    }

    private string WriteSummary(string name, bool succeeded, string? profile)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var summary = new Dictionary<string, object?>
        {
            ["startedAt"] = "2026-09-23T00:30:00.0000000Z",
            ["completedAt"] = "2026-09-23T01:45:00.0000000Z",
            ["sha"] = "f6856040deadbeeff6856040deadbeeff6856040",
            ["gitRef"] = profile == "rc" ? "release/rc-20260923T083000Z" : "origin/master",
            ["trigger"] = profile == "rc" ? "rc" : "scheduled",
            ["runId"] = Guid.NewGuid().ToString(),
            ["logDir"] = dir,
            ["outcome"] = succeeded ? "green" : "TESTS",
            ["succeeded"] = succeeded,
            ["coverageComplete"] = succeeded,
            ["testsPassed"] = succeeded,
            ["policyHash"] = "policy-hash-c599",
            ["reasons"] = Array.Empty<string>(),
            ["builds"] = Array.Empty<object>(),
            ["suites"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "client", ["name"] = "client", ["log"] = "", ["exitCode"] = succeeded ? 0 : 1,
                    ["timedOut"] = false, ["skipped"] = false, ["result"] = succeeded ? "pass" : "FAIL",
                    ["countsParsed"] = true, ["passed"] = 460, ["failed"] = succeeded ? 0 : 1,
                    ["skippedCount"] = 0, ["durationSeconds"] = 350, ["failedTests"] = Array.Empty<string>(),
                },
            },
        };
        if (profile is not null)
        {
            summary["profile"] = profile;
            summary["candidateId"] = "rc-20260923T083000Z";
        }

        var path = Path.Combine(dir, "summary.json");
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private async Task<(int ExitCode, string Output)> RunReportAsync(string summaryPath, string? apiOverride = null)
    {
        var script = Path.Combine(RepositoryRoot(), "scripts", "nightly-report.ps1");
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File", script,
                     "-Summary", summaryPath,
                     "-Board", _boardName,
                     "-Api", apiOverride ?? _app.BaseAddress,
                 })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>Recipient-side read: what the API actually stored, never the script's own claim.</summary>
    private async Task<List<JsonElement>> ListCardsAsync()
    {
        using var client = _app.CreateClient();
        var all = new List<JsonElement>();
        foreach (var status in new[] { "Backlog", "InProgress", "Review", "NeedsDecision", "Done" })
        {
            var response = await client.GetAsync($"/api/cards?boardId={_boardId}&status={status}");
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var rows = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("cards", out var cards)
                ? cards
                : body;
            if (rows.ValueKind != JsonValueKind.Array) continue;
            all.AddRange(rows.EnumerateArray());
        }
        return all;
    }

    private static List<string> Labels(JsonElement card) =>
        card.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
            ? labels.EnumerateArray().Select(l => l.GetString() ?? string.Empty).ToList()
            : [];

    private static string Title(JsonElement card) =>
        card.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;

    private static Guid Id(JsonElement card) => card.GetProperty("id").GetGuid();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Antiphon.sln");
    }
}
