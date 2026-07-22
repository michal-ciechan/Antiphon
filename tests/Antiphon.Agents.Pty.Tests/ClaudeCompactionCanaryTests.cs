using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Compaction canary (risk rows 5–6 of the Telegram-bot-agents epic): pins the two undocumented
/// surfaces compaction detection depends on —
///  1. the compact-boundary record Claude Code writes to the session JSONL (captured to and then
///     pinned against <c>tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl</c>, the single
///     source of truth for both <c>TranscriptNormalizer</c> and fakeclaude); and
///  2. the <c>Compacted (ctrl+o to see full summary)</c> screen line the fallback detector regexes.
///
/// First green run CAPTURES the fixture (writes it into the repo tree, to be committed); later
/// runs PIN it — a CLI version that changes the shape fails here, not in production.
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
public class ClaudeCompactionCanaryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    [Test]
    public async Task Compact_writes_a_boundary_entry_and_renders_the_compacted_screen_line()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        Console.WriteLine($"Test cwd: {Environment.CurrentDirectory}");
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        Console.WriteLine("READY SCREEN:\n" + runner.SnapshotScreen());
        runner.ClearLiveBuffer();

        // Two real turns with some substance so /compact has enough conversation to summarize
        // (a single one-word turn can be refused as too short to compact).
        await runner.SendLineAsync("List five common breeds of dog, one per line, then stop.");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
            .ShouldBeTrue("the first seed turn must complete before compacting");
        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Now list five common breeds of cat, one per line, then stop.");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
            .ShouldBeTrue("the second seed turn must complete before compacting");

        runner.ClearLiveBuffer();
        await runner.SendLineAsync("/compact");
        var compacted = await runner.WaitForOutputAsync(
            s => s.Contains("Compacted ("), TimeSpan.FromMinutes(4));
        compacted.ShouldBeTrue(
            "/compact must complete and render the Compacted line. Screen:\n" + runner.SnapshotScreen()
            + "\n---- raw tail ----\n" + Tail(runner.SnapshotText(), 2000));

        // Risk row 6: pin the exact screen line the fallback detector will regex.
        var screenLine = runner.SnapshotScreen()
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("Compacted ("));
        Console.WriteLine($"PINNED SCREEN LINE: {screenLine}");
        screenLine.ShouldNotBeNull();
        screenLine.ShouldContain("Compacted (ctrl+o to see full summary)");

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        // Risk row 5: find the session JSONL (~/.claude/projects/<encoded-cwd>/<session-id>.jsonl —
        // located by session id glob so cwd encoding stays Claude's business) and extract the
        // compact-boundary record.
        string? jsonlPath = null;
        for (var i = 0; i < 15 && jsonlPath is null; i++)
        {
            jsonlPath = FindSessionJsonl(sessionId);
            if (jsonlPath is null) await Task.Delay(1000);
        }
        if (jsonlPath is null)
        {
            var projects = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            var newest = Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(3)
                .Select(f => $"{f.LastWriteTimeUtc:o} {f.FullName}");
            Console.WriteLine("No JSONL for the session. Newest transcripts:\n  " + string.Join("\n  ", newest));
        }
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Console.WriteLine($"Session JSONL: {jsonlPath}");

        var boundaryLine = ExtractBoundaryLine(jsonlPath!);
        boundaryLine.ShouldNotBeNull("the transcript must contain a compact-boundary record");
        Console.WriteLine($"OBSERVED BOUNDARY LINE:\n{boundaryLine}");

        CaptureOrPinFixture(boundaryLine!);
    }

    private static string? FindSessionJsonl(string sessionId)
    {
        var projects = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects))
            return null;
        return Directory
            .EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    // The boundary is whatever record marks the compaction in the JSONL. Dump every candidate so a
    // shape change is fully visible in test output, then prefer the explicit system/compact record.
    private static string? ExtractBoundaryLine(string jsonlPath)
    {
        var candidates = new List<(string Line, string Why)>();
        foreach (var line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                var type = GetString(root, "type");
                var subtype = GetString(root, "subtype");
                if (subtype?.Contains("compact", StringComparison.OrdinalIgnoreCase) == true)
                    candidates.Add((line, $"subtype={subtype}"));
                else if (root.TryGetProperty("isCompactSummary", out var ics) && ics.ValueKind == JsonValueKind.True)
                    candidates.Add((line, "isCompactSummary=true"));
                else if (root.TryGetProperty("compactMetadata", out _))
                    candidates.Add((line, "has compactMetadata"));
                else if (type == "summary")
                    candidates.Add((line, "type=summary"));
            }
        }

        Console.WriteLine($"Compact-record candidates: {candidates.Count}");
        foreach (var (line, why) in candidates)
            Console.WriteLine($"  [{why}] {Truncate(line, 400)}");

        // Prefer the explicit boundary marker; fall back to the first compact-ish record.
        var best = candidates.FirstOrDefault(c => c.Why.StartsWith("subtype="));
        if (string.IsNullOrEmpty(best.Line))
            best = candidates.FirstOrDefault();
        return string.IsNullOrEmpty(best.Line) ? null : best.Line;
    }

    /// <summary>
    /// First green run captures the fixture into the repo tree (commit it); later runs pin the
    /// STRUCTURE (type/subtype + top-level key set) — uuids/timestamps/token counts may differ.
    /// </summary>
    private static void CaptureOrPinFixture(string observedLine)
    {
        var repoRoot = FindRepoRoot();
        var fixturePath = Path.Combine(
            repoRoot, "tests", "Antiphon.Tests", "Agents", "Fixtures", "compact-boundary.jsonl");

        if (!File.Exists(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, observedLine + "\n");
            Console.WriteLine($"FIXTURE CAPTURED (commit it): {fixturePath}");
            return;
        }

        var fixtureLine = File.ReadLines(fixturePath).First(l => !string.IsNullOrWhiteSpace(l));
        using var fixture = JsonDocument.Parse(fixtureLine);
        using var observed = JsonDocument.Parse(observedLine);

        GetString(observed.RootElement, "type").ShouldBe(GetString(fixture.RootElement, "type"),
            "compact-boundary record TYPE changed — CLI drift; re-observe and re-pin");
        GetString(observed.RootElement, "subtype").ShouldBe(GetString(fixture.RootElement, "subtype"),
            "compact-boundary record SUBTYPE changed — CLI drift; re-observe and re-pin");

        var fixtureKeys = fixture.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        var observedKeys = observed.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        observedKeys.ShouldBe(fixtureKeys,
            $"compact-boundary top-level keys changed — CLI drift; re-observe and re-pin. Fixture: {fixturePath}");
        Console.WriteLine("Fixture pin holds: shape unchanged.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string Tail(string s, int max) => s.Length <= max ? s : s[^max..];
}
