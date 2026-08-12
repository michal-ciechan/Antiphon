using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
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

    private static async Task<PtyAgentRunner> LaunchReadyFakeAsync(
        IDictionary<string, string>? env = null)
    {
        var runner = new PtyAgentRunner();
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
        var ready = await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("fake Claude should print its readiness banner");
        runner.ClearLiveBuffer();
        return runner;
    }

    // The BUG path: text and the submitting CR in a SINGLE write. The TUI reads it as a paste — the CR
    // collapses to a literal newline and the line is NOT submitted. This is exactly what the old
    // DeliverAsync did (`body.TrimEnd() + "\r"` in one SendInputAsync), so the message was stranded.
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

        await runner.WriteAsync("queued message");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

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
        await runner.WriteAsync(body);
        await Task.Delay(25);
        await runner.WriteAsync("\r");

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
        await runner.WriteAsync("\x1b[200~" + body + "\x1b[201~");
        await Task.Delay(25);
        await runner.WriteAsync("\r");

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
        await runner.WriteAsync(body);
        await Task.Delay(40);
        await runner.WriteAsync("\r");

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

        await runner.SendLineAsync("/compact");

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

        await runner.SendLineAsync("/compact");

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

        await runner.SendLineAsync("first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();
        runner.SnapshotText().ShouldNotContain("Compacted (",
            customMessage: "auto-compaction must not fire before the configured turn count");

        await runner.SendLineAsync("second");
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

            await runner.SendLineAsync("hello transcript");
            (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:hello transcript"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            await runner.SendLineAsync("/compact");
            (await runner.WaitForOutputAsync(s => s.Contains("Compacted ("), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();

            await runner.KillAsync(TimeSpan.FromSeconds(2));

            var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.ShouldBe(3);
            lines[0].ShouldContain("\"type\":\"user\"");
            lines[0].ShouldContain("hello transcript");
            lines[1].ShouldContain("\"type\":\"assistant\"");
            lines[1].ShouldContain("\"stop_reason\":\"end_turn\"");
            lines[2].ShouldContain("\"subtype\":\"compact_boundary\"");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
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

            await runner.SendLineAsync("/model opus");
            (await runner.WaitForOutputAsync(s => s.Contains("FAKE local command: /model"), TimeSpan.FromSeconds(5)))
                .ShouldBeTrue();
            await runner.KillAsync(TimeSpan.FromSeconds(2));

            var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.ShouldBe(2);

            string ContentOf(string line)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                doc.RootElement.GetProperty("type").GetString().ShouldBe("user");
                return doc.RootElement.GetProperty("message").GetProperty("content").GetString()!;
            }

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
            try { File.Delete(path); } catch { }
        }
    }

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

        var runner = new PtyAgentRunner();
        await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30, env: env);
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

    // Two queued messages, each submitted on its own turn — the queue flushes one message per turn-end,
    // so each must round-trip independently when delivered the correct (two-write) way.
    [Test]
    public async Task Two_separate_turns_each_submit()
    {
        SkipIfUnavailable();
        await using var runner = await LaunchReadyFakeAsync();

        await runner.SendLineAsync("first");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:first"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await runner.SendLineAsync("second");
        (await runner.WaitForOutputAsync(s => s.Contains("SUBMITTED:second"), TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
