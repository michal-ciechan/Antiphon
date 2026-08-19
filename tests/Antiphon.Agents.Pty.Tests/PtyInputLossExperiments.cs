using System.Runtime.InteropServices;
using System.Text;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0027 bench. Not assertions — a measuring instrument. Each experiment varies ONE thing and
/// records what the Node peer received, including the negative results. Run it explicitly:
/// <c>dotnet run --project tests/Antiphon.Agents.Pty.Tests --treenode-filter "/*/*/PtyInputLossExperiments/*"</c>
/// Results land in <c>TestOutput/card-0027/</c> next to the test binary.
/// </summary>
[NotInParallel("Headed")]
[Category("Card0027")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class PtyInputLossExperiments
{
    private static readonly StringBuilder Report = new();

    private static void SkipIfUnavailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
        if (!NodeStdinProbe.NodeAvailable)
            throw new SkipTestException("node.exe not on PATH");
        if (!File.Exists(NodeStdinProbe.ProbePath))
            throw new SkipTestException($"probe not staged at {NodeStdinProbe.ProbePath}");
    }

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static void Flush(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0027");
        Directory.CreateDirectory(dir);
        name = $"{name}.{NodeStdinProbe.RuntimeName}";
        File.WriteAllText(
            Path.Combine(dir, name + ".txt"),
            $"runtime={NodeStdinProbe.RuntimeName}{Environment.NewLine}{Report}");
        Console.WriteLine($"[card-0027] wrote {Path.Combine(dir, name + ".txt")}");
        Report.Clear();
    }

    /// <summary>E1 — size sweep through the production encoding, fresh session per size.</summary>
    [Test]
    public async Task E1_size_sweep()
    {
        SkipIfUnavailable();
        Line("E1 size sweep — production encoding (LF + bracketed paste + delayed CR), Node raw mode");
        Line("sent\tgot\tchunks\tturns\tmaxPerTurn\tmissing\tdistinctChunkSizes");
        foreach (var size in new[] { 500, 1024, 1366, 2320, 4000, 4262, 5185, 5471, 8192, 16384, 43000 })
        {
            await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);
            var body = NodeStdinProbe.MarkedBodyOfBytes(size);
            var r = await probe.DeliverAsync(body);
            Line($"{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.Turns}\t{r.MaxChunksPerTurn}\t{r.MissingCount}\t[{string.Join(",", r.DistinctSizes)}]");
        }
        Flush("E1-size-sweep");
    }

    /// <summary>E2 — the same sizes into a peer that blocks the event loop between reads.</summary>
    [Test]
    public async Task E2_slow_drain()
    {
        SkipIfUnavailable();
        Line("E2 slow drain — peer spins 25ms on every data event (models render-between-reads)");
        Line("sent\tgot\tchunks\tturns\tmaxPerTurn\tmissing\tdistinctChunkSizes");
        foreach (var size in new[] { 1366, 4262, 5185, 16384, 43000 })
        {
            await using var probe = await NodeStdinProbe.StartAsync(blockMs: 25, chunkLog: false);
            var body = NodeStdinProbe.MarkedBodyOfBytes(size);
            var r = await probe.DeliverAsync(body, timeout: TimeSpan.FromSeconds(90));
            Line($"{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.Turns}\t{r.MaxChunksPerTurn}\t{r.MissingCount}\t[{string.Join(",", r.DistinctSizes)}]");
        }
        Flush("E2-slow-drain");
    }

    /// <summary>E3 — session age / phase: repeated deliveries down ONE long-lived session.</summary>
    [Test]
    public async Task E3_repeated_deliveries_one_session()
    {
        SkipIfUnavailable();
        Line("E3 phase/session age — 12 deliveries down one session, cumulative bytes tracked");
        Line("i\tcumulativeBefore\tsent\tgot\tchunks\tturns\tmaxPerTurn\tmissing");
        await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);
        var cumulative = 0;
        for (var i = 0; i < 12; i++)
        {
            var size = 1366 + (i * 397); // walk the phase grid deliberately
            var body = NodeStdinProbe.MarkedBodyOfBytes(size);
            var r = await probe.DeliverAsync(body);
            Line($"{i}\t{cumulative}\t{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.Turns}\t{r.MaxChunksPerTurn}\t{r.MissingCount}");
            cumulative += body.Length + 12 + 1 + 13;
        }
        Flush("E3-phase");
    }

    /// <summary>E4 — encoding variants at the sizes that stranded live.</summary>
    [Test]
    public async Task E4_encoding_variants()
    {
        SkipIfUnavailable();
        Line("E4 encoding variants at 1402 and 5185 bytes");
        Line("variant\tsent\tgot\tchunks\tmissing\tpasteStart\tpasteEnd");
        foreach (var size in new[] { 1402, 5185 })
        {
            foreach (var wrap in new[] { true, false })
            {
                foreach (var raw in new[] { true, false })
                {
                    await using var probe = await NodeStdinProbe.StartAsync(raw: raw, chunkLog: false);
                    var body = NodeStdinProbe.MarkedBodyOfBytes(size);
                    var r = await probe.DeliverAsync(body, wrap: wrap);
                    Line($"wrap={wrap},raw={raw}\t{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.MissingCount}\t{r.HasPasteStart}\t{r.HasPasteEnd}");
                }
            }
        }
        Flush("E4-encoding");
    }

    /// <summary>E5 — a concurrent writer racing the body write.</summary>
    [Test]
    public async Task E5_concurrent_writer()
    {
        SkipIfUnavailable();
        Line("E5 concurrent writer — a second task writes while the body is in flight");
        Line("case\tsent\tgot\tchunks\tmissing");
        foreach (var size in new[] { 1402, 5185 })
        {
            await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);
            var body = NodeStdinProbe.MarkedBodyOfBytes(size);
            var racer = Task.Run(async () =>
            {
                for (var i = 0; i < 40; i++)
                {
                    await probe.WriteRawAsync("\x1b[A"); // a harmless cursor-up, like a keypress
                    await Task.Delay(1);
                }
            });
            var r = await probe.DeliverAsync(body);
            await racer;
            Line($"racer\t{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.MissingCount}");
        }
        Flush("E5-concurrent");
    }

    /// <summary>E6 — a partial line already sitting in the composer before the body lands.</summary>
    [Test]
    public async Task E6_prior_partial_line()
    {
        SkipIfUnavailable();
        Line("E6 prior partial line — unterminated text written before the body");
        Line("prefixBytes\tsent\tgot\tchunks\tmissing");
        foreach (var prefix in new[] { 0, 1, 2, 100, 1022, 1024 })
        {
            await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false);
            if (prefix > 0) await probe.WriteRawAsync(new string('p', prefix));
            await Task.Delay(50);
            var body = NodeStdinProbe.MarkedBodyOfBytes(5185);
            var r = await probe.DeliverAsync(body);
            Line($"{prefix}\t{body.Length}\t{r.Bytes}\t{r.Chunks}\t{r.MissingCount}");
        }
        Flush("E6-prior-partial");
    }
}
