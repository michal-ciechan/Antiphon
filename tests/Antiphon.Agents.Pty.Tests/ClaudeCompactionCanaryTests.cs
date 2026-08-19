using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Compaction canary (risk rows 5–6 of the Telegram-bot-agents epic): pins the undocumented
/// surfaces compaction detection depends on —
///  1. the compact-boundary record Claude Code writes to the session JSONL (captured to and then
///     pinned against <c>tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl</c>, the single
///     source of truth for both <c>TranscriptNormalizer</c> and fakeclaude);
///  2. the <c>Compacted (ctrl+o to see full summary)</c> screen line the fallback detector regexes; and
///  3. (CARD-0041) the FULL record set a manual <c>/compact WITH ARGUMENTS</c> writes — raw typed
///     prompt, boundary with <c>trigger:"manual"</c>, <c>isCompactSummary</c> continuation, caveat,
///     and the command-wrapper pair — plus the decisive negative that NONE of them carries a
///     stop_reason. That set is <c>Fixtures/compact-full-manual.jsonl</c>. The sibling
///     <c>ClaudeLocalCommandCanaryTests</c> pinned the wrapper records on 2026-07-31 and missed
///     these two shapes, which is how a compacted session came to read "working" for two days.
///
/// First green run CAPTURES the fixtures (writes them into the repo tree, to be committed); later
/// runs PIN them — a CLI version that changes the shape fails here, not in production.
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[ParallelLimiter<ProcessSpawnLimit>]
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

        // ASSERT-AND-REPORT (CARD-0041): does a BARE /compact also write the raw typed prompt as a
        // plain user record? The live miss was a /compact WITH arguments, and the plan wants this
        // observed rather than guessed. Reported, not asserted — the fix does not depend on it.
        var bareRaw = File.ReadLines(jsonlPath!).Count(l => IsRawTypedCompactPrompt(l, "/compact"));
        Console.WriteLine($"BARE /compact raw typed user records: {bareRaw} "
            + "(0 = only the <command-name> wrapper is written for the bare form)");
    }

    /// <summary>
    /// CARD-0041's surface: a manual <c>/compact WITH ARGUMENTS</c> — the shape that left a session
    /// reading "working" for two days — writes SIX records, and the two that matter carry no marker
    /// any pre-existing rule recognised: the RAW typed prompt (a plain user record, in ADDITION to
    /// the <c>&lt;command-name&gt;</c> wrapper) and the continuation summary. The decisive negative
    /// is pinned too: NO record after the raw prompt carries a stop_reason, i.e. no TurnEnd is ever
    /// coming, which is why the boundary itself has to end the turn.
    ///
    /// Sibling canary: <c>ClaudeLocalCommandCanaryTests</c> pinned the wrapper records on
    /// 2026-07-31 and missed exactly this shape.
    /// </summary>
    [Test]
    public async Task Manual_compact_with_args_writes_raw_prompt_boundary_and_continuation_with_no_turn_end()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");
        const string typed = "/compact Keep the dog and cat lists in the summary.";

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        await runner.SendLineAsync("List five common breeds of dog, one per line, then stop.");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
            .ShouldBeTrue("the first seed turn must complete before compacting. Screen:\n" + runner.SnapshotScreen());
        runner.ClearLiveBuffer();
        await runner.SendLineAsync("Now list five common breeds of cat, one per line, then stop.");
        (await runner.WaitForOutputAsync(s => DonePattern.IsMatch(s), TimeSpan.FromMinutes(3)))
            .ShouldBeTrue("the second seed turn must complete before compacting. Screen:\n" + runner.SnapshotScreen());

        runner.ClearLiveBuffer();
        await runner.SendLineAsync(typed);
        (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromMinutes(4)))
            .ShouldBeTrue("/compact WITH ARGUMENTS must complete. Screen:\n" + runner.SnapshotScreen());

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        string? jsonlPath = null;
        for (var i = 0; i < 15 && jsonlPath is null; i++)
        {
            jsonlPath = FindSessionJsonl(sessionId);
            if (jsonlPath is null) await Task.Delay(1000);
        }
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");

        var all = File.ReadLines(jsonlPath!).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var rawIndex = Array.FindIndex(all, l => IsRawTypedCompactPrompt(l, typed));
        Console.WriteLine($"raw typed record index: {rawIndex} of {all.Length}");
        rawIndex.ShouldBeGreaterThanOrEqualTo(0,
            "Claude must record the literal typed text as a plain user record — this is shape 1 of "
            + "CARD-0041, the record no exclusion matched");

        // The compaction's own slice: from the typed prompt through its <local-command-stdout>
        // record. Everything after belongs to the next thing the session does (/exit here).
        var afterRaw = all.Skip(rawIndex).ToArray();
        var stdoutIndex = Array.FindIndex(
            afterRaw, l => (UserContent(l) ?? "").Contains("<local-command-stdout>"));
        var tail = stdoutIndex >= 0 ? afterRaw.Take(stdoutIndex + 1).ToArray() : afterRaw;

        // The boundary, with its MANUAL trigger — the fact the whole fix keys on.
        var boundary = tail.FirstOrDefault(l => GetStringAt(l, "subtype") == "compact_boundary");
        boundary.ShouldNotBeNull("a manual /compact must write a compact_boundary record");
        using (var doc = JsonDocument.Parse(boundary!))
        {
            doc.RootElement.TryGetProperty("compactMetadata", out var meta).ShouldBeTrue();
            GetString(meta, "trigger").ShouldBe("manual",
                "the trigger is what separates a between-turns /compact from a mid-turn auto compaction");
        }

        // The continuation summary: structural flag AND the exact wording the rules match by text.
        var continuation = tail.FirstOrDefault(IsCompactSummaryRecord);
        continuation.ShouldNotBeNull("compaction must write its continuation summary as a user record");
        var continuationText = UserContent(continuation!);
        Console.WriteLine($"CONTINUATION TEXT (first 160): {Truncate(continuationText ?? "", 160)}");
        continuationText.ShouldNotBeNull();
        continuationText!.TrimStart().ShouldStartWith(
            Antiphon.SessionRunner.Contracts.TranscriptKinds.CompactionContinuationPromptPrefix,
            customMessage: "the prefix the working rules match on has drifted — re-pin it in TranscriptKinds");
        Antiphon.SessionRunner.Contracts.TranscriptKinds
            .IsCompactionContinuationPrompt("UserPrompt", continuationText).ShouldBeTrue();

        // The local-command wrapper pair (the 2026-07-31 canary's shape) rides along.
        tail.ShouldContain(l => (UserContent(l) ?? "").Contains("<command-name>"));
        tail.ShouldContain(l => (UserContent(l) ?? "").Contains("<local-command-stdout>"));
        // Reported, not asserted: the isMeta caveat is dropped by the normalizer either way.
        Console.WriteLine($"isMeta records after the raw prompt: {tail.Count(IsMetaRecord)}");

        // THE fact the card rests on: nothing after the typed prompt ends a turn.
        foreach (var line in tail)
            StopReasonOf(line).ShouldBeNull($"no stop_reason may follow the /compact prompt. Line: {Truncate(line, 300)}");

        CaptureOrPinFullFixture(tail);
    }

    private static bool IsRawTypedCompactPrompt(string line, string typed)
    {
        var content = UserContent(line);
        return content is not null
            && content.TrimEnd() == typed.TrimEnd()
            && !IsCompactSummaryRecord(line)
            && !IsMetaRecord(line);
    }

    private static bool IsCompactSummaryRecord(string line) =>
        BoolAt(line, "isCompactSummary");

    private static bool IsMetaRecord(string line) => BoolAt(line, "isMeta");

    private static bool BoolAt(string line, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static string? GetStringAt(string line, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return GetString(doc.RootElement, prop);
        }
        catch (JsonException) { return null; }
    }

    // Claude writes user content either as a bare string or as an array of blocks.
    private static string? UserContent(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (GetString(doc.RootElement, "type") != "user") return null;
            if (!doc.RootElement.TryGetProperty("message", out var msg)) return null;
            if (!msg.TryGetProperty("content", out var content)) return null;
            if (content.ValueKind == JsonValueKind.String) return content.GetString();
            if (content.ValueKind != JsonValueKind.Array) return null;
            return string.Join("\n", content.EnumerateArray()
                .Select(b => GetString(b, "text"))
                .Where(t => t is not null));
        }
        catch (JsonException) { return null; }
    }

    private static string? StopReasonOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("message", out var msg) ? GetString(msg, "stop_reason") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// First green run captures the compaction slice (commit it); later runs pin its SHAPE — the
    /// ordered sequence of (type, subtype, isCompactSummary, isMeta) over the <c>user</c> and
    /// <c>system</c> records, which is exactly what the working rules consume. Bodies, uuids and
    /// timestamps are free to differ, and Claude's own bookkeeping records (<c>attachment</c>,
    /// <c>last-prompt</c>, <c>ai-title</c>, <c>mode</c>, …) are ignored: they vary run to run and
    /// <c>TranscriptNormalizer</c> never looks at them.
    /// </summary>
    private static void CaptureOrPinFullFixture(string[] tail)
    {
        var repoRoot = FindRepoRoot();
        var fixturePath = Path.Combine(
            repoRoot, "tests", "Antiphon.Tests", "Agents", "Fixtures", "compact-full-manual.jsonl");

        static string[] Shapes(IEnumerable<string> lines) => lines
            .Where(l => GetStringAt(l, "type") is "user" or "system")
            .Select(l => $"{GetStringAt(l, "type")}/{GetStringAt(l, "subtype") ?? "-"}"
                + $"/summary={BoolAt(l, "isCompactSummary")}/meta={BoolAt(l, "isMeta")}")
            .ToArray();

        Console.WriteLine("OBSERVED SLICE SHAPES:\n  " + string.Join("\n  ", Shapes(tail)));

        if (!File.Exists(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllLines(fixturePath, tail);
            Console.WriteLine($"FULL FIXTURE CAPTURED (commit it): {fixturePath}");
            return;
        }

        Shapes(tail).ShouldBe(
            Shapes(File.ReadLines(fixturePath).Where(l => !string.IsNullOrWhiteSpace(l))),
            $"the post-compaction record SEQUENCE changed — CLI drift; re-observe and re-pin. Fixture: {fixturePath}");
        Console.WriteLine("Full-fixture pin holds: post-compaction record sequence unchanged.");
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
