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
