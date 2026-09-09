using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Replays REAL effort-dialog PTY streams (golden/card-0449) through the production emulator.
///
/// The dismissal path had never been exercised against a real rendered screen: every prior round
/// used the scripted <see cref="EffortTestScreen"/> fake, whose non-dialog screen is three clean
/// lines. The one real dismissal that ever ran failed, because <see cref="TerminalScreen"/> wrapped
/// immediately at the last column where ConPTY assumes a deferred wrap — Claude's full-width
/// 120-column rules pushed the cursor a row too far, the dialog's erase-and-repaint left a ghost
/// title behind, and the resolver's remnant gate blocked for its whole budget (CARD-0449).
///
/// Every stream is replayed in three chunkings — one write, split per Ink frame, and escape-safe
/// 256-char pieces — so the investigation's open question ("one atomic write or several
/// time-separated writes?") cannot decide whether the fix holds. Each test also asserts chunk
/// invariance: the rendered grid must not depend on where the reads were cut.
/// </summary>
[Category("Unit")]
public class ClaudeEffortDismissalReplayTests
{
    private const string RealDismissal = "effort-dismissal-23b5663c.ansi";
    private const string ExcisedDismissal = "effort-dismissal-23b5663c-banner-excised.ansi";

    /// <summary>Ink starts every frame by hiding the cursor.</summary>
    private const string FrameStart = "\x1b[?25l";

    /// <summary>The dismissal's first erase. Everything before it is the dialog frame.</summary>
    private const string DismissalStart = "\x1b[2K\x1b[1A";

    private static readonly string Rule = new('─', 120);

