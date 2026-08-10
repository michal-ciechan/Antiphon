using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0006: a session must never bind to a transcript it cannot prove is its own.
///
/// The live miss (2026-08-09): an agent's <c>&lt;session-id&gt;.jsonl</c> never appeared, so cwd
/// discovery fell back to "the most recently written transcript in this cwd" — which was the human
/// operator's own Claude Code conversation. The agent then reported 65 of the operator's file edits
/// as its own work, computed working/idle from a stranger's turns, and (had it been channel-bound)
/// would have relayed that private conversation to Telegram.
///
/// These tests drive the REAL <see cref="TranscriptTailer"/> against a temp transcript tree
/// (CLAUDE_CONFIG_DIR), exactly like <see cref="TranscriptTailerCompactionTests"/>.
/// </summary>
[NotInParallel("ClaudeConfigDirEnv")] // mutates the process-wide CLAUDE_CONFIG_DIR variable
public class TranscriptAdoptionSafetyTests
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RefusalWindow = TimeSpan.FromSeconds(2);

    // ---------------------------------------------------------------- §8.1 core adoption rules

    /// <summary>
    /// THE test for this card. A cwd-matching transcript that predates the launch and keeps being
    /// appended to (the operator, typing) was adopted by the old code after 30 seconds on nothing
    /// but recency. It must now be refused — twice over: its history starts before this session's
    /// child process (C3) and none of its prompts is text this session was ever sent (C4) — and the
    /// refusal must reach the server as a fault event, not just a log line.
    /// </summary>
    [Test]
    public async Task Preexisting_actively_written_transcript_in_same_cwd_is_never_adopted()
    {
        using var tree = new TranscriptTree("operator-collision");
        var childStart = DateTime.UtcNow;

        // The operator's conversation: same cwd, started an hour ago, still being written.
        var operatorFile = tree.NewTranscript();
        await tree.AppendAsync(operatorFile, UserLine("o1", tree.Cwd, "what does the pty host do again?", childStart.AddHours(-1)));

        var input = new SessionInputLog();
        input.Append("Implement CARD-0006 in the session runner");

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: childStart, refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            // The operator keeps typing throughout — the exact condition the old heuristic accepted.
            var deadline = DateTime.UtcNow + RefusalWindow;
            var n = 0;
            while (DateTime.UtcNow < deadline)
            {
                await tree.AppendAsync(operatorFile, UserLine($"o{++n + 1}", tree.Cwd, $"and another operator question {n}", DateTime.UtcNow));
                await Task.Delay(200);
            }

            tailer.BoundTranscriptPath.ShouldBeNull("the operator's live conversation must never be adopted");
            tailer.Snapshot().Entries.ShouldBeEmpty();

            // The fault is what makes this a real assertion rather than a race with a grace timer:
            // it is published only after the tailer has EVALUATED this candidate and refused it on
            // the rules, so a green test means the mechanism ran — not that the clock had not
            // reached the old 30-second re-adoption window yet.
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(5));
            fault.ShouldNotBeNull("refusing every candidate must be reported, not silent");
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.AdoptionRefused);
            var detail = fault.RootElement.GetProperty("Detail").GetString().ShouldNotBeNull();
            detail.ShouldContain(operatorFile);
            detail.ShouldContain("predates the child start", Case.Insensitive);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// The timestamp filter alone is not enough, and this is why: an operator who starts a NEW
    /// conversation in the same cwd after the agent launched produces a file that passes cwd and
    /// epoch checks. Only content correlation separates it from the agent's own transcript.
    /// </summary>
    [Test]
    public async Task Fresh_file_created_after_launch_without_matching_content_is_refused()
    {
        using var tree = new TranscriptTree("fresh-stranger");
        var childStart = DateTime.UtcNow;

        var input = new SessionInputLog();
        input.Append("Implement CARD-0006 in the session runner");

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: childStart);
        tailer.Start();
        try
        {
            await Task.Delay(500); // let the exact-id grace lapse first

            var stranger = tree.NewTranscript();
            await tree.AppendAsync(stranger, UserLine("s1", tree.Cwd, "help me plan my holiday please", DateTime.UtcNow));

            await Task.Delay(RefusalWindow);
            tailer.BoundTranscriptPath.ShouldBeNull(
                "a file created after launch is still not this session's unless its content proves it");
            tailer.Snapshot().Entries.ShouldBeEmpty();
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    [Test]
    public async Task Candidate_with_mismatched_agent_name_record_is_refused()
    {
        using var tree = new TranscriptTree("agent-name-mismatch");
        var prompt = "Implement CARD-0006 in the session runner";
        var input = new SessionInputLog();
        input.Append(prompt);

        var file = tree.NewTranscript();
        await tree.AppendAsync(file, AgentNameLine("some-other-agent"));
        await tree.AppendAsync(file, UserLine("u1", tree.Cwd, prompt, DateTime.UtcNow));

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow.AddSeconds(-5), agentName: "antiphon-worker");
        tailer.Start();
        try
        {
            await Task.Delay(RefusalWindow);
            tailer.BoundTranscriptPath.ShouldBeNull("a transcript naming a DIFFERENT agent is not ours");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// C2b rejects on conflict only. Absence must stay neutral: operator sessions carry their own
    /// names, older Claude versions write none, and rejecting on absence would break every launch
    /// against a Claude version that stops writing the record.
    /// </summary>
    [Test]
    public async Task Candidate_with_no_agent_name_record_is_not_rejected_for_absence()
    {
        using var tree = new TranscriptTree("agent-name-absent");
        var prompt = "Implement CARD-0006 in the session runner";
        var input = new SessionInputLog();
        input.Append(prompt);

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow.AddSeconds(-5), agentName: "antiphon-worker");
        tailer.Start();
        try
        {
            await Task.Delay(400);
            var file = tree.NewTranscript();
            await tree.AppendAsync(file, UserLine("u1", tree.Cwd, prompt, DateTime.UtcNow));

            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries.ShouldHaveSingleItem().Text.ShouldBe(prompt);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// Two agents in one checkout, both id-forked. Under the old newest-mtime rule the later fork
    /// won for BOTH sessions; content correlation separates them, and the claim registry guarantees
    /// no file is ever double-tailed even if it did not.
    /// </summary>
    [Test]
    public async Task Two_sessions_in_one_cwd_adopt_their_own_forks_not_each_others()
    {
        using var tree = new TranscriptTree("two-siblings");
        var claims = new TranscriptClaimRegistry();

        var promptA = "Session A brief: rebuild the cost ledger";
        var promptB = "Session B brief: fix the telegram gateway";
        var inputA = new SessionInputLog();
        inputA.Append(promptA);
        var inputB = new SessionInputLog();
        inputB.Append(promptB);

        await using var hub = new HubEvents();
        var tailerA = NewTailer(hub, tree, inputA, childStartUtc: DateTime.UtcNow.AddSeconds(-5), claims: claims);
        var tailerB = NewTailer(hub, tree, inputB, childStartUtc: DateTime.UtcNow.AddSeconds(-5), claims: claims);
        tailerA.Start();
        tailerB.Start();
        try
        {
            await Task.Delay(400);
            var fileA = tree.NewTranscript();
            await tree.AppendAsync(fileA, UserLine("a1", tree.Cwd, promptA, DateTime.UtcNow));
            await Task.Delay(150);
            // B's fork is written LAST, so "newest wins" would hand it to A as well.
            var fileB = tree.NewTranscript();
            await tree.AppendAsync(fileB, UserLine("b1", tree.Cwd, promptB, DateTime.UtcNow));

            (await PollForEntriesAsync(tailerA, want: 1, TimeSpan.FromSeconds(10)))
                .ShouldHaveSingleItem().Text.ShouldBe(promptA);
            (await PollForEntriesAsync(tailerB, want: 1, TimeSpan.FromSeconds(10)))
                .ShouldHaveSingleItem().Text.ShouldBe(promptB);

            tailerA.BoundTranscriptPath.ShouldBe(fileA);
            tailerB.BoundTranscriptPath.ShouldBe(fileB);
        }
        finally
        {
            await tailerA.DisposeAsync();
            await tailerB.DisposeAsync();
        }
    }

    /// <summary>
    /// C1 on its own: even a candidate that satisfies every other rule (identical delivered text is
    /// entirely possible — two agents get the same launch note) is refused while another live
    /// session holds it.
    /// </summary>
    [Test]
    public async Task A_file_claimed_by_another_live_tailer_is_refused_even_when_it_qualifies()
    {
        using var tree = new TranscriptTree("claimed-elsewhere");
        var prompt = "Identical launch note delivered to both agents";
        var input = new SessionInputLog();
        input.Append(prompt);

        var file = tree.NewTranscript();
        await tree.AppendAsync(file, UserLine("u1", tree.Cwd, prompt, DateTime.UtcNow));

        var claims = new TranscriptClaimRegistry();
        claims.TryClaim(file, Guid.NewGuid()).ShouldBeTrue(); // some other live session owns it

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow.AddSeconds(-5), claims: claims);
        tailer.Start();
        try
        {
            await Task.Delay(RefusalWindow);
            tailer.BoundTranscriptPath.ShouldBeNull("one transcript, one session");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// Precedence: content beats timestamp. A <c>--resume</c> can fork to a new file whose copied
    /// history carries the ORIGINAL timestamps, so C3 is waived for a resume launch — otherwise
    /// every resumed session would refuse its own transcript.
    /// </summary>
    [Test]
    public async Task Resume_fork_with_copied_old_timestamps_is_adopted_on_content_match()
    {
        using var tree = new TranscriptTree("resume-fork");
        var autoContinue = "Continue where the previous session left off";
        var input = new SessionInputLog();
        input.Append(autoContinue);

        var childStart = DateTime.UtcNow;
        var file = tree.NewTranscript();
        // Copied history: timestamps from the ORIGINAL session, hours before this relaunch.
        await tree.AppendAsync(file, UserLine("h1", tree.Cwd, "the original conversation", childStart.AddHours(-3)));
        await tree.AppendAsync(file, UserLine("h2", tree.Cwd, autoContinue, childStart.AddHours(-3)));

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: childStart, resumeLaunch: true);
        tailer.Start();
        try
        {
            var entries = await PollForEntriesAsync(tailer, want: 2, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Text).ShouldContain(autoContinue);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// Refusing is not giving up: the tailer keeps polling for the session's lifetime, so a
    /// legitimate transcript that appears late still binds — and the heuristic bind is announced.
    /// </summary>
    [Test]
    public async Task Discovery_refusal_publishes_fault_event_and_rebinds_late_when_a_valid_file_appears()
    {
        using var tree = new TranscriptTree("late-rebind");
        var childStart = DateTime.UtcNow;
        var prompt = "The brief this session was actually given";
        var input = new SessionInputLog();
        input.Append(prompt);

        var stranger = tree.NewTranscript();
        await tree.AppendAsync(stranger, UserLine("s1", tree.Cwd, "an unrelated conversation", childStart.AddMinutes(-30)));

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: childStart, refusalFaultDelay: TimeSpan.FromMilliseconds(400));
        tailer.Start();
        try
        {
            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(6));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.AdoptionRefused);

            var real = tree.NewTranscript();
            await tree.AppendAsync(real, UserLine("r1", tree.Cwd, prompt, DateTime.UtcNow));

            (await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10)))
                .ShouldHaveSingleItem().Text.ShouldBe(prompt);

            var bound = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptBound, TimeSpan.FromSeconds(5));
            bound.ShouldNotBeNull("a heuristic bind is an audit event");
            bound!.RootElement.GetProperty("How").GetString().ShouldBe(TranscriptBindMethods.Discovery);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// A dead child writes no further transcript. Input WAS delivered and nothing was ever ingested,
    /// so that is reported at once rather than polled for silently until the runner restarts.
    /// </summary>
    [Test]
    public async Task Child_exit_with_delivered_input_and_no_transcript_faults_immediately()
    {
        using var tree = new TranscriptTree("child-exit");
        var input = new SessionInputLog();
        input.Append("A brief that was delivered before the child died");

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow);
        tailer.Start();
        try
        {
            await Task.Delay(400);
            tailer.NotifyChildExited();

            var fault = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(10));
            fault.ShouldNotBeNull();
            fault!.RootElement.GetProperty("Kind").GetString().ShouldBe(TranscriptFaultKinds.TranscriptMissing);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    // ------------------------------------------------------------- §8.2 fork-follow and restart

    /// <summary>
    /// A sibling's <c>/clear</c> fork lands in the same cwd, newer than the file we are tailing, and
    /// would satisfy the old fork rule outright. It must not be followed: it is claimed by its own
    /// session and holds none of our text.
    /// </summary>
    [Test]
    public async Task Clear_fork_of_a_sibling_session_is_not_followed()
    {
        using var tree = new TranscriptTree("sibling-fork");
        var sessionId = Guid.NewGuid();
        var mine = tree.ExactTranscript(sessionId);
        await tree.AppendAsync(mine, UserLine("m1", tree.Cwd, "my own first prompt", DateTime.UtcNow));

        var input = new SessionInputLog();
        input.Append("my own first prompt");

        await using var hub = new HubEvents();
        var claims = new TranscriptClaimRegistry();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow.AddSeconds(-5), claims: claims, sessionId: sessionId);
        tailer.Start();
        try
        {
            (await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10)))
                .ShouldHaveSingleItem().Text.ShouldBe("my own first prompt");

            await Task.Delay(200);
            var siblingFork = tree.NewTranscript();
            claims.TryClaim(siblingFork, Guid.NewGuid()).ShouldBeTrue();
            await tree.AppendAsync(siblingFork, UserLine("s1", tree.Cwd, "the sibling's post-clear prompt", DateTime.UtcNow));

            await Task.Delay(RefusalWindow);
            tailer.BoundTranscriptPath.ShouldBe(mine, "a sibling's fork is not ours to follow");
            tailer.Snapshot().Entries.Select(e => e.Text)
                .ShouldNotContain("the sibling's post-clear prompt");
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// The deliberate deferral: a fresh <c>/clear</c> fork holds only the local-command record,
    /// which identifies nobody, so the switch waits for this session's next real prompt to land in
    /// it. Harmless — the old file is quiet, so working/idle reads idle and the queued post-clear
    /// delivery is exactly what completes the identification.
    /// </summary>
    [Test]
    public async Task Clear_fork_is_followed_once_it_contains_this_sessions_next_prompt()
    {
        using var tree = new TranscriptTree("own-clear-fork");
        var sessionId = Guid.NewGuid();
        var mine = tree.ExactTranscript(sessionId);
        await tree.AppendAsync(mine, UserLine("m1", tree.Cwd, "before the clear", DateTime.UtcNow));

        var input = new SessionInputLog();
        input.Append("before the clear");

        await using var hub = new HubEvents();
        var tailer = NewTailer(hub, tree, input, childStartUtc: DateTime.UtcNow.AddSeconds(-5), sessionId: sessionId);
        tailer.Start();
        try
        {
            await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));

            await Task.Delay(200);
            var fork = tree.NewTranscript();
            await tree.AppendAsync(fork, UserLine("f1", tree.Cwd, "<command-name>/clear</command-name>", DateTime.UtcNow));

            await Task.Delay(RefusalWindow);
            tailer.BoundTranscriptPath.ShouldBe(mine, "a command record alone identifies nobody");

            // The queued post-clear delivery goes in, and lands in the fork.
            input.Append("the delivery that followed the clear");
            await tree.AppendAsync(fork, UserLine("f2", tree.Cwd, "the delivery that followed the clear", DateTime.UtcNow));

            var entries = await PollForEntriesAsync(tailer, want: 3, TimeSpan.FromSeconds(15));
            entries.Select(e => e.Text).ShouldContain("the delivery that followed the clear");
            tailer.BoundTranscriptPath.ShouldBe(fork);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// Restart re-adoption without guessing. The sidecar names the file this session was already
    /// reading, so a newer, busier stranger in the same cwd is never even considered — this is what
    /// replaced the 30-second active-pre-existing heuristic that caused the incident.
    /// </summary>
    [Test]
    public async Task Sidecar_path_is_retailed_directly_after_restart_with_no_discovery()
    {
        using var tree = new TranscriptTree("sidecar-retail");

        var mine = tree.NewTranscript();
        await tree.AppendAsync(mine, UserLine("m1", tree.Cwd, "work from before the runner restart", DateTime.UtcNow.AddMinutes(-5)));

        // A newer, actively-written stranger in the same cwd — the old rule's winner.
        var stranger = tree.NewTranscript();
        await tree.AppendAsync(stranger, UserLine("s1", tree.Cwd, "the operator, still typing", DateTime.UtcNow));

        await using var hub = new HubEvents();
        // After a restart the input log is EMPTY: nothing could satisfy C4, which is exactly why
        // the sidecar has to carry the answer.
        var tailer = NewTailer(
            hub, tree, new SessionInputLog(),
            childStartUtc: DateTime.UtcNow.AddMinutes(-10),
            knownTranscriptPath: mine,
            restartAdopt: true);
        tailer.Start();
        try
        {
            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries.ShouldHaveSingleItem().Text.ShouldBe("work from before the runner restart");
            tailer.BoundTranscriptPath.ShouldBe(mine);
        }
        finally
        {
            await tailer.DisposeAsync();
        }
    }

    /// <summary>
    /// The migration shim for sessions that predate sidecars: allowed ONLY when there is exactly one
    /// active cwd-matching candidate, only on the restart path, and never silently — it announces
    /// itself as a heuristic bind. Two candidates and it refuses, because picking between them would
    /// be the guess this card exists to remove.
    /// </summary>
    [Test]
    public async Task Restart_without_sidecar_uses_migration_shim_only_for_unique_candidate()
    {
        // Two active candidates: refuse.
        using (var tree = new TranscriptTree("shim-ambiguous"))
        {
            var a = tree.NewTranscript();
            var b = tree.NewTranscript();
            await tree.AppendAsync(a, UserLine("a1", tree.Cwd, "candidate one", DateTime.UtcNow));
            await tree.AppendAsync(b, UserLine("b1", tree.Cwd, "candidate two", DateTime.UtcNow));

            await using var hub = new HubEvents();
            var tailer = NewTailer(
                hub, tree, new SessionInputLog(),
                childStartUtc: DateTime.UtcNow.AddHours(-1),
                restartAdopt: true,
                refusalFaultDelay: TimeSpan.FromMilliseconds(400));
            tailer.Start();
            try
            {
                await Task.Delay(RefusalWindow);
                tailer.BoundTranscriptPath.ShouldBeNull("two candidates is a coin toss, not an identification");
                (await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptFault, TimeSpan.FromSeconds(5)))
                    .ShouldNotBeNull();
            }
            finally
            {
                await tailer.DisposeAsync();
            }
        }

        // Exactly one active candidate: adopt, and say so.
        using (var tree = new TranscriptTree("shim-unique"))
        {
            var only = tree.NewTranscript();
            await tree.AppendAsync(only, UserLine("o1", tree.Cwd, "the one live conversation here", DateTime.UtcNow));

            await using var hub = new HubEvents();
            var tailer = NewTailer(
                hub, tree, new SessionInputLog(),
                childStartUtc: DateTime.UtcNow.AddHours(-1),
                restartAdopt: true);
            tailer.Start();
            try
            {
                (await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10)))
                    .ShouldHaveSingleItem().Text.ShouldBe("the one live conversation here");

                var bound = await hub.WaitForAsync(SessionRunnerEventNames.SessionTranscriptBound, TimeSpan.FromSeconds(5));
                bound.ShouldNotBeNull();
                bound!.RootElement.GetProperty("How").GetString().ShouldBe(TranscriptBindMethods.MigrationShim);
            }
            finally
            {
                await tailer.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Claims must be rebuilt from sidecars BEFORE any session is adopted — the adoption sweep runs
    /// to completion before the HTTP API listens, so a freshly launched session can never race the
    /// restore and discover a file a surviving session still owns.
    /// </summary>
    [Test]
    public async Task Claims_are_restored_from_sidecars_before_new_adoption_runs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-sidecar-claims-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var survivor = Guid.NewGuid();
            var transcriptPath = Path.Combine(root, "survivor.jsonl");
            new TranscriptSidecar
            {
                SessionId = survivor,
                Cwd = root,
                ChildStartUtc = DateTime.UtcNow.AddMinutes(-10),
                TranscriptPath = transcriptPath,
                How = TranscriptBindMethods.Discovery,
            }.SaveAtomic(TranscriptSidecar.PathFor(root, survivor));

            var settings = new SessionRunnerSettings { SessionLogPath = root };
            await using var runtime = new SessionRunnerRuntime(
                Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);

            await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);

            runtime.TranscriptClaims.IsClaimedByOther(transcriptPath, Guid.NewGuid())
                .ShouldBeTrue("a surviving session's transcript stays off-limits to everyone else");
            runtime.TranscriptClaims.TryClaim(transcriptPath, survivor)
                .ShouldBeTrue("the owning session re-claims its own file (--resume reuses the id)");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------------------------------------------------------------------------- test plumbing

    private static TranscriptTailer NewTailer(
        HubEvents hub,
        TranscriptTree tree,
        SessionInputLog inputLog,
        DateTime? childStartUtc = null,
        string? agentName = null,
        bool resumeLaunch = false,
        TranscriptClaimRegistry? claims = null,
        string? knownTranscriptPath = null,
        bool restartAdopt = false,
        TimeSpan? refusalFaultDelay = null,
        Guid? sessionId = null) =>
        new(
            sessionId ?? Guid.NewGuid(),
            tree.Cwd,
            hub.Hub,
            NullLogger.Instance,
            exactIdGrace: ShortGrace,
            forkScanInterval: TimeSpan.FromMilliseconds(300),
            claims: claims,
            inputLog: inputLog,
            childStartUtc: childStartUtc,
            agentName: agentName,
            resumeLaunch: resumeLaunch,
            knownTranscriptPath: knownTranscriptPath,
            restartAdopt: restartAdopt,
            refusalFaultDelay: refusalFaultDelay);

    /// <summary>A minimal Claude "user" JSONL record: cwd (rule C2), timestamp (C3), prompt text (C4).</summary>
    private static string UserLine(string uuid, string cwd, string text, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            type = "user",
            uuid,
            cwd,
            timestamp = timestamp.ToUniversalTime().ToString("o"),
            message = new { role = "user", content = text },
        });

    /// <summary>The meta record every <c>--name</c>ed launch writes (rule C2b).</summary>
    private static string AgentNameLine(string agentName) =>
        JsonSerializer.Serialize(new { type = "agent-name", agentName });

    private static async Task<IReadOnlyList<RunnerTranscriptEvent>> PollForEntriesAsync(
        TranscriptTailer tailer, int want, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = tailer.Snapshot();
            if (snapshot.Entries.Count >= want)
                return snapshot.Entries;
            await Task.Delay(150);
        }

        return tailer.Snapshot().Entries;
    }

    /// <summary>A temp CLAUDE_CONFIG_DIR tree plus a session cwd, torn down together.</summary>
    private sealed class TranscriptTree : IDisposable
    {
        private readonly string _configDir;
        private readonly string _projectDir;

        public TranscriptTree(string label)
        {
            _configDir = Path.Combine(Path.GetTempPath(), $"antiphon-adopt-{label}-{Guid.NewGuid():N}");
            _projectDir = Path.Combine(_configDir, "projects", "C--src-Antiphon");
            Directory.CreateDirectory(_projectDir);
            Cwd = Path.Combine(Path.GetTempPath(), $"agent-cwd-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Cwd);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _configDir);
        }

        public string Cwd { get; }

        /// <summary>A self-chosen <c>&lt;uuid&gt;.jsonl</c>, i.e. what an id-fork produces.</summary>
        public string NewTranscript() => Path.Combine(_projectDir, Guid.NewGuid().ToString("D") + ".jsonl");

        /// <summary>The file Claude writes when it DOES honour <c>--session-id</c>.</summary>
        public string ExactTranscript(Guid sessionId) => Path.Combine(_projectDir, sessionId.ToString("D") + ".jsonl");

        public Task AppendAsync(string path, string line) => File.AppendAllTextAsync(path, line + "\n");

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
            try { Directory.Delete(_configDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(Cwd, recursive: true); } catch { /* best effort */ }
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

                await Task.Delay(100);
            }

            return null;
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { await _pump; } catch { /* drained */ }
            _cts.Dispose();
        }
    }
}
