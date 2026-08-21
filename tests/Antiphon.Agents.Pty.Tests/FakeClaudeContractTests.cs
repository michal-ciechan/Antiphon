using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Locks the Claude TUI <em>submit contract</em> against the fake Claude (a real console program driven
/// through the same ConPTY path as the real CLI). These run in CI — no <c>ANTIPHON_HEADED_TESTS</c>, no
/// real Claude, no auth — because the peer is deterministic.
///
/// They exist because the bug that shipped (queued messages landing in the composer but never submitting)
/// was invisible to <c>FakeAgentProtocolAdapter</c>, a C#-level stub whose <c>SendInputAsync</c> is just
/// <c>SentInput += input</c> — it has no terminal, so <c>"body\r"</c> in one write and <c>"body"</c> then
/// <c>"\r"</c> in two writes look identical to it. The defect lived at the real PTY boundary, which only a
/// live peer exercises. <c>SessionMessageQueueService.DeliverAsync</c> now sends the body and the
/// submitting CR as two separate writes; these tests pin both sides of that distinction.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class FakeClaudeContractTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    private static void SkipIfUnavailable()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
    }

    /// <summary>
    /// CARD-0045: the fake IS the model of the <b>inbox conhost</b> typing path (CARD-0028), so every
    /// test reached through this helper means inbox and must say so rather than inheriting whatever
    /// <c>ANTIPHON_PTY_BACKEND</c> the launching shell happened to export. Pinned, not inherited: on
    /// the modern pseudoconsole the bracketed-paste markers reach the fake, its clip model exempts
    /// paste-mode content, and tests asserting "a chunk must be lost" fail <em>because the fix
    /// works</em>. The inbox behaviour still ships as the fallback on any machine without the
    /// redistributable, so it stays pinned here; the modern half lives in
    /// <see cref="LaunchClippingFakeOnModernPtyAsync"/>.
    /// </summary>
    /// <param name="alsoAwaitBanner">
    /// An armed model's startup announcement (e.g. <c>SWALLOWENTER:</c>). Every model announces
    /// itself, and the buffer is cleared right after readiness — so a test that wants to prove its
    /// model really armed has to wait for that line HERE, not read it back afterwards.
    /// </param>
    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(
        IDictionary<string, string>? env = null, string? alsoAwaitBanner = null)
    {
        var launch = Stopwatch.StartNew();
        var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
        ShouldBeInbox(runner);
        // CARD-0050 S3: runaway bound — success returns on the banner. Under the concurrent
        // double-suite load a cold fakeclaude launch was measured >15s.
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Claude ready") && (alsoAwaitBanner is null || s.Contains(alsoAwaitBanner)),
            TimeSpan.FromSeconds(45));
        launch.Stop();
        ready.ShouldBeTrue(
            "fake Claude should print its readiness banner"
            + (alsoAwaitBanner is null ? "" : $" and announce {alsoAwaitBanner}")
            + $"; spawn-to-banner={launch.Elapsed.TotalMilliseconds:F0}ms; raw: " + runner.SnapshotText());
        runner.ClearLiveBuffer();
        return runner;
    }

    /// <summary>The declaration, asserted once per launch, so a regression names itself (CARD-0045).</summary>
    private static void ShouldBeInbox(PtyAgentRunner runner) =>
        runner.Backend!.Backend.ShouldBe(
            PtyBackend.InboxConhost,
            "these tests pin inbox-conhost facts and must declare that backend — an inherited "
            + "ANTIPHON_PTY_BACKEND must not be able to change what this suite means");

    // The BUG path: text and the submitting CR in a SINGLE write. The TUI reads it as a paste — the CR
    // collapses to a literal newline and the line is NOT submitted. This is exactly what the old
    // DeliverAsync did (`body.TrimEnd() + "\r"` in one SendInputAsync), so the message was stranded.
    // CARD-0050 S3: the one-write (paste) arm stays time-based — a single write can only be split,
    // never merged, so it does not have the body→CR burst-gap race the echo-gated helper fixes.
    [Test]
    public async Task Text_and_CR_in_one_write_does_NOT_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("queued message\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(2));
        submitted.ShouldBeFalse("text+CR in one write must be treated as a paste, not a submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The FIX path: body, then the CR as a SEPARATE write — the two-write shape DeliverAsync now uses
    // (two SendInputAsync calls). The lone CR is a discrete Enter and submits the buffered line.
    [Test]
    public async Task Body_then_separate_CR_submits()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await EchoGatedSubmit.SendAsync(runner, "queued message");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:queued message"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue("body followed by a separate CR must submit");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The fragmentation hazard, as MEASURED against real Claude (probe runs 2026-07-31): a body
    // with CR/CRLF line endings fragments — each mid-body \r acts as Enter and submits the
    // fragment before it. (\n endings are always literal and land intact; the original 2026-07-29
    // live miss carried CR endings through a pipeline that did not normalize them. The server's
    // DeliverAsync now normalizes to LF — SessionMessageQueuePtyIntegrationTests pins that.)
    [Test]
    public async Task Unbracketed_body_with_CR_line_endings_fragments_into_partial_turns()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD first line of a big paste\r\n"
            + string.Join("\r\n", Enumerable.Range(1, 10).Select(i => $"line {i} " + new string('x', 60)))
            + "\r\nTAIL last line";
        await runner.WriteAsync(body);
        await Task.Delay(25);
        await runner.WriteAsync("\r");

        // The head submits as its OWN fragment turn — the very corruption LF-normalization prevents.
        var fragmented = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:HEAD first line of a big paste") && !s.Contains("TAIL last line\\n"),
            TimeSpan.FromSeconds(5));
        fragmented.ShouldBeTrue("a CR-line-ending body must fragment (the hazard being modelled)");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The counterpart: the SAME multi-line body with \n endings lands intact even UNWRAPPED —
    // measured behavior of real Claude (gap-split LF probe, 2026-07-31). Newlines are literal;
    // only \r submits.
    [Test]
    public async Task Unbracketed_body_with_LF_line_endings_submits_as_one_intact_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD first line of a big paste\n"
            + string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i} " + new string('x', 60)))
            + "\nTAIL last line";
        await EchoGatedSubmit.SendAsync(runner, body);

        var intact = await runner.WaitForOutputAsync(
            s =>
            {
                var flat = s.Replace("\r", "").Replace("\n", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    flat, @"SUBMITTED:(?:(?!FAKE response).)*HEAD first line(?:(?!FAKE response).)*TAIL last line");
            },
            TimeSpan.FromSeconds(5));
        intact.ShouldBeTrue("an LF-line-ending body must submit whole, as one turn");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The FIX contract: the same large body wrapped in bracketed paste (\e[200~..\e[201~) is immune to
    // chunking — it lands whole, and the separate CR submits it as ONE turn.
    [Test]
    public async Task Bracket_wrapped_large_multiline_body_submits_as_one_intact_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD first line of a big paste\n"
            + string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i} " + new string('x', 60)))
            + "\nTAIL last line";
        await EchoGatedSubmit.SendAsync(runner, "\x1b[200~" + body + "\x1b[201~");

        // One SUBMITTED marker carrying head AND tail (newlines escaped into the single marker line).
        var intact = await runner.WaitForOutputAsync(
            s =>
            {
                var flat = s.Replace("\r", "").Replace("\n", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    flat, @"SUBMITTED:(?:(?!FAKE response).)*HEAD first line(?:(?!FAKE response).)*TAIL last line");
            },
            TimeSpan.FromSeconds(5));
        intact.ShouldBeTrue("a bracket-wrapped multi-line body must submit whole, as one turn");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // SendLineAsync is the runner primitive (body, 20ms, "\r") that the queue's two-write delivery mirrors;
    // pin that it submits against the same peer, so a regression in either layer is caught here.
    [Test]
    public async Task SendLineAsync_submits_a_turn_and_emits_idle_signal()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("hello there");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:hello there"), TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue();

        // The turn-end signal RunnerClaudeAdapter keys on must reach our capture so higher layers see
        // "idle". We assert the " for Ns" done pattern specifically — the idle OSC title is also emitted
        // but ConPTY consumes window-title sequences, so the done pattern is the one that survives.
        // (Waited-for, not snapshotted: the SUBMITTED match above can win the race against the
        // trailing turn-end bytes.)
        var done = await runner.WaitForOutputAsync(s => s.Contains(" for 1s"), TimeSpan.FromSeconds(3));
        done.ShouldBeTrue("done pattern (\" for Ns\") must be emitted at turn end");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // A batched delivery is ONE multi-line paste burst followed by a lone CR. The whole body must
    // submit as one turn, and the SUBMITTED marker must escape newlines so it stays one assertable line.
    [Test]
    public async Task Multi_line_paste_then_lone_cr_submits_whole_body_with_escaped_marker()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "[context]\nfirst message\nsecond message\n\n[current]\nthird message respond now";
        await EchoGatedSubmit.SendAsync(runner, body);

        var escaped = body.Replace("\n", "\\n");
        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:" + escaped),
            TimeSpan.FromSeconds(5));
        submitted.ShouldBeTrue(
            "the whole multi-line body must submit as one turn with an escaped marker. Raw output:\n"
            + runner.SnapshotText());

        // Wall-of-text bodies must not flood the screen: the response echo truncates at 60 chars
        // of the escaped form (single assertable line).
        var echoed = await runner.WaitForOutputAsync(
            s => s.Contains("FAKE response to: " + escaped[..60]), TimeSpan.FromSeconds(3));
        echoed.ShouldBeTrue("response echo must be the escaped body truncated to 60 chars");
        runner.SnapshotText().ShouldNotContain("FAKE response to: " + escaped[..61]);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // Compaction is not a turn: /compact renders the pinned "Compacted (...)" screen line and NO
    // " for Ns" done pattern — the detectors must never read a compaction as a completed turn.
    [Test]
    public async Task Slash_compact_emits_compacted_screen_line_and_no_turn_end()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await EchoGatedSubmit.SendAsync(runner, "/compact");

        var compacted = await runner.WaitForOutputAsync(
            s => s.Contains("Compacted (ctrl+o to see full summary)"), TimeSpan.FromSeconds(5));
        compacted.ShouldBeTrue("/compact must render the pinned Compacted screen line");

        await Task.Delay(300); // give any (wrong) turn-end output time to arrive
        runner.SnapshotText().ShouldNotContain(" for 1s",
            customMessage: "compaction must not emit the turn-end done pattern");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The fallback screen detector must trip on the fake's (pinned) Compacted line — keeps the
    // fake, the detector regex, and the canary-pinned real line locked together.
    [Test]
    public async Task Compacted_screen_line_from_fakeclaude_trips_the_detector()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await EchoGatedSubmit.SendAsync(runner, "/compact");

        var detected = await new ClaudeCompactedDetector { MaxWait = TimeSpan.FromSeconds(5) }
            .WaitAsync(runner);
        detected.ShouldBeTrue("ClaudeCompactedDetector must trip on the pinned Compacted line");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Compact_after_turns_env_emits_compacted_after_nth_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(
            new Dictionary<string, string> { ["ANTIPHON_FAKE_COMPACT_AFTER_TURNS"] = "2" });

        await EchoGatedSubmit.SendAsync(runner, "first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();
        runner.SnapshotText().ShouldNotContain("Compacted (",
            customMessage: "auto-compaction must not fire before the configured turn count");

        await EchoGatedSubmit.SendAsync(runner, "second");
        var compacted = await runner.WaitForOutputAsync(
            s => s.Contains("Compacted (ctrl+o to see full summary)"), TimeSpan.FromSeconds(5));
        compacted.ShouldBeTrue("auto-compaction must fire after the Nth turn");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Transcript_path_env_appends_user_assistant_and_boundary_lines()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-transcript-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            await EchoGatedSubmit.SendAsync(runner, "hello transcript");
            (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:hello transcript"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            await EchoGatedSubmit.SendAsync(runner, "/compact");
            (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            // One turn (user + assistant) then the manual-compaction set of six (pinned below).
            var lines = await WaitForTranscriptLinesAsync(path, 8);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBe(8);
            lines[0].ShouldContain("\"type\":\"user\"");
            lines[0].ShouldContain("hello transcript");
            lines[1].ShouldContain("\"type\":\"assistant\"");
            lines[1].ShouldContain("\"stop_reason\":\"end_turn\"");
            lines[1].ShouldContain("\"model\":\"claude-opus-4\"");
            lines[3].ShouldContain("\"subtype\":\"compact_boundary\"");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // CARD-0041: a manual /compact writes SIX records, not one, and two of them are the shapes that
    // left a compacted session reading "working" forever — the RAW typed prompt (recorded in
    // addition to the <command-name> wrapper) and the isCompactSummary continuation. Modelling only
    // the boundary, as this fake used to, made the bug unreproducible. Real-Claude shape pinned by
    // ClaudeCompactionCanaryTests; this pins the fake's mirror, and that the normalizer's rules
    // classify each record the way the working checks need.
    [Test]
    public async Task Manual_compact_with_args_writes_the_full_measured_record_set()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-compact-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            await EchoGatedSubmit.SendAsync(runner, "/compact keep the API contract notes");
            (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("/compact WITH ARGUMENTS must still compact — the live shape had args");
            // The screen line is written BEFORE the records — and this is the fake's first JSON
            // serialization, whose warm-up is slow enough to lose the whole set to the kill below.
            var lines = await WaitForTranscriptLinesAsync(path, 6);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBe(6);

            // 1. The raw typed text, as a plain user record: no isCompactSummary, no isMeta. This is
            //    the record the working rule tripped over, and it is deliberately NOT excluded —
            //    the boundary outranks it instead.
            ContentOf(lines[0]).ShouldBe("/compact keep the API contract notes");
            lines[0].ShouldNotContain("isCompactSummary");
            lines[0].ShouldNotContain("isMeta");

            // 2. The boundary, carrying the MANUAL trigger the working rules key on (the
            //    trigger→"Context compacted (manual)" mapping is pinned in TranscriptNormalizerTests).
            lines[1].ShouldContain("\"subtype\":\"compact_boundary\"");
            lines[1].ShouldContain("\"trigger\":\"manual\"");

            // 3. The continuation summary: the structural flag AND the text prefix the rule matches.
            lines[2].ShouldContain("\"isCompactSummary\":true");
            TranscriptKinds.IsCompactionContinuationPrompt("UserPrompt", ContentOf(lines[2]))
                .ShouldBeTrue();

            // 4. The isMeta caveat — TranscriptNormalizer.FromUser drops these, so no rule sees it.
            lines[3].ShouldContain("\"isMeta\":true");

            // 5-6. The local-command wrapper pair.
            var wrapper = ContentOf(lines[4]);
            TranscriptKinds.IsLocalCommandRecord("UserPrompt", wrapper).ShouldBeTrue();
            wrapper.ShouldContain("<command-args>keep the API contract notes</command-args>");
            TranscriptKinds.IsLocalCommandRecord("UserPrompt", ContentOf(lines[5])).ShouldBeTrue();

            // The whole point: no turn end is coming for any of it.
            lines.ShouldAllBe(l => !l.Contains("stop_reason"));
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // The auto path is the opposite case: nothing was typed, the boundary lands MID-turn, and the
    // working rules must NOT read it as an end.
    [Test]
    public async Task Auto_compaction_writes_an_auto_boundary_and_the_continuation_only()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-autocompact-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path,
                ["ANTIPHON_FAKE_COMPACT_AFTER_TURNS"] = "1",
            });

            await EchoGatedSubmit.SendAsync(runner, "do the big thing");
            (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            var lines = await WaitForTranscriptLinesAsync(path, 4);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBe(4, "user + assistant, then the auto boundary and its continuation");
            lines[2].ShouldContain("\"trigger\":\"auto\"");
            lines[2].ShouldNotContain("command-name");
            lines[2].ShouldNotContain("\"trigger\":\"manual\"",
                customMessage: "an auto boundary must never rank as a turn end");
            lines[3].ShouldContain("\"isCompactSummary\":true");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    /// <summary>
    /// CARD-0072 (S1): the armed turn dies on an API-error stub — ONE synthetic assistant record
    /// carrying top-level <c>error</c>/<c>isApiErrorMessage:true</c>/<c>apiErrorStatus</c> and its
    /// OWN <c>stop_sequence</c> TurnEnd (shape verbatim from the 23 measured stubs; the real-record
    /// pins live in TranscriptNormalizerTests' api-error-stubs.jsonl fixture — no headed canary
    /// exists because hitting a real limit on purpose is neither reproducible nor affordable).
    /// The NEXT turn answers normally: the measured revival — a typed "Continue" into exactly this
    /// state worked immediately, six times across the sweep.
    /// </summary>
    [Test]
    public async Task An_armed_api_error_kills_that_turn_only_and_writes_the_measured_stub_record()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-apierror-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path,
                ["ANTIPHON_FAKE_API_ERROR"] = "rate_limit",
            });

            await EchoGatedSubmit.SendAsync(runner, "doomed turn");
            (await runner.WaitForOutputAsync(
                    s => s.Contains("You've hit your session limit"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the dead turn renders the error text, not a response");

            var lines = await WaitForTranscriptLinesAsync(path, 2);
            lines.Length.ShouldBe(2, "the prompt was recorded (the API call happened) plus ONE stub record");
            ContentOf(lines[0]).ShouldBe("doomed turn");

            // The stub, structurally: top-level fields beside an ordinary text block + TurnEnd.
            using (var doc = System.Text.Json.JsonDocument.Parse(lines[1]))
            {
                var root = doc.RootElement;
                root.GetProperty("error").GetString().ShouldBe("rate_limit");
                root.GetProperty("isApiErrorMessage").GetBoolean().ShouldBeTrue();
                root.GetProperty("apiErrorStatus").GetInt32().ShouldBe(429);
                var msg = root.GetProperty("message");
                msg.GetProperty("model").GetString().ShouldBe("<synthetic>");
                msg.GetProperty("stop_reason").GetString().ShouldBe(
                    "stop_sequence", "the stub carries its OWN TurnEnd — the working rules read idle unchanged");
                TranscriptKinds.IsApiErrorStub(
                        TranscriptKinds.AssistantText, root.GetProperty("isApiErrorMessage").GetBoolean())
                    .ShouldBeTrue();
            }

            // The revival: the very next turn answers normally, with no stub fields.
            await EchoGatedSubmit.SendAsync(runner, "Continue");
            (await runner.WaitForOutputAsync(s => s.Contains("FAKE response to: Continue"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("a turn after the armed one must answer normally — the measured revival");
            var after = await WaitForTranscriptLinesAsync(path, 4);
            after.Length.ShouldBe(4);
            after[3].ShouldContain("\"stop_reason\":\"end_turn\"");
            after[3].ShouldNotContain("isApiErrorMessage");

            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // Auth stubs carry NO apiErrorStatus on the real records — the absence itself is measured, so
    // the fake must model a missing member, not a null one.
    [Test]
    public async Task An_auth_stub_omits_the_status_field_entirely()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-apierror-auth-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path,
                ["ANTIPHON_FAKE_API_ERROR"] = "authentication_failed",
            });

            await EchoGatedSubmit.SendAsync(runner, "doomed");
            (await runner.WaitForOutputAsync(s => s.Contains("Login expired"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            var lines = await WaitForTranscriptLinesAsync(path, 2);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBe(2);
            lines[1].ShouldContain("\"error\":\"authentication_failed\"");
            lines[1].ShouldContain("\"isApiErrorMessage\":true");
            lines[1].ShouldNotContain("apiErrorStatus",
                customMessage: "the real auth stubs have NO status member at all");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // The transcript is written after the screen output, so "the screen said Compacted" is not
    // evidence the records landed. Poll for the expected count, then assert on what arrived.
    // CARD-0050: the read handle shares ReadWrite so polling never blocks the fake's append (the
    // old File.ReadAllLines share mode could starve the writer into its retry loop), and a deadline
    // miss fails HERE with the fake's timing sidecar attached — the late/lost/starved distinction
    // is the whole diagnosis for this flake cast.
    private static async Task<string[]> WaitForTranscriptLinesAsync(string path, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var lines = Array.Empty<string>();
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    lines = (await ReadSharedLinesAsync(path))
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToArray();
                    if (lines.Length >= expected)
                        return lines;
                }
                catch (IOException)
                {
                    // Mid-append; try again.
                }
            }
            await Task.Delay(50);
        }

        lines.Length.ShouldBeGreaterThanOrEqualTo(
            expected,
            $"transcript at {path} was still {expected - lines.Length} record(s) short at the 10s "
            + $"deadline. Timing sidecar (process-start → per-record stamps):\n{ReadTimingSidecar(path)}");
        return lines;
    }

    /// <summary>Reads the whole file WITHOUT excluding concurrent writers (FileShare.ReadWrite).</summary>
    private static async Task<string[]> ReadSharedLinesAsync(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync()).Split('\n');
    }

    private static string ReadTimingSidecar(string path)
    {
        try
        {
            var sidecar = path + ".timing";
            return File.Exists(sidecar)
                ? string.Join("\n", File.ReadAllLines(sidecar))
                : "(no sidecar written — the fake never reached its first append)";
        }
        catch (Exception ex)
        {
            return $"(sidecar unreadable: {ex.Message})";
        }
    }

    /// <summary>The <c>message</c> object of an assistant record — id, stop_reason, content.</summary>
    private static System.Text.Json.JsonElement MessageOf(string jsonLine)
    {
        var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
        doc.RootElement.GetProperty("type").GetString().ShouldBe("assistant");
        return doc.RootElement.GetProperty("message").Clone();
    }

    private static string ContentOf(string jsonLine)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
        doc.RootElement.GetProperty("type").GetString().ShouldBe("user");
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()!;
    }

    // Local built-in commands (/clear, /model, /status …) write ONLY <command-name> +
    // <local-command-stdout> USER records to the JSONL — no assistant line, no TurnEnd (no API call
    // happens). That absence is the working/idle hazard fixed 2026-07-31 (a session counting these
    // as activity read "working" forever and stranded WhenIdle deliveries). Real-Claude record
    // shape pinned by ClaudeLocalCommandCanaryTests; this pins the fake's mirror of it.
    [Test]
    public async Task Local_slash_command_writes_command_records_and_no_turn_end()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-localcmd-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            await EchoGatedSubmit.SendAsync(runner, "/model opus");
            (await runner.WaitForOutputAsync(s => s.Contains("FAKE local command: /model"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.ShouldBe(2);

            var invocation = ContentOf(lines[0]);
            invocation.ShouldStartWith("<command-name>/model</command-name>");
            invocation.ShouldContain("<command-args>opus</command-args>");
            var stdout = ContentOf(lines[1]);
            stdout.ShouldStartWith("<local-command-stdout>");

            // Both records match the shared classifier the server/client working checks key on.
            Antiphon.SessionRunner.Contracts.TranscriptKinds
                .IsLocalCommandRecord("UserPrompt", invocation).ShouldBeTrue();
            Antiphon.SessionRunner.Contracts.TranscriptKinds
                .IsLocalCommandRecord("UserPrompt", stdout).ShouldBeTrue();

            // And crucially: NO assistant line, NO turn end.
            lines.ShouldAllBe(l => !l.Contains("\"type\":\"assistant\"") && !l.Contains("stop_reason"));
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // CARD-0046. Real Claude writes one API response as SEVERAL JSONL records — a signature-only
    // thinking record, then the text record — and stamps EVERY one with the response's stop_reason,
    // so what reaches the server FIRST is a bare TurnEnd with no text at all. Settlement fired on it
    // and handed six callers the delegate's preamble instead of its verdict. The fake could not
    // reproduce any of that: it emitted one record per turn and NO message.id, so nothing below the
    // service layer could exercise same-response identity. This pins the modelled shape.
    [Test]
    public async Task A_split_final_response_reaches_the_server_as_a_bare_turn_end_then_text()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-split-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path,
                ["ANTIPHON_FAKE_SPLIT_FINAL"] = "1",
            });

            await EchoGatedSubmit.SendAsync(runner, "report the verdict");
            (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:report the verdict"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            // user + thinking + text, where the one-record default writes only user + text.
            var lines = await WaitForTranscriptLinesAsync(path, 3);
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            lines.Length.ShouldBe(3);

            var thinking = MessageOf(lines[1]);
            var text = MessageOf(lines[2]);

            // The thinking record comes FIRST and carries the response's stop_reason, so the server
            // sees a finished turn before any text exists. Its thinking string is EMPTY (signature
            // only — true of all 1 936 thinking blocks measured), which is what makes
            // TranscriptNormalizer yield a bare TurnEnd and nothing else for it. The normalizer half
            // is pinned over the REAL records in TranscriptNormalizerTests.
            var block = thinking.GetProperty("content")[0];
            block.GetProperty("type").GetString().ShouldBe("thinking");
            block.GetProperty("thinking").GetString().ShouldBe("");
            block.TryGetProperty("signature", out _).ShouldBeTrue();
            thinking.GetProperty("stop_reason").GetString().ShouldBe("end_turn");
            text.GetProperty("stop_reason").GetString().ShouldBe("end_turn");
            text.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("text");

            thinking.GetProperty("id").GetString()
                .ShouldBe(
                    text.GetProperty("id").GetString(),
                    "one response, one message.id — that shared id is what settlement waits on");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // Default OFF, and the id is present either way: the one-record shape is also real, and it is
    // what every other transcript test in this file counts lines against.
    [Test]
    public async Task An_unsplit_turn_still_carries_a_message_id_on_its_single_record()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-unsplit-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            await EchoGatedSubmit.SendAsync(runner, "report the verdict");
            var lines = await WaitForTranscriptLinesAsync(path, 2);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBe(2, "one user record and ONE assistant record — no split without the flag");
            var message = MessageOf(lines[1]);
            message.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("text");
            message.GetProperty("id").GetString().ShouldNotBeNullOrWhiteSpace();
            // One record: its AssistantText part is normalized (and persisted) BEFORE its own
            // TurnEnd part, so the same-id check settlement makes (CARD-0046) is already satisfied
            // when the boundary is acted on — which is why emitting the id defers nothing.
            message.GetProperty("stop_reason").GetString().ShouldBe("end_turn");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    /// <summary>
    /// CARD-0055's load-bearing assumption (a), in CI: <b>an Enter on an already-empty composer is a
    /// no-op</b> — no submit, no transcript record, no turn. The confirm loop re-presses Enter up to
    /// <c>SubmitAttempts</c> times and NEVER re-types, so if a stray Enter did anything at all the
    /// retry could double-submit — for a channel reply, a duplicate message to a human.
    ///
    /// <para>This is the fake's half. Against real Claude it is
    /// <c>ClaudeSubmitConfirmCanaryTests.Enter_on_an_empty_composer_is_a_no_op</c> (headed,
    /// <c>[Explicit]</c>) — the fake models the contract, only the canary can measure it.</para>
    /// </summary>
    [Test]
    public async Task Enter_on_an_empty_composer_submits_nothing()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-emptyenter-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path });

            // One real submit first, so the composer is empty the way it is after a SUCCESSFUL
            // delivery — which is the state the retry Enters actually land in.
            await EchoGatedSubmit.SendAsync(runner, "the body that did submit");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:the body that did submit"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            (await WaitForTranscriptLinesAsync(path, 2)).Length.ShouldBe(2);

            // The re-presses the confirm loop would send: SubmitAttempts is 3, so two more.
            await runner.WriteAsync("\r");
            await Task.Delay(300);
            await runner.WriteAsync("\r");
            await Task.Delay(1000);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.ShouldBe(2,
                "an Enter on an empty composer must record nothing — two Enters produced "
                + $"{lines.Length - 2} extra record(s): {string.Join(" | ", lines.Skip(2))}");
            runner.SnapshotText().Split("SUBMITTED:").Length.ShouldBe(2,
                "exactly one submit marker; a second means an empty Enter re-submitted something");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    /// <summary>
    /// The modelled failure state itself (CARD-0055, <c>ANTIPHON_FAKE_SWALLOW_ENTER</c>): the CR is
    /// eaten, the screen advances anyway — the signal the old rule called Delivered — and the body
    /// stays in the composer, so the NEXT Enter submits it. That last clause is what makes an
    /// Enter-only retry safe, and what the end-to-end pin
    /// (<c>SessionMessageQueuePtyIntegrationTests</c>) rides on.
    /// </summary>
    [Test]
    public async Task A_swallowed_enter_redraws_holds_the_body_and_the_next_enter_submits_it_once()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-swallow-{Guid.NewGuid():N}.jsonl");
        try
        {
            // The banner is awaited inside the launch (and asserted there): a test that believed it
            // armed this model and did not would otherwise pass for the wrong reason.
            await using var runner = await LaunchReadyFakeAsync(
                new Dictionary<string, string>
                {
                    ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = path,
                    [SwallowEnterVar] = "1",
                },
                alsoAwaitBanner: "SWALLOWENTER:count=1");

            await runner.WriteAsync("the body whose Enter is eaten");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            // Eaten: the screen advanced (this is the false "output advanced" signal) but nothing
            // was submitted and nothing was recorded.
            (await runner.WaitForOutputAsync(s => s.Contains("SWALLOWED-ENTER:"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the swallowed Enter must still redraw — a dead terminal is not the bug");
            await Task.Delay(500);
            runner.SnapshotText().ShouldNotContain("SUBMITTED:");
            File.Exists(path).ShouldBeFalse("a swallowed Enter must write no transcript record");

            // The composer kept the body, so an Enter-only retry — no re-type — submits it.
            await runner.WriteAsync("\r");
            (await runner.WaitForOutputAsync(
                s => s.Contains("SUBMITTED:the body whose Enter is eaten"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the re-pressed Enter must submit the body the composer still holds");

            var lines = await WaitForTranscriptLinesAsync(path, 2);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            // ONE prompt: not zero (stranded), not two (the re-press double-submitting).
            lines.Count(l => l.Contains("\"type\":\"user\"")).ShouldBe(1);
            ContentOf(lines[0]).ShouldBe("the body whose Enter is eaten");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    /// <summary>The env var, named once — the pty integration suite arms the same model.</summary>
    private const string SwallowEnterVar = "ANTIPHON_FAKE_SWALLOW_ENTER";

    // The 2026-08-08 live miss: SendLineAsync used to write its body RAW, so a CRLF prompt (a C#
    // raw-string literal in a CRLF source file — the card work prompt) either fragmented at each
    // mid-body \r or stranded whole in the composer when the CRs fell inside the paste window.
    // The agent's supervised relaunch typed its prompt and then sat "never came back" for half an
    // hour. SendLineAsync now routes through PtyInputEncoding (LF-normalize + bracketed paste), so
    // the same CRLF body must land as ONE intact submitted turn.
    [Test]
    public async Task SendLineAsync_with_CRLF_multiline_body_submits_as_one_intact_turn()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var body = "HEAD work on card CARD-0001\r\n\r\nDescription:\r\nreview the doc and TAIL write the spec";
        await runner.SendLineAsync(body);

        var intact = await runner.WaitForOutputAsync(
            s =>
            {
                var flat = s.Replace("\r", "").Replace("\n", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    flat, @"SUBMITTED:(?:(?!FAKE response).)*HEAD work on card(?:(?!FAKE response).)*TAIL write the spec");
            },
            TimeSpan.FromSeconds(5));
        intact.ShouldBeTrue(
            "a CRLF multi-line body sent via SendLineAsync must submit whole, as one turn. Raw output:\n"
            + runner.SnapshotText());

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// CARD-0128 S2a mechanism pin. Before the production gate, the 20ms writer-side pause and CR
    /// both reached this deaf fake before it read stdin; on wake they drained as one burst and the
    /// CR folded into the body. The gate cannot see tail evidence until wake, then creates the
    /// discrete pause from that evidence, so this is deterministically red on the old code and
    /// green on the gated code.
    /// </summary>
    [Test]
    public async Task SendLineAsync_waits_for_deaf_start_composer_evidence_before_submitting()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync(
            env: new Dictionary<string, string> { ["ANTIPHON_FAKE_DEAF_START_MS"] = "300" },
            alsoAwaitBanner: "DEAFSTART:");

        const string body = "deterministic gated submit";
        await runner.SendLineAsync(body);

        runner.LastSendLineOutcome.ShouldBe(SendLineGateOutcome.EvidenceSeen);
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:" + body), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue("the gated CR must arrive as a discrete Enter after the deaf fake consumes the body");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ------------------------------------------------------------------------------------------
    // CARD-0028 — the TUI's chunk clipping, modelled. OPT-IN (ANTIPHON_FAKE_STDIN_CLIP).
    //
    // fakeclaude receives every byte; the real consumer keeps ONE ~1024-byte read chunk per
    // event-loop turn and drops the rest (CARD-0027). Because the fake was lossless where the real
    // one clips, the 2026-08-10 investigation used it to clear our stack and shipped ceilings on
    // that reading — and they were wrong twice. These pin the fake's model of the MEASURED
    // behaviour, the same way the CR-vs-LF tests above pin the measured line-ending behaviour.
    //
    // The default stays OFF and unchanged: PtyLargeWriteTests deliberately pins that OUR transport
    // delivers 43 KB whole, which is true and must stay pinned.
    // ------------------------------------------------------------------------------------------

    /// <summary>Body of <paramref name="lines"/> 7-byte marker lines — the resolution CARD-0027 measured the cut at.</summary>
    private static string MarkedBody(int lines) =>
        string.Concat(Enumerable.Range(0, lines).Select(i => $"Q{i:D5}\n"));

    /// <summary>Surviving markers as contiguous RUNS — "0-145" says whole chunk, "3,17,42" says the model is wrong.</summary>
    private static string Runs(IEnumerable<int> markers)
    {
        var runs = new List<(int Lo, int Hi)>();
        foreach (var i in markers.OrderBy(x => x))
        {
            if (runs.Count > 0 && runs[^1].Hi == i - 1) runs[^1] = (runs[^1].Lo, i);
            else runs.Add((i, i));
        }
        return runs.Count == 0 ? "(none)" : string.Join(",", runs.Select(r => $"{r.Lo}-{r.Hi}"));
    }

    /// <summary>The fake's own account of what it ate — the first thing to read when one of these fails.</summary>
    private static string ClipNotes(string raw) =>
        string.Join(" | ", raw.Split('\n')
            .Select(l => l.Trim('\r', ' '))
            .Where(l => l.StartsWith("CLIP", StringComparison.Ordinal)));

    /// <summary>Which marked lines reached the child. CR/LF stripped first: ConPTY soft-wraps long lines.</summary>
    private static HashSet<int> MarkersIn(string raw) =>
        System.Text.RegularExpressions.Regex
            .Matches(raw.Replace("\r", "").Replace("\n", ""), @"Q(\d{5})")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();

    /// <summary>
    /// The clipping fake, with a burst gap wide enough that ONE write is ONE turn.
    ///
    /// <para>The fake groups arriving bytes into bursts by a quiet gap, and a burst is what the clip
    /// model treats as one event-loop turn. ConPTY does not hand a single write to a .NET child as a
    /// single read: measured here (<c>ANTIPHON_FAKE_DEBUG_INPUT=1</c>, READS lines), a 1 399-byte
    /// write arrives as 2-5 reads up to ~14 ms apart — and it strips the bracketed-paste markers on
    /// the way, so the first read is the body's first 6 bytes. At the default 12 ms gap that split
    /// the body across two bursts and made the survivor boundary jitter by 6 bytes between runs.
    /// 80 ms swallows the delivery jitter without coming near the 300 ms this file leaves before the
    /// submitting CR, so the CR is still its own burst — a discrete Enter, as it must be.</para>
    /// </summary>
    private static async Task<PtyAgentRunner> LaunchClippingFakeAsync(
        params (string Key, string Value)[] extraEnv)
    {
        var env = new Dictionary<string, string>
        {
            ["ANTIPHON_FAKE_STDIN_CLIP"] = "1",
            ["ANTIPHON_FAKE_BURST_MS"] = "80",
        };
        foreach (var (k, v) in extraEnv) env[k] = v;

        // CARD-0045: inbox, declared. The clip model is a model of TYPED input, which is all the
        // inbox conhost can deliver — on the modern backend the markers survive, the paste is exempt
        // and every clip assertion below would (correctly) find nothing lost.
        var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
        ShouldBeInbox(runner);
        // The CLIP banner carries the seed and the chunk size: a test that believed it enabled
        // clipping but did not must fail here, not by mysteriously observing no loss.
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Claude ready") && s.Contains("CLIP:mode="), TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("the clipping fake must announce its model (CLIP:mode=…)");
        runner.ClearLiveBuffer();
        return runner;
    }

    /// <summary>Paste a body through the production encoding, submit with a separate CR, report the survivors.</summary>
    private static async Task<HashSet<int>> PasteAndSubmitAsync(PtyAgentRunner runner, string body)
    {
        await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
        await Task.Delay(300); // past the 80ms burst gap: the submitting CR must be its own turn
        await runner.WriteAsync("\r");

        var submitted = await runner.WaitForOutputAsync(
            s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(15));
        submitted.ShouldBeTrue(
            "a clipped body must still SUBMIT — clipping must never eat the submitting CR. Raw:\n"
            + runner.SnapshotText());
        return MarkersIn(runner.SnapshotText());
    }

    // The opt-in is load-bearing: with the variable unset the fake is the lossless peer that
    // PtyLargeWriteTests pins. If this ever goes red, clipping has leaked into the default and
    // every "our transport is lossless" test has quietly started testing something else.
    [Test]
    public async Task Clipping_is_off_by_default_and_a_two_chunk_body_arrives_whole()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200)); // 1 399 bytes = 2 chunks

        got.Count.ShouldBe(200, "with clipping OFF the fake must lose nothing");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // MEASURED: 810 and 972-byte bodies were whole 3/3 against real Claude. A body that fits in one
    // read chunk has no earlier chunk to lose — this is the boundary BriefInlineMaxBytes sits under.
    [Test]
    [Arguments(115)] // 805 bytes
    [Arguments(138)] // 966 bytes
    public async Task A_body_inside_one_read_chunk_survives_clipping_whole(int lines)
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync();

        var got = await PasteAndSubmitAsync(runner, MarkedBody(lines));

        got.Count.ShouldBe(lines,
            $"a {lines * 7}-byte body fits in one 1024-byte read chunk and must arrive whole even "
            + "with clipping on (measured whole 3/3 at 810 and 972 bytes)");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The live shape: 1 400 bytes = 2 chunks, the head goes, the survivor is a WHOLE chunk cut on a
    // 1024-byte boundary. Real Claude's R4 run reported survivors at bytes 1029-1400 with 7-byte
    // markers, i.e. marker 147 onward — exactly what this asserts.
    [Test]
    public async Task A_body_spanning_two_chunks_arrives_as_its_final_whole_chunk()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync();

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200));

        // Marker i occupies bytes [7i, 7i+7); the cut at 1024 makes 147 (byte 1029) the first whole one.
        got.ShouldNotContain(0, "the HEAD must be gone — that is the failure that stranded four briefs");
        got.Where(i => i < 147).ShouldBeEmpty("nothing before the 1024-byte boundary may survive");
        got.ShouldContain(147, "the survivor starts at the first marker past body byte 1024");
        got.ShouldContain(199, "and runs to the end of the body — survivors are whole chunks");
        got.Count.ShouldBe(53, "exactly markers 147-199 survive (one whole 1024-byte chunk)");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The other live signature: a 5 194-byte body kept its FIRST chunk only. Same mechanism, the
    // other survivor — which end survives is the TUI's render timing, not the body.
    [Test]
    public async Task Keep_first_models_the_first_chunk_only_shape()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync(("ANTIPHON_FAKE_STDIN_CLIP_KEEP", "first"));

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200));

        var why = $"survivors: {Runs(got)} — {ClipNotes(runner.SnapshotText())}";
        got.ShouldContain(0, "the head must survive in the first-chunk shape. " + why);
        got.ShouldContain(145, "…up to the marker that ends inside byte 1024. " + why);
        got.ShouldNotContain(199, "and the tail must be gone. " + why);
        got.Count.ShouldBe(146, "markers 0-145 fit inside the first 1024-byte chunk. " + why);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The read quantum is UTF-8 BYTES, and that rule cannot be tested here — it is pinned by
    // StdinClipModelTests instead. This is why, MEASURED rather than assumed:
    //
    // ConPTY narrows non-ASCII input to ONE BYTE per character before a .NET console peer reads it
    // (and strips the bracketed-paste markers on the way), even though the peer's console input
    // codepage reads back as 65001. So an em-dash-heavy body — 3 bytes per dash on the wire, the
    // exact shape that made a 900-CHARACTER brief 2 700 bytes and mangled it (21743a5) — reaches
    // this peer as one byte per dash, and down here bytes and chars are the same number.
    //
    // Pinned so nobody "improves" the unit test into a pty test and gets a green run for the wrong
    // reason. If this goes red, the transport changed and the parity canary should be re-measured.
    [Test]
    public async Task Non_ascii_input_reaches_a_dotnet_peer_narrowed_to_one_byte_per_char()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync(("ANTIPHON_FAKE_DEBUG_INPUT", "1"));

        // "Q00000—\n" = 8 chars, 10 bytes. 128 lines = 1 024 chars / 1 280 bytes (1 279 after the
        // trailing newline is trimmed), + 12 bytes of paste markers = 1 291 bytes written.
        var encoded = PtyInputEncoding.EncodeBody(
            string.Concat(Enumerable.Range(0, 128).Select(i => $"Q{i:D5}—\n")));
        System.Text.Encoding.UTF8.GetByteCount(encoded).ShouldBe(1291);

        await runner.WriteAsync(encoded);
        (await runner.WaitForOutputAsync(s => s.Contains("READS:"), TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("the fake must report the reads it got");

        var flat = runner.SnapshotText().Replace("\r", "").Replace("\n", "");
        var stamps = System.Text.RegularExpressions.Regex.Match(flat, @"stamps=\[([^\]]*)\]");
        stamps.Success.ShouldBeTrue("READS line: " + flat);
        var received = stamps.Groups[1].Value.Split(',')
            .Sum(s => int.Parse(s.Split(':')[1]));

        received.ShouldBe(1023,
            "1 279 body bytes, minus 12 for the paste markers conhost strips, minus 2 for each of "
            + "the 128 em-dashes it narrows to a single byte. A body that is 1 280 BYTES on the wire "
            + "arrives as 1 023 — under the read quantum, so it is not even clipped");
        ClipNotes(runner.SnapshotText()).ShouldBeEmpty(
            "and that is the trap: the fake CANNOT show byte-vs-char chunking, so StdinClipModelTests "
            + "pins that rule directly");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // Live, this loss is NON-deterministic — three identical 1 400-byte trials gave whole, clipped,
    // clipped. A flaky fake is useless to CI, so the default mode is the deterministic worst case.
    [Test]
    public async Task Deterministic_clipping_gives_identical_survivors_on_three_identical_trials()
    {
        SkipIfUnavailable();

        var results = new List<HashSet<int>>();
        var notes = new List<string>();
        for (var rep = 0; rep < 3; rep++)
        {
            await using var runner = await LaunchClippingFakeAsync();
            results.Add(await PasteAndSubmitAsync(runner, MarkedBody(200)));
            notes.Add($"rep{rep}: {Runs(results[^1])} — {ClipNotes(runner.SnapshotText())}");
            await runner.KillAsync(TimeSpan.FromSeconds(2));
        }

        var why = string.Join("\n", notes);
        results[1].SetEquals(results[0]).ShouldBeTrue("trial 1 must match trial 0.\n" + why);
        results[2].SetEquals(results[0]).ShouldBeTrue("trial 2 must match trial 0.\n" + why);
        results[0].Count.ShouldBe(53, "and all three must be the worst case: one surviving chunk.\n" + why);
    }

    // The dose-response that named the mechanism: more gap between writes ⇒ more chunks survive
    // (0ms kept 8 %, 25ms 26 %, 50-100ms 56 %). Spreading the same body over two turns saves both
    // chunks — the fake's bursts ARE its event-loop turns, so the gap is what changes the verdict.
    [Test]
    public async Task A_gap_between_writes_saves_the_chunk_that_would_otherwise_be_dropped()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync();

        var body = PtyInputEncoding.NormalizeBody(MarkedBody(200)); // ASCII: 1 char = 1 byte
        await runner.WriteAsync(body[..700]);
        await Task.Delay(300); // > the burst gap: the halves land in two different turns
        await runner.WriteAsync(body[700..]);
        await Task.Delay(300);
        await runner.WriteAsync("\r");

        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:"), TimeSpan.FromSeconds(15)))
            .ShouldBeTrue();
        var got = MarkersIn(runner.SnapshotText());

        got.Count.ShouldBe(200,
            "the same 1 400-byte body written as two sub-chunk halves in two turns must arrive "
            + "whole — that monotonic gap→survival response is what identified the mechanism");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // The non-determinism, offered separately from the CI worst case. Turn grouping is the live
    // variable: every chunk in its own turn is the "whole" outcome that one of the three identical
    // trials produced.
    [Test]
    public async Task Random_clipping_with_every_chunk_in_its_own_turn_loses_nothing()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync(
            ("ANTIPHON_FAKE_STDIN_CLIP", "random"),
            ("ANTIPHON_FAKE_STDIN_CLIP_SPLIT_PCT", "100"),
            ("ANTIPHON_FAKE_STDIN_CLIP_SEED", "7"));

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200));

        got.Count.ShouldBe(200, "chunks that land in separate turns all survive");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Random_clipping_with_one_turn_keeps_exactly_one_whole_chunk()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeAsync(
            ("ANTIPHON_FAKE_STDIN_CLIP", "random"),
            ("ANTIPHON_FAKE_STDIN_CLIP_SPLIT_PCT", "0"),
            ("ANTIPHON_FAKE_STDIN_CLIP_SEED", "7"));

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200));

        // Whichever chunk the seed picked, it is ONE contiguous whole chunk — never a mixture.
        var why = $"survivors: {Runs(got)} — {ClipNotes(runner.SnapshotText())}";
        var survived = got.OrderBy(i => i).ToList();
        survived.Zip(survived.Skip(1)).ShouldAllBe(p => p.Second == p.First + 1,
            "survivors are a single contiguous run, never scattered lines. " + why);
        // markers 0-145 (first chunk) or 147-199 (second)
        survived.Count.ShouldBeOneOf([146, 53], why);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // A non-deterministic failure is only useful if it can be replayed: the fake prints its seed and
    // the same seed must reproduce the same loss.
    [Test]
    public async Task Random_clipping_replays_identically_for_the_same_seed()
    {
        SkipIfUnavailable();

        async Task<(HashSet<int> Markers, string Note)> RunAsync()
        {
            await using var runner = await LaunchClippingFakeAsync(
                ("ANTIPHON_FAKE_STDIN_CLIP", "random"),
                ("ANTIPHON_FAKE_STDIN_CLIP_SPLIT_PCT", "50"),
                ("ANTIPHON_FAKE_STDIN_CLIP_SEED", "20260811"));
            var got = await PasteAndSubmitAsync(runner, MarkedBody(400)); // 2 799 bytes = 3 chunks
            var note = $"{Runs(got)} — {ClipNotes(runner.SnapshotText())}";
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            return (got, note);
        }

        var first = await RunAsync();
        var second = await RunAsync();

        second.Markers.SetEquals(first.Markers).ShouldBeTrue(
            "the same seed must reproduce the same survivors.\n"
            + $"run 1: {first.Note}\nrun 2: {second.Note}");
    }

    // ------------------------------------------------------------------------------------------
    // CARD-0037 — the OTHER input path. Everything above is TYPED input, which is all the inbox
    // conhost can deliver: it strips ESC[200~/ESC[201~ before the child sees them, so the fake
    // above never receives a marker and the clip model always applies.
    //
    // Through the SHIPPED modern pseudoconsole the markers arrive, and the measured behaviour is
    // the opposite: real Claude took 86 400 bytes in ONE bracketed write with zero loss (2/2) while
    // the identical body unwrapped on the same binary still lost 25%. A fake that kept clipping
    // pastes would assert a defect production no longer has — the CARD-0028 drift, mirrored.
    // ------------------------------------------------------------------------------------------

    /// <summary>The clipping fake behind the shipped modern pseudoconsole, so the markers survive.</summary>
    private static async Task<PtyAgentRunner> LaunchClippingFakeOnModernPtyAsync(
        params (string Key, string Value)[] extraEnv)
    {
        if (!ConPtyRedistributable.TryLocate(out _, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);

        var env = new Dictionary<string, string>
        {
            ["ANTIPHON_FAKE_STDIN_CLIP"] = "1",
            ["ANTIPHON_FAKE_BURST_MS"] = "80",
        };
        foreach (var (k, v) in extraEnv) env[k] = v;

        var runner = new PtyAgentRunner("modern");
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
        runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
            "this test is meaningless on the inbox conhost — it would strip the markers and the fake "
            + "would correctly clip");

        // Every armed model announces itself, and the LAST banner is what says the startup writes
        // are all in: waiting only for CLIP: and then clearing the buffer would race a later one
        // out of existence.
        var wantsPlaceholder = env.ContainsKey(PastePlaceholderVar);
        var ready = await runner.WaitForOutputAsync(
            s => s.Contains("Fake Claude ready")
                 && s.Contains("CLIP:mode=")
                 && (!wantsPlaceholder || s.Contains("PASTEPLACEHOLDER:")),
            TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue(
            "the fake must announce every model it was armed with (CLIP:mode=…"
            + (wantsPlaceholder ? ", PASTEPLACEHOLDER:…" : "") + "): " + runner.SnapshotText());
        runner.ClearLiveBuffer();
        return runner;
    }

    private const string PastePlaceholderVar = "ANTIPHON_FAKE_PASTE_PLACEHOLDER";

    /// <summary>
    /// The step-4 contract: the SAME body, the SAME armed clip model, the SAME single write — and
    /// because the markers reach the fake this time, nothing is dropped. Paired with
    /// <see cref="A_body_spanning_two_chunks_arrives_as_its_final_whole_chunk"/> above, which sends
    /// the identical body on the inbox backend and keeps 53 of its 200 markers.
    /// </summary>
    [Test]
    public async Task A_bracketed_paste_is_not_clipped_even_with_the_clip_model_armed()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeOnModernPtyAsync();

        var got = await PasteAndSubmitAsync(runner, MarkedBody(200)); // 1 399 bytes = 2 chunks typed

        got.Count.ShouldBe(200,
            "a bracketed paste takes the composer's paste path, which has no per-turn discard. "
            + $"Survivors: {Runs(got)} — {ClipNotes(runner.SnapshotText())}");
        ClipNotes(runner.SnapshotText()).ShouldBeEmpty(
            "and the model must not even have run: a CLIPPED note here means the exemption is "
            + "deciding per-burst instead of per-paste");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// A paste far past the point where typed input is reduced to one chunk. 43 200 bytes is the
    /// brief ceiling the modern backend carries (DelegationSettings.ModernPtyBriefInlineMaxBytes),
    /// measured whole 2/2 against real Claude — so the fake has to carry it too, or the ceiling
    /// tests above it are asserting against a peer that cannot do what production does.
    /// </summary>
    [Test]
    public async Task A_43KB_bracketed_paste_arrives_whole_with_clipping_armed()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeOnModernPtyAsync();

        const int lines = 6_171; // 6 171 x 7 = 43 197 bytes
        var got = await PasteAndSubmitAsync(runner, MarkedBody(lines));

        got.Count.ShouldBe(lines,
            $"43 KB in one bracketed write must arrive whole. Survivors: {Runs(got)} — "
            + ClipNotes(runner.SnapshotText()));

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The paste path costs the composer echo, and delivery verification reads that echo. With the
    /// placeholder model armed the fake renders what the real composer renders — the placeholder
    /// and NOTHING of the body — and <see cref="ComposerDeliveryEvidence"/> still has to certify
    /// the delivery. This is the CI proof that the verification fix works against a real screen and
    /// not only against hand-written strings.
    /// </summary>
    [Test]
    public async Task A_collapsed_paste_still_produces_composer_evidence()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchClippingFakeOnModernPtyAsync((PastePlaceholderVar, "1"));

        var before = runner.SnapshotScreen();
        var body = MarkedBody(200);
        await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
        (await runner.WaitForScreenAsync(s => s.Contains("Pasted text #"), TimeSpan.FromSeconds(15)))
            .ShouldBeTrue("the composer must collapse the paste: " + runner.SnapshotText());

        var after = runner.SnapshotScreen();
        MarkersIn(after).ShouldBeEmpty("the collapsed paste shows none of the body — that is the point");
        ComposerDeliveryEvidence.IsVisible(before, after, body).ShouldBeTrue(
            "a collapsed paste must still verify as delivered, or the queue withholds its Enter and "
            + "kills always-on sessions as wedged on every large delivery. Screen:\n" + after);

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The other half of the collapse, and CARD-0055's D1 premise in CI: the placeholder is a
    /// COMPOSER rendering, and the JSONL record still carries the whole body. Delivery confirmation
    /// matches the body's head window against that record text — a fake whose collapsed paste
    /// recorded only <c>[Pasted text #N +M lines]</c> would model a world in which every large
    /// delivery fails to confirm, retries three times and parks.
    ///
    /// <para>Measured against real Claude by
    /// <c>ClaudeSubmitConfirmCanaryTests.A_collapsed_pastes_jsonl_record_carries_the_full_body_not_the_placeholder</c>
    /// (2026-08-16: 4 804 chars pasted, composer showed the placeholder, the record carried all
    /// 4 804 with every marked line present).</para>
    /// </summary>
    [Test]
    public async Task A_collapsed_pastes_transcript_record_carries_the_full_body()
    {
        SkipIfUnavailable();
        var path = Path.Combine(Path.GetTempPath(), $"fakeclaude-collapsed-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var runner = await LaunchClippingFakeOnModernPtyAsync(
                (PastePlaceholderVar, "1"), ("ANTIPHON_FAKE_TRANSCRIPT_PATH", path));

            var body = MarkedBody(200);
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(body));
            (await runner.WaitForScreenAsync(s => s.Contains("Pasted text #"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue("the composer must collapse the paste: " + runner.SnapshotText());

            await Task.Delay(100);
            await runner.WriteAsync("\r");
            var lines = await WaitForTranscriptLinesAsync(path, 2);
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            lines.Length.ShouldBeGreaterThanOrEqualTo(1, "the collapsed paste must submit");
            var recorded = ContentOf(lines[0]);
            recorded.ShouldNotContain("Pasted text #",
                customMessage: "the placeholder is what the SCREEN shows; the record carries what the API receives");
            MarkersIn(recorded).Count.ShouldBe(200,
                $"all 200 marked lines must be in the record — got {MarkersIn(recorded).Count}");
            PromptSubmissionMatch.IsConfirmedBy(body, recorded).ShouldBeTrue(
                "and the production matcher must confirm the delivery from it");
        }
        finally
        {
            try { File.Delete(path); File.Delete(path + ".timing"); } catch { }
        }
    }

    // ---- CARD-0103: readiness must prove the TUI reads input -------------------------------
    //
    // The measured defect: Claude paints its banner and composer within seconds, then is NOT
    // input-responsive for tens of seconds on a loaded machine. An unresponsive TUI emits nothing,
    // and "nothing" is exactly what a quiet-period ready detector reads as ready — the same failure
    // genre as CARD-0047's silent modal and CARD-0048's silent DA1 stall. ANTIPHON_FAKE_DEAF_START_MS
    // models it: banner out, stdin unread for N ms, then everything buffered is processed IN ORDER.

    /// <summary>Ctrl+U is the probe's clear keystroke; the fake has to model both halves of it.</summary>
    [Test]
    public async Task Ctrl_u_empties_the_composer_and_the_rendered_screen()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.WriteAsync("zzprobetoken");
        (await runner.WaitForScreenAsync(
            screen => ComposerDeliveryEvidence.FragmentIsVisible(screen, "zzprobetoken"),
            TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("the composer echo must render the typed token. Screen:\n" + runner.SnapshotScreen());

        await runner.WriteAsync(ComposerInputProbe.KillLine);

        (await runner.WaitForScreenAsync(
            screen => !ComposerDeliveryEvidence.FragmentIsVisible(screen, "zzprobetoken"),
            TimeSpan.FromSeconds(10)))
            .ShouldBeTrue(
                "Ctrl+U must clear the RENDERED screen, not just an internal buffer — the probe's "
                + "clear check reads the screen, so a model that dropped only the buffer would report "
                + "a composer that will not empty. Screen:\n" + runner.SnapshotScreen());

        // And the killed line is really gone: a following Enter submits nothing.
        await runner.WriteAsync("\r");
        await Task.Delay(500);
        runner.SnapshotText().ShouldNotContain("SUBMITTED:zzprobetoken",
            customMessage: "Ctrl+U must remove the body, not merely hide it");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The whole point, in one test: a TUI that is deaf for LONGER than the probe budget must make
    /// the probe say NO. Today's readiness rule says yes to this session, every time, and the brief
    /// typed into it lands nowhere for minutes. A 60s deaf window against a 3s budget is
    /// deterministic — no launch takes a minute.
    /// </summary>
    [Test]
    public async Task A_tui_that_is_not_reading_fails_the_probe_instead_of_passing_quietly()
    {
        SkipIfUnavailable();
        // Launched directly rather than through LaunchReadyFakeAsync: that helper clears the live
        // buffer once the banner lands, and the OLD readiness rule below needs the banner still in
        // it (visible output, THEN quiet — CARD-0052). A deaf fake writes nothing else, ever, so a
        // cleared buffer would make the rule fail for the wrong reason.
        await using var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(
            FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30,
            env: new Dictionary<string, string> { ["ANTIPHON_FAKE_DEAF_START_MS"] = "60000" });
        ShouldBeInbox(runner);
        (await runner.WaitForOutputAsync(s => s.Contains("DEAFSTART:"), TimeSpan.FromSeconds(45)))
            .ShouldBeTrue("the deaf-start model must announce that it armed");

        // The OLD rule's verdict on this exact session, asserted first so the test says what it is
        // about: visible output, then quiet, is entirely satisfied by a TUI that reads nothing.
        (await runner.WaitForQuietAfterVisibleAsync(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(15)))
            .ShouldBeTrue("a deaf TUI is the quietest possible session — that is the lie being fixed");

        var result = await ComposerInputProbe.RunAsync(
            ComposerInputProbe.TokenFor(Guid.NewGuid()),
            _ => Task.FromResult(runner.SnapshotScreen()),
            (input, token) => runner.WriteAsync(input, token),
            ComposerProbeOptions.FromMilliseconds(
                timeoutMs: 3000, pollIntervalMs: 100, retypeIntervalMs: 30_000, clearTimeoutMs: 2000),
            null,
            CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.NeverAppeared,
            "ready must be FALSE on a TUI that is not reading. Screen:\n" + runner.SnapshotScreen());

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// And the other half: the probe must not give up on a session that is merely LATE. Input into a
    /// deaf TUI is buffered, not lost (measured: all three pastes landed on wake, in order), so a
    /// budget wider than the dead zone turns a parked brief into a slow launch.
    /// </summary>
    [Test]
    public async Task A_tui_that_wakes_inside_the_budget_passes_and_the_probe_waited_for_it()
    {
        SkipIfUnavailable();
        const int deafMs = 10_000;
        await using var runner = await LaunchReadyFakeAsync(
            env: new Dictionary<string, string> { ["ANTIPHON_FAKE_DEAF_START_MS"] = deafMs.ToString() },
            alsoAwaitBanner: "DEAFSTART:");

        // Honest rather than flaky: if the launch itself ate the deaf window there is nothing left to
        // measure, and asserting on it anyway would make this a load detector.
        var sinceStart = DateTime.UtcNow - runner.StartedAt;
        if (sinceStart > TimeSpan.FromMilliseconds(deafMs - 3000))
            throw new SkipTestException(
                $"launch took {sinceStart.TotalSeconds:F1}s of the {deafMs}ms deaf window — nothing left to wait for");

        var result = await ComposerInputProbe.RunAsync(
            ComposerInputProbe.TokenFor(Guid.NewGuid()),
            _ => Task.FromResult(runner.SnapshotScreen()),
            (input, token) => runner.WriteAsync(input, token),
            ComposerProbeOptions.FromMilliseconds(
                timeoutMs: 60_000, pollIntervalMs: 100, retypeIntervalMs: 30_000, clearTimeoutMs: 10_000),
            null,
            CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.Responsive,
            "the buffered token is processed on wake. Screen:\n" + runner.SnapshotScreen());
        result.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1),
            "a healthy fake answers in milliseconds; this one had to be waited for, which is the point");
        result.Writes.ShouldBe(1, "the retype belt must not fire inside a 10s stall — the buffer keeps it");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>The control: an ordinary launch answers the probe at once and costs ~nothing.</summary>
    [Test]
    public async Task A_reading_tui_answers_the_probe_immediately()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        var result = await ComposerInputProbe.RunAsync(
            ComposerInputProbe.TokenFor(Guid.NewGuid()),
            _ => Task.FromResult(runner.SnapshotScreen()),
            (input, token) => runner.WriteAsync(input, token),
            ComposerProbeOptions.FromMilliseconds(
                timeoutMs: 30_000, pollIntervalMs: 100, retypeIntervalMs: 30_000, clearTimeoutMs: 10_000),
            null,
            CancellationToken.None);

        result.Outcome.ShouldBe(ComposerProbeOutcome.Responsive,
            "Screen:\n" + runner.SnapshotScreen());
        result.Writes.ShouldBe(1);
        result.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10),
            "the probe is a per-launch cost, so a healthy one has to be cheap");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // Two queued messages, each submitted on its own turn — the queue flushes one message per turn-end,
    // so each must round-trip independently when delivered the correct (two-write) way.
    [Test]
    public async Task Two_separate_turns_each_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await EchoGatedSubmit.SendAsync(runner, "first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await EchoGatedSubmit.SendAsync(runner, "second");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:second"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    // ------------------------------------------------------------------------------------------
    // CARD-0101 / test-coverage plan P0-2 — why this fake had to learn a second argv parser.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The fake was, for months, the reason a shredding command line looked correct.
    ///
    /// <para><c>--echo-args</c> prints the vector <b>.NET</b> parsed, and .NET's parser accepts a
    /// doubled <c>""</c> inside a quoted argument as one escaped quote. <c>CommandLineToArgvW</c> —
    /// the parser <c>claude.exe</c>, node and bun actually use — SPLITS there. So the inbox backend
    /// was correct for .NET children and shredding for the child that matters, and
    /// <c>SessionMessageQueuePtyIntegrationTests.Launch_args_reach_the_child_process</c> passed on
    /// the failing shape for months because the only child it ever asked was this one.</para>
    ///
    /// <para>Both arms are the assertion, and neither works alone:</para>
    /// <list type="number">
    /// <item><b>Through the real pty, on the production composition</b> — the two lines AGREE. That
    /// is the fix working; on its own it proves nothing, because it also agrees when the strict
    /// line is a copy of the loose one.</item>
    /// <item><b>Direct spawn on the PRE-fix (Porta doubling) command line</b> — the two lines
    /// DIVERGE, and <c>--echo-args</c> alone still reports the intended argument intact. That
    /// divergence is the whole reason <c>--echo-argv-strict</c> exists, and it is why nobody may
    /// "simplify" this back to one line.</item>
    /// </list>
    ///
    /// <para>Arm 2 deliberately does NOT go through a pseudoconsole: since <c>aa1c8f1</c> +
    /// <c>7a92098</c> both backends escape correctly and <see cref="LaunchArgvGuard"/> refuses the
    /// launch outright if they ever stop — so the shredding line can no longer be built by the
    /// production path at all. That is the desired state; measuring the child's parser therefore
    /// needs a spawn that bypasses it. <c>ProcessStartInfo.Arguments</c> (the string overload, not
    /// <c>ArgumentList</c>) is passed to <c>CreateProcess</c> verbatim, which is exactly the raw
    /// line we need.</para>
    /// </summary>
    [Test]
    public async Task A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet()
    {
        SkipIfUnavailable();

        // One intended argument, carrying a literal quote: the delegate-basics shape.
        const string intended = "a \"quoted\" word";

        // ---- arm 1: the production composition, through a real ConPTY ----------------------------
        var runner = new PtyAgentRunner("inbox");
        await runner.StartAsync(
            FakeClaudeExe, ["--echo-args", "--echo-argv-strict", "--append-system-prompt", intended],
            cols: 220, rows: 30);
        ShouldBeInbox(runner);
        try
        {
            var sawBoth = await runner.WaitForOutputAsync(
                s => Unwrap(s).Contains("ARGS:") && Unwrap(s).Contains("ARGVSTRICT:"),
                TimeSpan.FromSeconds(45));
            sawBoth.ShouldBeTrue("the fake must print both argv lines: " + runner.SnapshotText());

            var text = Unwrap(runner.SnapshotText());
            text.ShouldNotContain("ARGVSTRICTWARN:",
                customMessage: "argv[0] must be this process's own exe, or the strict vector is offset "
                    + "by one and every assertion below it is meaningless");

            var loose = LineAfter(text, "ARGS:");
            var strict = LineAfter(text, "ARGVSTRICT:");
            strict.ShouldBe(loose,
                "on the CORRECTED escaping the two parsers must agree — this is the arm that goes red "
                + "if the production composition regresses. loose=[" + loose + "] strict=[" + strict + "]");
            strict.ShouldContain(
                "--append-system-prompt␟a \"quoted\" word",
                customMessage: "and the quoted value must arrive as ONE argument: " + text);
        }
        finally
        {
            await runner.KillAsync(TimeSpan.FromSeconds(2));
            await runner.DisposeAsync();
        }

        // ---- arm 2: the PRE-fix line, spawned raw ------------------------------------------------
        // Porta.Pty 1.0.7's WindowsArguments.Format, replicated by LaunchArgvGuard.FormatPortaStyle:
        // wrap every argument in quotes, double every inner quote. This is what shipped.
        var portaLine = LaunchArgvGuard.FormatPortaStyle(
            FakeClaudeExe, ["--echo-args", "--echo-argv-strict", "--append-system-prompt", intended]);
        var (looseRaw, strictRaw) = await SpawnAndReadArgvLinesAsync(portaLine);

        looseRaw.ShouldContain(
            "--append-system-prompt␟a \"quoted\" word",
            customMessage: "the .NET parser reports the intended argument INTACT on the shredding line — "
                + "which is precisely how this went unnoticed for months. Line was: " + portaLine);
        strictRaw.ShouldNotContain(
            "--append-system-prompt␟a \"quoted\" word",
            customMessage: "a native child's CRT must NOT see the intended argument on the pre-fix line. "
                + "If this ever passes, either the fake stopped using CommandLineToArgvW or Porta's "
                + "formatter changed — check LaunchArgvGuardTests' reflection pin before touching this.");
        strictRaw.Split('␟').Length.ShouldBeGreaterThan(
            looseRaw.Split('␟').Length,
            "the split is the defect: the native parser sees MORE arguments than were intended, and "
            + $"each extra one is a fragment of the system prompt. loose=[{looseRaw}] strict=[{strictRaw}]");
    }

    /// <summary>
    /// Spawns the fake on a RAW command line (no pty, no re-escaping) and returns its two argv
    /// lines. <c>ProcessStartInfo.Arguments</c> is handed to <c>CreateProcess</c> as-is, so the
    /// hostile line reaches the child byte for byte.
    /// </summary>
    private static async Task<(string Loose, string Strict)> SpawnAndReadArgvLinesAsync(string commandLine)
    {
        // The app is the first token of the composed line; everything after it is the argument tail.
        var quoted = commandLine.StartsWith('"');
        var appEnd = quoted ? commandLine.IndexOf('"', 1) + 1 : commandLine.IndexOf(' ');
        var arguments = appEnd < 0 || appEnd >= commandLine.Length ? string.Empty : commandLine[appEnd..].TrimStart();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = FakeClaudeExe,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            },
        };

        var loose = string.Empty;
        var strict = string.Empty;
        process.Start();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while ((loose.Length == 0 || strict.Length == 0) && !cts.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null) break;
                if (line.StartsWith("ARGS:", StringComparison.Ordinal)) loose = line["ARGS:".Length..];
                else if (line.StartsWith("ARGVSTRICT:", StringComparison.Ordinal)) strict = line["ARGVSTRICT:".Length..];
            }
        }
        catch (OperationCanceledException) { /* fall through to the assertions below */ }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        loose.ShouldNotBeEmpty("the fake must print its ARGS line even on a hostile command line");
        strict.ShouldNotBeEmpty("the fake must print its ARGVSTRICT line even on a hostile command line");
        return (loose, strict);
    }

    /// <summary>ConPTY soft-wraps at the terminal width; rejoin before matching a long banner line.</summary>
    private static string Unwrap(string raw) => raw.Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>
    /// The text between <paramref name="prefix"/> and the next banner marker on an unwrapped
    /// snapshot. Nothing here can rely on line boundaries — they were just removed — so the next
    /// known prefix is the terminator.
    /// </summary>
    private static string LineAfter(string unwrapped, string prefix)
    {
        var start = unwrapped.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += prefix.Length;
        var rest = unwrapped[start..];
        foreach (var terminator in new[] { "ARGVSTRICT:", "ARGS:", "]0;" })
        {
            var at = rest.IndexOf(terminator, StringComparison.Ordinal);
            if (at >= 0) rest = rest[..at];
        }
        // TrimEnd, because a terminal row is space-padded to the window width by definition and
        // whether that padding has arrived when the snapshot is taken is a race: this comparison
        // failed once on six trailing spaces the renderer had filled in on the ARGS: row and not
        // yet on the last one. Nothing this test asserts about ends in a space.
        return rest.TrimEnd();
    }
}
