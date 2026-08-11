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
    // The tailer must discover it, or reply routing silently breaks. Since CARD-0006 a cwd match is
    // no longer enough on its own — the fork is identified by containing a prompt this session was
    // actually sent (rule C4), so the input log carries that text.
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
            var inputLog = new SessionInputLog();
            inputLog.Append("hello from the delivered prompt");
            // Short grace so the test is fast (the production default is 10s).
            var tailer = new TranscriptTailer(
                antiphonSessionId, cwd, hub, NullLogger.Instance,
                exactIdGrace: TimeSpan.FromMilliseconds(300),
                inputLog: inputLog);
            tailer.Start();
            try
            {
                // Claude forks AFTER the exact-id grace: writes to a NEW <uuid>.jsonl.
                await Task.Delay(600);
                var forked = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
                await File.WriteAllTextAsync(
                    forked, UserLine("u1", cwd, "hello from the delivered prompt") + "\n");
                await File.AppendAllTextAsync(forked, FixtureLine() + "\n"); // + a compact boundary

                var entries = await PollForEntriesAsync(tailer, want: 2, TimeSpan.FromSeconds(10));

                entries.Select(e => e.Kind).ShouldBe(
                    [TranscriptKinds.UserPrompt, TranscriptKinds.CompactBoundary]);
                entries[0].Text.ShouldBe(
                    "hello from the delivered prompt", "must adopt the forked file, not the stale/other-cwd ones");
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

    // The real-world bug (2026-07-22): a RECENT transcript from a previous session in the same cwd
    // exists, and the fresh fork appears LATE (after the exact-id grace). The tailer must keep
    // waiting for the fork — never adopt the recent-but-stale previous transcript.
    [Test]
    public async Task Tailer_does_not_adopt_a_recent_but_stale_transcript_while_waiting_for_the_fork()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"antiphon-tailer-stale-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(configDir, "projects", "C--src-ClaudeBot-agents-family");
        Directory.CreateDirectory(projectDir);
        var cwd = Path.Combine(Path.GetTempPath(), $"agent-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        // A previous session's transcript in the SAME cwd, written JUST NOW (recent mtime) — this is
        // the file the buggy fallback wrongly grabbed.
        var recentStale = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
        await File.WriteAllTextAsync(recentStale, UserLine("prev", cwd, "previous session") + "\n");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        try
        {
            var antiphonSessionId = Guid.NewGuid();
            var hub = new SessionRunnerEventHub();
            var inputLog = new SessionInputLog();
            inputLog.Append("the real one, delivered to this session");
            var tailer = new TranscriptTailer(
                antiphonSessionId, cwd, hub, NullLogger.Instance,
                exactIdGrace: TimeSpan.FromMilliseconds(300),
                inputLog: inputLog);
            tailer.Start();
            try
            {
                // Grace elapses with only the recent-stale file present — the tailer must NOT adopt it.
                await Task.Delay(1500);
                tailer.Snapshot().Entries.ShouldBeEmpty("the recent-but-stale previous transcript must not be adopted");

                // The real fork finally appears — now it must be adopted.
                var forked = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
                await File.WriteAllTextAsync(
                    forked, UserLine("u1", cwd, "the real one, delivered to this session") + "\n");

                var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
                entries.ShouldHaveSingleItem().Text.ShouldBe("the real one, delivered to this session");
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

    // NOTE: re-adoption across a runner restart used to be pinned here as "adopt any pre-existing,
    // actively-written, cwd-matching file after 30s". That heuristic is GONE (CARD-0006) — it is
    // what bound a session to the human operator's live conversation on 2026-08-09. A restarted
    // runner now re-tails the exact path recorded in its own transcript sidecar; that is pinned by
    // TranscriptAdoptionSafetyTests.Sidecar_path_is_retailed_directly_after_restart_with_no_discovery.

    // Live miss 2026-07-31: /clear forks the conversation MID-SESSION to a fresh self-chosen file
    // (real shape pinned by ClaudeLocalCommandCanaryTests) — the file being tailed goes quiet and
    // everything after lands in the fork. The tailer must FOLLOW it, or transcript ingestion (and
    // with it working/idle and channel reply dispatch) silently dies for the rest of the session
    // (an AZ Care reply after a /clear never reached Telegram).
    [Test]
    public async Task Tailer_follows_a_mid_session_fork_such_as_clear()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"antiphon-tailer-midfork-{Guid.NewGuid():N}");
        var projectDir = Path.Combine(configDir, "projects", "C--src-ClaudeBot-agents-family");
        Directory.CreateDirectory(projectDir);
        var cwd = Path.Combine(Path.GetTempPath(), $"agent-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        var jsonlPath = Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl");
        await File.WriteAllTextAsync(jsonlPath, UserLine("u1", cwd, "before clear") + "\n");

        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);
        try
        {
            var hub = new SessionRunnerEventHub();
            var inputLog = new SessionInputLog();
            inputLog.Append("the first prompt after the clear");
            var tailer = new TranscriptTailer(
                sessionId, cwd, hub, NullLogger.Instance,
                forkScanInterval: TimeSpan.FromMilliseconds(300),
                inputLog: inputLog);
            tailer.Start();
            try
            {
                (await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10)))
                    .ShouldContain(e => e.Text == "before clear");

                // /clear: a FRESH conversation file appears; the original goes quiet forever.
                await Task.Delay(300);
                var fork = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
                await File.WriteAllTextAsync(
                    fork, UserLine("u2", cwd, "<command-name>/clear</command-name>") + "\n");
                await File.AppendAllTextAsync(
                    fork, UserLine("u3", cwd, "the first prompt after the clear") + "\n");

                var entries = await PollForEntriesAsync(tailer, want: 3, TimeSpan.FromSeconds(10));
                entries.ShouldContain(
                    e => e.Text == "the first prompt after the clear",
                    "the tailer must switch to the forked conversation file");
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
