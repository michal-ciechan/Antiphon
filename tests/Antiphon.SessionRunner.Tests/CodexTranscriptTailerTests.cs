using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0099 S1 + CARD-0006: <see cref="CodexTranscriptTailer"/> must never bind a rollout it
/// cannot prove is its own.
///
/// <para>Codex is the hard case the Grok tailer got to skip. Grok honours <c>--session-id</c>, so
/// its transcript path is known before launch and the whole hazard class is unreachable. Codex has
/// no such flag (<c>codex --help</c>, 0.147.0) and its interactive TUI never prints its session id
/// on screen — measured against a real session through a modern ConPTY, where the banner shows
/// version, model, directory and permissions and nothing else, while <c>codex exec</c> DOES print
/// <c>session id: &lt;uuid&gt;</c>. So the rollout has to be discovered, and discovery has to carry
/// the same C1-C4 evidence Claude's does. These tests drive the REAL tailer against a temp
/// <c>CODEX_HOME/sessions</c> tree, seeded with rollouts captured verbatim from real sessions.</para>
/// </summary>
public class CodexTranscriptTailerTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    /// <summary>The prompt recorded in <c>codex-tui-turn.jsonl</c> — the C4 needle.</summary>
    private const string TuiFixturePrompt = "b2526831 and nothing else.";

    // ------------------------------------------------------------------ the happy path (C1-C4)

    /// <summary>
    /// A real TUI rollout in our cwd, containing a prompt this session was actually sent, binds by
    /// discovery and ingests. The end-to-end proof that the pipeline runs on the file Codex really
    /// writes, not on a shape.
    /// </summary>
    [Test]
    public async Task A_rollout_in_our_cwd_containing_our_prompt_binds_and_ingests()
    {
        using var tree = new CodexTree();
        var fixture = tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: fixture.FirstTimestamp.AddSeconds(-1));
        tailer.Start();
        try
        {
            var bound = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptBound, TimeSpan.FromSeconds(10));
            bound.ShouldNotBeNull("a positively identified rollout must bind");
            bound!.RootElement.GetProperty("How").GetString().ShouldBe(TranscriptBindMethods.Discovery);
            tailer.BoundTranscriptPath.ShouldBe(fixture.Path);

            var entries = await PollForEntriesAsync(tailer, 3, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Kind).ShouldBe(
                [TranscriptKinds.UserPrompt, TranscriptKinds.AssistantText, TranscriptKinds.TurnEnd]);
            await Assert.That(entries[2].StopReason).IsEqualTo("end_turn");
            entries[1].ApiCallId.ShouldBe(entries[2].ApiCallId,
                "the final_answer identity must survive the normalizer-to-runner event mapping");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// THE test for this card's binding rules. Another Codex session in the SAME directory — the
    /// operator's own, or a sibling agent's — is refused, because none of its prompts is text this
    /// session was ever sent (C4). It is refused even though it is the only cwd-matching rollout
    /// and even though it is newer, and the refusal reaches the server as a fault rather than a
    /// log line.
    /// </summary>
    [Test]
    public async Task A_strangers_rollout_in_the_same_cwd_is_never_adopted()
    {
        using var tree = new CodexTree();
        var stranger = tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append("Build CARD-0099 S1: the Codex transcript pipeline");

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            childStartUtc: stranger.FirstTimestamp.AddSeconds(-1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull("refusing every candidate must be reported, not silent");
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.AdoptionRefused);
            var detail = fault.RootElement.GetProperty("Detail").GetString().ShouldNotBeNull();
            detail.ShouldContain(stranger.Path);
            detail.ShouldContain("no prompt in it matches input delivered to this session");
            // The originator is reported so an operator can see whose session was refused — and is
            // deliberately not a gate, because a human running `codex` here writes codex-tui too.
            detail.ShouldContain("codex-tui");

            tailer.BoundTranscriptPath.ShouldBeNull("a stranger's conversation must never be adopted");
            tailer.UnboundReason.ShouldBe("refused");
            await Assert.That(tailer.Snapshot().Entries).IsEmpty();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// C2 is EXACT for Codex — <c>session_meta.cwd</c> is a recorded field, not a directory name to
    /// decode. A rollout from another checkout is not even a near-miss, so it produces no refusal;
    /// what it produces is the empty-census fault, so an agent running unbound is still visible.
    /// </summary>
    [Test]
    public async Task A_rollout_from_another_cwd_is_not_a_candidate_and_raises_the_empty_census_fault()
    {
        using var tree = new CodexTree();
        var elsewhere = tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            cwd: Path.Combine(Path.GetTempPath(), "some-other-checkout"),
            childStartUtc: elsewhere.FirstTimestamp.AddSeconds(-1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
            fault.RootElement.GetProperty("Detail").GetString()
                .ShouldNotBeNull()
                .ShouldContain("0 cwd-matched");

            await Assert.That(tailer.BoundTranscriptPath).IsNull();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// C3. A rollout whose first timestamped record predates our child cannot be ours — even when
    /// the cwd matches and (as here) the prompt text matches too, which is exactly the shape a
    /// resumed sibling in the same directory produces.
    /// </summary>
    [Test]
    public async Task A_rollout_that_predates_the_child_is_refused_even_when_the_prompt_matches()
    {
        using var tree = new CodexTree();
        var older = tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            // The child started an hour after that rollout's first record.
            childStartUtc: older.FirstTimestamp.AddHours(1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
            fault.RootElement.GetProperty("Detail").GetString()
                .ShouldNotBeNull()
                .ShouldContain("1 cwd-matched rollout(s) older than the child were refused (C3)");

            await Assert.That(tailer.BoundTranscriptPath).IsNull();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// CARD-0190 S3: stale C3 refusals say that this session has not written a rollout; a newer
    /// same-cwd stranger says that a rollout appeared but could not be adopted. The latter must
    /// remain AdoptionRefused and lead the capped diagnostic detail.
    /// </summary>
    [Test]
    public async Task Post_start_stranger_rollout_reports_AdoptionRefused_before_stale_C3_candidates()
    {
        using var tree = new CodexTree();
        var stale = Enumerable.Range(0, 6).Select(_ => tree.Seed("codex-tui-turn.jsonl")).ToArray();
        var childStart = stale[0].FirstTimestamp.AddHours(1);
        // The diagnostic promises newest-first ordering within C3 refusals. Make that order
        // explicit instead of relying on NTFS's timing granularity for a burst of fixture writes.
        foreach (var (rollout, index) in stale.Select((rollout, index) => (rollout, index)))
            File.SetLastWriteTimeUtc(rollout.Path, childStart.AddMinutes(-6 + index));
        var postStart = tree.Seed("codex-tui-turn.jsonl", childStart.AddSeconds(1));
        File.SetLastWriteTimeUtc(postStart.Path, childStart.AddMinutes(1));

        var input = new SessionInputLog();
        input.Append("A prompt unique to this session, not present in any rollout");

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            childStartUtc: childStart,
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.AdoptionRefused);
            var detail = fault.RootElement.GetProperty("Detail").GetString().ShouldNotBeNull();
            detail.ShouldContain(postStart.Path);
            detail.StartsWith(postStart.Path, StringComparison.Ordinal).ShouldBeTrue(
                "the post-start C4 refusal must lead the diagnostic detail");
            detail.Contains(stale[0].Path, StringComparison.Ordinal).ShouldBeFalse(
                "the capped detail must prioritize post-start C4 refusals over stale C3 candidates");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// A session that has not received its first input is legitimately waiting for Codex to create
    /// its rollout. Stale same-cwd rollouts must not turn that wait into refusal incidents.
    /// </summary>
    [Test]
    public async Task Stale_same_cwd_rollouts_with_no_input_delivered_stay_silent_indefinitely()
    {
        using var tree = new CodexTree();
        var first = tree.Seed("codex-tui-turn.jsonl");
        tree.Seed("codex-tui-turn.jsonl");

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, new SessionInputLog(),
            childStartUtc: first.FirstTimestamp.AddHours(1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(200));
        tailer.Start();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800));

            hub.Count(SessionRunnerEventNames.SessionTranscriptFault).ShouldBe(0,
                "stale candidates before the first delivered input are a normal first-prompt wait");
            tailer.BoundTranscriptPath.ShouldBeNull();
            tailer.UnboundReason.ShouldBe("awaiting-input");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// The refusal grace period belongs to the delivered input, not to the child process that may
    /// have been waiting for a prompt for much longer.
    /// </summary>
    [Test]
    public async Task First_input_starts_the_refusal_clock_from_the_input_not_the_child_start()
    {
        using var tree = new CodexTree();
        var first = tree.Seed("codex-tui-turn.jsonl");
        tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            childStartUtc: first.FirstTimestamp.AddHours(1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1300));
            hub.Count(SessionRunnerEventNames.SessionTranscriptFault).ShouldBe(0);

            input.Append("The first prompt delivered after this session waited for Codex");
            var elapsedSinceInput = Stopwatch.StartNew();
            tailer.UnboundReason.ShouldBe("locating");
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(5));

            fault.ShouldNotBeNull();
            var unboundSeconds = fault!.RootElement.GetProperty("UnboundSeconds").GetDouble();
            unboundSeconds.ShouldBeInRange(0.3, elapsedSinceInput.Elapsed.TotalSeconds + 0.1,
                "the refusal clock starts when input is delivered, not when the child started; the upper bound admits only post-input scheduling time");
            tailer.UnboundReason.ShouldBe("missing");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// CARD-0190 S2: after runner adoption C4's bounded input evidence is intentionally empty,
    /// but the sidecar's first-input fact still makes stale rollout refusals visible.
    /// </summary>
    [Test]
    public async Task Input_delivered_before_a_restart_is_remembered_from_the_sidecar()
    {
        using var tree = new CodexTree();
        var stale = tree.Seed("codex-tui-turn.jsonl");

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, new SessionInputLog(),
            firstInputUtc: DateTime.UtcNow.AddMinutes(-1),
            childStartUtc: stale.FirstTimestamp.AddHours(1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(300));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(6));
            fault.ShouldNotBeNull("the sidecar fact must preserve post-restart refusal reporting");
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
            tailer.BoundTranscriptPath.ShouldBeNull();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// CARD-0190 S2: a dead child after runner adoption still faults when a prior delivered input
    /// is known solely from the sidecar, even though the in-memory C4 evidence starts empty.
    /// </summary>
    [Test]
    public async Task Child_exit_with_input_delivered_before_restart_faults_from_the_sidecar()
    {
        using var tree = new CodexTree();

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, new SessionInputLog(),
            firstInputUtc: DateTime.UtcNow.AddMinutes(-1),
            childStartUtc: DateTime.UtcNow);
        tailer.Start();
        try
        {
            tailer.NotifyChildExited();
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull("the sidecar fact must preserve post-restart child-exit reporting");
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// C3 is waived on a resume launch: <c>codex resume</c> replays a conversation whose records
    /// legitimately predate the relaunch, and refusing that would break resume re-adoption. C4 is
    /// NOT waived — the resumed conversation still has to contain text we sent.
    /// </summary>
    [Test]
    public async Task A_resume_launch_waives_C3_but_still_requires_C4()
    {
        using var tree = new CodexTree();
        var older = tree.Seed("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            childStartUtc: older.FirstTimestamp.AddHours(1),
            resumeLaunch: true);
        tailer.Start();
        try
        {
            var bound = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptBound, TimeSpan.FromSeconds(10));
            bound.ShouldNotBeNull("a resumed rollout's copied history legitimately predates the relaunch");
            await Assert.That(tailer.BoundTranscriptPath).IsEqualTo(older.Path);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// C1. Two sessions in one directory cannot both read one rollout — the loser keeps looking
    /// rather than tailing a conversation somebody else already owns.
    /// </summary>
    [Test]
    public async Task A_rollout_claimed_by_another_live_session_is_refused_even_when_it_qualifies()
    {
        using var tree = new CodexTree();
        var fixture = tree.Seed("codex-tui-turn.jsonl");

        var claims = new TranscriptClaimRegistry();
        claims.TryClaim(fixture.Path, Guid.NewGuid()).Claimed.ShouldBeTrue();

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input,
            childStartUtc: fixture.FirstTimestamp.AddSeconds(-1),
            claims: claims);
        tailer.Start();
        try
        {
            await Task.Delay(Settle);
            tailer.BoundTranscriptPath.ShouldBeNull("the rollout belongs to another session");
            await Assert.That(tailer.Snapshot().Entries).IsEmpty();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    // --------------------------------------------------------------- measured runtime behaviour

    /// <summary>
    /// Measured 2026-08-20: the rollout is created LAZILY at the first submit — a real session left
    /// up for 30 s with a rendered idle composer and zero bytes written produced no file at all. So
    /// the tailer must start against a missing (indeed absent) sessions tree and pick the rollout
    /// up when it appears, without faulting in the meantime.
    /// </summary>
    [Test]
    public async Task A_lazily_created_rollout_is_picked_up_when_it_finally_appears()
    {
        using var tree = new CodexTree(createRoot: false);
        var cwd = CodexTree.FixtureCwd("codex-tui-turn.jsonl");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(
            hub, tree, input, cwd: cwd,
            childStartUtc: CodexTree.FixtureFirstTimestamp("codex-tui-turn.jsonl").AddSeconds(-1),
            refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            await Task.Delay(700);
            tailer.BoundTranscriptPath.ShouldBeNull("nothing has been written yet");
            hub.Count(SessionRunnerEventNames.SessionTranscriptFault)
                .ShouldBe(0, "a sessions root that does not exist yet is the normal pre-first-submit state");

            var fixture = tree.Seed("codex-tui-turn.jsonl");

            var bound = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptBound, TimeSpan.FromSeconds(10));
            bound.ShouldNotBeNull();
            tailer.BoundTranscriptPath.ShouldBe(fixture.Path);
            await Assert.That((await PollForEntriesAsync(tailer, 3, TimeSpan.FromSeconds(10))).Count)
                .IsEqualTo(3);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// Measured 2026-08-20: Codex holds the rollout open for the whole session, and a naive read
    /// throws <c>IOException: being used by another process</c> — this is a real trap, not a
    /// theoretical one; the S1 probe hit it on its first attempt to read a live rollout. Both the
    /// tailer and the discovery probe must share write AND delete. The assertion drives the same
    /// file through a naive reader first, so a future edit that drops the share flags goes red.
    /// </summary>
    [Test]
    public async Task A_rollout_held_open_by_the_writer_is_still_read()
    {
        using var tree = new CodexTree();
        var fixture = tree.Seed("codex-tui-turn.jsonl");

        // Mimic Codex's own handle: opened for writing, tolerating readers who tolerate a writer.
        await using var writerHandle = new FileStream(
            fixture.Path, FileMode.Open, FileAccess.Write, FileShare.Read);

        Should.Throw<IOException>(() => File.ReadAllLines(fixture.Path))
            .Message.ShouldContain("another process");

        var input = new SessionInputLog();
        input.Append(TuiFixturePrompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: fixture.FirstTimestamp.AddSeconds(-1));
        tailer.Start();
        try
        {
            var entries = await PollForEntriesAsync(tailer, 3, TimeSpan.FromSeconds(10));
            await Assert.That(entries.Count).IsEqualTo(3);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// A restart re-tails the recorded rollout directly instead of re-running discovery: after a
    /// restart the input log is empty, so C4 could never be satisfied, and the heuristic that used
    /// to fill that gap is what bound an agent to the operator's own conversation (CARD-0006).
    /// </summary>
    [Test]
    public async Task Sidecar_path_is_retailed_directly_after_restart_with_no_discovery()
    {
        using var tree = new CodexTree();
        var fixture = tree.Seed("codex-tui-turn.jsonl");

        await using var hub = new HubEvents();
        // No input log at all — exactly the post-restart state, in which discovery must find
        // nothing and only the sidecar can produce a bind.
        var tailer = NewTailer(
            hub, tree, inputLog: null,
            childStartUtc: fixture.FirstTimestamp.AddHours(1),
            knownTranscriptPath: fixture.Path);
        tailer.Start();
        try
        {
            var entries = await PollForEntriesAsync(tailer, 3, TimeSpan.FromSeconds(10));
            tailer.BoundTranscriptPath.ShouldBe(fixture.Path);
            entries.Count.ShouldBe(3);

            // A sidecar re-tail is self-evidently this session's file and stays off the audit trail.
            await Assert.That(hub.Count(SessionRunnerEventNames.SessionTranscriptBound)).IsEqualTo(0);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// The child died after we typed at it and no rollout we could identify ever appeared. That is
    /// reported immediately rather than polled for the rest of the runner's life — and a session
    /// nobody typed at stays silent, because a rollout is only ever created by a submit.
    /// </summary>
    [Test]
    public async Task Child_exit_with_delivered_input_and_no_rollout_faults_and_without_input_stays_silent()
    {
        using var tree = new CodexTree();

        await using var hub = new HubEvents();
        var typedAt = new SessionInputLog();
        typedAt.Append("Build CARD-0099 S1: the Codex transcript pipeline");
        var tailer = NewTailer(hub, tree, typedAt, childStartUtc: DateTime.UtcNow);
        tailer.Start();
        try
        {
            tailer.NotifyChildExited();
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
            fault.RootElement.GetProperty("Detail").GetString()
                .ShouldNotBeNull()
                .ShouldContain("input had been delivered");
        }
        finally
        {
            await tailer.DisposeAsync();
        }

        await using var quietHub = new HubEvents();
        var quiet = NewTailer(quietHub, tree, new SessionInputLog(), childStartUtc: DateTime.UtcNow);
        quiet.Start();
        try
        {
            quiet.NotifyChildExited();
            await Task.Delay(Settle);
            await Assert.That(quietHub.Count(SessionRunnerEventNames.SessionTranscriptFault)).IsEqualTo(0);
        }
        finally
        {
            await quiet.DisposeAsync();
        }
    }

    /// <summary>
    /// The sessions root follows the CHILD's environment first: a session launched with its own
    /// CODEX_HOME writes there, and looking in this process's home instead would find nothing (or,
    /// worse, somebody else's rollouts).
    /// </summary>
    [Test]
    public async Task ResolveSessionsRoot_prefers_the_launch_environment_then_falls_back()
    {
        var fromLaunch = CodexTranscriptTailer.ResolveSessionsRoot(
            new Dictionary<string, string> { ["CODEX_HOME"] = @"C:\custom\codex" });
        fromLaunch.ShouldBe(Path.Combine(@"C:\custom\codex", "sessions"));

        var fallback = CodexTranscriptTailer.ResolveSessionsRoot(null);
        await Assert.That(fallback).IsEqualTo(Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "sessions"));
    }

    // ---------------------------------------------------------------------------- test plumbing

    private static CodexTranscriptTailer NewTailer(
        HubEvents hub,
        CodexTree tree,
        SessionInputLog? inputLog,
        string? cwd = null,
        DateTime? childStartUtc = null,
        DateTime? firstInputUtc = null,
        bool resumeLaunch = false,
        TranscriptClaimRegistry? claims = null,
        string? knownTranscriptPath = null,
        TimeSpan? refusalFaultDelay = null) =>
        new(
            Guid.NewGuid(),
            cwd ?? CodexTree.FixtureCwd("codex-tui-turn.jsonl"),
            hub.Hub,
            NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50),
            locatePollInterval: TimeSpan.FromMilliseconds(50),
            claims: claims,
            inputLog: inputLog,
            firstInputUtc: firstInputUtc,
            childStartUtc: childStartUtc,
            resumeLaunch: resumeLaunch,
            knownTranscriptPath: knownTranscriptPath,
            sessionsRoot: tree.SessionsRoot,
            refusalFaultDelay: refusalFaultDelay ?? TimeSpan.FromMilliseconds(300),
            refusalFaultRepeat: TimeSpan.FromMilliseconds(500));

    private static async Task<IReadOnlyList<RunnerTranscriptEvent>> PollForEntriesAsync(
        CodexTranscriptTailer tailer, int want, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = tailer.Snapshot();
            if (snapshot.Entries.Count >= want)
                return snapshot.Entries;
            await Task.Delay(50);
        }

        return tailer.Snapshot().Entries;
    }

    private sealed record SeededRollout(string Path, DateTime FirstTimestamp);

    /// <summary>
    /// A temp <c>CODEX_HOME/sessions/YYYY/MM/DD</c> tree, seeded with rollouts copied byte for byte
    /// out of the captured fixtures. Copied rather than rewritten on purpose: the cwd and
    /// timestamps a candidate is judged on are the REAL recorded ones, so a test can only pass by
    /// reading the file the way production does.
    /// </summary>
    private sealed class CodexTree : IDisposable
    {
        private readonly string _codexHome;

        public CodexTree(bool createRoot = true)
        {
            _codexHome = Path.Combine(Path.GetTempPath(), $"antiphon-codex-{Guid.NewGuid():N}");
            SessionsRoot = Path.Combine(_codexHome, "sessions");
            if (createRoot)
                Directory.CreateDirectory(Path.Combine(SessionsRoot, "2026", "08", "20"));
        }

        public string SessionsRoot { get; }

        public SeededRollout Seed(string fixture, DateTimeOffset? firstTimestamp = null)
        {
            var dir = Path.Combine(SessionsRoot, "2026", "08", "20");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"rollout-2026-08-20T16-16-28-{Guid.NewGuid():D}.jsonl");
            if (firstTimestamp is null)
            {
                File.Copy(CodexTranscriptNormalizerTests.FixturePath(fixture), target);
                return new SeededRollout(target, FixtureFirstTimestamp(fixture));
            }

            var source = File.ReadAllText(CodexTranscriptNormalizerTests.FixturePath(fixture));
            var original = JsonDocument.Parse(CodexTranscriptNormalizerTests.ReadFixtureLines(fixture)[0])
                .RootElement.GetProperty("timestamp").GetString()!;
            File.WriteAllText(
                target,
                source.Replace($"\"timestamp\":\"{original}\"", $"\"timestamp\":\"{firstTimestamp:O}\"", StringComparison.Ordinal));
            return new SeededRollout(target, firstTimestamp.Value.UtcDateTime);
        }

        /// <summary>The cwd the fixture's own <c>session_meta</c> records — the C2 needle.</summary>
        public static string FixtureCwd(string fixture) =>
            SessionMeta(fixture).GetProperty("cwd").GetString()!;

        public static DateTime FixtureFirstTimestamp(string fixture) =>
            DateTimeOffset.Parse(
                JsonDocument.Parse(CodexTranscriptNormalizerTests.ReadFixtureLines(fixture)[0])
                    .RootElement.GetProperty("timestamp").GetString()!).UtcDateTime;

        private static JsonElement SessionMeta(string fixture) =>
            JsonDocument.Parse(CodexTranscriptNormalizerTests.ReadFixtureLines(fixture)[0])
                .RootElement.GetProperty("payload");

        public void Dispose()
        {
            try { Directory.Delete(_codexHome, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Drains the runner event hub so a test can wait for a specific published event.</summary>
    private sealed class HubEvents : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly List<RunnerServerSentEvent> _received = new();
        private readonly Task _pump;

        public HubEvents()
        {
            Hub = new SessionRunnerEventHub();
            var reader = Hub.Subscribe(_cts.Token);
            _pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in reader.ReadAllAsync(_cts.Token))
                    {
                        lock (_received)
                            _received.Add(evt);
                    }
                }
                catch (OperationCanceledException) { }
                catch (ChannelClosedException) { }
            });
        }

        public SessionRunnerEventHub Hub { get; }

        public async Task<JsonDocument?> WaitForAsync(string eventName, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                RunnerServerSentEvent[] snapshot;
                lock (_received)
                    snapshot = _received.ToArray();

                foreach (var evt in snapshot)
                {
                    if (evt.EventName == eventName)
                        return JsonDocument.Parse(evt.Json);
                }

                await Task.Delay(50);
            }

            return null;
        }

        public int Count(string eventName)
        {
            lock (_received)
                return _received.Count(e => e.EventName == eventName);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { await _pump; } catch { /* drained */ }
            _cts.Dispose();
        }
    }
}
