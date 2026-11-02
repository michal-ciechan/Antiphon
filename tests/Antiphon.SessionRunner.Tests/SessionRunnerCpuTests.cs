using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1624: the desktop SessionRunner spent ~0.5 cores re-deriving answers that had not changed —
/// unbound tailers rescanning every transcript on the machine four times a second, candidates
/// re-matching every prompt against a re-normalized input log on every poll, and the CPU watchdog
/// copying and re-classifying every Running session's whole transcript every five seconds. These
/// witnesses count the work instead of timing it (fake time, diagnostic counters), and the
/// behaviour tests pin that binding, re-matching and the idle judgement still happen when
/// something DID change.
/// </summary>
public class SessionRunnerCpuTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    // ------------------------------------------------------------------ (A) discovery backoff

    [Test]
    public async Task Unbound_tailer_backs_off_full_scans_while_nothing_changes()
    {
        using var tree = new Tree();
        // A machine's worth of other projects: never ours, never changing.
        for (var i = 0; i < 20; i++)
            tree.Write(tree.OtherDir, Guid.NewGuid(), UserLine("o", "/elsewhere/project", "someone else's prompt text", tree.Start));
        var input = new SessionInputLog();
        input.Append("this session's own distinctive prompt");
        await using var world = tree.Tailer(input);

        await world.PumpAsync(240); // 60 s of polls

        var elapsed = world.Time.GetUtcNow() - tree.Start;
        var scans = world.Tailer.LocateCounters.FullScans;
        // 250 ms doubling to the 5 s cap: ~5 scans to reach it, then one per cap.
        var bound = 6 + (int)Math.Ceiling(elapsed / TranscriptTailer.MaxFullScanInterval) + 2;
        scans.ShouldBeLessThanOrEqualTo(bound,
            $"{world.Tailer.LocateCounters.Ticks} polls over {elapsed.TotalSeconds:F1}s of unchanged root must not each rescan it");
        scans.ShouldBeGreaterThan(1, "the full scan must keep running at the backed-off cadence");
        world.Tailer.BoundTranscriptPath.ShouldBeNull();
    }

    [Test]
    public async Task Transcript_appearing_after_backoff_binds_within_one_poll_in_the_cwd_project_dir()
    {
        using var tree = new Tree();
        var input = new SessionInputLog();
        input.Append("please implement the backoff for CARD-1624 now");
        await using var world = tree.Tailer(input);
        await world.PumpAsync(80);
        var scansBefore = world.Tailer.LocateCounters.FullScans;

        // Claude forked the id: a self-chosen file in the cwd's project dir carrying our prompt.
        var forked = tree.Write(tree.CwdDir, Guid.NewGuid(),
            UserLine("u1", tree.Cwd, "please implement the backoff for CARD-1624 now", world.Time.GetUtcNow()));

        var polls = await world.PumpUntilAsync(() => world.Tailer.BoundTranscriptPath is not null, maxPolls: 40);
        world.Tailer.BoundTranscriptPath.ShouldBe(forked);
        polls.ShouldBeLessThanOrEqualTo(2, "a new file in the cwd's project dir must bind within about one poll");
        world.Tailer.LocateCounters.FullScans.ShouldBeGreaterThan(scansBefore);
    }

    [Test]
    public async Task Transcript_in_an_unwatched_dir_still_binds_within_the_backoff_cap()
    {
        using var tree = new Tree();
        var input = new SessionInputLog();
        input.Append("please implement the backoff for CARD-1624 now");
        await using var world = tree.Tailer(input);
        await world.PumpAsync(80);

        var elsewhere = Path.Combine(tree.Root, "some-other-encoding");
        Directory.CreateDirectory(elsewhere);
        var forked = tree.Write(elsewhere, Guid.NewGuid(),
            UserLine("u1", tree.Cwd, "please implement the backoff for CARD-1624 now", world.Time.GetUtcNow()));

        var polls = await world.PumpUntilAsync(() => world.Tailer.BoundTranscriptPath is not null, maxPolls: 60);
        world.Tailer.BoundTranscriptPath.ShouldBe(forked);
        polls.ShouldBeLessThanOrEqualTo((int)(TranscriptTailer.MaxFullScanInterval / Poll) + 2);
    }

    [Test]
    public async Task A_relevant_change_resets_the_scan_interval_to_one_poll()
    {
        using var tree = new Tree();
        var input = new SessionInputLog();
        input.Append("a prompt that no transcript here carries");
        await using var world = tree.Tailer(input);
        await world.PumpAsync(80);

        // A stranger's post-start file in our cwd: refused (C4), but a change discovery must look at.
        tree.Write(tree.CwdDir, Guid.NewGuid(), UserLine("s1", tree.Cwd, "a stranger's own prompt text", world.Time.GetUtcNow()));
        var beforeChange = world.Tailer.LocateCounters.FullScans;
        await world.PumpAsync(1);
        var afterChange = world.Tailer.LocateCounters.FullScans;
        afterChange.ShouldBe(beforeChange + 1, "the watched cwd dir changed: the next poll scans");
        await world.PumpAsync(1);
        world.Tailer.LocateCounters.FullScans.ShouldBe(afterChange + 1,
            "right after a change the full scan runs at the poll interval again");
        world.Tailer.BoundTranscriptPath.ShouldBeNull();
    }

    // ------------------------------------------------------------------ (B) probe and C4 caching

    [Test]
    public async Task Unchanged_candidate_is_not_reprobed_and_a_touched_one_is_reprobed_once()
    {
        using var tree = new Tree();
        var input = new SessionInputLog();
        input.Append("our own prompt that the stranger never typed");
        var stranger = tree.Write(tree.CwdDir, Guid.NewGuid(),
            UserLine("s1", tree.Cwd, "a stranger's own prompt text", tree.Start.AddSeconds(1)),
            UserLine("s2", tree.Cwd, "and a second stranger prompt", tree.Start.AddSeconds(2)));
        await using var world = tree.Tailer(input);
        await world.PumpAsync(8);

        var counters = world.Tailer.LocateCounters;
        var opens = counters.Opens;
        var matches = input.MatchQueries;
        var scans = counters.FullScans;
        await world.PumpAsync(120);
        counters.FullScans.ShouldBeGreaterThan(scans, "later scans must have looked at the candidate");
        counters.Opens.ShouldBe(opens, "an unchanged candidate must not be opened again");
        input.MatchQueries.ShouldBe(matches, "with no new prompt and no new input, C4 must not be re-run");

        // Touch it: one new (still foreign) prompt. Exactly one re-read, and a C4 re-check.
        File.AppendAllText(stranger, UserLine("s3", tree.Cwd, "a third stranger prompt here", world.Time.GetUtcNow()) + "\n");
        await world.PumpAsync(3);
        counters.Opens.ShouldBe(opens + 1, "a changed candidate is re-probed exactly once");
        input.MatchQueries.ShouldBeGreaterThan(matches, "a new prompt in a candidate re-runs C4");
        world.Tailer.BoundTranscriptPath.ShouldBeNull();
    }

    [Test]
    public async Task New_input_rechecks_C4_against_an_unchanged_candidate_and_binds()
    {
        using var tree = new Tree();
        var input = new SessionInputLog();
        input.Append("the first thing this session was told");
        var candidate = tree.Write(tree.CwdDir, Guid.NewGuid(),
            UserLine("c1", tree.Cwd, "the second thing this session was told", tree.Start.AddSeconds(1)));
        await using var world = tree.Tailer(input);
        await world.PumpAsync(40);
        world.Tailer.BoundTranscriptPath.ShouldBeNull("C4 cannot pass before the input arrives");

        input.Append("the second thing this session was told");
        var polls = await world.PumpUntilAsync(() => world.Tailer.BoundTranscriptPath is not null, maxPolls: 10);
        world.Tailer.BoundTranscriptPath.ShouldBe(candidate);
        polls.ShouldBeLessThanOrEqualTo(2, "an input-log append must trigger the re-check on the next poll");
    }

    [Test]
    public void Input_log_normalizes_its_buffer_once_per_append_not_once_per_query()
    {
        var log = new SessionInputLog();
        log.Append("\u001b[200~first delivered body with enough text\u001b[201~\r");
        var version = log.Version;
        for (var i = 0; i < 50; i++)
            log.MatchesRecordedInput("first delivered body with enough text").ShouldBeTrue();
        log.BufferNormalizations.ShouldBe(1);

        log.Append("second delivered body, also distinctive");
        log.Version.ShouldBeGreaterThan(version);
        log.MatchesRecordedInput("second delivered body, also distinctive")
            .ShouldBeTrue("an append must invalidate the cached normalized buffer");
        log.MatchesRecordedInput("first delivered body with enough text").ShouldBeTrue();
        log.BufferNormalizations.ShouldBe(2);
    }

    [Test]
    public void Normalized_record_overload_matches_the_string_overload()
    {
        string[] records =
        [
            "", "short", "line one\nline two and more text here", "  padded   body with spaces  ",
            "\u001b[31mcoloured\u001b[0m prompt body text", "line oneline two and more text here",
        ];
        string?[] bodies =
        [
            null, "", "ok", "line one\nline two and more text here", "padded body with spaces",
            "coloured prompt body text", "line one line two and more text here",
        ];
        foreach (var record in records)
        foreach (var body in bodies)
        {
            PromptSubmissionMatch.IsConfirmedByNormalized(body, new PromptSubmissionMatch.NormalizedRecord(record))
                .ShouldBe(PromptSubmissionMatch.IsConfirmedBy(body, record), $"body '{body}' record '{record}'");
        }
    }

    // ------------------------------------------------------------------ (C) watchdog

    [Test]
    public void Incremental_working_verdict_equals_a_full_classify_at_every_step()
    {
        var kinds = new[]
        {
            TranscriptKinds.UserPrompt, TranscriptKinds.AssistantText, TranscriptKinds.TurnEnd,
            TranscriptKinds.ToolCall, TranscriptKinds.TurnTitle, TranscriptKinds.CompactBoundary,
            TranscriptKinds.QueuedUserPrompt, TranscriptKinds.SessionRestartBoundary,
        };
        var random = new Random(1624);
        for (var run = 0; run < 200; run++)
        {
            var entries = new List<RunnerTranscriptEvent>();
            var accumulator = new TranscriptWorkingState.WorkingAccumulator();
            accumulator.Verdict.ShouldBe(TranscriptWorkingState.Classify(entries));
            for (var seq = 1; seq <= 12; seq++)
            {
                var kind = kinds[random.Next(kinds.Length)];
                var text = kind == TranscriptKinds.UserPrompt && random.Next(4) == 0
                    ? "[Request interrupted by user]" : "text";
                var entry = new RunnerTranscriptEvent(Guid.Empty, seq, kind, null, null, null, null, text,
                    null, null, null, null, null);
                entries.Add(entry);
                accumulator.Observe(entry);
                accumulator.Verdict.ShouldBe(TranscriptWorkingState.Classify(entries));
            }
        }
    }

    [Test]
    public async Task Watchdog_judges_idle_from_the_running_verdict_without_snapshotting_the_transcript()
    {
        await using var world = await WatchdogWorld.StartAsync(idle: true);

        // Cool CPU: judged, never killed, and no transcript snapshot taken.
        for (var i = 0; i < 4; i++)
            await world.SweepAsync(cpuSecondsDelta: 0);
        world.Child.Kills.ShouldBe(0);
        world.Tailer.SnapshotCount.ShouldBe(0, "the watchdog must not copy the transcript to judge idle");

        // Idle and hot for the sustained window: still killed, exactly as before.
        await world.SweepAsync(cpuSecondsDelta: 5);
        await world.SweepAsync(cpuSecondsDelta: 5);
        world.Child.Kills.ShouldBeGreaterThan(0, "an idle session burning a core must still be killed");
        world.Tailer.SnapshotCount.ShouldBe(0);
    }

    [Test]
    public async Task Watchdog_never_kills_a_working_session_however_hot()
    {
        await using var world = await WatchdogWorld.StartAsync(idle: false);
        for (var i = 0; i < 5; i++)
            await world.SweepAsync(cpuSecondsDelta: 5);
        world.Child.Kills.ShouldBe(0);
    }

    // ------------------------------------------------------------------ fixtures

    private static string UserLine(string uuid, string cwd, string text, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            type = "user",
            uuid,
            cwd,
            timestamp = timestamp.ToUniversalTime().ToString("o"),
            message = new { role = "user", content = text },
        });

    private static string AssistantEndTurnLine(string uuid, string cwd) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            uuid,
            cwd,
            message = new
            {
                id = "msg_c1624",
                role = "assistant",
                stop_reason = "end_turn",
                content = new object[] { new { type = "text", text = "done" } },
            },
        });

    private sealed class Tree : IDisposable
    {
        private readonly string _base = Path.Combine(Path.GetTempPath(), "c1624-" + Guid.NewGuid().ToString("N"));

        public Tree()
        {
            Root = Path.Combine(_base, "projects");
            Cwd = Path.Combine(_base, "agent-cwd");
            Directory.CreateDirectory(Cwd);
            CwdDir = Path.Combine(Root, TranscriptTailer.ClaudeProjectDirName(Cwd)!);
            OtherDir = Path.Combine(Root, "other-project");
            Directory.CreateDirectory(CwdDir);
            Directory.CreateDirectory(OtherDir);
        }

        public DateTimeOffset Start { get; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public string Root { get; }
        public string Cwd { get; }
        public string CwdDir { get; }
        public string OtherDir { get; }

        public string Write(string dir, Guid id, params string[] lines)
        {
            var path = Path.Combine(dir, id.ToString("D") + ".jsonl");
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return path;
        }

        public TailerWorld Tailer(SessionInputLog input)
        {
            var time = new FakeTimeProvider(Start);
            var tailer = new TranscriptTailer(
                Guid.NewGuid(), Cwd, new SessionRunnerEventHub(), NullLogger.Instance,
                exactIdGrace: TimeSpan.Zero, inputLog: input, childStartUtc: Start.UtcDateTime,
                projectsRoot: Root, timeProvider: time);
            tailer.Start();
            return new TailerWorld(tailer, time);
        }

        public void Dispose()
        {
            try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class TailerWorld(TranscriptTailer tailer, FakeTimeProvider time) : IAsyncDisposable
    {
        public TranscriptTailer Tailer => tailer;
        public FakeTimeProvider Time => time;

        /// <summary>Lets the locate loop complete <paramref name="polls"/> more polls of fake time.</summary>
        public async Task PumpAsync(int polls) => await PumpUntilAsync(() => false, polls);

        /// <summary>Polls until <paramref name="done"/> or the budget; returns the polls it took.</summary>
        public async Task<int> PumpUntilAsync(Func<bool> done, int maxPolls)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            for (var poll = 1; poll <= maxPolls; poll++)
            {
                var before = tailer.LocateCounters.Ticks;
                while (tailer.LocateCounters.Ticks == before && tailer.BoundTranscriptPath is null)
                {
                    DateTime.UtcNow.ShouldBeLessThan(deadline, "the locate loop stopped polling");
                    time.Advance(Poll);
                    await Task.Delay(5);
                }
                if (done())
                    return poll;
                if (tailer.BoundTranscriptPath is not null)
                    return poll;
            }
            return maxPolls;
        }

        public async ValueTask DisposeAsync() => await tailer.DisposeAsync();
    }

    private sealed class WatchdogWorld : IAsyncDisposable
    {
        private readonly string _root;
        private readonly SessionRunnerRuntime _runtime;
        private readonly SessionCpuWatchdogService _watchdog;
        private readonly FakeTimeProvider _time;
        private readonly CpuProbe _probe;

        private WatchdogWorld(string root, SessionRunnerRuntime runtime, SessionCpuWatchdogService watchdog,
            FakeTimeProvider time, TranscriptTailer tailer, FakeChild child, CpuProbe probe)
        {
            _root = root;
            _probe = probe;
            _runtime = runtime;
            _watchdog = watchdog;
            _time = time;
            Tailer = tailer;
            Child = child;
        }

        public TranscriptTailer Tailer { get; }
        public FakeChild Child { get; }

        public static async Task<WatchdogWorld> StartAsync(bool idle)
        {
            var root = Path.Combine(Path.GetTempPath(), "c1624-wd-" + Guid.NewGuid().ToString("N"));
            var projectDir = Path.Combine(root, "projects", "some-encoded-cwd");
            var cwd = Path.Combine(root, "agent-cwd");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(cwd);
            var sessionId = Guid.NewGuid();
            string[] lines = idle
                ? [UserLine("u1", cwd, "do the thing", DateTimeOffset.UtcNow), AssistantEndTurnLine("a1", cwd)]
                : [UserLine("u1", cwd, "do the thing", DateTimeOffset.UtcNow)];
            File.WriteAllText(Path.Combine(projectDir, sessionId.ToString("D") + ".jsonl"), string.Join("\n", lines) + "\n");

            var settings = new SessionRunnerSettings
            {
                SessionLogPath = root,
                CpuWatchdogHotCpuPercent = 50,
                CpuWatchdogSustainedSeconds = 10,
                CpuWatchdogMinUptimeSeconds = 0,
            };
            var tailer = new TranscriptTailer(sessionId, cwd, new SessionRunnerEventHub(), NullLogger.Instance,
                projectsRoot: Path.Combine(root, "projects"));
            tailer.Start();
            var expected = idle ? TranscriptWorkingState.WorkingVerdict.Idle : TranscriptWorkingState.WorkingVerdict.Working;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (tailer.WorkingVerdict != expected && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            tailer.WorkingVerdict.ShouldBe(expected, "the tailer must ingest the fabricated transcript first");

            var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(5));
            var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
            var child = new FakeChild();
            var session = new SessionRunnerRuntime.RunnerSession(sessionId, settings, new SessionRunnerEventHub(), NullLogger.Instance);
            session.BindChildForTest(child, tailer, DateTime.UtcNow, childPid: 4242);
            runtime.Track(session);
            var probe = new CpuProbe();
            var watchdog = new SessionCpuWatchdogService(runtime, probe, Options.Create(settings), time,
                NullLogger<SessionCpuWatchdogService>.Instance);
            return new WatchdogWorld(root, runtime, watchdog, time, tailer, child, probe);
        }

        public async Task SweepAsync(double cpuSecondsDelta)
        {
            _probe.Current += TimeSpan.FromSeconds(cpuSecondsDelta);
            await _watchdog.SweepOnceAsync(CancellationToken.None);
            _time.Advance(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            try { await _runtime.DisposeAsync(); } catch { /* best effort */ }
            try { await Tailer.DisposeAsync(); } catch { /* best effort */ }
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class CpuProbe : IProcessCpuProbe
    {
        public TimeSpan Current { get; set; }

        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => Current;
    }

    internal sealed class FakeChild : ISessionChild
    {
        public int Kills { get; private set; }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(string input, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct)
        {
            Kills++;
            Exited?.Invoke(new ChildExit(null, "KilledByRequest"));
            return Task.FromResult(true);
        }
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct) => Task.FromResult<ChildScreen?>(null);
        public event Action<ChildExit>? Exited;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
