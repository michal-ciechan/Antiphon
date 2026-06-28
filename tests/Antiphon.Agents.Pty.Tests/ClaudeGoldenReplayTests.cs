using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Golden record/replay: a captured stream of REAL Claude PTY bytes (one completed turn, ANSI and
/// cursor optimisation intact — see <c>golden/claude-turn.golden.txt</c>) replayed through our parsing
/// stack. Deterministic and CI-friendly because the bytes are fixed; faithful because they came off a
/// real <c>claude</c> session, not a hand-written approximation.
///
/// This is the layer the fake can't cover: the fake models Claude's <em>input/submit contract</em>, but
/// our <see cref="TerminalScreen"/>, <see cref="AnsiStripper"/>, <see cref="ClaudeResponseAnalyzer"/> and
/// the done-signal detector must correctly parse Claude's real <em>output</em>. When a future Claude
/// changes its output format, these fail — re-capture the golden and the diff shows exactly what moved.
///
/// The recording was produced by prompting a live agent to "Reply with exactly GOLDEN-MARKER-7919 and
/// nothing else" and slicing the one turn from the runner's raw buffer.
/// </summary>
[Category("Pty")]
public class ClaudeGoldenReplayTests
{
    private const string Marker = "GOLDEN-MARKER-7919";

    // The same signal RunnerClaudeAdapter / ClaudeCrunchedDetector key on for turn completion.
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static string LoadGolden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "claude-turn.golden.txt");
        File.Exists(path).ShouldBeTrue($"golden recording missing at {path}");
        return File.ReadAllText(path);
    }

    [Test]
    public void Golden_turn_carries_the_done_signal_our_detector_keys_on()
    {
        var golden = LoadGolden();

        // ClaudeCrunchedDetector fires on this pattern; its presence in real bytes is what makes turn-end
        // detection work. If Claude drops the "… for Ns" summary, this fails and we re-evaluate the detector.
        DonePattern.IsMatch(golden).ShouldBeTrue("real Claude turn must contain the \" for Ns\" done signal");
    }

    [Test]
    public void ClaudeResponseAnalyzer_extracts_the_response_from_real_bytes()
    {
        var golden = LoadGolden();

        var response = ClaudeResponseAnalyzer.ExtractResponse(golden);

        // ExtractResponse strips ANSI and returns the region before the "… for Ns" summary — the marker
        // (Claude's actual reply) must survive that extraction.
        response.ShouldContain(Marker);
        // The cooking-verb summary must be trimmed off the end of the response region.
        DonePattern.IsMatch(response).ShouldBeFalse("the \" for Ns\" summary must be trimmed from the response");
    }

    [Test]
    public void AnsiStripper_preserves_the_visible_marker()
    {
        var golden = LoadGolden();

        var clean = AnsiStripper.Clean(golden) ?? "";

        clean.ShouldContain(Marker, customMessage: "stripping ANSI must keep the visible text");
    }

    [Test]
    public void TerminalScreen_ingests_real_bytes_without_choking()
    {
        var golden = LoadGolden();

        // This golden is a mid-stream slice of one turn, so it has no clean screen-init and its absolute
        // cursor moves reference rows set up before the cut — a faithful grid reconstruction isn't possible
        // from a partial stream (that's what the controlled TerminalScreenTests cover). What we DO pin here:
        // feeding real Claude bytes — with their actual ESC/CSI/OSC mix and cursor optimisation — must not
        // throw and must still produce a coherent grid. This catches a parser that trips on real sequences.
        var screen = new TerminalScreen(200, 60);
        Should.NotThrow(() => screen.Feed(golden));

        screen.GetScreenText().ShouldNotBeNull();
        screen.Rows.ShouldBe(60);
    }
}
