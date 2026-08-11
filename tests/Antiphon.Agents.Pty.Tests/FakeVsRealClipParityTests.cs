using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0028's honesty check: the SAME bodies through the real Claude TUI and through the clipping
/// fake, asserting they agree on the boundary and on whole-chunk survival.
///
/// <para><b>Why this has to exist.</b> The fake earns its keep by standing in for the real thing, so
/// something must fail when it stops matching. <see cref="FakeClaudeContractTests"/> pins the fake
/// against numbers a human copied out of an investigation; those numbers were measured once, in
/// August 2026, against one Claude build. Without a canary the fake would keep asserting them long
/// after they stopped being true — which is precisely the failure mode CARD-0028 exists to close
/// (the 2026-08-10 investigation trusted a fake that could not exhibit the defect at all).</para>
///
/// <para><b>What it asserts.</b> Three claims, in increasing strength:</para>
/// <list type="number">
///   <item>a body inside ONE read chunk arrives whole in BOTH, every repeat — the property the
///     delivery ceilings are built on;</item>
///   <item>a body spanning two chunks still loses in the real TUI. If real Claude arrives whole in
///     every repeat, the defect is GONE and the fake is now modelling fiction — that is a failure
///     here, deliberately, because it means the ceilings can be relaxed and the model retired;</item>
///   <item>when the real TUI does lose, what survives is a whole number of ~1024-byte chunks cut on
///     the same boundary the fake cuts on — not merely "less than we sent".</item>
/// </list>
///
/// <para>Real Claude is non-deterministic above the boundary (three identical 1 400-byte trials gave
/// whole, clipped, clipped), so claim 2 is asserted over repeats and claim 3 over the repeats that
/// actually lost. The fake is the deterministic worst case and is asserted per trial.</para>
///
/// <para>Costs NO model turns: it reads the composer, never submits. Headed and <c>[Explicit]</c>
/// (<c>ANTIPHON_HEADED_TESTS=1</c>) — it needs a real, authenticated Claude and one fresh TUI per
/// trial, so it never runs in a normal suite. Run it deliberately after a Claude upgrade, or when a
/// clip-model test starts looking wrong:</para>
/// <code>
/// $env:ANTIPHON_HEADED_TESTS=1
/// dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-fake/ `
///   -- --treenode-filter "/*/*/FakeVsRealClipParityTests/*"
/// </code>
/// </summary>
[NotInParallel("Headed")]
[Category("Card0028")]
[Explicit]
public class FakeVsRealClipParityTests
{
    private const int MarkerBytes = 7;   // "Q00000\n"
    private const int ChunkBytes = 1024; // the measured read quantum

    private static readonly StringBuilder Report = new();

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static void Flush(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0028");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".txt"), Report.ToString());
        Console.WriteLine($"[card-0028] wrote {Path.Combine(dir, name + ".txt")}");
        Report.Clear();
    }

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    private static string MarkedBody(int lines) =>
        string.Concat(Enumerable.Range(0, lines).Select(i => $"Q{i:D5}\n"));

    [Test]
    public async Task Fake_and_real_claude_agree_on_the_chunk_boundary()
    {
        ClSession.SkipIfNotEligible();
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe}");

        // 805 and 966 bytes sit inside one read chunk (measured whole 3/3 at 810 and 972);
        // 1 399 spans two, which is where the loss lives.
        int[] sizes = [115, 138, 200];
        const int reps = 3;

        Line("peer\tlines\tbodyBytes\tsurvivingRuns\tverdict");

        var fake = new Dictionary<int, Survivors>();
        foreach (var lines in sizes)
        {
            fake[lines] = await FakeAsync(lines);
            Line($"fake\t{lines}\t{Bytes(lines)}\t{fake[lines].Runs}\t{Verdict(fake[lines], lines)}");
        }

        var real = new Dictionary<int, List<Survivors>>();
        foreach (var lines in sizes)
        {
            real[lines] = [];
            for (var rep = 0; rep < reps; rep++)
            {
                var got = await RealClaudeAsync(lines);
                real[lines].Add(got);
                Line($"real#{rep}\t{lines}\t{Bytes(lines)}\t{got.Runs}\t{Verdict(got, lines)}");
            }
        }

        Flush("R1-fake-vs-real-clip-parity");

