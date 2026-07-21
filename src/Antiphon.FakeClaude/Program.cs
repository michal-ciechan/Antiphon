using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.FakeClaude;

/// <summary>
/// A deterministic stand-in for the Claude Code CLI's <em>terminal contract</em> — NOT a visual TUI
/// emulator. It models only the behaviours our PTY / session-runner stack actually depends on, so we
/// can lock them in fast, CI-friendly tests without launching the real Claude (which is slow,
/// non-deterministic, auth-gated, and can't run in CI). Deliberately tiny: when it grows, it grows by
/// adding a modelled behaviour we have a test for — never by mimicking Claude's pixels.
///
/// Contract modelled:
///  * <b>Submit semantics</b> — a lone CR/LF arriving as its own input <em>burst</em> (a discrete Enter
///    keypress) submits the buffered line; text and a trailing CR arriving in the SAME burst are treated
///    as a paste: the CR becomes a literal newline and the line is NOT submitted. This is the exact
///    distinction that broke <c>SessionMessageQueueService.DeliverAsync</c> (sending <c>text+"\r"</c> in
///    one write left the message in the composer, never submitting it).
///  * <b>Turn-end signal</b> our detectors key on — a <c>" for Ns"</c> token (matching
///    <c>RunnerClaudeAdapter.DonePattern</c>). We also emit the idle OSC title, but ConPTY consumes
///    window-title sequences, so the done pattern is the signal that actually survives to our capture.
///  * <b>Composer echo</b> — typed/pasted text is echoed to the screen like the real composer renders
///    it. Delivery verification (<c>ComposerDeliveryEvidence</c>) reads the rendered screen for the
///    typed body before submitting; a fake that swallowed input silently would make every verified
///    delivery look wedged.
///  * <b>Readiness</b> — print a banner then go quiet, so the quiet-period ready detector settles.
///
/// <para><b>Why timing, not read boundaries.</b> ConPTY does not preserve write boundaries as read
/// boundaries — a single <c>WriteFile("body\r")</c> can surface to the child as one read or several, and
/// separate writes can coalesce. So we re-group incoming bytes into bursts by a quiet gap (no new bytes
/// for <c>ANTIPHON_FAKE_BURST_MS</c>, default 12ms). That is both robust to ConPTY fragmentation and
/// faithful to how the real Claude input handler distinguishes a fast paste from a typed Enter. The
/// runner's <c>SendLineAsync</c> waits 20ms between the body and the CR, comfortably above the gap, so
/// they land as two bursts; a combined <c>"body\r"</c> write lands as one.</para>
///
/// Output markers (<c>SUBMITTED:&lt;line&gt;</c>) are for test assertions — not meant to look like Claude.
/// Tests assert the contract, never the appearance, which is what keeps this from rotting.
/// </summary>
internal static class Program
{
    // OSC 0 ; U+2733 BEL — idle title, like Claude at turn end. (ConPTY usually consumes this; emitted anyway.)
    private const string IdleTitle = "\x1b]0;✳\x07";

