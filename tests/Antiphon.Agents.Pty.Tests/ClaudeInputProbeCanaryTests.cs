using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0103's input-responsiveness probe, measured against REAL Claude. The whole fix rests on two
/// claims about the real TUI, and neither can be established by the fake — the fake is a model, and
/// a model that is wrong about these is a fix that does nothing:
///
/// <list type="number">
/// <item><b>A short token typed into the composer RENDERS, and renders promptly on a healthy
/// session.</b> If it did not, the probe would fail every launch and the ready gate would be a
/// launch-killer rather than a launch-saver.</item>
/// <item><b>Ctrl+U empties that composer, verifiably, on the rendered screen.</b> The probe writes
/// junk into a session that is about to be handed a boot prompt; if the junk cannot be taken back
/// out, the prompt arrives spliced onto it. <c>ClaudeHarness</c> has used Ctrl+U against real Claude
/// since 2026-07, but as a best-effort recovery step, never as a pinned contract.</item>
/// </list>
///
/// <para>The third claim — that the probe says NO to a TUI that is <b>not</b> reading — cannot be
/// staged against real Claude on demand: the deaf window is a load artefact (the live repro needed
/// ~100% CPU across 8 cores, 35 <c>claude.exe</c> and 137 <c>Antiphon.PtyHost</c> processes) and this
/// card's plan explicitly says not to re-run it. It is modelled instead, deterministically, by
/// <c>FakeClaudeContractTests.A_tui_that_is_not_reading_fails_the_probe_instead_of_passing_quietly</c>
/// through <c>ANTIPHON_FAKE_DEAF_START_MS</c>. What this file measures is the half that a wrong model
/// would break silently: the healthy round trip.</para>
///
/// <para>Headed, opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>) and <c>[Explicit]</c>. Costs no model turns —
/// nothing here is ever submitted, which is the point.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0103")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeInputProbeCanaryTests
{
    /// <summary>
    /// The healthy round trip, end to end through the production helper: token in, token rendered,
    /// token gone. The elapsed time is printed because it is the per-launch price of the gate — the
    /// live control measurement put it at 0.74s for a 5 829-char body, so a 10-char token has no
    /// excuse to be slow.
    /// </summary>
    [Test]
    public async Task A_ready_claude_answers_the_input_probe_and_the_composer_is_left_empty()
    {
        ClSession.SkipIfNotEligible();

        // The deployment runs modern (CARD-0037 step 4), so that is the pty this measures.
        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions",
            "--session-id", Guid.NewGuid().ToString("D"));
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());
        Console.WriteLine($"backend={runner.Backend?.Backend} reason={runner.Backend?.Reason}");

        if (!await new ClaudeReadyDetector().WaitAsync(runner))
            throw new SkipTestException("real Claude TUI did not reach a ready state");

        var token = ComposerInputProbe.TokenFor(Guid.NewGuid());
        var stopwatch = Stopwatch.StartNew();
        var result = await ComposerInputProbe.RunAsync(
            token,
            _ => Task.FromResult(runner.SnapshotScreen()),
            (input, ct) => runner.WriteAsync(input, ct),
            ComposerProbeOptions.FromMilliseconds(
                timeoutMs: 90_000, pollIntervalMs: 250, retypeIntervalMs: 30_000, clearTimeoutMs: 10_000),
            Console.WriteLine,
            CancellationToken.None);
        stopwatch.Stop();
        Console.WriteLine(
            $"PROBE: outcome={result.Outcome} writes={result.Writes} elapsed={stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine("SCREEN:\n" + runner.SnapshotScreen());

        result.Outcome.ShouldBe(
            ComposerProbeOutcome.Responsive,
            "a ready Claude must answer the probe \u2014 otherwise the gate fails healthy launches, which "
            + "is a worse failure than the one it fixes. Screen:\n" + runner.SnapshotScreen());
        result.Writes.ShouldBe(1, "one token, no belt-and-braces re-type needed on a healthy session");

        // And the composer really is empty afterwards: an Enter now must submit nothing at all. If
        // the probe had left the token standing, this would start a turn (and cost a model call).
        await runner.WriteAsync("\r");
        await Task.Delay(TimeSpan.FromSeconds(6));
        var after = runner.SnapshotScreen();
        Console.WriteLine("AFTER ENTER:\n" + after);
        ComposerDeliveryEvidence.FragmentIsVisible(after, token).ShouldBeFalse(
            "the probe token must be gone from the composer BEFORE anything else types into it \u2014 a "
            + $"prompt appended to a standing '{token}' arrives spliced onto junk");

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Ctrl+U on its own, isolated from the probe's own retry logic, because the probe's verdict
    /// depends entirely on it: a kill-line that only clears an internal buffer while leaving the row
    /// painted would make every launch report "the composer will not clear".
    ///
    /// <para>Scope is a SINGLE typed line, which is all that has been measured and all the probe
    /// ever needs. What empties a composer holding a multi-line body, or a collapsed
    /// <c>[Pasted text #N]</c> placeholder, is deliberately NOT asserted here \u2014 that is CARD-0103
    /// slice 3's measurement, and the fakeclaude model is single-line for the same reason.</para>
    /// </summary>
    [Test]
    public async Task Ctrl_u_clears_a_single_typed_line_from_the_rendered_composer()
    {
        ClSession.SkipIfNotEligible();

        await using var runner = new PtyAgentRunner("modern");
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions",
            "--session-id", Guid.NewGuid().ToString("D"));
        await runner.StartAsync(app, args, cols: 120, rows: 30, env: ClSession.HeadedSafeEnv());

        if (!await new ClaudeReadyDetector().WaitAsync(runner))
            throw new SkipTestException("real Claude TUI did not reach a ready state");

        const string typed = "zzclearmecanary";
        await runner.WriteAsync(typed);
        (await runner.WaitForScreenAsync(
                s => ComposerDeliveryEvidence.FragmentIsVisible(s, typed), TimeSpan.FromSeconds(20)))
            .ShouldBeTrue("the composer must echo a short typed line. Screen:\n" + runner.SnapshotScreen());

        await runner.WriteAsync(ComposerInputProbe.KillLine);

        var cleared = await runner.WaitForScreenAsync(
            s => !ComposerDeliveryEvidence.FragmentIsVisible(s, typed), TimeSpan.FromSeconds(10));
        Console.WriteLine("AFTER CTRL+U:\n" + runner.SnapshotScreen());
        cleared.ShouldBeTrue(
            "Ctrl+U must clear the RENDERED composer \u2014 the probe reads the screen, so a clear that is "
            + "invisible there is no clear at all. Screen:\n" + runner.SnapshotScreen());

        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