    // ── Fixture loading ─────────────────────────────────────────────────────────

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "golden", "card-0449", name);

    private static byte[] Bytes(string name)
    {
        var path = FixturePath(name);
        File.Exists(path).ShouldBeTrue($"fixture missing at {path}");
        return File.ReadAllBytes(path);
    }

    /// <summary>The fixtures carry no BOM, so a single stateless decode is the whole stream.</summary>
    private static string Text(string name) => Encoding.UTF8.GetString(Bytes(name));

    // ── Chunkings ───────────────────────────────────────────────────────────────

    /// <summary>Exclusive end of the escape sequence (or single character) starting at <paramref name="i"/>.</summary>
    private static int TokenEnd(string t, int i)
    {
        if (t[i] != '\x1b') return i + 1;
        if (i + 1 >= t.Length) return t.Length;
        switch (t[i + 1])
        {
            case '[':
            {
                var j = i + 2;
                while (j < t.Length && !(t[j] >= '@' && t[j] <= '~')) j++;
                return j < t.Length ? j + 1 : t.Length;
            }
            case ']':
            {
                var j = i + 2;
                while (j < t.Length)
                {
                    if (t[j] == '\x07') return j + 1;
                    if (t[j] == '\x1b' && j + 1 < t.Length && t[j + 1] == '\\') return j + 2;
                    j++;
                }
                return t.Length;
            }
            case 'P': case 'X': case '^': case '_':
            {
                var j = i + 2;
                while (j < t.Length)
                {
                    if (t[j] == '\x1b' && j + 1 < t.Length && t[j + 1] == '\\') return j + 2;
                    j++;
                }
                return t.Length;
            }
            case '(': case ')': case '*': case '+':
                return Math.Min(i + 3, t.Length);
            default:
                return i + 2;
        }
    }

    /// <summary>
    /// Successive 256-char pieces, each extended so it never ends inside an escape sequence.
    /// The extension is not cosmetic: in every one of these fixtures a plain 256-byte cut lands
    /// inside a CSI (and, undecoded, inside a UTF-8 multibyte character), and
    /// <see cref="TerminalScreen.Feed"/> keeps no parser state between calls — a naive cut would
    /// fail on the parser rather than on deferred wrap, which is a different card entirely.
    /// </summary>
    private static List<string> Pieces(string t, int size = 256)
    {
        var list = new List<string>();
        var start = 0;
        while (start < t.Length)
        {
            var end = Math.Min(start + size, t.Length);
            var i = start;
            while (i < end)
            {
                var next = TokenEnd(t, i);
                if (next > end) { end = next; break; }
                i = next;
            }
            list.Add(t[start..end]);
            start = end;
        }
        return list;
    }

    /// <summary>Split before each frame start; empty pieces dropped.</summary>
    private static List<string> Frames(string t)
    {
        var bounds = new List<int> { 0 };
        for (var j = t.IndexOf(FrameStart, StringComparison.Ordinal); j >= 0;
             j = t.IndexOf(FrameStart, j + 1, StringComparison.Ordinal))
            if (j > 0) bounds.Add(j);
        bounds.Add(t.Length);
        var list = new List<string>();
        for (var i = 0; i + 1 < bounds.Count; i++)
            if (bounds[i + 1] > bounds[i]) list.Add(t[bounds[i]..bounds[i + 1]]);
        return list;
    }

    private static List<string> Chunks(string t, string mode) => mode switch
    {
        "whole" => [t],
        "frames" => Frames(t),
        "pieces" => Pieces(t),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown chunking")
    };

    private static TerminalScreen Replay(string text, string mode)
    {
        var screen = new TerminalScreen(120, 30);
        foreach (var chunk in Chunks(text, mode)) screen.Feed(chunk);
        return screen;
    }

    /// <summary>The dialog paint: for a dismissal stream, everything before the first erase.</summary>
    private static string DialogFrame(string text)
    {
        var at = text.IndexOf(DismissalStart, StringComparison.Ordinal);
        return at < 0 ? text : text[..at];
    }

    private static void ShouldBeChunkInvariant(TerminalScreen actual, string text, string mode)
    {
        if (mode == "whole") return;
        actual.GetRows().ShouldBe(Replay(text, "whole").GetRows(),
            $"the rendered grid must not depend on read boundaries ({mode})");
    }

    // ── V-11: the dialog paint ──────────────────────────────────────────────────

    [Test]
    [Arguments(RealDismissal, "whole"), Arguments(RealDismissal, "frames"), Arguments(RealDismissal, "pieces")]
    [Arguments("effort-dialog-9184ec6d.ansi", "whole"), Arguments("effort-dialog-9184ec6d.ansi", "frames"), Arguments("effort-dialog-9184ec6d.ansi", "pieces")]
    [Arguments("effort-dialog-2968433b.ansi", "whole"), Arguments("effort-dialog-2968433b.ansi", "frames"), Arguments("effort-dialog-2968433b.ansi", "pieces")]
    [Arguments("effort-dialog-787bfee2.ansi", "whole"), Arguments("effort-dialog-787bfee2.ansi", "frames"), Arguments("effort-dialog-787bfee2.ansi", "pieces")]
    public void Real_dialog_paints_directly_under_its_rule(string fixture, string mode)
    {
        var dialog = DialogFrame(Text(fixture));
        var screen = Replay(dialog, mode);
        var rows = screen.GetRows();

        // The whole defect in one assertion: under an immediate wrap the 120-char rule line-feeds
        // itself, so the CR + \x1b[1B that follows lands the title two rows below instead of one.
        rows[5].ShouldBe(Rule, "row 5 is the full-width rule");
        screen.FindRow("Use Fable 5.1").ShouldBe(6, "the title sits DIRECTLY under its rule");
        rows[6].Trim().ShouldBe("Use Fable 5.1 at high effort by default?");
        rows[8].ShouldContain("high is the default effort for Fable 5.1");
        rows[9].Trim().ShouldBe("task. You can change this any time with /effort.");
        rows[11].ShouldContain("estimated cost");
        rows[13].Trim().ShouldBe("> Keep xhigh");
        rows[14].Trim().ShouldBe("Switch Fable 5.1 to high effort");

        var text = screen.GetScreenText();
        ClaudeEffortPrompt.Parse(text)
            .ShouldBe(new("Fable 5.1", "xhigh", "high", ClaudeEffortOption.Keep));
        ClaudeEffortPrompt.HasRemnant(text).ShouldBeTrue("a live dialog is a remnant by definition");
        ShouldBeChunkInvariant(screen, dialog, mode);
    }

    // ── V-12: the dismissal ─────────────────────────────────────────────────────

    [Test]
    [Arguments(RealDismissal, "whole"), Arguments(RealDismissal, "frames"), Arguments(RealDismissal, "pieces")]
    [Arguments(ExcisedDismissal, "whole"), Arguments(ExcisedDismissal, "frames"), Arguments(ExcisedDismissal, "pieces")]
    public void Real_dismissal_leaves_no_dialog_rows(string fixture, string mode)
    {
        var stream = Text(fixture);
        var screen = Replay(stream, mode);
        var rows = screen.GetRows();

        rows[5].ShouldEndWith(" task-f418105f ─");
        rows[6].ShouldContain("Try \"how do I log an error?\"");
        rows[7].ShouldBe(Rule);
        rows[8].ShouldContain("bypass permissions on");
        rows[8].ShouldContain("◉ xhigh · /effort");
        for (var r = 9; r < 30; r++) rows[r].ShouldBe("", $"row {r} must be empty — no ghost survives");
        screen.FindRow("Try \"how do I log an error?\"")
            .ShouldBe(screen.FindRow("task-f418105f") + 1, "the composer sits DIRECTLY under the banner rule");

        var text = screen.GetScreenText();
        ClaudeEffortPrompt.HasRemnant(text).ShouldBeFalse("this is the incident: a ghost title blocked the gate");
        ClaudeEffortPrompt.Parse(text).ShouldBeNull();
        ClaudeScreen.ComposerIsLive(text).ShouldBeTrue();
        ClaudeEffortPrompt.CurrentEffort(text).ShouldBe("xhigh");
        screen.CursorRow.ShouldBe(6);
        screen.CursorCol.ShouldBe(2);
        ShouldBeChunkInvariant(screen, stream, mode);

        if (fixture != RealDismissal || mode != "frames") return;

        // The banner write and its retraction are separate frames, so this chunking exposes the
        // one intermediate screen the resolver can actually poll. It is remnant-free and has a
        // live composer, but it is NOT settled against the final frame — the banner row survives
        // Stable — so the settle pair spans one extra poll here. That is the gate working, not a
        // reason to special-case the banner (a replay with the banner excised ghosts identically).
        var frames = Frames(stream);
        var intermediate = new TerminalScreen(120, 30);
        for (var k = 0; k < frames.Count - 1; k++) intermediate.Feed(frames[k]);
        var intermediateRows = intermediate.GetRows();
        intermediateRows[9].ShouldBe(new string(' ', 52) + "You've used 83% of your weekly limit · resets 12am (Europe/London)");
        intermediateRows[8].ShouldNotContain("◉ xhigh · /effort");
        var intermediateText = intermediate.GetScreenText();
        ClaudeEffortPrompt.HasRemnant(intermediateText).ShouldBeFalse();
        ClaudeScreen.ComposerIsLive(intermediateText).ShouldBeTrue();
        ClaudeScreen.IsSettled(intermediateText, text).ShouldBeFalse("the banner row is content, not chrome");
    }

    // ── V-13: the resolver over the replay ──────────────────────────────────────

    [Test]
    [Arguments(RealDismissal, "whole"), Arguments(RealDismissal, "frames"), Arguments(RealDismissal, "pieces")]
    [Arguments(ExcisedDismissal, "whole"), Arguments(ExcisedDismissal, "frames"), Arguments(ExcisedDismissal, "pieces")]
    public async Task Resolver_clears_a_replayed_dismissal(string fixture, string mode)
    {
        var stream = Text(fixture);
        var at = stream.IndexOf(DismissalStart, StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0, "the dismissal marker must be present");

        var screen = new TerminalScreen(120, 30);
        screen.Feed(stream[..at]);
        var pending = new Queue<string>(Chunks(stream[at..], mode));
        var writes = new List<string>();
        var postEnter = 0;

        // No sleeps: the resolver's own 50 ms cadence paces the pieces, one per poll.
        Task<string> Snapshot(CancellationToken ct)
        {
            if (writes.Contains("\r"))
            {
                postEnter++;
                if (pending.Count > 0) screen.Feed(pending.Dequeue());
            }
            return Task.FromResult(screen.GetScreenText());
        }

        Task Write(string key, CancellationToken ct) { writes.Add(key); return Task.CompletedTask; }

        var result = await ClaudeEffortPrompt.ResolveAsync(Snapshot, Write,
            ClaudeEffortIntent.Read(["--effort", "xhigh"]), TimeSpan.FromMilliseconds(8000),
            CancellationToken.None);

        result.Cleared.ShouldBeTrue(result.Detail);
        writes.ShouldBe(["\r"], result.Detail);
        result.Detail.ShouldContain("Enter=1");
        result.Detail.ShouldContain("two settled clear observations");
        Regex.IsMatch(result.Detail, @"\[polls=\d+ clear=2 last=\S+ composer=live\]")
            .ShouldBeTrue(result.Detail);
        pending.ShouldBeEmpty("every chunk must have been served");
        if (fixture == RealDismissal && mode == "frames")
            postEnter.ShouldBeGreaterThanOrEqualTo(3, "the banner frame is not settled against the final one");
    }

    // ── V-21: the bytes ─────────────────────────────────────────────────────────

    [Test]
    [Arguments(RealDismissal, 3957, "18c89534612034390d52a09b2e96093687b4be379f98a2d03684620f01ee9d4c")]
    [Arguments(ExcisedDismissal, 3868, "15f7381f74850dc508e26eb0b32810bf87e7bc11d95e86c0bac4e0c637f9b23b")]
    [Arguments("effort-dialog-9184ec6d.ansi", 2637, "791729350b491bb6805e72e2b79dc7e3b706fe2a37c9a72b44a07b11c72d03b2")]
    [Arguments("effort-dialog-2968433b.ansi", 2631, "cb41ba5defe7afa6089a93dff943d714c02391deb9812ccddf97fa39cbcd77e1")]
    [Arguments("effort-dialog-787bfee2.ansi", 2631, "c478644172a24472166370c4af7470ddf5442957e35583b0623ce2db98a21a30")]
    public void Fixture_hashes_are_pinned(string fixture, int size, string sha256)
    {
        var bytes = Bytes(fixture);
        bytes.Length.ShouldBe(size, $"{fixture}: git normalisation or a hand edit changed the bytes");
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().ShouldBe(sha256, fixture);
    }

    [Test]
    public void Banner_excised_fixture_is_derived_from_the_real_one()
    {
        // The one 89-byte usage-banner write, removed and nothing else. The investigation blamed
        // this banner for the settle failure; the excised replay ghosts identically without it.
        var real = Bytes(RealDismissal);
        var banner = Encoding.UTF8.GetBytes(
            "\x1b[53G\x1b[38;2;255;193;7mYou've used 83% of your weekly limit · resets 12am (Europe/London)");
        banner.Length.ShouldBe(89);
        real.AsSpan(3694, banner.Length).SequenceEqual(banner)
            .ShouldBeTrue("the excision span must still be exactly the banner write");
        Bytes(ExcisedDismissal).ShouldBe([.. real[..3694], .. real[3783..]]);
    }
}
