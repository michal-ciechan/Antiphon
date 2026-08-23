using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0080 S1: headed canaries measuring the REAL Grok Build TUI (grok.exe) — the measurements
/// the first-class plan (docs/superpowers/plans/2026-08-18-grok-first-class-acp.md) demands before
/// S2 builds a transcript tailer on updates.jsonl. Everything RunnerGrokAdapter and fakegrok
/// currently assume about Grok's PTY behaviour ("same submit contract as FakeClaude", "Crunched
/// for Ns" turn-end markers, an idle OSC title) was inferred, never measured — the exact gap the
/// CARD-0030/0037 canaries closed for Claude.
///
/// <para><b>Ground truth is updates.jsonl</b>, the ACP update stream the TUI persists live to
/// <c>~/.grok/sessions/&lt;enc-cwd&gt;/&lt;session-id&gt;/updates.jsonl</c>: <c>user_message_chunk</c>
/// is what the model actually received, <c>turn_completed</c> (with <c>stop_reason</c> + usage) is
/// the explicit turn end S2 will tail. The screen is only ever the thing being measured against it.</para>
///
/// <para>Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>), <c>[Explicit]</c>: each test spends real
/// Grok turns. Launch shape is the production one (<c>--always-approve --no-alt-screen
/// --session-id</c>, modern conpty backend) so the measured TUI is the TUI Antiphon runs.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0080")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokCanaryTests
{
    /// <summary>RunnerGrokAdapter's current screen turn-end heuristic, measured here against reality.</summary>
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    /// <summary>
    /// What the real TUI prints ("Worked for 1.7s" — decimal seconds, measured 1.0.5): the
    /// production regex above only matches when the duration happens to be integer-formatted.
    /// </summary>
    private static readonly Regex DecimalDonePattern = new(@" for \d+(\.\d+)?s", RegexOptions.Compiled);
    private static readonly Regex OscTitle = new(@"\x1b\][02];([^\x07\x1b]*)(?:\x07|\x1b\\)", RegexOptions.Compiled);

    /// <summary>
    /// CARD-0157 ceiling-drift tripwire: <c>~/.grok/models_cache.json</c>
    /// <c>models.*.info.context_window</c> must stay 500 000, matching
    /// <c>SelfReportedCeilingTokens</c> on Grok's catalog entry.
    /// </summary>
    [Test]
    public void Models_cache_context_window_is_still_500000()
    {
        GkSession.SkipIfNotEligible();
        var path = Path.Combine(GkSession.DefaultGrokHome, "models_cache.json");
        File.Exists(path).ShouldBeTrue($"models_cache.json missing at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var models = doc.RootElement.GetProperty("models");
        var counted = 0;
        foreach (var model in models.EnumerateObject())
        {
            var window = model.Value.GetProperty("info").GetProperty("context_window").GetInt32();
            window.ShouldBe(500_000,
                $"{model.Name} context_window drifted from the CARD-0157 catalog constant SelfReportedCeilingTokens=500_000");
            counted++;
        }

        counted.ShouldBeGreaterThan(0, "models_cache.json declared no models");
    }

    // ------------------------------------------------------------------ 1. composer submit contract

    /// <summary>
    /// Item 1: the composer submit contract on the modern backend — empty Enter, text+CR in one
    /// write, a bracketed multi-line paste, and a large TYPED (unwrapped) body. fakegrok
    /// originally modelled "same as FakeClaude" (text+CR is a paste, LF is literal); the
    /// measurements here disproved both, and fakegrok now models what this canary pins. Each
    /// phase's verdict is read from user_message_chunk rows — what the model received, not what
    /// the screen suggested.
    /// </summary>
    [Test]
    public async Task Composer_submit_contract_on_the_modern_backend()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Composer_submit_contract_on_the_modern_backend));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
                "the deployment runs modern; measuring the inbox conhost would measure the wrong pty. "
                + "Reason: " + runner.Backend!.Reason);
            await GkSession.WaitForReadyAsync(runner);
            log("READY SCREEN:\n" + GkSession.Tail(runner.SnapshotScreen(), 1200));
            log($"updates.jsonl exists pre-first-turn: {File.Exists(updates)} "
                + $"rows={GkSession.ReadUpdates(updates).Count}");

            // Phase A — Enter on an empty composer must be a no-op (the CARD-0055 retry shape).
            await runner.WriteAsync("\r");
            await Task.Delay(1500);
            await runner.WriteAsync("\r");
            await Task.Delay(3000);
            var afterEmptyEnters = GkSession.ReadUpdates(updates).Count(r => r.Kind == "user_message_chunk");
            log($"PHASE A user chunks after 2 empty Enters: {afterEmptyEnters}");
            afterEmptyEnters.ShouldBe(0, "an Enter on an empty composer must submit nothing");

            // Phase B — text + CR in ONE write SUBMITS directly (measured 1.0.5). Grok has no
            // Claude-style paste-window heuristic on unbracketed input: every CR is Enter. This is
            // the opposite of FakeClaude's contract, which fakegrok wrongly copied before this
            // canary — the drift it exists to catch.
            await runner.WriteAsync("GK-ONESHOT reply with exactly OK and nothing else\r");
            var oneshotSubmitted = await WaitForUpdateAsync(updates,
                r => r.Kind == "user_message_chunk" && r.Text?.Contains("GK-ONESHOT") == true,
                TimeSpan.FromSeconds(15));
            log($"PHASE B text+CR-in-one-write submitted directly: {oneshotSubmitted is not null}");
            oneshotSubmitted.ShouldNotBeNull(
                "text+CR in one write must submit — measured contract of grok 1.0.5. Screen:\n"
                + runner.SnapshotScreen());
            (await WaitForUpdateAsync(updates, r => r.Kind == "turn_completed", TimeSpan.FromMinutes(3)))
                .ShouldNotBeNull("turn 1 must complete. Screen:\n" + runner.SnapshotScreen());

            // Phase C — bracketed multi-line paste (the production queue encoding), Enter separate.
            // First run measured that Grok's composer INGESTS a paste slowly (~3 KB of a 4.3 KB
            // body rendered after 20 s), the queued Enter firing only once ingestion completes —
            // so this phase measures the ingestion rate as well as intactness.
            var pasteBody = BuildMarkedBody("GK-PASTE", 60);
            var pasteClock = Stopwatch.StartNew();
            await runner.WriteAsync(PtyInputEncoding.EncodeBody(pasteBody));
            await Task.Delay(500);
            log("PHASE C composer 500ms after paste:\n" + GkSession.Tail(runner.SnapshotScreen(), 500));
            await runner.WriteAsync("\r");
            var polls = 0;
            var pasteChunk = await WaitForUpdateAsync(updates,
                r => r.Kind == "user_message_chunk" && r.Text?.Contains("GK-PASTE-HEAD") == true,
                TimeSpan.FromMinutes(5),
                onPoll: () =>
                {
                    if (++polls % 100 == 0)
                        log($"  ingest {pasteClock.Elapsed.TotalSeconds:F0}s: composer tail: "
                            + GkSession.Truncate(LastMarkerLine(runner.SnapshotScreen(), "GK-PASTE"), 90));
                });
            log($"PHASE C user chunk after {pasteClock.Elapsed.TotalSeconds:F1}s "
                + $"({pasteBody.Length / Math.Max(1.0, pasteClock.Elapsed.TotalSeconds):F0} chars/s ingest)");
            if (pasteChunk is null)
            {
                log("PHASE C: no matching user chunk. Screen:\n" + GkSession.Tail(runner.SnapshotScreen(), 1200));
                DumpRows(log, updates);
            }
            pasteChunk.ShouldNotBeNull("the pasted body must submit. Screen:\n" + runner.SnapshotScreen());
            log($"PHASE C sent {pasteBody.Length} chars, recorded {pasteChunk!.Text!.Length} chars; "
                + $"newlines survived: {pasteChunk.Text.Contains('\n')}");
            pasteChunk.Text!.ShouldContain("GK-PASTE-HEAD");
            pasteChunk.Text!.ShouldContain("GK-PASTE-TAIL", customMessage:
                $"a bracketed paste must land INTACT — sent {pasteBody.Length} chars, "
                + $"recorded {pasteChunk.Text.Length}: a clipped paste breaks every S2 delivery confirm");
            // Measured 1.0.5: the composer DROPS every LF — lines join with NO separator (4450
            // sent, 4389 recorded = exactly the 61 newlines). S2's delivery-confirm normalization
            // must not assume line structure survives; if a Grok update starts preserving
            // newlines, this pin goes red and the normalization can be revisited.
            pasteChunk.Text.Contains('\n').ShouldBeFalse(
                "grok 1.0.5 drops pasted newlines wholesale; a change here changes what "
                + "PromptSubmissionMatch-style confirmation must normalize");
            (await WaitForUpdateAsync(updates,
                r => r.Kind == "turn_completed" && CountKind(updates, "turn_completed") >= 2,
                TimeSpan.FromMinutes(3))).ShouldNotBeNull("turn 2 must complete");

            // Phase D — the same body TYPED: unwrapped, LF endings, one write. On the inbox conhost
            // Claude's composer clips typed input to ~1 read chunk (CARD-0027/28); measure whether
            // Grok's composer clips, and how fast it ingests raw typing.
            var typedBody = BuildMarkedBody("GK-TYPED", 60);
            var typedClock = Stopwatch.StartNew();
            await runner.WriteAsync(typedBody);
            await Task.Delay(500);
            await runner.WriteAsync("\r");
            var typedChunk = await WaitForUpdateAsync(updates,
                r => r.Kind == "user_message_chunk" && r.Text?.Contains("GK-TYPED") == true,
                TimeSpan.FromMinutes(5));
            log(typedChunk is null
                ? "PHASE D typed body: NO user chunk within 5min. Screen:\n" + GkSession.Tail(runner.SnapshotScreen(), 1000)
                : $"PHASE D typed body after {typedClock.Elapsed.TotalSeconds:F1}s: sent {typedBody.Length} chars, "
                    + $"recorded {typedChunk.Text!.Length}; head={typedChunk.Text.Contains("GK-TYPED-HEAD")} "
                    + $"tail={typedChunk.Text.Contains("GK-TYPED-TAIL")} newlines={typedChunk.Text.Contains('\n')}");
            typedChunk.ShouldNotBeNull("the typed body must submit");
            // Measured 1.0.5: NO stdin clip at 4.4 KB typed — Grok's composer does not have
            // Claude's one-chunk-per-turn loss mode (CARD-0027) on the modern backend.
            typedChunk!.Text!.ShouldContain("GK-TYPED-HEAD");
            typedChunk.Text!.ShouldContain("GK-TYPED-TAIL", customMessage:
                "a 4.4 KB TYPED body must land whole — grok 1.0.5 measured no composer clip; if this "
                + "goes red the CARD-0027/28 clip model applies to Grok after all and the ceilings "
                + "need re-measuring");

            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    // ------------------------------------------------ 2 + 3. turn-end signals and flush latency

    /// <summary>
    /// Items 2 and 3, measured on the same turn (one signal set, two questions). Item 2:
    /// RunnerGrokAdapter detects turn-complete via quiet time + a <c>" for \d+s"</c> regex + an
    /// idle OSC title — do those screen signals exist at all for Grok, and how do they relate in
    /// time to the <c>turn_completed</c> row? Item 3: is updates.jsonl flushed per-update or
    /// buffered? Claude's JSONL once lagged 45+ s and a healthy session was killed on that false
    /// evidence (CARD-0055's grace-pull history) — S2 must know Grok's flush shape before it is
    /// built. Every signal is timestamped from the same Stopwatch, poll interval 25 ms.
    /// </summary>
    [Test]
    public async Task Turn_end_signals_vs_turn_completed_and_updates_flush_latency()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Turn_end_signals_vs_turn_completed_and_updates_flush_latency));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);
            runner.ClearLiveBuffer();

            var clock = Stopwatch.StartNew();
            await runner.WriteAsync("Reply with exactly the single word PONG and nothing else.");
            await Task.Delay(50);
            var tEnter = clock.Elapsed;
            await runner.WriteAsync("\r");

            TimeSpan? tFileExists = null, tUserChunk = null, tFirstAgentRow = null, tTurnCompleted = null;
            TimeSpan? tScreenPong = null, tDonePattern = null, tDecimalDone = null, tIdleTitle = null;
            var titles = new List<(TimeSpan At, string Title)>();
            var seenTitles = new HashSet<string>();
            GrokUpdateRow? turnCompleted = null;

            var deadline = TimeSpan.FromMinutes(3);
            while (clock.Elapsed < deadline)
            {
                var raw = runner.SnapshotText();
                if (tScreenPong is null && raw.Contains("PONG")) tScreenPong = clock.Elapsed;
                if (tDonePattern is null && DonePattern.IsMatch(raw)) tDonePattern = clock.Elapsed;
                if (tDecimalDone is null && DecimalDonePattern.IsMatch(raw)) tDecimalDone = clock.Elapsed;
                foreach (Match m in OscTitle.Matches(raw))
                {
                    if (seenTitles.Add(m.Value))
                    {
                        titles.Add((clock.Elapsed, m.Groups[1].Value));
                        tIdleTitle ??= clock.Elapsed;
                    }
                }

                if (tFileExists is null && File.Exists(updates)) tFileExists = clock.Elapsed;
                if (tFileExists is not null)
                {
                    var rows = GkSession.ReadUpdates(updates);
                    if (tUserChunk is null && rows.Any(r => r.Kind == "user_message_chunk" && r.Text?.Contains("PONG") == true))
                        tUserChunk = clock.Elapsed;
                    if (tFirstAgentRow is null && rows.Any(r =>
                            r.Kind is "agent_message_chunk" or "agent_thought_chunk" or "tool_call"))
                        tFirstAgentRow = clock.Elapsed;
                    var done = rows.FirstOrDefault(r => r.Kind == "turn_completed");
                    if (done is not null && tTurnCompleted is null)
                    {
                        tTurnCompleted = clock.Elapsed;
                        turnCompleted = done;
                    }
                }

                // Run on a few seconds past turn_completed so late screen markers are still seen.
                if (tTurnCompleted is not null && clock.Elapsed - tTurnCompleted.Value > TimeSpan.FromSeconds(10))
                    break;
                await Task.Delay(25);
            }

            string Fmt(TimeSpan? t) => t is null ? "never" : $"{(t.Value - tEnter).TotalMilliseconds:F0}ms";
            log($"MEASUREMENTS (relative to Enter at t0):");
            log($"  updates.jsonl exists:            {Fmt(tFileExists)}");
            log($"  user_message_chunk in file:      {Fmt(tUserChunk)}");
            log($"  first agent row in file:         {Fmt(tFirstAgentRow)}");
            log($"  PONG visible on screen:          {Fmt(tScreenPong)}");
            log($"  turn_completed row in file:      {Fmt(tTurnCompleted)}");
            log($"  screen ' for Ns' done marker:    {Fmt(tDonePattern)}  (production regex)");
            log($"  screen ' for N.Ns' done marker:  {Fmt(tDecimalDone)}  (decimal-tolerant)");
            log($"  first OSC title:                 {Fmt(tIdleTitle)}");
            foreach (var (at, title) in titles)
                log($"    title @{(at - tEnter).TotalMilliseconds:F0}ms: '{title}'");
            log("turn_completed row: " + GkSession.Truncate(turnCompleted?.Raw, 600));
            log("FINAL SCREEN:\n" + GkSession.Tail(runner.SnapshotScreen(), 1200));

            await runner.KillAsync(TimeSpan.FromSeconds(3));

            tTurnCompleted.ShouldNotBeNull("a turn_completed row must land in updates.jsonl — it is "
                + "the structured turn end the whole S2 design tails");
            turnCompleted!.StopReason.ShouldBe("end_turn");
            tUserChunk.ShouldNotBeNull("the user prompt must appear in updates.jsonl");
            (tUserChunk!.Value - tEnter).ShouldBeLessThan(TimeSpan.FromSeconds(30),
                "S2's transcript-confirmed delivery polls this file the way CARD-0055 polls Claude's "
                + "JSONL; a flush lag past 30s means the tailer needs a grace-pull design from day one");
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    // ------------------------------------------------------------------ 4. Esc / interrupt shape

    /// <summary>
    /// Item 4: what lands in updates.jsonl when a turn is interrupted mid-flight — a distinct
    /// stop_reason on a turn_completed row, some other row, or nothing (the unmarked-turn shape
    /// that strands working/idle in every adapter that has ever had it)?
    /// </summary>
    [Test]
    public async Task Esc_interrupt_shape_in_updates_jsonl()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Esc_interrupt_shape_in_updates_jsonl));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            await runner.WriteAsync(
                "Write out the numbers from one to three hundred as English words, one per line. Use no tools.");
            await Task.Delay(50);
            await runner.WriteAsync("\r");

            // Interrupt only once the turn is demonstrably mid-flight.
            (await WaitForUpdateAsync(updates,
                r => r.Kind is "agent_message_chunk" or "agent_thought_chunk",
                TimeSpan.FromMinutes(2))).ShouldNotBeNull(
                "the turn must start streaming before it can be interrupted. Screen:\n" + runner.SnapshotScreen());

            await runner.WriteAsync("\x1b");
            log("ESC sent; waiting 20s for whatever lands…");
            await Task.Delay(TimeSpan.FromSeconds(20));

            var rows = GkSession.ReadUpdates(updates);
            log("ROW KINDS: " + string.Join(", ",
                rows.GroupBy(r => $"{r.Method}:{r.Kind}").Select(g => $"{g.Key}×{g.Count()}")));
            var completions = rows.Where(r => r.Kind == "turn_completed").ToList();
            foreach (var c in completions)
                log($"turn_completed stop_reason='{c.StopReason}': " + GkSession.Truncate(c.Raw, 400));
            log("LAST 5 ROWS:");
            foreach (var r in rows.TakeLast(5))
                log("  " + GkSession.Truncate(r.Raw, 300));
            log("SCREEN AFTER ESC:\n" + GkSession.Tail(runner.SnapshotScreen(), 1200));

            // Is the composer usable afterwards?
            runner.ClearLiveBuffer();
            await runner.WriteAsync("GK-AFTER-ESC");
            var echoed = await runner.WaitForScreenAsync(
                s => s.Contains("GK-AFTER-ESC"), TimeSpan.FromSeconds(5));
            log($"composer echoes typed text after ESC: {echoed}");

            await runner.KillAsync(TimeSpan.FromSeconds(3));

            completions.ShouldNotBeEmpty(
                "an interrupted turn MUST leave an explicit marker in updates.jsonl — if nothing "
                + "lands, Grok has the exact unmarked-interrupt shape that strands working/idle, and "
                + "S2 needs to know that before it builds on turn_completed");
            // Measured 1.0.5: {"sessionUpdate":"turn_completed","stop_reason":"cancelled"} (no
            // usage block) with _meta.cancelTrigger:"esc", cancellationCategory:"MidTurnAbort".
            // An interrupted turn is a marked turn END — no Claude-style interrupt-prompt
            // predicate is needed for Grok.
            completions.ShouldContain(c => c.StopReason == "cancelled",
                "the interrupt must be an explicit turn_completed with stop_reason=cancelled");
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    // ------------------------------------------------------------------ 5. session_recap shape

    /// <summary>
    /// Item 5: session_recap — the plan's working theory is "Grok's auto-compaction analog". A real
    /// auto row is already on disk from the session that built 5754e02
    /// (<c>{"sessionUpdate":"session_recap","summary":"…","auto":true}</c>, landing ~3 min AFTER the
    /// final turn_completed). This canary measures whether a recap can be induced on demand from
    /// the TUI's own command surface, and pins the row shape when one is obtained.
    /// </summary>
    [Test]
    public async Task Session_recap_shape_and_trigger()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Session_recap_shape_and_trigger));
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        var updates = GkSession.UpdatesPath(GkSession.DefaultGrokHome, cwd, sessionId);

        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);

            // The slash menu holds 100+ commands and renders only a filtered window, so probe by
            // typing candidate names — the menu narrows as you type, and a candidate that exists
            // shows up as a menu row above the composer line.
            string? command = null;
            foreach (var candidate in new[] { "/compact", "/recap", "/summarize" })
            {
                await runner.WriteAsync(candidate);
                await Task.Delay(800);
                var screen = runner.SnapshotScreen();
                var menuRows = screen.Split('\n')
                    .Where(l => l.Contains(candidate[1..], StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("│ >", StringComparison.Ordinal))
                    .ToList();
                log($"probe {candidate}: {menuRows.Count} menu rows"
                    + (menuRows.Count > 0 ? ":\n  " + string.Join("\n  ", menuRows.Select(r => r.Trim())) : ""));
                if (menuRows.Count > 0) command ??= candidate;
                // Esc does NOT clear Grok's composer (measured — probes concatenated); backspace out.
                foreach (var _ in candidate)
                    await runner.WriteAsync("\x7f");
                await Task.Delay(300);
            }

            if (command is not null)
            {
                // A recap of an empty session may refuse; give it one cheap turn of substance first.
                await runner.WriteAsync("Reply with exactly OK and nothing else.");
                await Task.Delay(50);
                await runner.WriteAsync("\r");
                (await WaitForUpdateAsync(updates, r => r.Kind == "turn_completed", TimeSpan.FromMinutes(3)))
                    .ShouldNotBeNull("the setup turn must complete");

                var rowsBefore = GkSession.ReadUpdates(updates).Count;
                await runner.WriteAsync(command);
                await Task.Delay(300);
                await runner.WriteAsync("\r");
                var finished = await runner.WaitForScreenAsync(
                    s => s.Contains("Compaction completed", StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromMinutes(2));
                log($"screen 'Compaction completed': {finished}");
                var recap = await WaitForUpdateAsync(updates,
                    r => r.Kind == "session_recap", TimeSpan.FromSeconds(45));
                log(recap is null
                    ? $"{command} produced NO session_recap row within 45s of completion"
                    : "session_recap row: " + recap.Raw);
                log($"rows before compact: {rowsBefore}");
                DumpRows(log, updates);
                var summaryPath = Path.Combine(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId), "summary.json");
                if (File.Exists(summaryPath)) log("summary.json: " + File.ReadAllText(summaryPath));

                // /recap ("Summarize the session so far") is the command actually named like the
                // row — measure what IT emits.
                var rowsBeforeRecap = GkSession.ReadUpdates(updates).Count;
                await runner.WriteAsync("/recap");
                await Task.Delay(300);
                await runner.WriteAsync("\r");
                var recapRow = await WaitForUpdateAsync(updates,
                    r => r.Kind == "session_recap", TimeSpan.FromMinutes(2));
                log(recapRow is null
                    ? "/recap produced NO session_recap row within 2min. Screen:\n"
                        + GkSession.Tail(runner.SnapshotScreen(), 1200)
                    : "/recap session_recap row: " + recapRow.Raw);
                var newRows = GkSession.ReadUpdates(updates).Skip(rowsBeforeRecap).ToList();
                log($"rows emitted by /recap ({newRows.Count}):");
                foreach (var r in newRows) log("  " + GkSession.Truncate(r.Raw, 400));

                // Measured 1.0.5: compaction and recap are DIFFERENT rows — the plan's
                // "session_recap = auto-compaction analog" theory was wrong. /compact emits
                // compaction_checkpoint + auto_compact_completed (and no turn_completed);
                // /recap emits exactly one session_recap {summary, auto:false}; the auto:true
                // variant is the background session-summary (seen ~3.5 min after a turn end
                // in the 5754e02 session).
                GkSession.ReadUpdates(updates).ShouldContain(r => r.Kind == "auto_compact_completed",
                    "/compact must land its explicit completion row");
                var compactRow = GkSession.ReadUpdates(updates)
                    .Last(r => r.Kind == "auto_compact_completed");
                using (var compactDoc = JsonDocument.Parse(compactRow.Raw))
                {
                    var update = compactDoc.RootElement.GetProperty("params").GetProperty("update");
                    update.GetProperty("tokens_before").ValueKind.ShouldBe(JsonValueKind.Number,
                        "auto_compact_completed.tokens_before is the occupancy-before wire tripwire");
                    update.GetProperty("tokens_after").ValueKind.ShouldBe(JsonValueKind.Number,
                        "auto_compact_completed.tokens_after is the occupancy-after wire tripwire");
                }
                recapRow.ShouldNotBeNull("/recap must land a session_recap row");
                recapRow!.Raw.ShouldContain("\"summary\"");
                recapRow.Raw.ShouldContain("\"auto\":false");
            }
            else
            {
                log("No recap-shaped command on the menu — recap appears to be "
                    + "auto-only. The historical auto row shape stands as the measurement.");
            }

            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }
    }

    // ------------------------------------------- 6. fresh cwd / fresh GROK_HOME launch blocking

    /// <summary>
    /// Item 6: does a first-ever launch block on a modal? Claude's per-cwd trust dialog silently
    /// wedged every fresh-directory launch (CARD-0047); the plan's theory is Grok has no per-cwd
    /// state at all (auth is global in ~/.grok). Arm A: brand-new cwd, real auth — must reach a
    /// usable composer. Arm B: fresh GROK_HOME (no auth) — record exactly what blocks, since that
    /// screen is what a misconfigured deployment would sit on forever.
    /// </summary>
    [Test]
    public async Task Fresh_cwd_and_fresh_grok_home_launch_blocking()
    {
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Fresh_cwd_and_fresh_grok_home_launch_blocking));

        // Arm A — fresh cwd, real GROK_HOME.
        var sessionId = Guid.NewGuid().ToString("D");
        var cwd = GkSession.TempCwd();
        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(GkSession.GrokExePath, GkSession.LaunchArgs(sessionId), cwd: cwd, cols: 120, rows: 30);
            await GkSession.WaitForReadyAsync(runner);
            log("ARM A (fresh cwd) SCREEN:\n" + GkSession.Tail(runner.SnapshotScreen(), 1500));

            await runner.WriteAsync("GK-FRESH-CWD-PROBE");
            var echoed = await runner.WaitForScreenAsync(
                s => s.Contains("GK-FRESH-CWD-PROBE"), TimeSpan.FromSeconds(5));
            echoed.ShouldBeTrue(
                "a brand-new cwd must reach a usable composer — if typed text is not echoed, "
                + "something modal is swallowing input, which is the CARD-0047 wedge shape. Screen:\n"
                + runner.SnapshotScreen());
            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(GkSession.SessionDirectory(GkSession.DefaultGrokHome, cwd, sessionId));
            GkSession.BestEffortDelete(cwd);
        }

        // Arm B — fresh GROK_HOME: no auth.json, nothing. Record what a never-authenticated
        // deployment sits on. This must NOT reach a normal composer silently.
        var freshHome = Directory.CreateTempSubdirectory("antiphon-grok-home").FullName;
        var cwdB = GkSession.TempCwd();
        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath,
                GkSession.LaunchArgs(Guid.NewGuid().ToString("D")),
                cwd: cwdB,
                env: new Dictionary<string, string> { ["GROK_HOME"] = freshHome },
                cols: 120, rows: 30);
            // Give it ample time to render whatever it renders (or exit).
            await runner.WaitForQuietAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(45));
            var exited = runner.Exited.IsCompleted;
            log($"ARM B (fresh GROK_HOME) exited={exited}"
                + (exited ? $" code={runner.Exited.Result}" : ""));
            log("ARM B SCREEN:\n" + runner.SnapshotScreen());

            if (!exited)
            {
                // Does it swallow typed input (modal) or echo it (composer)?
                await runner.WriteAsync("GK-NOAUTH-PROBE");
                var echoedB = await runner.WaitForScreenAsync(
                    s => s.Contains("GK-NOAUTH-PROBE"), TimeSpan.FromSeconds(5));
                log($"ARM B composer echo with no auth: {echoedB}");
                // Measured 1.0.5: a never-authenticated GROK_HOME parks on a device-code login
                // screen ("Approve in your browser to finish signing in." + code + "Waiting for
                // approval..."), swallowing all input and never exiting — the one blocking-modal
                // shape Grok has. It is global (per-home), not per-cwd, so unlike CARD-0047 it
                // can only bite a deployment whose auth was never set up, not every fresh
                // worktree. Fail-fast + incident is the right response, not an auto-answer.
                echoedB.ShouldBeFalse(
                    "the no-auth login screen must read as a blocking modal (input swallowed) — "
                    + "if it starts echoing, the wedge-detection story for Grok changes");
                await runner.KillAsync(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception ex)
        {
            log("TEST EXCEPTION: " + ex.Message);
            throw;
        }
        finally
        {
            GkSession.BestEffortDelete(freshHome);
            GkSession.BestEffortDelete(cwdB);
        }
    }

    // ------------------------------------------------------------------------------- helpers

    private static string BuildMarkedBody(string marker, int lines)
    {
        var sb = new StringBuilder();
        sb.Append($"{marker}-HEAD reply with exactly OK and nothing else; ignore everything below.\n");
        for (var i = 0; i < lines; i++)
            sb.Append($"{marker}-L{i:D3} ignorable filler {new string('x', 40)}\n");
        sb.Append($"{marker}-TAIL end of filler. Reply with exactly OK.");
        return sb.ToString();
    }

    private static void DumpRows(Action<string> log, string updatesPath)
    {
        var rows = GkSession.ReadUpdates(updatesPath);
        log($"UPDATES DUMP ({rows.Count} rows):");
        foreach (var r in rows)
            log("  " + GkSession.Truncate(r.Raw, 400));
    }

    private static int CountKind(string updatesPath, string kind) =>
        GkSession.ReadUpdates(updatesPath).Count(r => r.Kind == kind);

    private static async Task<GrokUpdateRow?> WaitForUpdateAsync(
        string updatesPath, Func<GrokUpdateRow, bool> predicate, TimeSpan timeout, Action? onPoll = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = GkSession.ReadUpdates(updatesPath).FirstOrDefault(predicate);
            if (match is not null) return match;
            onPoll?.Invoke();
            await Task.Delay(50);
        }
        return null;
    }

    /// <summary>The last composer line containing a body marker — the ingestion progress gauge.</summary>
    private static string LastMarkerLine(string screen, string marker) =>
        screen.Split('\n').LastOrDefault(l => l.Contains(marker))?.Trim() ?? "<none visible>";
}
