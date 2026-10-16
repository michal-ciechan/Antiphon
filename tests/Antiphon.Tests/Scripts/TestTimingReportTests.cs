using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1313 S5. The report reads durable history and never changes a selection.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingReportTests : CheckpointTestBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Test]
    public async Task C1313_ComparableHistory()
    {
        var root = Fresh("history");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var events = new List<object>();
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("main", value, started.AddMinutes(value), value, candidateSha: "sha-" + value));
        events.Add(Sample("main", 1, started, 1, membership: "other-mem"));
        events.Add(Sample("main", 1, started, 2, os: "windows"));
        events.Add(Sample("main", 1, started, 3, runtime: "8.0.0"));
        events.Add(Sample("main", 1, started, 4, concurrency: 8));
        events.Add(Sample("main", 1, started, 5, host: "host-b"));
        for (var value = 1; value <= 9; value++)
            events.Add(Sample("short", value, started.AddMinutes(value), value));
        foreach (var value in new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 })
            events.Add(Sample("zeros", value, started.AddMinutes(events.Count), events.Count));
        WriteEvents(root, events);

        using var doc = await Report(root);
        var main = One(doc, "main", "mem");
        main.GetProperty("sampleCount").GetInt32().ShouldBe(10, "comparable-series");
        main.GetProperty("median").GetDouble().ShouldBe(5.5);
        main.GetProperty("p90").GetDouble().ShouldBe(9);
        main.GetProperty("latest").GetDouble().ShouldBe(10);
        main.GetProperty("priorFiveMedian").GetDouble().ShouldBe(3);
        main.GetProperty("latestFiveMedian").GetDouble().ShouldBe(8);
        main.GetProperty("absoluteChange").GetDouble().ShouldBe(5);
        Math.Abs(main.GetProperty("percentageChange").GetDouble() - (500d / 3d)).ShouldBeLessThan(0.001);
        main.GetProperty("insufficientHistory").GetBoolean().ShouldBeFalse();
        Groups(doc).Count(item => item.GetProperty("setId").GetString() == "main" && item.GetProperty("sampleCount").GetInt32() == 1)
            .ShouldBe(5, "comparable-series");

        var brief = One(doc, "short", "mem");
        brief.GetProperty("insufficientHistory").GetBoolean().ShouldBeTrue();
        Missing(brief, "absoluteChange").ShouldBeTrue();
        Missing(brief, "percentageChange").ShouldBeTrue();

        var zeros = One(doc, "zeros", "mem");
        zeros.GetProperty("priorFiveMedian").GetDouble().ShouldBe(0);
        Missing(zeros, "percentageChange").ShouldBeTrue();
    }

    [Test]
    public async Task C1313_AttemptCost()
    {
        var root = Fresh("cost");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        WriteEvents(root,
        [
            Cost("e1", 10, "passed", "ordinary", "build-a", 5, started, 1),
            Cost("e1", 10, "passed", "ordinary", "build-a", 5, started, 1),
            Cost("e2", 20, "passed", "ordinary", "build-a", 5, started.AddMinutes(1), 2),
            Cost("e3", 30, "passed", "ordinary", "build-b", 4, started.AddMinutes(2), 3),
            Cost("e4", 100, "failed", "ordinary", "build-a", 5, started.AddMinutes(3), 4),
            Cost("e5", 50, "passed", "retry", "build-a", 5, started.AddMinutes(4), 5),
        ]);
        using var doc = await Report(root);
        var group = One(doc, "cost", "mem");
        group.GetProperty("median").GetDouble().ShouldBe(20, "attempt-cost");
        group.GetProperty("p90").GetDouble().ShouldBe(30, "attempt-cost");
        group.GetProperty("sampleCount").GetInt32().ShouldBe(3);
        group.GetProperty("totalAttemptedCost").GetDouble().ShouldBe(219, "attempt-cost");
    }

    [Test]
    public async Task C1313_EstimatesNeverSelect()
    {
        var root = Fresh("estimates");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var events = new List<object>();
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("A", value, started.AddMinutes(value), value));
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("B", 100000, started.AddMinutes(value), value));
        for (var value = 1; value <= 4; value++)
            events.Add(Sample("D", value, started.AddMinutes(value), value));
        WriteEvents(root, events);
        var selection = Path.Combine(Fresh("selection"), "selection.json");
        await File.WriteAllTextAsync(selection, """
            {"digest":"fixed-digest","activation":"unqualified","executionEvidence":false,"selectedSets":[{"id":"A"},{"id":"B"},{"id":"C"},{"id":"D"}]}
            """);

        using var doc = await Report(root, selection);
        doc.RootElement.GetProperty("digest").GetString().ShouldBe("fixed-digest");
        doc.RootElement.GetProperty("activation").GetString().ShouldBe("unqualified");
        doc.RootElement.GetProperty("executionEvidence").GetBoolean().ShouldBeFalse();
        var ids = doc.RootElement.GetProperty("selectedSets").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        ids.ShouldBe(new[] { "A", "B", "C", "D" }, "budget-never-omits");
        doc.RootElement.GetProperty("budgetObservations").EnumerateArray().Select(item => item.GetString()).ShouldContain("budget-observed");
        Estimate(doc, "A").GetProperty("unbudgeted").GetBoolean().ShouldBeFalse();
        Estimate(doc, "A").GetProperty("provisional").GetBoolean().ShouldBeFalse();
        Estimate(doc, "C").GetProperty("unbudgeted").GetBoolean().ShouldBeTrue();
        Estimate(doc, "D").GetProperty("provisional").GetBoolean().ShouldBeTrue();
        Estimate(doc, "D").GetProperty("sampleCount").GetInt32().ShouldBe(4);
    }

    private static object Sample(string setId, double window, DateTimeOffset started, int sequence, string membership = "mem",
        string os = "linux", string runtime = "9.0.0", int concurrency = 1, string host = "host-a", string candidateSha = "sha") => new
    {
        eventKind = "set-completed",
        setKind = "class",
        setId,
        membershipDigest = membership,
        measurementKind = "inclusive-case-window",
        os,
        architecture = "x64",
        runtimeVersion = runtime,
        concurrency,
        profile = "rc",
        hostId = host,
        repositoryIdentity = "repo",
        candidateSha,
        outcome = "passed",
        purpose = "ordinary",
        caseWindowSeconds = window,
        wallSeconds = window,
        startedAt = started,
        sequence,
    };

    private static object Cost(string eventId, double wall, string outcome, string purpose, string buildId, double buildSeconds,
        DateTimeOffset started, int sequence) => new
    {
        eventId,
        eventKind = "set-completed",
        setKind = "class",
        setId = "cost",
        membershipDigest = "mem",
        measurementKind = "inclusive-case-window",
        os = "linux",
        architecture = "x64",
        runtimeVersion = "9.0.0",
        concurrency = 1,
        profile = "rc",
        hostId = "host-a",
        repositoryIdentity = "repo",
        outcome,
        purpose,
        caseWindowSeconds = wall,
        wallSeconds = wall,
        producingBuildId = buildId,
        buildSeconds,
        startedAt = started,
        sequence,
    };

    private async Task<JsonDocument> Report(string root, string? selection = null)
    {
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "test-timing-report.ps1");
        var arguments = "-NoProfile -File \"" + script + "\" -Roots \"" + root + "\" -GroupBy class -Json";
        if (selection is not null)
            arguments += " -SelectionPath \"" + selection + "\"";
        var start = new ProcessStartInfo("pwsh", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        RegisterCheckpointChild(process);
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, stderr + stdout);
        return JsonDocument.Parse(stdout);
    }

    private string Fresh(string name)
    {
        var path = Path.Combine(TempDir(), name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteEvents(string root, IEnumerable<object> events)
    {
        var lines = events.Select(item => JsonSerializer.Serialize(item, Json));
        File.WriteAllText(Path.Combine(root, "events.jsonl"), string.Join('\n', lines) + "\n");
    }

    private static JsonElement One(JsonDocument doc, string setId, string membership) =>
        Groups(doc).Single(item => item.GetProperty("setId").GetString() == setId
            && item.GetProperty("membershipDigest").GetString() == membership);

    private static JsonElement Estimate(JsonDocument doc, string setId) =>
        doc.RootElement.GetProperty("estimates").EnumerateArray().Single(item => item.GetProperty("setId").GetString() == setId);

    private static IEnumerable<JsonElement> Groups(JsonDocument doc)
    {
        var groups = doc.RootElement.GetProperty("groups");
        if (groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in groups.EnumerateArray())
                yield return item;
            yield break;
        }

        yield return groups;
    }

    private static bool Missing(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
}