        // ---- Claim 1: inside one chunk, nothing is lost — in either peer, every repeat.
        foreach (var lines in sizes.Where(l => Bytes(l) <= ChunkBytes))
        {
            fake[lines].IsWhole(lines).ShouldBeTrue(
                $"the fake must not clip a {Bytes(lines)}-byte body: it fits in one read chunk");
            real[lines].ShouldAllBe(r => r.IsWhole(lines),
                $"real Claude kept a {Bytes(lines)}-byte body whole 3/3 when measured; if that has "
                + "changed, the boundary the delivery ceilings sit under has moved and "
                + "DelegationSettings.BriefInlineMaxBytes must be re-derived");
        }

        // ---- Claim 2: above one chunk, the real TUI still loses.
        const int big = 200;
        var lostReps = real[big].Where(r => !r.IsWhole(big)).ToList();
        lostReps.ShouldNotBeEmpty(
            $"real Claude took all {reps} repeats of a {Bytes(big)}-byte body whole. Either this "
            + "machine got lucky (the loss is timing-dependent — re-run) or the defect is FIXED, in "
            + "which case the fake's clip model is now fiction and the ceilings built on it can be "
            + "revisited. Do not just widen this assertion");
        fake[big].IsWhole(big).ShouldBeFalse(
            "and the fake's deterministic worst case must lose it every time");

        // ---- Claim 3: what survives is ONE whole chunk, cut on the 1024-byte grid — in both.
        // WHICH chunk survives is the TUI's render timing, not the body (1 414 and 2 332-byte
        // bodies kept their last chunk; a 5 194-byte one kept its first), so the shapes are
        // reported rather than asserted. The grid is the claim.
        var chunkSets = SingleChunkSets(big);
        var shapes = string.Join(" | ",
            chunkSets.Select((s, c) => $"chunk{c}={Describe(s)}"));

        foreach (var lost in lostReps)
        {
            var why =
                $"real Claude's survivors ({lost.Runs}, {lost.Captured} lines) are not ONE whole "
                + $"~{ChunkBytes}-byte chunk of the body ({shapes}). The fake models exactly that — "
                + "one chunk per event-loop turn, cut on the chunk grid — so if the real cut has "
                + "moved off the grid, or more than one chunk now survives a single turn, re-measure "
                + "before trusting any clip test built on it";

            // When the composer inlined the paste we can check WHICH lines survived; when it
            // collapsed it to a placeholder, its own count is all there is — and a whole chunk has
            // an exact line count, so the count alone still discriminates.
            if (lost.Markers.Count > 0)
                chunkSets.Any(set => set.SetEquals(lost.Markers)).ShouldBeTrue(why);
            else
                chunkSets.Any(set => set.Count == lost.Captured).ShouldBeTrue(why);
        }
        chunkSets.Any(set => set.SetEquals(fake[big].Markers)).ShouldBeTrue(
            $"and so must the fake's ({fake[big].Runs}) — {shapes}");