    private static int Main(string[] args)
    {
        var banner = GetArg(args, "--banner") ?? "Fake Claude ready";
        var burstGapMs = int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_BURST_MS"), out var g) ? g : 12;
        TryEnableRawConsole();

        var stdout = Console.OpenStandardOutput();
        void Write(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        // Startup banner, then quiet — lets the quiet-period readiness detector settle.
        Write(banner + "\r\n");
        Write(IdleTitle);

        // Background reader: accumulate raw stdin bytes and stamp the arrival time of the latest byte.
        var stdin = Console.OpenStandardInput();
        var gate = new object();
        var pending = new List<byte>();
        var clock = Stopwatch.StartNew();
        long lastByteMs = 0;
        var eof = false;

        var reader = new Thread(() =>
        {
            var buf = new byte[8192];
            while (true)
            {
                int n;
                try { n = stdin.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n <= 0) { lock (gate) eof = true; break; }
                lock (gate)
                {
                    for (var i = 0; i < n; i++) pending.Add(buf[i]);
                    lastByteMs = clock.ElapsedMilliseconds;
                }
            }
        })
        { IsBackground = true, Name = "fakeclaude-stdin" };
        reader.Start();

        var composer = new StringBuilder();

        while (true)
        {
            Thread.Sleep(3);

            byte[]? burst = null;
            lock (gate)
            {
                if (eof && pending.Count == 0) break;
                if (pending.Count > 0 && clock.ElapsedMilliseconds - lastByteMs >= burstGapMs)
                {
                    burst = pending.ToArray();
                    pending.Clear();
                }
            }
            if (burst is null) continue;

            // Ctrl-C (ETX, 3) / Ctrl-D (EOT, 4) — exit cleanly, like a real CLI.
            if (Array.IndexOf(burst, (byte)3) >= 0 || Array.IndexOf(burst, (byte)4) >= 0)
                break;

            var chunk = Encoding.UTF8.GetString(burst);

            // Bracketed paste (\e[200~ ... \e[201~): wrapped content is always literal — a CR inside is a
            // newline, never a submit. Strip the markers and treat the burst as paste text.
            var wasBracketedPaste = chunk.Contains("\x1b[200~") || chunk.Contains("\x1b[201~");
            if (wasBracketedPaste)
                chunk = chunk.Replace("\x1b[200~", string.Empty).Replace("\x1b[201~", string.Empty);

            // A lone-Enter burst (only CR/LF, not part of a paste) submits the buffered line.
            var isLoneEnter = !wasBracketedPaste
                && chunk.Length > 0
                && chunk.All(c => c is '\r' or '\n');

            if (isLoneEnter)
            {
                var text = composer.ToString().Trim();
                composer.Clear();
                if (text.Length == 0) continue; // bare Enter on an empty composer — nothing to submit.
                SubmitTurn(Write, text);
                continue;
            }

            // Text — optionally with a trailing CR if this was a paste. Accumulate into the composer and
            // do NOT submit; the CR collapses to a literal newline. THIS is the paste trap that bit us.
            var composerText = chunk.Replace("\r\n", "\n").Replace('\r', '\n');
            composer.Append(composerText);

            // Composer echo — the real TUI renders typed/pasted text in the composer (raw-mode consoles
            // don't echo, so we must). Delivery verification (ComposerDeliveryEvidence) reads the
            // rendered screen for exactly this; without the echo every verified delivery would look
            // like a wedged terminal. (We can't clear it on submit like the real composer — the fake's
            // screen is append-only — but verification only needs presence, not clearing.)
            Write(composerText.Replace("\n", "\r\n"));
        }

        return 0;
    }

    private static void SubmitTurn(Action<string> write, string text)
    {
        // Deterministic, assertable echo. Slash-commands echo their name so slash routing/dispatch tests
        // can assert behaviour without depending on Claude's real (variable) output.
        write("\r\n");
        write($"SUBMITTED:{text}\r\n");
        write($"FAKE response to: {text}\r\n");
        // Turn-end signals: the " for Ns" done pattern (survives ConPTY) AND the idle title (usually consumed).
        write("Crunched for 1s\r\n");
        write(IdleTitle);
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Put the console into raw VT input mode: keystrokes arrive as unbuffered bytes (no line buffering,
    // no echo, no Ctrl-C processing). Without this the console would line-buffer input and deliver a whole
    // "hello\r\n" line on Enter regardless of how it was written — which would erase the very paste-vs-Enter
    // distinction we exist to model. Best-effort: under ConPTY the inherited mode is usually already close.
    private static void TryEnableRawConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var stdIn = GetStdHandle(STD_INPUT_HANDLE);
            if (GetConsoleMode(stdIn, out var inMode))
            {
                inMode &= ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_PROCESSED_INPUT);
                inMode |= ENABLE_VIRTUAL_TERMINAL_INPUT;
                SetConsoleMode(stdIn, inMode);
            }

            var stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(stdOut, out var outMode))
            {
                outMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                SetConsoleMode(stdOut, outMode);
            }
        }
        catch
        {
            // Best-effort only; the test harness drives us through ConPTY where defaults are workable.
        }
    }

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
