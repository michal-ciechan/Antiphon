using System.Runtime.InteropServices;
using System.Text;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0030's bench. CARD-0027 concluded "bracketed paste is not the discriminator" from an
/// experiment whose two arms may both have been unwrapped — CLAUDE.md records that conhost strips
/// <c>ESC[200~</c>/<c>ESC[201~</c> before a .NET peer sees them. These experiments settle what
/// actually reaches the child, with the raw bytes as evidence rather than a line count.
///
/// <para>The decisive variable is <b>who asked for the mode</b>: a terminal only brackets a paste
/// for a client that has sent <c>ESC[?2004h</c>. The probe never did, so every previous "no markers"
/// reading was taken from a client that was not entitled to them.</para>
///
/// Exploratory and <c>[Explicit]</c> — these print a table, they do not gate CI. What they
/// established is pinned by <see cref="PtyBracketedPasteContractTests"/>.
/// </summary>
[NotInParallel("Headed")]
[Category("Card0030")]
[Explicit]
public class PtyPasteMarkerExperiments
{
    private static readonly StringBuilder Report = new();

    private static void Line(string s)
    {
        Console.WriteLine(s);
        Report.AppendLine(s);
    }

    private static void Flush(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".txt");
        File.WriteAllText(path, Report.ToString());
        Console.WriteLine($"[card-0030] wrote {path}");
        Report.Clear();
    }

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
    /// The question CARD-0027 never asked of the wire: does the child SEE <c>ESC[200~</c>?
    /// Both arms, because the answer is expected to depend on whether the client enabled DECSET
    /// 2004 — and if it does, every "bracketed paste changes nothing" result taken without it is
    /// void.
    /// </summary>
    [Test]
    public async Task Markers_reaching_the_child_with_and_without_decset_2004()
    {
        SkipIfUnavailable();
        Line("arm\tdecset2004\tbytes\tchunks\thas200~\thas201~\theadHex\tmissing");

        foreach (var decset in new[] { false, true })
        {
            await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: decset);
            var body = NodeStdinProbe.MarkedBodyOfBytes(300);
            var r = await probe.DeliverAsync(body);
            Line($"wrapped\t{decset}\t{r.Bytes}\t{r.Chunks}\t{r.HasPasteStart}\t{r.HasPasteEnd}\t"
                 + $"{r.HeadHex}\t{r.MissingCount}");
        }

        // Control: the same body with no markers written at all. If the "wrapped" arms above look
        // identical to this one, the markers are being stripped somewhere between our WriteFile and
        // the child's read.
        await using (var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: true))
        {
            var body = NodeStdinProbe.MarkedBodyOfBytes(300);
            var r = await probe.DeliverAsync(body, wrap: false);
            Line($"unwrapped\ttrue\t{r.Bytes}\t{r.Chunks}\t{r.HasPasteStart}\t{r.HasPasteEnd}\t"
                 + $"{r.HeadHex}\t{r.MissingCount}");
        }

        // And the marker on its own, with nothing else in the write: rules out "the body's first
        // bytes displaced it" as an explanation for a missing prefix.
        await using (var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: true))
        {
            await probe.WriteRawAsync(PtyInputEncoding.PasteStart + "L0000 hello" + PtyInputEncoding.PasteEnd);
            await Task.Delay(50);
            await probe.WriteRawAsync("\nPROBE-REPORT\r");
            var r = await probe.ReadReportAsync(TimeSpan.FromSeconds(20));
            Line($"markers-only\ttrue\t{r.Bytes}\t{r.Chunks}\t{r.HasPasteStart}\t{r.HasPasteEnd}\t"
                 + $"{r.HeadHex}\t{r.MissingCount}");
        }

        Flush("E1-markers");
    }

    /// <summary>
    /// The hidden variable in every previous measurement: <b>which conhost</b> serves the
    /// pseudoconsole. <c>CreatePseudoConsole</c> in kernel32 runs the inbox
    /// <c>System32\conhost.exe</c> — 10.0.19041.1 here, a 2020 build that predates bracketed-paste
    /// support. Windows Terminal ships and drives its own OpenConsole.exe (1.24). If the markers
    /// survive under one and not the other, "the terminal can do it and we cannot" has a cause and
    /// a fix, and it is not a property of ConPTY at all.
    /// </summary>
    [Test]
    public async Task Markers_by_conpty_backend()
    {
        SkipIfUnavailable();
        Line("backend\tdecset2004\tbytes\tchunks\thas200~\thas201~\theadHex\ttailHex");

        foreach (var (name, dll) in Backends())
            foreach (var decset in new[] { false, true })
            {
                try
                {
                    var r = await ConPtyProbe.RunAsync(dll,
                        PtyInputEncoding.EncodeBody(NodeStdinProbe.MarkedBodyOfBytes(300)),
                        decset2004: decset);
                    Line($"{name}\t{decset}\t{r.Bytes}\t{r.Chunks}\t{r.HasPasteStart}\t{r.HasPasteEnd}\t"
                         + $"{r.HeadHex}\t{r.TailHex}");
                }
                catch (Exception ex)
                {
                    Line($"{name}\t{decset}\tERROR\t{ex.Message.Replace('\n', ' ')}");
                }
            }

        Flush("E3-conpty-backend-markers");
    }

    /// <summary>
    /// Sanity: does our own ConPTY host actually attach the child to the pseudoconsole? The child
    /// reports through a FILE, not through stdout — "I saw the text" is not evidence of attachment
    /// when an unattached child writes to the parent's stdout and looks identical.
    /// </summary>
    [Test]
    public async Task Host_selftest()
    {
        SkipIfUnavailable();
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030");
        Directory.CreateDirectory(dir);

        foreach (var (name, dll) in Backends())
            {
                var f = Path.Combine(dir, $"selftest-{Guid.NewGuid():N}.txt").Replace("\\", "/");
                var script = $"require('fs').writeFileSync('{f}', 'stdinTTY='+process.stdin.isTTY+"
                             + "' stdoutTTY='+process.stdout.isTTY+' cols='+process.stdout.columns);"
                             + "console.log('CONPTY-OK');setTimeout(()=>{},3000)";
                await using var host = ConPtyHost.Start(
                    NodeStdinProbe.NodeExe, ["-e", script],
                    AppContext.BaseDirectory, null, 120, 30, dll);
                var ok = await host.WaitForAsync("CONPTY-OK", TimeSpan.FromSeconds(10));
                var said = File.Exists(f) ? File.ReadAllText(f) : "<no file>";
                Line($"{name}	sawOutputOnPty={ok}	child:{said}	{ConPtyHost.Diagnostics}");
            }
        Flush("E0-host-selftest");
    }

    /// <summary>
    /// The card's actual question, with the one variable that had never been varied: real Claude,
    /// the production encoding, one write — under the inbox conhost (markers stripped, so the TUI
    /// sees a burst of keystrokes) and under a modern OpenConsole (markers delivered, so the TUI
    /// sees a PASTE). Three repeats per arm because the loss is timing-dependent and one sample
    /// proves nothing either way.
    /// </summary>
    [Test]
    public async Task Real_claude_by_conpty_backend()
    {
        ClSession.SkipIfNotEligible();
        Line("backend\tsentLines\tsentBytes\trep\tcapturedLines\tkept%\tverdict");

        foreach (var (name, dll) in Backends())
            foreach (var lines in new[] { 200 })
                for (var rep = 0; rep < 3; rep++)
                {
                    var (captured, screen) = await ClaudeTrialAsync(dll, lines);
                    var pct = captured * 100 / lines;
                    var verdict = captured >= lines ? "WHOLE" : captured <= 0 ? "NOTHING" : "LOST";
                    Line($"{name}\t{lines}\t{lines * 27}\t{rep}\t{captured}\t{pct}\t{verdict}");
                    SaveScreen($"claude-{name}-{lines}-rep{rep}", screen);
                }

        Flush("E4-real-claude-by-backend");
    }

    /// <summary>
    /// The controls the headline result needs before it can be believed, and the size sweep that
    /// says how far it goes.
    ///
    /// <para><b>A</b> — the same modern backend with the markers REMOVED. If an unwrapped body still
    /// loses there, the discriminator is bracketed paste; if it survives, the newer conhost is doing
    /// something else as well and the story is incomplete.</para>
    /// <para><b>B</b> — sizes far past anything the ceilings allow. One read chunk is 1 024 bytes;
    /// 43 KB is 43 of them.</para>
    /// <para><b>C</b> — the card's window-size hypothesis, on the losing backend, where it was
    /// supposed to help.</para>
    /// </summary>
    [Test]
    public async Task Real_claude_controls_and_size_sweep()
    {
        ClSession.SkipIfNotEligible();
        var redist = ConPtyHost.FindRedistConPty();
        if (redist is null) throw new SkipTestException("no redistributable conpty.dll on this machine");

        Line("case\tbackend\tsentLines\tsentBytes\trep\tcaptured\tkept%\tverdict");

        for (var rep = 0; rep < 3; rep++)
            await RunAsync("A unwrapped", redist, 200, rep, wrap: false);

        foreach (var lines in new[] { 200, 600, 1600 })
            for (var rep = 0; rep < 2; rep++)
                await RunAsync($"B size={lines * 27}", redist, lines, rep, wrap: true);

        foreach (var (cols, rows) in new[] { ((short)200, (short)50), ((short)400, (short)100) })
            for (var rep = 0; rep < 2; rep++)
                await RunAsync($"C window={cols}x{rows}", null, 200, rep, wrap: true, cols: cols, rows: rows);

        Flush("E5-controls-and-sizes");

        async Task RunAsync(string label, string? dll, int lines, int rep, bool wrap,
            short cols = 120, short rows = 250)
        {
            var (captured, screen) = await ClaudeTrialAsync(dll, lines, wrap, cols, rows);
            var pct = captured * 100 / lines;
            var verdict = captured >= lines ? "WHOLE" : captured <= 0 ? "NOTHING" : "LOST";
            Line($"{label}\t{(dll is null ? "inbox" : "OpenConsole")}\t{lines}\t{lines * 27}\t{rep}\t"
                 + $"{captured}\t{pct}\t{verdict}");
            SaveScreen($"{label}-rep{rep}", screen);
        }
    }

    /// <summary>
    /// Where the modern backend stops delivering, and whether pacing INSIDE one bracketed paste
    /// gets past it. 5 400 bytes arrives whole; 16 200 arrived as nothing at all, which is a wall,
    /// not the old partial loss — so the envelope needs measuring rather than assuming.
    /// </summary>
    [Test]
    public async Task Real_claude_delivery_envelope()
    {
        ClSession.SkipIfNotEligible();
        var redist = ConPtyHost.FindRedistConPty();
        if (redist is null) throw new SkipTestException("no redistributable conpty.dll on this machine");

        Line("case\tsentLines\tsentBytes\tchunk\tgapMs\tcaptured\tkept%\tverdict");

        foreach (var lines in new[] { 600, 1600, 3200 })
            for (var rep = 0; rep < 2; rep++)
                await RunAsync("single-write", lines, 0, 0);

        // Same bodies, but the payload is handed over in pieces with a gap. The markers still
        // bracket the whole thing — only the WRITE is split.
        foreach (var lines in new[] { 1600, 3200 })
            await RunAsync("paced", lines, 1024, 25);

        Flush("E6-envelope");

        async Task RunAsync(string label, int lines, int chunkBytes, int gapMs)
        {
            var (captured, screen) = await ClaudeTrialAsync(redist, lines, chunkBytes: chunkBytes, gapMs: gapMs);
            var pct = captured * 100 / lines;
            var verdict = captured >= lines ? "WHOLE" : captured <= 0 ? "NOTHING" : "LOST";
            Line($"{label}\t{lines}\t{lines * 27}\t{chunkBytes}\t{gapMs}\t{captured}\t{pct}\t{verdict}");
            SaveScreen($"{label}-{lines * 27}-c{chunkBytes}-g{gapMs}", screen);
        }
    }

    /// <summary>One trial = one fresh Claude process (a reused session makes the matrix alternate
    /// with the trial index — residue, not physics). Costs no model turns: the body is pasted into
    /// the composer and never submitted, and the composer's own counter is the oracle.</summary>
    private static async Task<(int Captured, string Screen)> ClaudeTrialAsync(
        string? conptyDll, int lines, bool wrap = true, short cols = 120, short rows = 250,
        int chunkBytes = 0, int gapMs = 0)
    {
        var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
        await using var host = ConPtyHost.Start(app, args, AppContext.BaseDirectory,
            ClSession.HeadedSafeEnv(), cols: cols, rows: rows, conptyDllPath: conptyDll);

        // Readiness must be POSITIVE evidence, not a quiet period: a slow launch is silent, and a
        // quiet-only gate fires before the TUI has attached to stdin — the body then vanishes into
        // a console buffer nobody is reading and every arm reads NOTHING for the wrong reason.
        var ready = await host.WaitForScreenAsync(
            t => t.Contains("for shortcuts") || t.Contains("bypass permissions on") || t.Contains("Try \""),
            TimeSpan.FromSeconds(90));
        if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state: " + host.ScreenText);
        await host.WaitForQuietAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        await Task.Delay(4000); // the backend socket, as ClaudeReadyDetector's MinTotalWait does

        // Plumbing control FIRST: a marker far under one read chunk must appear in the composer.
        // Without it, "NOTHING captured" is ambiguous between the defect under test and a host that
        // is not delivering input at all — which is exactly the wrong answer this bench produced
        // twice before the control existed.
        host.Write("CTRLOK");
        if (!await host.WaitForScreenAsync(t => t.Contains("CTRLOK"), TimeSpan.FromSeconds(20)))
            throw new InvalidOperationException(
                "input never reached the TUI at all — plumbing, not loss:\n" + host.ScreenText);
        host.Write(new string('', 6)); // backspace it away so it cannot be counted as body
        await Task.Delay(500);

        var body = string.Concat(Enumerable.Range(0, lines).Select(i => $"P{i:D5}-{new string('x', 20)}\n"));
        var payload = wrap ? PtyInputEncoding.EncodeBody(body) : PtyInputEncoding.NormalizeBody(body);
        if (chunkBytes <= 0)
        {
            host.Write(payload);
        }
        else
        {
            for (var i = 0; i < payload.Length; i += chunkBytes)
            {
                host.Write(payload.Substring(i, Math.Min(chunkBytes, payload.Length - i)));
                if (gapMs > 0) await Task.Delay(gapMs);
            }
        }
        // Wait for the composer to SHOW something before timing out on quiet: a big paste renders
        // in bursts, and a fixed quiet window snapshots an empty screen and calls it total loss
        // (it did — 16 200 bytes read NOTHING twice, then WHOLE once the wait was long enough).
        await host.WaitForScreenAsync(t => t.Contains("Pasted text") || t.Contains("P00000-"),
            TimeSpan.FromSeconds(60));
        await host.WaitForQuietAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        var screen = host.ScreenText;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030", "screens",
            $"raw-{Guid.NewGuid():N}.txt"), host.OutputText);
        return (CapturedLines(screen), screen);
    }

    /// <summary>What the composer actually holds: its own placeholder count when it collapsed the
    /// paste, else the distinct markers on screen.</summary>
    private static int CapturedLines(string screen)
    {
        var total = 0;
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(screen, @"\[Pasted text #\d+ \+(\d+) lines\]"))
            total += int.Parse(m.Groups[1].Value) + 1;
        var literal = System.Text.RegularExpressions.Regex.Matches(screen, @"P(\d{5})-")
            .Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
        return total > 0 ? total : literal.Count;
    }

    private static void SaveScreen(string label, string screen)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030", "screens");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        File.WriteAllText(Path.Combine(dir, safe + ".txt"), screen);
    }

    private static string Visible(string s) => s
        .Replace(((char)27).ToString(), "<ESC>").Replace(((char)13).ToString(), "<CR>").Replace(((char)10).ToString(), "<LF>");

    /// <summary>Every pseudoconsole implementation available on this machine, most likely first.</summary>
    private static IEnumerable<(string Name, string? Dll)> Backends()
    {
        yield return ("kernel32/conhost-19041", null);

        var redist = ConPtyHost.FindRedistConPty();
        if (redist is not null)
        {
            yield return ("redist-conpty.dll+its-own-OpenConsole", redist);

            var wtOpenConsole = ConPtyHost.FindWindowsTerminalOpenConsole();
            if (wtOpenConsole is not null)
            {
                var staged = Path.Combine(AppContext.BaseDirectory, "TestOutput", "card-0030", "conpty-wt");
                string? dll = null;
                try { dll = ConPtyHost.StageConPty(redist, wtOpenConsole, staged); }
                catch (Exception ex) { Console.WriteLine("[card-0030] could not stage WT OpenConsole: " + ex.Message); }
                if (dll is not null) yield return ("redist-conpty.dll+WindowsTerminal-OpenConsole-1.24", dll);
            }
        }
    }

    /// <summary>
    /// What shape does OUR write have on the wire, and does the window size change it? The card's
    /// hypothesis 2 says a wider window re-renders more cheaply; that only matters if the input
    /// side is unchanged, so measure the input side across window sizes first.
    /// </summary>
    [Test]
    public async Task Write_shape_across_window_sizes()
    {
        SkipIfUnavailable();
        Line("cols x rows\tbytes\tchunks\tturns\tmaxPerTurn\tspanMs\tdistinctSizes\tmissing");

        foreach (var (cols, rows) in new[] { (120, 30), (200, 50), (400, 100) })
        {
            await using var probe = await NodeStdinProbe.StartAsync(
                chunkLog: false, cols: cols, rows: rows, decset2004: true);
            var body = NodeStdinProbe.MarkedBodyOfBytes(5185);
            var r = await probe.DeliverAsync(body);
            Line($"{cols}x{rows}\t{r.Bytes}\t{r.Chunks}\t{r.Turns}\t{r.MaxChunksPerTurn}\t{r.SpanMs}\t"
                 + $"[{string.Join(",", r.DistinctSizes)}]\t{r.MissingCount}");
        }

        Flush("E2-window-size-write-shape");
    }
}
