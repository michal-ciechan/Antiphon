using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Local-command canary (live miss 2026-07-31: after <c>/model</c> — and again after <c>/clear</c> —
/// the session read as permanently "working" and WhenIdle deliveries stranded). Pins, against REAL
/// Claude, the JSONL shape the working/idle fix depends on:
///
///  * <c>/clear</c> FORKS a fresh conversation file (self-chosen uuid, same project dir); the
///    ORIGINAL session file gains nothing. The runner's TranscriptTailer carries the session over
///    via its cwd-based fork re-discovery — that is the only reason post-/clear activity is seen
///    at all.
///  * The fork's first records are: an isMeta caveat, then a USER record whose text starts with
///    <c>&lt;command-name&gt;/clear</c> — and NO assistant record / no turn-completion follows
///    (no API call happens). That record must match
///    <see cref="TranscriptKinds.IsLocalCommandRecord"/> — the single predicate the server's
///    IsWorkingAsync and the client's isWorking() exclude from activity (their handling is pinned
///    by SessionMessageQueueServiceTests and SessionTranscriptPanel.test.tsx; fakeclaude mirrors
///    the record shape, pinned by FakeClaudeContractTests). Counting it as activity is exactly
///    what read "working" forever and stranded queued deliveries.
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeLocalCommandCanaryTests
{
    [Test]
    public async Task Clear_writes_a_command_record_and_no_turn_completion_after_it()
    {
        ClSession.SkipIfNotEligible();
        var sessionId = Guid.NewGuid().ToString("D");
        var testStartUtc = DateTime.UtcNow.AddSeconds(-5);

        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions", "--session-id", sessionId);
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        // A completed real turn first: guarantees the lazily-created session JSONL exists and gives
        // the working/idle fold a genuine TurnEnd to sit before the command records.
        await runner.SendLineAsync("Reply with the single word PONG and nothing else.");
        (await runner.WaitForOutputAsync(
            s => System.Text.RegularExpressions.Regex.IsMatch(s, @" for \d+s"), TimeSpan.FromMinutes(2)))
            .ShouldBeTrue("the first turn must complete. Screen:\n" + runner.SnapshotScreen());

        // The original session file must exist before /clear (created by the completed turn above).
        string? jsonlPath = null;
        for (var i = 0; i < 15 && jsonlPath is null; i++)
        {
            jsonlPath = FindSessionJsonl(sessionId);
            if (jsonlPath is null) await Task.Delay(1000);
        }
        jsonlPath.ShouldNotBeNull($"session JSONL for {sessionId} must exist under ~/.claude/projects");
        Console.WriteLine($"Session JSONL: {jsonlPath}");
        var projectDir = Path.GetDirectoryName(jsonlPath!)!;

        // The command under test. Typed slowly on purpose: "/clear" opens the slash-command
        // autocomplete, and an Enter that races the dropdown can land as selection-accept instead
        // of execute. The rendered screen is NOT reliable evidence here (old scrollback rows can
        // survive a successful clear) — the LOG evidence below is the gate.
        await runner.WriteAsync("/clear");
        await Task.Delay(800);
        await runner.WriteAsync("\r");

        // LOG evidence, and the executed-gate in one: /clear forks a FRESH conversation file —
        // the command record lands in the fork, and the original session file gains nothing
        // (observed against real Claude 2026-07-31; the runner TranscriptTailer's cwd-based fork
        // re-discovery is what keeps the session tracked afterwards).
        (string Text, int LineIndex) commandRecord = default;
        string? forkPath = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline && forkPath is null)
        {
            foreach (var candidate in Directory.EnumerateFiles(projectDir, "*.jsonl"))
            {
                if (string.Equals(candidate, jsonlPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Only forks created by THIS run — earlier canary runs leave their own forks behind.
                if (File.GetLastWriteTimeUtc(candidate) < testStartUtc)
                    continue;
                var record = ReadUserTexts(candidate).FirstOrDefault(t =>
                    t.Text.TrimStart().StartsWith("<command-name>/clear", StringComparison.Ordinal));
                if (record.Text is not null)
                {
                    forkPath = candidate;
                    commandRecord = record;
                    break;
                }
            }
            if (forkPath is null) await Task.Delay(1000);
        }
        Console.WriteLine("POST-/clear SCREEN (excerpt):\n" + Tail(runner.SnapshotScreen(), 600));

        await runner.SendLineAsync("/exit");
        await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(10)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));

        forkPath.ShouldNotBeNull(
            "/clear must fork a fresh conversation file carrying the <command-name>/clear record "
            + $"in {projectDir}. Screen:\n" + runner.SnapshotScreen());
        Console.WriteLine($"FORK JSONL: {forkPath}");

        // The original file must NOT contain the command record (fork semantics).
        ReadUserTexts(jsonlPath!)
            .Any(t => t.Text.TrimStart().StartsWith("<command-name>/clear", StringComparison.Ordinal))
            .ShouldBeFalse("/clear must fork — its record must not land in the original session file");
        Console.WriteLine($"OBSERVED COMMAND RECORD: {Truncate(commandRecord.Text, 200)}");

        // The record must match the shared classifier the working checks build on.
        TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, commandRecord.Text)
            .ShouldBeTrue("TranscriptKinds.IsLocalCommandRecord must match what real Claude writes");
        // And it must NOT look like an interrupt (the other no-TurnEnd marker family).
        TranscriptKinds.IsInterruptPrompt(TranscriptKinds.UserPrompt, commandRecord.Text).ShouldBeFalse();

        // No assistant record (so no turn completion of ANY kind) may follow the command record —
        // the exact absence that stranded working/idle before the fix.
        CountAssistantLinesAfter(forkPath!, commandRecord.LineIndex).ShouldBe(0,
            "a local command makes no API call — nothing assistant-shaped may follow its record");
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

    /// <summary>All user-record texts in file order (string content or text blocks), with line index.</summary>
    private static List<(string Text, int LineIndex)> ReadUserTexts(string jsonlPath)
    {
        var results = new List<(string, int)>();
        var index = -1;
        foreach (var line in File.ReadLines(jsonlPath))
        {
            index++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "user")
                    continue;
                if (!root.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("content", out var content))
                    continue;

                if (content.ValueKind == JsonValueKind.String && content.GetString() is { } s)
                    results.Add((s, index));
                else if (content.ValueKind == JsonValueKind.Array)
                    foreach (var block in content.EnumerateArray())
                        if (block.TryGetProperty("text", out var text) && text.GetString() is { } t)
                            results.Add((t, index));
            }
        }
        return results;
    }

    private static int CountAssistantLinesAfter(string jsonlPath, int lineIndex)
    {
        var count = 0;
        var index = -1;
        foreach (var line in File.ReadLines(jsonlPath))
        {
            index++;
            if (index <= lineIndex || string.IsNullOrWhiteSpace(line))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "assistant")
                    count++;
            }
        }
        return count;
    }

    private static string Truncate(string? s, int n) =>
        s is null ? "<null>" : s.Length <= n ? s : s[..n] + "…";

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}
