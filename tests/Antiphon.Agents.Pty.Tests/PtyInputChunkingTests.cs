using System.Runtime.InteropServices;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0027's CI-runnable half: the two platform facts the root cause rests on, pinned against a
/// real JS runtime on the real ConPTY path.
///
/// <para><b>Fact 1 — nothing we own loses a byte.</b> <see cref="PtyLargeWriteTests"/> already shows
/// that for a .NET peer; this shows it for a JavaScript one, which is what the real agent TUI is.
/// If either goes red, the loss has moved into code we own and the whole diagnosis changes.</para>
///
/// <para><b>Fact 2 — the body does not arrive as one read.</b> ConPTY hands a multi-KB write to the
/// client as a run of ~1 KB reads, and they land in the SAME event-loop turn. Every byte is there,
/// so this is not loss — it is the precondition that makes loss possible in a consumer that folds
/// same-turn chunks into one state update. The measured cut points in the real Claude TUI fall
/// exactly on these boundaries (body byte 1029 at 7-byte resolution, i.e. 1024).</para>
///
/// See <c>docs/investigations/2026-08-11-pty-chunk-loss-root-cause-CARD-0027.md</c>.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
public class PtyInputChunkingTests
{
    private static void SkipIfUnavailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
        if (!NodeStdinProbe.NodeAvailable)
            throw new SkipTestException("no JS runtime (node.exe) on PATH");
        if (!File.Exists(NodeStdinProbe.ProbePath))
            throw new SkipTestException($"probe not staged at {NodeStdinProbe.ProbePath}");
    }

    /// <summary>
    /// The sizes that mangled live — 1 366 and 2 320 lost their heads, 5 185 lost its middle —
    /// plus one well past them. A JS peer on the same path receives all of it.
    /// </summary>
    [Test]
    [Arguments(1366)]
    [Arguments(2320)]
    [Arguments(5185)]
    [Arguments(16384)]
    public async Task A_body_that_mangled_live_reaches_a_js_runtime_peer_whole(int size)
    {
        SkipIfUnavailable();
        await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);

        var body = NodeStdinProbe.MarkedBodyOfBytes(size);
        var result = await probe.DeliverAsync(body);

        result.MissingCount.ShouldBe(0,
            $"a {body.Length}-byte body must reach a JS peer whole — missing runs: "
            + string.Join(",", result.MissingRuns.Select(r => $"{r[0]}-{r[1]}")));
        result.Bytes.ShouldBeGreaterThanOrEqualTo(body.Length,
            "every byte of the body must be delivered, not merely most of them");
    }

    /// <summary>
    /// A real TUI renders between reads instead of sitting in a read loop. Draining slowly does not
    /// cost bytes either — a pipe write blocks, it does not discard.
    /// </summary>
    [Test]
    public async Task A_peer_that_blocks_between_reads_still_receives_every_byte()
    {
        SkipIfUnavailable();
        await using var probe = await NodeStdinProbe.StartAsync(blockMs: 25, chunkLog: false);

        var body = NodeStdinProbe.MarkedBodyOfBytes(5185);
        var result = await probe.DeliverAsync(body, timeout: TimeSpan.FromSeconds(90));

        result.MissingCount.ShouldBe(0,
            "blocking the consumer between reads must not lose bytes — if this goes red, the "
            + "console input path HAS started dropping and the diagnosis in CARD-0027 is wrong");
    }

    /// <summary>
    /// The precondition for the TUI-side loss, stated as an assertion so it cannot quietly stop
    /// being true: a multi-KB body is delivered as several sub-2KB reads, and more than one of them
    /// lands in a single event-loop turn. A consumer that collapses same-turn chunks keeps exactly
    /// one of them — which is what the real Claude composer was measured doing.
    /// </summary>
    [Test]
    public async Task A_multi_kb_body_arrives_as_several_reads_inside_one_event_loop_turn()
    {
        SkipIfUnavailable();
        await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);

        var body = NodeStdinProbe.MarkedBodyOfBytes(5185);
        var result = await probe.DeliverAsync(body);

        result.Chunks.ShouldBeGreaterThan(1,
            "a 5 KB body is not handed over as one read — that fragmentation is what a consumer "
            + "has to reassemble correctly");
        result.DistinctSizes.Max().ShouldBeLessThan(2048,
            "the read quantum is ~1 KB; the measured cut points in the live miss sit on it");
        result.MaxChunksPerTurn.ShouldBeGreaterThan(1,
            "several reads land in the SAME event-loop turn — the condition under which a "
            + "last-write-wins accumulator keeps one chunk and silently drops the rest");
    }
}
