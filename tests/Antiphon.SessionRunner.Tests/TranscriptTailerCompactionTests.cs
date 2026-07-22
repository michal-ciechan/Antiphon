using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// File-driven tailer coverage (the first tailer tests — none existed before PR 6): the REAL
/// <see cref="TranscriptTailer"/> tail loop must emit a <c>CompactBoundary</c> part when the
/// pinned compact-boundary line (Fixtures/compact-boundary.jsonl, captured by
/// ClaudeCompactionCanaryTests) is appended to a session JSONL. Uses CLAUDE_CONFIG_DIR to point
/// the tailer's projects-root resolution at a temp tree — no real ~/.claude involved.
/// </summary>
[NotInParallel("ClaudeConfigDirEnv")] // mutates the process-wide CLAUDE_CONFIG_DIR variable
public class TranscriptTailerCompactionTests
{
    private static string FixtureLine() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "compact-boundary.jsonl"))
            .First(l => !string.IsNullOrWhiteSpace(l));

    [Test]
    public async Task Tailer_emits_CompactBoundary_event_for_boundary_line()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"antiphon-tailer-test-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(configDir, "projects", "some-encoded-cwd");
        Directory.CreateDirectory(projectDir);
        var sessionId = Guid.NewGuid();
        var jsonlPath = Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        try
        {
            var hub = new SessionRunnerEventHub();
            var tailer = new TranscriptTailer(sessionId, Path.GetTempPath(), hub, NullLogger.Instance);
            tailer.Start();
            try
            {
                // A normal turn first, then the pinned boundary — mirrors a real post-turn compaction.
                await File.AppendAllTextAsync(jsonlPath,
                    """{"type":"user","uuid":"u1","message":{"role":"user","content":"hello"}}""" + "\n");
                await File.AppendAllTextAsync(jsonlPath, FixtureLine() + "\n");

                var entries = await PollForEntriesAsync(tailer, want: 2, TimeSpan.FromSeconds(15));

                entries.Select(e => e.Kind).ShouldBe(
                    [TranscriptKinds.UserPrompt, TranscriptKinds.CompactBoundary]);
                var boundary = entries[^1];
                boundary.StopReason.ShouldBeNull("a compaction is not a turn end");
                boundary.Text.ShouldBe("Context compacted (manual)");
            }
            finally
            {
                await tailer.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            try { Directory.Delete(configDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // Interactive Claude forks --session-id: the transcript lands in a self-chosen <uuid>.jsonl.
    // The tailer must discover it by matching the session cwd, or reply routing silently breaks.
    [Test]
    public async Task Tailer_discovers_forked_transcript_by_cwd_when_session_id_is_not_honored()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"antiphon-tailer-fork-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(configDir, "projects", "C--src-ClaudeBot-agents-family");
        Directory.CreateDirectory(projectDir);
        var cwd = Path.Combine(Path.GetTempPath(), $"agent-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        // A stale transcript from an EARLIER session in the SAME cwd (must not be adopted).
        var stale = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
        await File.WriteAllTextAsync(stale, UserLine("old", cwd, "stale") + "\n");
        // A transcript for a DIFFERENT cwd (must not be adopted).
        var otherCwd = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
        await File.WriteAllTextAsync(otherCwd, UserLine("other", Path.GetTempPath(), "other") + "\n");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        try
        {
            var antiphonSessionId = Guid.NewGuid(); // the id we PASSED; Claude will ignore it
            var hub = new SessionRunnerEventHub();
            var tailer = new TranscriptTailer(antiphonSessionId, cwd, hub, NullLogger.Instance);
            tailer.Start();
            try
            {
                // Claude forks: writes to a NEW <uuid>.jsonl (not the antiphon id) after start.
                await Task.Delay(500);
                var forked = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
                await File.WriteAllTextAsync(forked, UserLine("u1", cwd, "hello") + "\n");
                await File.AppendAllTextAsync(forked, FixtureLine() + "\n"); // + a compact boundary

                // The tailer must adopt the forked file (grace is 10s) and emit its entries.
                var entries = await PollForEntriesAsync(tailer, want: 2, TimeSpan.FromSeconds(20));

                entries.Select(e => e.Kind).ShouldBe(
                    [TranscriptKinds.UserPrompt, TranscriptKinds.CompactBoundary]);
                entries[0].Text.ShouldBe("hello", "must adopt the forked file, not the stale/other-cwd ones");
            }
            finally
            {
                await tailer.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            try { Directory.Delete(configDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
        }
    }

    // A minimal Claude "user" JSONL line carrying a cwd field (what the tailer discovers by).
    private static string UserLine(string uuid, string cwd, string text) => JsonSerializer.Serialize(new
    {
        type = "user",
        uuid,
        cwd,
        message = new { role = "user", content = text },
    });

    private static async Task<IReadOnlyList<RunnerTranscriptEvent>> PollForEntriesAsync(
        TranscriptTailer tailer, int want, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = tailer.Snapshot();
            if (snapshot.Entries.Count >= want)
                return snapshot.Entries;
            await Task.Delay(200);
        }
        return tailer.Snapshot().Entries;
    }
}
