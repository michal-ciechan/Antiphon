using System.Text;
using System.Text.RegularExpressions;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0027's cheap reproduction. The expensive one
/// (<see cref="ClaudePasteLossCanaryTests"/>) spends a real turn per trial to read Claude's JSONL;
/// this one spends NONE. It pastes into the composer and never submits, reading the loss straight
/// off the rendered screen: Claude renders a large paste as <c>[Pasted text #N +M lines]</c>, and M
/// is its own count of what it captured. A 190-line body showing <c>+37 lines</c> has already lost
/// 152 lines before any model call happens — validated against the JSONL run, where the screen
/// placeholder and the transcript agreed exactly.
///
/// <para>That makes the whole matrix runnable at will: size, bracketed paste on/off, write chunk
/// size, and the gap between chunks — one variable at a time, no tokens.</para>
///
/// Headed and opt-in (<c>ANTIPHON_HEADED_TESTS=1</c>).
/// </summary>
[NotInParallel("Headed")]
[Category("Card0027")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeComposerCaptureProbeTests
{
    private static readonly StringBuilder Report = new();

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static void Flush(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0027");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".txt"), Report.ToString());
        Console.WriteLine($"[card-0027] wrote {Path.Combine(dir, name + ".txt")}");
        Report.Clear();
    }

    /// <summary>Lines of exactly 27 bytes, each naming its index.</summary>
    private static string MarkedBody(int lines) =>
        string.Concat(Enumerable.Range(0, lines).Select(i => $"P{i:D5}-{new string('x', 20)}\n"));

    private const int LineBytes = 27;

    [Test]
    public async Task Composer_capture_matrix()
    {
        ClSession.SkipIfNotEligible();

        Line("case\tsentLines\tsentBytes\tcapturedLines\tcapturedBytes\tkept%\tverdict");

        // A — size sweep, production encoding, one write. The baseline shape.
        foreach (var lines in new[] { 20, 40, 60, 90, 160, 200 })
            await TrialAsync($"A single-write wrap size={lines * LineBytes}", lines,
                chunkBytes: 0, gapMs: 0, wrap: true);

        // B — same body, NO bracketed paste. Isolates the paste path from the typing path.
        foreach (var lines in new[] { 60, 200 })
            await TrialAsync($"B single-write NOWRAP size={lines * LineBytes}", lines,
                chunkBytes: 0, gapMs: 0, wrap: false);

        // C — the write is split into chunks with a gap. If the loss is chunks racing inside ONE
        // event-loop turn, a gap that puts each chunk in its own turn is the difference between
        // whole and mangled. 0ms is the control (splitting alone changes nothing).
        foreach (var gap in new[] { 0, 2, 10, 25, 50, 100 })
            await TrialAsync($"C chunked=1024 gap={gap}ms", 200,
                chunkBytes: 1024, gapMs: gap, wrap: true);

        // D — chunk size at a fixed, generous gap: is the survivor a WRITE boundary or a READ one?
        foreach (var chunk in new[] { 256, 512, 2048, 4096 })
            await TrialAsync($"D chunked={chunk} gap=50ms", 200,
                chunkBytes: chunk, gapMs: 50, wrap: true);

        Flush("R2-composer-matrix");
    }

    /// <summary>
    /// Where exactly does the cut fall? 7-byte marker lines put the answer within 7 bytes, and the
    /// surviving RUNS say whether the survivor is one contiguous fragment (a whole read chunk) or
    /// several.
    /// </summary>
    [Test]
    public async Task Cut_point_resolution()
    {
        ClSession.SkipIfNotEligible();
        Line("rep\tsentBytes\tsurvivingRuns (marker index -> byte = idx*7)");

        for (var rep = 0; rep < 3; rep++)
        {
            await using var runner = new PtyAgentRunner();
            var (app, args) = ClSession.BuildLaunch(
                ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 120, rows: 250, env: ClSession.HeadedSafeEnv());
            if (!await new ClaudeReadyDetector().WaitAsync(runner))
                throw new SkipTestException("real Claude TUI did not reach a ready state");
            runner.ClearLiveBuffer();

            const int lines = 200; // 1400 bytes at 7 bytes/line
            var body = string.Concat(Enumerable.Range(0, lines).Select(i => $"Q{i:D5}\n"));
            await runner.WriteAsync(PtyInputEncoding.WrapIfMultiline(PtyInputEncoding.NormalizeBody(body)));
            await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(15));

            var screen = runner.SnapshotScreen();
            SaveScreen($"cut-rep{rep}", screen);
            var seen = Regex.Matches(screen, @"Q(\d{5})")
                .Select(m => int.Parse(m.Groups[1].Value)).Where(i => i < lines).ToHashSet();
            var runs = new List<(int Lo, int Hi)>();
            foreach (var i in Enumerable.Range(0, lines).Where(seen.Contains))
            {
                if (runs.Count > 0 && runs[^1].Hi == i - 1) runs[^1] = (runs[^1].Lo, i);
                else runs.Add((i, i));
            }
            Line($"{rep}\t{body.Length}\t"
                + string.Join(" ", runs.Select(r => $"[{r.Lo}-{r.Hi} = bytes {r.Lo * 7}-{(r.Hi + 1) * 7}]")));
            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }

        Flush("R4-cut-point");
    }

    /// <summary>
    /// The boundary that matters to the product: a body that fits in ONE read chunk has no second
    /// chunk to lose, and everything above it does. Three repeats per size, because the loss above
    /// the boundary is timing-dependent and a single sample proves nothing either way.
    /// </summary>
    [Test]
    public async Task Single_chunk_boundary()
    {
        ClSession.SkipIfNotEligible();

        Line("case\tsentLines\tsentBytes\tcapturedLines\tcapturedBytes\tkept%\tverdict");
        foreach (var lines in new[] { 30, 36, 37, 38, 39, 40, 50 })
            for (var rep = 0; rep < 3; rep++)
                await TrialAsync($"S bytes={lines * LineBytes} rep{rep}", lines,
                    chunkBytes: 0, gapMs: 0, wrap: true);

        Flush("R3-single-chunk-boundary");
    }

    /// <summary>
    /// One trial = one fresh Claude process. Reusing a session made the matrix alternate
    /// LOST/NOTHING in lockstep with the trial index — residue in the composer, not physics.
    /// Launching the TUI costs no tokens, so isolation is free and the numbers become readable.
    /// </summary>
    private static async Task TrialAsync(
        string label, int lines, int chunkBytes, int gapMs, bool wrap)
    {
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        // A tall viewport is load-bearing, not cosmetic: when Claude inlines a paste instead of
        // collapsing it to a placeholder, counting markers on a 30-row screen measures the WINDOW,
        // not the loss. 250 rows fits every body in this matrix, so a missing marker is missing.
        await runner.StartAsync(app, args, cols: 120, rows: 250, env: ClSession.HeadedSafeEnv());
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        var body = MarkedBody(lines);
        var payload = wrap
            ? PtyInputEncoding.WrapIfMultiline(PtyInputEncoding.NormalizeBody(body))
            : PtyInputEncoding.NormalizeBody(body);

        if (chunkBytes <= 0)
        {
            await runner.WriteAsync(payload);
        }
        else
        {
            for (var i = 0; i < payload.Length; i += chunkBytes)
            {
                await runner.WriteAsync(payload.Substring(i, Math.Min(chunkBytes, payload.Length - i)));
                if (gapMs > 0) await Task.Delay(gapMs);
            }
        }

        // Let the composer settle — the placeholder only renders after the paste is absorbed.
        await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(15));

        var screen = runner.SnapshotScreen();
        var captured = CapturedLines(screen);
        var pct = lines == 0 ? 0 : captured * 100 / lines;
        var verdict = captured >= lines ? "WHOLE" : captured <= 0 ? "NOTHING" : "LOST";
        Line($"{label}\t{lines}\t{lines * LineBytes}\t{captured}\t{captured * LineBytes}\t{pct}\t{verdict}");
        SaveScreen(label, screen);
        await runner.KillAsync(TimeSpan.FromSeconds(3));
    }

    /// <summary>Keeps the raw rendered screen for every trial — the oracle must stay auditable.</summary>
    private static void SaveScreen(string label, string screen)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0027", "screens");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        File.WriteAllText(Path.Combine(dir, safe + ".txt"), screen);
    }

    /// <summary>
    /// How many body lines the composer actually holds: the paste placeholder's own count when it
    /// rendered one, otherwise the distinct markers visible on screen.
    /// </summary>
    private static int CapturedLines(string screen)
    {
        var total = 0;
        foreach (Match m in Regex.Matches(screen, @"\[Pasted text #\d+ \+(\d+) lines\]"))
            total += int.Parse(m.Groups[1].Value) + 1; // "+N lines" = N beyond the first

        var literal = Regex.Matches(screen, @"P(\d{5})-")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
        return total > 0 ? total : literal.Count;
    }

}
