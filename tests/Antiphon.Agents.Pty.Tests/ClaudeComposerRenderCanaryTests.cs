using System.Text;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Composer-render canary: what does a real Claude TUI actually SHOW in the composer after we
/// type a message body the way <c>DeliverAsync</c> does (whole body in one write, no CR)?
///
/// This pins the ground truth the planned delivery-time content check depends on — "verify the
/// composer contains the text before pressing Enter" only works if we know which part of the
/// text is visible. Observed against claude on 2026-07-21 (now asserted as the contract):
///  * short single line   → rendered VERBATIM (start and end both visible). Stable.
///  * huge single line    → the composer viewport shows the SUFFIX (tail near the cursor); the
///                          start and middle scroll out of view. No paste placeholder. Stable
///                          across runs. Wrapping can split tokens across rows, so any
///                          contains-check must strip ALL whitespace from screen and needle first.
///  * multi-line wall     → NON-DETERMINISTIC between runs (paste-burst detection timing):
///                          sometimes the PREFIX lines render plus a "[Pasted text #N +X lines]"
///                          placeholder (tail hidden); sometimes the TAIL lines render with no
///                          placeholder (start hidden). A content check must therefore accept
///                          EITHER first-line, last-line, or placeholder as delivery evidence.
///
/// Each scenario gets a FRESH real Claude at the production terminal size (120x30). Observations
/// are written to <c>claude-composer-render.observed.json</c> in the test output; the hard
/// assertions are only the invariants the content check would rely on.
///
/// Opt-in headed: <c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH; self-skips otherwise.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
public class ClaudeComposerRenderCanaryTests
{
    private const string StartMarker = "STARTMARKERALPHA";
    private const string MiddleMarker = "MIDDLEMARKERBRAVO";
    private const string EndMarker = "ENDMARKERZULU";

    [Test]
    public async Task Composer_render_contract_for_delivery_style_writes()
    {
        ClSession.SkipIfNotEligible();

        var observed = new Dictionary<string, object?>();

        // ── Scenario 1: short single line ────────────────────────────────────────
        {
            var body = $"{StartMarker} short composer message {EndMarker}";
            var screen = await TypeAndSnapshotAsync(body);
            observed["short.startVisible"] = screen.Contains(StartMarker);
            observed["short.endVisible"] = screen.Contains(EndMarker);
            observed["short.screen"] = screen;
        }

        // ── Scenario 2: huge single line (~6000 chars ≈ 50 wrapped rows > 30-row screen) ──
        {
            var filler = new StringBuilder();
            for (var i = 0; filler.Length < 2800; i++) filler.Append($"wall{i:D4} ");
            var body = $"{StartMarker} {filler} {MiddleMarker} {filler} {EndMarker}";
            var screen = await TypeAndSnapshotAsync(body);
            observed["hugeLine.length"] = body.Length;
            observed["hugeLine.startVisible"] = screen.Contains(StartMarker);
            observed["hugeLine.middleVisible"] = screen.Contains(MiddleMarker);
            observed["hugeLine.endVisible"] = screen.Contains(EndMarker);
            observed["hugeLine.pastedPlaceholder"] = screen.Contains("asted text");
            observed["hugeLine.screen"] = screen;
        }

        // ── Scenario 3: multi-line wall (40 lines, one write — exactly how DeliverAsync sends bodies) ──
        {
            var lines = Enumerable.Range(0, 40).Select(i => i switch
            {
                0 => $"{StartMarker} first line of the wall",
                39 => $"{EndMarker} last line of the wall",
                _ => $"line{i:D2} of the multi-line wall",
            });
            var body = string.Join("\n", lines);
            var screen = await TypeAndSnapshotAsync(body);
            observed["multiLine.startVisible"] = screen.Contains(StartMarker);
            observed["multiLine.endVisible"] = screen.Contains(EndMarker);
            observed["multiLine.pastedPlaceholder"] = screen.Contains("asted text");
            observed["multiLine.screen"] = screen;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "claude-composer-render.observed.json");
        File.WriteAllText(path, JsonSerializer.Serialize(observed, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Composer render observations written to {path}");
        foreach (var (k, v) in observed.Where(kv => !kv.Key.EndsWith(".screen")))
            Console.WriteLine($"  {k} = {v}");

        // The STABLE invariants a composer content check relies on. (Multi-line rendering is
        // non-deterministic — prefix+placeholder on some runs, tail-only on others — so for it
        // we pin only "at least one form of evidence is visible".) If any of these fail,
        // Claude's composer rendering changed — re-observe and redesign the check before re-pinning.
        ((bool)observed["short.startVisible"]!).ShouldBeTrue("short body start must be visible in the composer");
        ((bool)observed["short.endVisible"]!).ShouldBeTrue("short body end must be visible in the composer");
        ((bool)observed["hugeLine.endVisible"]!).ShouldBeTrue("huge single line: the SUFFIX (cursor tail) must stay visible");
        var multiLineEvidence = (bool)observed["multiLine.startVisible"]!
            || (bool)observed["multiLine.endVisible"]!
            || (bool)observed["multiLine.pastedPlaceholder"]!;
        multiLineEvidence.ShouldBeTrue(
            "multi-line wall: at least one of first-line / last-line / [Pasted text] placeholder must be visible");
    }

    /// <summary>
    /// Fresh real Claude at production size (120x30) → type the body in ONE write (the
    /// DeliverAsync body shape, no CR) → wait for the render to settle → return the rendered screen.
    /// </summary>
    private static async Task<string> TypeAndSnapshotAsync(string body)
    {
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 120, rows: 30);

        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        await runner.WriteAsync(body);

        // Wait until SOME reaction lands (any marker or a paste placeholder), then let the
        // render settle before the verdict snapshot — the echo-probe bug taught us that a
        // single fixed 750ms settle window under-waits on a real TUI.
        await runner.WaitForScreenAsync(
            s => s.Contains(StartMarker) || s.Contains(EndMarker) || s.Contains("asted text"),
            TimeSpan.FromSeconds(15));
        await Task.Delay(2000);

        var screen = runner.SnapshotScreen();
        await runner.KillAsync(TimeSpan.FromSeconds(2));
        return screen;
    }
}