        Line($"shapes: fake=chunk{ChunkIndexOf(chunkSets, fake[big])} real="
            + string.Join(",", lostReps.Select(r =>
                $"chunk{(r.Markers.Count > 0 ? ChunkIndexOf(chunkSets, r) : chunkSets.FindIndex(s => s.Count == r.Captured))}")));
        Flush("R2-fake-vs-real-clip-shapes");
    }

    private static int Bytes(int lines) => lines * MarkerBytes - 1; // the trailing newline is trimmed

    private static string Verdict(Survivors s, int lines) =>
        s.IsWhole(lines) ? "WHOLE" : s.Markers.Count == 0 ? "NOTHING" : "CLIPPED";

    /// <summary>
    /// The markers wholly inside each ~1024-byte chunk of the body — i.e. every survivor set a
    /// one-chunk-per-turn model can produce. A cut matching none of these is off the chunk grid,
    /// which is the finding that would invalidate the fake.
    /// </summary>
    private static List<HashSet<int>> SingleChunkSets(int lines)
    {
        var bodyBytes = Bytes(lines);
        var chunks = (bodyBytes + ChunkBytes - 1) / ChunkBytes;
        return Enumerable.Range(0, chunks)
            .Select(c => Enumerable.Range(0, lines)
                .Where(i => i * MarkerBytes >= c * ChunkBytes
                            && (i + 1) * MarkerBytes <= Math.Min((c + 1) * ChunkBytes, bodyBytes + 1))
                .ToHashSet())
            .ToList();
    }

    private static int ChunkIndexOf(List<HashSet<int>> chunkSets, Survivors s) =>
        chunkSets.FindIndex(set => set.SetEquals(s.Markers));

    private static string Describe(HashSet<int> set) =>
        set.Count == 0 ? "(none)" : $"{set.Min()}-{set.Max()}";

    /// <summary>
    /// What a peer captured. <paramref name="Captured"/> is the count — from the composer's own
    /// <c>[Pasted text #N +M lines]</c> placeholder when it rendered one (M is its exact count of
    /// what it took, and it agreed with the JSONL when both were read), otherwise from the literal
    /// markers on screen. <paramref name="Markers"/> is which lines, and is EMPTY when the composer
    /// collapsed the paste — a count alone cannot say where the cut fell.
    /// </summary>
    private sealed record Survivors(int Captured, HashSet<int> Markers, string Runs, string? Note)
    {
        public bool IsWhole(int lines) => Captured >= lines;
    }

    /// <summary>The clipping fake, driven exactly as the contract tests drive it.</summary>
    private static async Task<Survivors> FakeAsync(int lines)
    {
        await using var runner = new PtyAgentRunner();
        await runner.StartAsync(FakeClaudeExe, [], cols: 120, rows: 250,
            env: new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_STDIN_CLIP"] = "1",
                // One write must land as one turn: ConPTY hands it over as several reads ~14ms apart.
                ["ANTIPHON_FAKE_BURST_MS"] = "80",
            });
        (await runner.WaitForOutputAsync(s => s.Contains("CLIP:mode="), TimeSpan.FromSeconds(15)))
            .ShouldBeTrue("the fake must start with clipping armed");
        runner.ClearLiveBuffer();

        await runner.WriteAsync(PtyInputEncoding.EncodeBody(MarkedBody(lines)));
        await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(15));

        var raw = runner.SnapshotText();
        await runner.KillAsync(TimeSpan.FromSeconds(3));
        return Read(raw, lines);
    }

    /// <summary>
    /// One trial = one fresh real Claude, pasted into and never submitted (so it costs no tokens).
    /// A tall viewport is load-bearing: on a 30-row screen a missing marker would only mean it
    /// scrolled. Reusing a session made the earlier matrix alternate in lockstep with the trial
    /// index — residue, not physics.
    /// </summary>
    private static async Task<Survivors> RealClaudeAsync(int lines)
    {
        await using var runner = new PtyAgentRunner();
        var (app, args) = ClSession.BuildLaunch(
            ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await runner.StartAsync(app, args, cols: 120, rows: 250, env: ClSession.HeadedSafeEnv());
        if (!await new ClaudeReadyDetector().WaitAsync(runner))
            throw new SkipTestException("real Claude TUI did not reach a ready state");
        runner.ClearLiveBuffer();

        await runner.WriteAsync(PtyInputEncoding.EncodeBody(MarkedBody(lines)));
        await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(15));

        var screen = runner.SnapshotScreen();
        await runner.KillAsync(TimeSpan.FromSeconds(3));
        return Read(screen, lines);
    }

    private static Survivors Read(string screen, int lines)
    {
        var flat = screen.Replace("\r", "").Replace("\n", "");
        var markers = Regex.Matches(flat, @"Q(\d{5})")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(i => i < lines)
            .ToHashSet();

        // The composer often collapses a paste to "[Pasted text #N +M lines]" instead of inlining
        // it. M is its own exact count of what it captured — the cheap oracle CARD-0027 used — but
        // it says nothing about WHICH lines, so the boundary claims fall back to the count.
        var placeholder = Regex.Matches(screen, @"\[Pasted text #\d+ \+(\d+) lines\]")
            .Sum(m => int.Parse(m.Groups[1].Value) + 1); // "+N lines" = N beyond the first

        var runs = new List<(int Lo, int Hi)>();
        foreach (var i in markers.OrderBy(x => x))
        {
            if (runs.Count > 0 && runs[^1].Hi == i - 1) runs[^1] = (runs[^1].Lo, i);
            else runs.Add((i, i));
        }

        var note = screen.Split('\n')
            .Select(l => l.Trim('\r', ' '))
            .FirstOrDefault(l => l.StartsWith("CLIPPED:", StringComparison.Ordinal));

        return new Survivors(
            placeholder > 0 ? placeholder : markers.Count,
            placeholder > 0 ? [] : markers,
            placeholder > 0
                ? $"[placeholder +{placeholder} lines]"
                : runs.Count == 0 ? "(none)" : string.Join(",", runs.Select(r => $"{r.Lo}-{r.Hi}")),
            note);
    }
}
