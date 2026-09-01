using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
///  * <b>Compaction</b> — a submitted <c>/compact</c>, with or without arguments (or
///    <c>ANTIPHON_FAKE_COMPACT_AFTER_TURNS=N</c> after the Nth turn) renders the pinned
///    <c>Compacted (ctrl+o to see full summary)</c> screen line with NO turn-end signal — compaction is
///    not a turn. With <c>ANTIPHON_FAKE_TRANSCRIPT_PATH</c> set, the FULL measured record set is
///    appended (CARD-0041): manual = raw typed prompt, <c>trigger:"manual"</c> boundary,
///    <c>isCompactSummary</c> continuation, <c>isMeta</c> caveat, <c>&lt;command-name&gt;</c> +
///    <c>&lt;local-command-stdout&gt;</c>; auto = <c>trigger:"auto"</c> boundary + continuation only
///    (nothing was typed). Always on — this is transcript-shape modelling, not input-loss behaviour.
///  * <b>Chunk clipping</b> (OPT-IN, <c>ANTIPHON_FAKE_STDIN_CLIP</c>) — the real TUI keeps ONE
///    ~1024-byte read chunk per event-loop turn and silently discards the rest, which is how briefs
///    arrived with their heads missing (CARD-0027). Default OFF: our transport is genuinely
///    lossless and <c>PtyLargeWriteTests</c> pins that. See <see cref="StdinClipModel"/>.
///    <b>Clipping is a property of TYPED input only</b> (CARD-0030/0037): content inside a
///    bracketed paste is exempt, because the composer accumulates from <c>ESC[200~</c> to
///    <c>ESC[201~</c> without the per-turn discard, and real Claude took 86 400 bytes that way in
///    ONE write with zero loss while the same body unwrapped on the same binary still clipped.
///  * <b>Paste placeholder</b> (OPT-IN, <c>ANTIPHON_FAKE_PASTE_PLACEHOLDER</c>) — a real paste of
///    any size collapses in the composer to <c>[Pasted text #N +M lines]</c> and the body is not
///    rendered at all, which is what delivery verification has to survive
///    (<c>ComposerDeliveryEvidence</c>).
///  * <b>Swallowed Enter</b> (OPT-IN, <c>ANTIPHON_FAKE_SWALLOW_ENTER=n</c>) — the first <em>n</em>
///    submitting CRs are eaten while the screen still redraws and the composer KEEPS the body: the
///    measured state that marked a delivery Sent on a redraw and left it unsubmitted for 104 minutes
///    (CARD-0055). Default OFF. See <see cref="SwallowEnterModel"/>.
///  * <b>JSONL transcript</b> (opt-in, <c>ANTIPHON_FAKE_TRANSCRIPT_PATH</c>) — <c>user</c> line on
///    submit, <c>assistant</c> (+<c>stop_reason:"end_turn"</c>, +<c>message.id</c>) line on turn end,
///    in the shapes <c>TranscriptNormalizer</c> parses, so tailer/normalizer tests can run file-driven.
///  * <b>API-error stub</b> (OPT-IN, <c>ANTIPHON_FAKE_API_ERROR=rate_limit|server_error|authentication_failed</c>)
///    — a turn killed by the API itself (usage limit, 529, auth-expired) is written to the JSONL as
///    ONE synthetic assistant record: <c>model:"&lt;synthetic&gt;"</c>, top-level <c>error</c> +
///    <c>isApiErrorMessage:true</c> (+ numeric <c>apiErrorStatus</c> when the real class carries
///    one), <c>stop_reason:"stop_sequence"</c>, and the error string as an ordinary text block —
///    so the stub carries its OWN TurnEnd and the session reads idle (CARD-0072; shapes verbatim
///    from the measured records). <c>ANTIPHON_FAKE_API_ERROR_AFTER_TURNS=N</c> (default 1) kills
///    the Nth submitted turn ONLY; later turns respond normally, modelling the measured revival —
///    a typed "Continue" into exactly this state worked immediately, six times across the sweep.
///  * <b>Kill line</b> (Ctrl+U, <c>0x15</c>) — empties the composer and erases the rendered row, the
///    keystroke <c>ClaudeHarness</c> has used against real Claude to clear a composer before a
///    re-type. Always on: it is an input primitive, not a failure mode. Modelled for a SINGLE typed
///    line only, which is what has been measured — what empties a composer holding a multi-line body
///    or a collapsed <c>[Pasted text #N]</c> placeholder is unmeasured and deliberately not guessed
///    at here (CARD-0103 slice 3 is gated on measuring it).
///  * <b>Deaf start</b> (OPT-IN, <c>ANTIPHON_FAKE_DEAF_START_MS=N</c>) — paint the banner, then do
///    not READ stdin for N ms. Input written meanwhile stays buffered in the pty and is processed in
///    order on wake, which is the measured shape (CARD-0103: the deaf TUI processed all three
///    buffered pastes when it woke, in order, minutes late). This is the state every output-side
///    readiness signal calls "ready": no output, no modal, a painted composer — and every byte
///    written into it looks lost until it isn't. Default OFF.
///  * <b>Remote-control menu</b> (OPT-IN, <c>ANTIPHON_FAKE_RC_MENU=1</c>) — a submitted
///    <c>/remote-control</c> renders the MANAGEMENT MENU shape (Disconnect / Show QR / Continue,
///    "Esc to continue") with no <c>remote-control is active</c> line, the modal the real TUI
///    opens when the bridge is already live (CARD-0292). While it stands, submitted input is
///    accepted into the TUI's own queue — a <c>queue-operation</c> <c>enqueue</c> JSONL record and
///    NO <c>user</c> record; a bare Esc clears the menu and drains the queue (enqueue → dequeue →
///    user). Distinct from Overlay: the menu QUEUES submits, an overlay DISCARDS bytes.
///  * <b>Overlay</b> (OPT-IN, <c>ANTIPHON_FAKE_OVERLAY_ON_COMMAND=/usage</c>) — after this command
///    is submitted, render a panel and DISCARD every typed byte until a bare Esc restores the
///    composer. Default OFF. Deaf-start <em>buffers</em> input and processes it late; an overlay
///    <em>consumes and discards</em> it (CARD-0137). The panel chrome includes the measured Grok
///    fragment <c>c copy session ID</c> so S6's detector can match.
///  * <b>Split final response</b> (OPT-IN, <c>ANTIPHON_FAKE_SPLIT_FINAL</c>) — real Claude writes one
///    API response as SEVERAL records: a signature-only <c>thinking</c> record, then the <c>text</c>
///    record, both stamped with the response's <c>stop_reason</c> and sharing one <c>message.id</c>.
///    The thinking record therefore reaches consumers as a BARE <c>TurnEnd</c>, which is what
///    settled six delegated tasks on their own preamble (CARD-0046). Default OFF: the one-record
///    shape is also real (measured 40% of fable's <c>end_turn</c> responses, 78% of opus's), and it
///    is what every existing transcript test counts.
///
/// <para><b>Why timing, not read boundaries.</b> ConPTY does not preserve write boundaries as read
/// boundaries — a single <c>WriteFile("body\r")</c> can surface to the child as one read or several, and
/// separate writes can coalesce. So we re-group incoming bytes into bursts by a quiet gap (no new bytes
/// for <c>ANTIPHON_FAKE_BURST_MS</c>, default 12ms). That is both robust to ConPTY fragmentation and
/// faithful to how the real Claude input handler distinguishes a fast paste from a typed Enter. The
/// runner's <c>SendLineAsync</c> now waits for evidence that the composer's tail consumed the body,
/// then waits 20ms before the CR. The gate prevents ConPTY delivery lag from compressing a writer-side
/// gap below this burst window; a combined <c>"body\r"</c> write still lands as one.</para>
///
/// Output markers (<c>SUBMITTED:&lt;line&gt;</c>) are for test assertions — not meant to look like Claude.
/// Tests assert the contract, never the appearance, which is what keeps this from rotting.
/// </summary>
internal static class Program
{
    // OSC 0 ; U+2733 BEL — idle title, like Claude at turn end. (ConPTY usually consumes this; emitted anyway.)
    private const string IdleTitle = "\x1b]0;✳\x07";

    /// <summary>Bracketed-paste opener, matched on the RAW bytes: the path decision precedes the decode.</summary>
    private static readonly byte[] PasteStartBytes = Encoding.ASCII.GetBytes("\x1b[200~");

    // PINNED-BY: ClaudeCompactionCanaryTests — the screen line real Claude renders after a compaction.
    private const string CompactedScreenLine = "Compacted (ctrl+o to see full summary)";

    private static int Main(string[] args)
    {
        // Unknown flags (including CARD-0306's --settings <path>) are ignored; they are not a prompt.
        var banner = GetArg(args, "--banner") ?? "Fake Claude ready";
        var debugInput = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_DEBUG_INPUT") == "1";
        var burstGapMs = int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_BURST_MS"), out var g) ? g : 12;
        var compactAfterTurns = int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_COMPACT_AFTER_TURNS"), out var cat) ? cat : 0;
        var transcriptPath = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_TRANSCRIPT_PATH");
        // OPT-IN (CARD-0046): write the turn-ending response as the TWO records real Claude writes —
        // a signature-only thinking record, then the text record, sharing one message.id. Default
        // OFF, like the clip model: the one-record shape is also real (f2bf457c settled correctly
        // from it), and every existing transcript test counts lines.
        var splitFinal = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_SPLIT_FINAL") == "1";
        // OPT-IN: models the real TUI dropping all but one read chunk per event-loop turn. Unset =
        // null = the fake stays the lossless peer PtyLargeWriteTests pins. See StdinClipModel.
        var clip = StdinClipModel.FromEnvironment();
        // OPT-IN: collapse a bracketed paste to "[Pasted text #N +M lines]" instead of echoing it,
        // the way the real composer does. Default OFF for two reasons — the exact line count at
        // which real Claude collapses has not been MEASURED (defaulting it on would be modelling a
        // guess), and every existing test that reads the fake's composer echo for the body it sent
        // is a pin worth keeping. Tests that need the paste-path rendering ask for it.
        var placeholder = PastePlaceholderModel.FromEnvironment();
        // OPT-IN (CARD-0055): eat the first n SUBMITTING Enters while still redrawing — the measured
        // state in which a delivery was marked Sent on a redraw and the body sat in the composer for
        // 104 minutes. Default OFF: our Enters are not eaten, and every other test submits on the first.
        var swallow = SwallowEnterModel.FromEnvironment();
        // OPT-IN (CARD-0103): go input-DEAF for N ms after painting. The banner is already out by
        // the time the reader starts, so every output-side ready signal reads this as a healthy,
        // settled session — which is exactly the lie the input probe exists to catch.
        var overlayOnCommand = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_OVERLAY_ON_COMMAND");
        if (string.IsNullOrWhiteSpace(overlayOnCommand)) overlayOnCommand = null;
        // OPT-IN (CARD-0292): /remote-control opens the MANAGEMENT MENU — what the real TUI does
        // when the command lands on a session whose bridge is already live. No "remote-control is
        // active" line. While the menu stands, submitted input is ACCEPTED into the TUI's own
        // queue (a queue-operation enqueue JSONL record, no user record — unlike an overlay,
        // which discards); Esc clears the menu and drains the queue (enqueue → dequeue → user).
        // Mirrors the measured incident shapes from session 70eb4c2d.
        var rcMenuEnabled = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_RC_MENU") == "1";
        var deafStartMs = int.TryParse(
            Environment.GetEnvironmentVariable("ANTIPHON_FAKE_DEAF_START_MS"), out var ds) && ds > 0 ? ds : 0;
        TryEnableRawConsole();

        var stdout = Console.OpenStandardOutput();
        void Write(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        // CARD-0050: stamp process start into the timing sidecar so a captured flake can measure
        // serializer/file-cache warm-up (first record's stamp minus this one) on a real failure.
        if (!string.IsNullOrEmpty(transcriptPath))
            AppendTiming(transcriptPath, 0, "\"process-start\"", gaveUp: false);

        // Startup banner, then quiet — lets the quiet-period readiness detector settle.
        Write(banner + "\r\n");
        // Printed only when clipping is on, and it carries the SEED: a non-deterministic failure is
        // only useful if it can be replayed.
        if (clip is not null) Write(clip.Describe() + "\r\n");
        if (placeholder is not null) Write(placeholder.Describe() + "\r\n");
        if (swallow is not null) Write(swallow.Describe() + "\r\n");
        if (overlayOnCommand is not null) Write($"OVERLAY:command={overlayOnCommand}\r\n");
        if (rcMenuEnabled) Write("RCMENU:enabled\r\n");
        if (deafStartMs > 0) Write($"DEAFSTART:ms={deafStartMs}\r\n");
        if (debugInput)
        {
            var h = GetStdHandle(STD_INPUT_HANDLE);
            Write(GetConsoleMode(h, out var m)
                ? $"INMODE:0x{m:X} vt={(m & ENABLE_VIRTUAL_TERMINAL_INPUT) != 0} cp={GetConsoleCP()}\r\n"
                : "INMODE:unavailable\r\n");
        }
        // --echo-args: print the argv verbatim (newline-escaped) so tests can assert that args —
        // notably a multi-line --append-system-prompt value — survived process-spawn quoting intact.
        if (Array.IndexOf(args, "--echo-args") >= 0)
            Write("ARGS:" + string.Join("␟", args).Replace("\r", "\\r").Replace("\n", "\\n") + "\r\n");
        // --echo-argv-strict: the SAME command line, re-parsed the way a NATIVE child's CRT parses
        // it (CARD-0101 / test-coverage plan P0-2). Kept BESIDE --echo-args, never replacing it:
        // the divergence between the two lines on a hostile input is what pins why this exists.
        if (Array.IndexOf(args, "--echo-argv-strict") >= 0)
            foreach (var strictLine in StrictArgvLines())
                Write(strictLine + "\r\n");
        Write(IdleTitle);

        // Background reader: accumulate raw stdin CHUNKS, each stamped with its arrival time. The
        // per-chunk stamps matter: if the main loop stalls (first-call JIT/serializer warm-up in
        // SubmitTurn), two keystrokes that arrived 20ms apart would otherwise merge into one burst
        // and read as a paste — the reader thread's timestamps preserve the true gaps regardless
        // of how late the drain runs.
        var gate = new object();
        var pending = new List<(long AtMs, byte[] Bytes)>();
        var clock = Stopwatch.StartNew();
        long lastByteMs = 0;
        var eof = false;

        // A REAL TUI does not sit in a tight read loop: it renders between reads, so it drains the
        // pty input pipe in bursts with gaps. That drain rate is what decides whether a large
        // written body survives — ANTIPHON_FAKE_STDIN_READ_DELAY_MS models a busy renderer so tests
        // can reproduce the 2026-08-10 mid-body loss deterministically. Default 0 = tight loop.
        var readDelayMs = int.TryParse(
            Environment.GetEnvironmentVariable("ANTIPHON_FAKE_STDIN_READ_DELAY_MS"), out var rd) ? rd : 0;

        var stdin = Console.OpenStandardInput();
        var reader = new Thread(() =>
        {
            // The deaf window is on the READ side, not the write side: bytes written during it stay
            // in the pty's input buffer and are drained here, in order, when the sleep ends. That is
            // the measured behaviour — input into a deaf TUI is LATE, never lost — and it is what
            // makes both the probe (wait longer than the dead zone) and an ordered clear keystroke
            // sound.
            if (deafStartMs > 0) Thread.Sleep(deafStartMs);
            var buf = new byte[8192];
            while (true)
            {
                int n;
                if (readDelayMs > 0) Thread.Sleep(readDelayMs);
                try { n = stdin.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n <= 0) { lock (gate) eof = true; break; }
                lock (gate)
                {
                    lastByteMs = clock.ElapsedMilliseconds;
                    pending.Add((lastByteMs, buf[..n]));
                }
            }
        })
        { IsBackground = true, Name = "fakeclaude-stdin" };
        reader.Start();

        var composer = new StringBuilder();
        var overlayActive = false;
        var rcMenuActive = false;
        var rcQueue = new List<string>();
        var turnCount = 0;
        // Inside a bracketed paste whose closing 201~ hasn't arrived yet (paste split across reads).
        var inPaste = false;
        // What the current paste has accumulated, when the placeholder model is rendering it: the
        // composer shows ONE placeholder for the whole paste, not one per read.
        var pasteBuffer = new StringBuilder();

        while (true)
        {
            Thread.Sleep(3);

            List<(long AtMs, byte[] Bytes)>? drained = null;
            lock (gate)
            {
                if (eof && pending.Count == 0) break;
                if (pending.Count > 0 && clock.ElapsedMilliseconds - lastByteMs >= burstGapMs)
                {
                    drained = new List<(long, byte[])>(pending);
                    pending.Clear();
                }
            }
            if (drained is null) continue;

            // Re-split the drained chunks into bursts by their ARRIVAL-time gaps (a late drain
            // must not glue a typed Enter onto the body it follows).
            var bursts = new List<byte[]>();
            var current = new List<byte>();
            for (var c = 0; c < drained.Count; c++)
            {
                if (c > 0 && drained[c].AtMs - drained[c - 1].AtMs >= burstGapMs && current.Count > 0)
                {
                    bursts.Add(current.ToArray());
                    current.Clear();
                }
                current.AddRange(drained[c].Bytes);
            }
            if (current.Count > 0)
                bursts.Add(current.ToArray());

            // The burst grouping is load-bearing for BOTH submit semantics and the clip model (one
            // burst = one event-loop turn), and ConPTY's read cadence is the thing that decides it.
            // Printing the per-read arrival stamps makes that cadence observable instead of a
            // guess — it is how the clip tests' burst-gap sizing was chosen.
            if (debugInput)
                Write($"READS:{drained.Count} bursts={bursts.Count} stamps=["
                    + string.Join(",", drained.Select(d => $"{d.AtMs}:{d.Bytes.Length}")) + "]\r\n");

            foreach (var burst in bursts)
            {
                if (!ProcessBurst(burst))
                    return 0;
            }
        }

        return 0;

        // One burst = one logical input event. Returns false on Ctrl-C/Ctrl-D (exit).
        bool ProcessBurst(byte[] burst)
        {
            // Ctrl-C (ETX, 3) / Ctrl-D (EOT, 4) — exit cleanly, like a real CLI.
            if (Array.IndexOf(burst, (byte)3) >= 0 || Array.IndexOf(burst, (byte)4) >= 0)
                return false;

            // CARD-0292: a bare Esc closes the standing /remote-control management menu ("Esc to
            // continue" — the key the menu itself documents) and DRAINS the queue it swallowed:
            // per queued body, a queue-operation dequeue record then a real user record, the
            // measured enqueue → dequeue → user shape. Typed input while the menu stands is
            // handled below, in the lone-Enter branch — the menu queues submits, it does not
            // discard bytes the way an overlay does.
            if (rcMenuActive && burst.Length == 1 && burst[0] == 0x1b)
            {
                rcMenuActive = false;
                Write("RCMENU:closed\r\n");
                DrainRcMenuQueue();
                return true;
            }

            // CARD-0137: an open overlay consumes and discards every typed byte. Bare Esc (a
            // single 0x1b, not a CSI) restores the composer. Deaf-start BUFFERS; overlay DROPS.
            if (overlayActive)
            {
                if (burst.Length == 1 && burst[0] == 0x1b)
                {
                    overlayActive = false;
                    Write("OVERLAY:closed\r\n");
                    return true;
                }
                Write("OVERLAY:drop\r\n");
                return true;
            }

            // Ctrl+U (NAK, 0x15) — kill line. Empties the composer AND erases the rendered row, which
            // is the half that matters to a caller: the verification everything here exists to serve
            // reads the RENDERED SCREEN, so a model that dropped the buffer while leaving the text
            // painted would report a composer that will not clear. Not applied inside a bracketed
            // paste — wrapped content is literal, control bytes included.
            if (!inPaste && !Contains(burst, PasteStartBytes) && Array.IndexOf(burst, (byte)0x15) >= 0)
            {
                composer.Clear();
                // CR home + erase the whole row. Single-line only, matching what has been measured
                // against real Claude; see the class doc.
                Write("\r\x1b[2K");
                return true;
            }

            // WHICH INPUT PATH this burst is on, decided before the clip model sees a byte.
            //
            // Clipping is what happens to TYPING (CARD-0027/0028): the composer keeps one read
            // chunk per event-loop turn and drops the rest. A bracketed paste is a DIFFERENT code
            // path — the composer accumulates from ESC[200~ until ESC[201~ and the per-turn discard
            // never applies to it — and the difference is measured, not assumed: through a modern
            // pseudoconsole real Claude took 86 400 bytes in one bracketed write with zero loss
            // (2/2), while the identical body unwrapped on the SAME binary still lost 25%
            // (CARD-0030). Our writes only ever took the typing path because the inbox conhost ate
            // the markers before the TUI could see them; with those markers delivered, a fake that
            // clipped anyway would assert a behaviour production no longer has — which is the exact
            // drift CARD-0028 exists to prevent, in the other direction.
            //
            // Paste MODE, not "this burst has a marker": ConPTY splits one bracketed write across
            // several reads and the continuation bursts carry no markers at all, so the exemption
            // has to persist from 200~ to 201~ the way the real TUI's does.
            var burstIsPaste = inPaste || Contains(burst, PasteStartBytes);

            // The burst IS the event-loop turn: bytes that arrived without a quiet gap between
            // them. Clipping (opt-in) keeps one read chunk of it and discards the rest, in UTF-8
            // BYTES — before the string decode, because the read quantum the real TUI drops is
            // measured in bytes and a char-based cut would disagree on any multibyte body.
            if (clip is not null && !burstIsPaste)
            {
                burst = clip.Apply(burst, out var clipNote);
                if (clipNote is not null) Write(clipNote + "\r\n");
            }

            var chunk = Encoding.UTF8.GetString(burst);
            if (Environment.GetEnvironmentVariable("ANTIPHON_FAKE_DEBUG_INPUT") == "1")
                Write("RAWBURST:" + string.Concat(chunk.Select(c => c < 32 ? $"<{(int)c}>" : c.ToString())) + "\r\n");

            // Bracketed paste (\e[200~ ... \e[201~): wrapped content is always literal — a CR inside is a
            // newline, never a submit. Paste MODE is tracked ACROSS bursts: ConPTY can split one
            // bracketed write over several reads, and the continuation chunks carry no markers — a
            // real TUI stays in paste mode from 200~ until 201~ regardless of chunking. Deciding
            // per-burst let a split bracketed paste fall into the unbracketed fragmentation hazard
            // below (observed 2026-07-31: the pinned bracket contract test fragmented under load).
            var pasteStart = chunk.Contains("\x1b[200~");
            var pasteEnd = chunk.Contains("\x1b[201~");
            var wasBracketedPaste = pasteStart || pasteEnd || inPaste;
            if (pasteStart) inPaste = true;
            if (pasteEnd) inPaste = false;
            if (pasteStart || pasteEnd)
                chunk = chunk.Replace("\x1b[200~", string.Empty).Replace("\x1b[201~", string.Empty);

            // A lone-Enter burst (only CR/LF, not part of a paste) submits the buffered line.
            var isLoneEnter = !wasBracketedPaste
                && chunk.Length > 0
                && chunk.All(c => c is '\r' or '\n');

            if (isLoneEnter)
            {
                var text = composer.ToString().Trim();
                if (text.Length == 0)
                {
                    // Bare Enter on an empty composer — nothing to submit, nothing recorded, no turn.
                    // This is the contract the delivery retry leans its whole weight on (CARD-0055):
                    // if the first Enter DID submit, every re-press lands here.
                    composer.Clear();
                    return true;
                }

                // CARD-0055's modelled failure: the CR is eaten, the screen still advances, and the
                // body STAYS in the composer — so the next Enter submits THIS body, which is what
                // makes an Enter-only retry (never a re-type) the safe fix. See SwallowEnterModel.
                if (swallow is not null && swallow.ShouldSwallow())
                {
                    Write($"SWALLOWED-ENTER:remaining={swallow.Remaining} held={text.Length}\r\n");
                    return true;
                }

                composer.Clear();

                // CARD-0292: input submitted while the management menu stands is ACCEPTED into
                // the TUI's own queue — an enqueue JSONL record and NO user record. Every
                // delivery layer sees success (the pty took the write, the transcript grew);
                // that silence is what the swallowed-input watchdog exists to see.
                if (rcMenuActive)
                {
                    rcQueue.Add(text);
                    if (transcriptPath is not null)
                        AppendTranscript(transcriptPath, JsonQueueOperationLine("enqueue", text));
                    Write($"RCMENU:enqueued={rcQueue.Count}\r\n");
                    return true;
                }

                if (rcMenuEnabled && text == "/remote-control")
                {
                    // The menu shape from the incident's runner snapshot: heading, action rows,
                    // footer — and deliberately NO "remote-control is active" line and no turn-end
                    // signal. The modal blocks until Enter or Esc; nobody is at a keyboard.
                    rcMenuActive = true;
                    Write("\r\n");
                    Write($"SUBMITTED:{text}\r\n");
                    Write("RCMENU:open\r\n");
                    Write("  Remote Control\r\n");
                    Write("\r\n");
                    Write("  This session is available in the Claude mobile app and at\r\n");
                    Write("  https://claude.ai/code/session_FAKE0000000000000000000000.\r\n");
                    Write("\r\n");
                    Write("    Disconnect this session\r\n");
                    Write("    Show QR code  Scan with your phone to open this session\r\n");
                    Write("  > Continue\r\n");
                    Write("\r\n");
                    Write("  Enter to select . Esc to continue\r\n");
                    return true;
                }

                if (overlayOnCommand is not null
                    && text.StartsWith(overlayOnCommand, StringComparison.OrdinalIgnoreCase))
                {
                    overlayActive = true;
                    Write("OVERLAY:open\r\n");
                    Write("Weekly limit (SuperGrok)\r\n");
                    Write("c copy session ID  |  Esc close\r\n");
                    return true;
                }

                if (IsCompactCommand(text))
                {
                    // Compaction is NOT a turn: no response echo, no " for Ns" done pattern.
                    Write("\r\n");
                    Write($"SUBMITTED:{text}\r\n");
                    EmitManualCompaction(Write, transcriptPath, text);
                    return true;
                }

                SubmitTurn(Write, text, transcriptPath, splitFinal);
                turnCount++;
                if (compactAfterTurns > 0 && turnCount == compactAfterTurns)
                    EmitAutoCompaction(Write, transcriptPath); // spontaneous (auto) compaction after the Nth turn
                return true;
            }

            // Input model MEASURED against real Claude via ConPTY probe runs (2026-07-31):
            //  * \n is ALWAYS a literal newline in the composer — multi-line LF bodies land intact
            //    regardless of size, chunk gaps, or bracket markers (current conhost builds strip
            //    the \e[200~/\e[201~ markers from written input before the client reads them, and
            //    real Claude keeps LF pastes intact anyway).
            //  * A \r MID-burst acts as Enter and SUBMITS the fragment before it — CR/CRLF line
            //    endings are the real fragmentation hazard (the 2026-07-29 live-miss shape; the
            //    server's DeliverAsync normalizes endings to LF for exactly this reason).
            //  * A single TRAILING \r at burst end is paste tail: it collapses to a literal
            //    newline and does NOT submit (the queued-message trap — text+CR in one write).
            if (wasBracketedPaste)
            {
                // Wrapped content is always literal, CRs included.
                var pasted = chunk.Replace("\r\n", "\n").Replace('\r', '\n');
                composer.Append(pasted);

                if (placeholder is null)
                {
                    Write(pasted.Replace("\n", "\r\n"));
                    return true;
                }

                // The real composer shows ONE placeholder for the whole paste, however many reads
                // it took, and shows it once the paste closes — so accumulate until 201~.
                pasteBuffer.Append(pasted);
                if (inPaste) return true;

                Write(placeholder.Render(pasteBuffer.ToString()) + "\r\n");
                pasteBuffer.Clear();
                return true;
            }

            var work = chunk.Replace("\r\n", "\r");
            var trailingCr = work.EndsWith('\r');
            if (trailingCr) work = work[..^1];
            if (work.Contains('\r'))
            {
                var segments = work.Split('\r');
                for (var s = 0; s < segments.Length - 1; s++)
                {
                    composer.Append(segments[s]);
                    Write(segments[s].Replace("\n", "\r\n"));
                    var fragment = composer.ToString().Trim();
                    composer.Clear();
                    Write("\r\n");
                    if (fragment.Length > 0)
                    {
                        SubmitTurn(Write, fragment, transcriptPath, splitFinal);
                        turnCount++;
                    }
                }
                work = segments[^1]; // the tail stays in the composer awaiting the next Enter
            }

            // Composer echo — the real TUI renders typed/pasted text in the composer (raw-mode consoles
            // don't echo, so we must). Delivery verification (ComposerDeliveryEvidence) reads the
            // rendered screen for exactly this; without the echo every verified delivery would look
            // like a wedged terminal. (We can't clear it on submit like the real composer — the fake's
            // screen is append-only — but verification only needs presence, not clearing.)
            var composerText = work + (trailingCr ? "\n" : "");
            composer.Append(composerText);
            Write(composerText.Replace("\n", "\r\n"));
            return true;
        }

        // CARD-0292: closing the menu drains what it swallowed — per body a dequeue record then a
        // real user record (the measured shape), then one ordinary response turn so the session
        // reads idle again. An empty queue just returns to the prompt.
        void DrainRcMenuQueue()
        {
            if (rcQueue.Count == 0)
            {
                Write(IdleTitle);
                return;
            }

            string last = "";
            foreach (var body in rcQueue)
            {
                if (transcriptPath is not null)
                {
                    AppendTranscript(transcriptPath, JsonQueueOperationLine("dequeue", body));
                    AppendTranscript(transcriptPath, JsonUserLine(body));
                }
                Write($"SUBMITTED:{body.Replace("\n", "\\n")}\r\n");
                last = body;
            }
            rcQueue.Clear();

            var echo = last.Replace("\n", "\\n");
            if (echo.Length > 60) echo = echo[..60];
            Write($"FAKE response to: {echo}\r\n");
            Write("Crunched for 1s\r\n");
            Write(IdleTitle);
            if (transcriptPath is not null)
                AppendTranscript(transcriptPath, JsonAssistantLine(echo, NewApiCallId()));
        }
    }

    // Built-in LOCAL commands (no API call): the real TUI handles these in-process and writes
    // <command-name>/<local-command-stdout> USER records to the JSONL — and NO assistant line, NO
    // TurnEnd. That absence is a working/idle hazard (a session that counted these as activity
    // read "working" forever — live miss 2026-07-31); real shape pinned by
    // ClaudeLocalCommandCanaryTests. Slash SKILLS (e.g. /remote-control) are NOT in this set —
    // they submit as real turns.
    private static readonly string[] LocalCommands = ["/clear", "/model", "/status", "/help", "/config"];

    // OPT-IN API-error stub (CARD-0072): which class kills a turn, and which turn dies. Read once —
    // the values never change mid-run, and SubmitTurn is called from two burst paths.
    private static readonly string? ApiErrorMode =
        Environment.GetEnvironmentVariable("ANTIPHON_FAKE_API_ERROR") is { Length: > 0 } m ? m : null;
    private static readonly int ApiErrorAfterTurns =
        int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_API_ERROR_AFTER_TURNS"), out var n) && n > 0 ? n : 1;
    private static int _apiTurnCount;

    private static void SubmitTurn(
        Action<string> write, string text, string? transcriptPath, bool splitFinal = false)
    {
        // Deterministic, assertable echo. Slash-commands echo their name so slash routing/dispatch tests
        // can assert behaviour without depending on Claude's real (variable) output. Newlines in the
        // submitted body are escaped so the marker stays a single assertable line (batched bodies are
        // multi-line); the response echo is truncated so wall-of-text bodies don't flood the screen.
        write("\r\n");
        var escaped = text.Replace("\n", "\\n");
        write($"SUBMITTED:{escaped}\r\n");

        var firstToken = text.TrimStart().Split(' ', 2, StringSplitOptions.None)[0].TrimEnd();
        if (LocalCommands.Contains(firstToken, StringComparer.OrdinalIgnoreCase))
        {
            var commandArgs = text.TrimStart().Split(' ', 2, StringSplitOptions.None) is [_, var rest] ? rest : "";
            if (transcriptPath is not null)
            {
                AppendTranscript(transcriptPath, JsonUserLine(
                    $"<command-name>{firstToken}</command-name>\n"
                    + $"            <command-message>{firstToken.TrimStart('/')}</command-message>\n"
                    + $"            <command-args>{commandArgs}</command-args>"));
                AppendTranscript(transcriptPath, JsonUserLine(
                    $"<local-command-stdout>FAKE {firstToken} output</local-command-stdout>"));
            }
            write($"FAKE local command: {firstToken}\r\n");
            write(IdleTitle);
            return;
        }

        // Guarded, not passed eagerly: first-call JSON serializer warm-up takes long enough to
        // stall the drain loop past the burst gap (glueing a following body+CR into one paste).
        if (transcriptPath is not null)
            AppendTranscript(transcriptPath, JsonUserLine(text));

        // The armed turn dies on an API-error stub INSTEAD of answering (CARD-0072): the prompt was
        // recorded above (real Claude records it — the API call happened and failed), then the one
        // synthetic assistant record lands and the composer returns to prompt. No done pattern —
        // a dead turn renders the error text, not " for Ns".
        _apiTurnCount++;
        if (ApiErrorMode is not null && _apiTurnCount == ApiErrorAfterTurns)
        {
            var (errorText, status) = ApiErrorShape(ApiErrorMode);
            write(errorText + "\r\n");
            write(IdleTitle);
            if (transcriptPath is not null)
                AppendTranscript(transcriptPath, JsonApiErrorStubLine(ApiErrorMode, status, errorText));
            return;
        }

        var echo = escaped.Length > 60 ? escaped[..60] : escaped;
        write($"FAKE response to: {echo}\r\n");
        // Turn-end signals: the " for Ns" done pattern (survives ConPTY) AND the idle title (usually consumed).
        write("Crunched for 1s\r\n");
        write(IdleTitle);
        if (transcriptPath is null)
            return;

        // ONE response, and it decides its own message.id up front — the two records below have to
        // share it, because that shared id is the only thing that tells a consumer they are one
        // response (CARD-0046).
        var apiCallId = NewApiCallId();
        if (splitFinal)
        {
            // The measured shape, in this order: the thinking record — signature only, thinking
            // text EMPTY in all 1 936 thinking blocks measured — carrying the response's
            // stop_reason, and therefore normalizing to a BARE TurnEnd. Then the text record, with
            // the same stop_reason and the same id.
            AppendTranscript(transcriptPath, JsonAssistantThinkingLine(apiCallId));
        }
        AppendTranscript(transcriptPath, JsonAssistantLine(echo, apiCallId));
    }

    // Faithful, and safe for every existing test: within ONE record the AssistantText part is
    // persisted before that record's TurnEnd part, so the same-id check settlement now makes
    // (CARD-0046) passes and no fake-driven test defers.
    private static string NewApiCallId() => $"msg_fake_{Guid.NewGuid():N}";

    // "/compact" with or without arguments — arguments are the normal shape (the live CARD-0041
    // session typed "/compact This session is being handed NEW, unrelated work…") and they are what
    // produced the RAW user record that the working rule tripped over.
    private static bool IsCompactCommand(string text) =>
        text == "/compact" || text.StartsWith("/compact ", StringComparison.Ordinal);

    /// <summary>
    /// A MANUAL <c>/compact</c>, modelled as the full record set real Claude writes (CARD-0041,
    /// measured from session e77fb0a7's JSONL). All six records matter to the working/idle rules:
    /// the RAW typed prompt and the continuation summary are the two that escaped the exclusions
    /// and left a compacted session reading "working" forever, and the boundary's <c>manual</c>
    /// trigger is what now ends the turn. Emitting only the boundary — as this fake used to — made
    /// the bug unreproducible in tests.
    /// </summary>
    private static void EmitManualCompaction(Action<string> write, string? transcriptPath, string typedText)
    {
        write(CompactedScreenLine + "\r\n");
        write(IdleTitle);
        if (transcriptPath is null)
            return;

        // 1. The literal typed text, as a plain user record — NOT isMeta, NOT isCompactSummary.
        AppendTranscript(transcriptPath, JsonUserLine(typedText));
        // 2. The boundary itself.
        AppendTranscript(transcriptPath, JsonCompactBoundaryLine("manual"));
        // 3. Compaction's synthetic continuation prompt (carries isCompactSummary).
        AppendTranscript(transcriptPath, JsonCompactSummaryLine());
        // 4. The caveat, isMeta — the normalizer drops these, and this pins that it keeps doing so.
        AppendTranscript(transcriptPath, JsonMetaUserLine(
            "Caveat: The messages below were generated by the user while running local commands."));
        // 5-6. The local-command wrapper pair, exactly as any other slash command writes them.
        var commandArgs = typedText.Split(' ', 2, StringSplitOptions.None) is [_, var rest] ? rest : "";
        AppendTranscript(transcriptPath, JsonUserLine(
            "<command-name>/compact</command-name>\n"
            + "            <command-message>compact</command-message>\n"
            + $"            <command-args>{commandArgs}</command-args>"));
        AppendTranscript(transcriptPath, JsonUserLine(
            $"<local-command-stdout>{CompactedScreenLine}</local-command-stdout>"));
    }

    /// <summary>
    /// AUTO compaction: fires when a request starts over the context threshold, i.e. MID-turn, with
    /// nothing typed — so there is no raw prompt and no command-wrapper pair, and the trigger says
    /// <c>auto</c>. The working rules must NOT read this boundary as a turn end.
    /// </summary>
    private static void EmitAutoCompaction(Action<string> write, string? transcriptPath)
    {
        write(CompactedScreenLine + "\r\n");
        write(IdleTitle);
        if (transcriptPath is null)
            return;
        AppendTranscript(transcriptPath, JsonCompactBoundaryLine("auto"));
        AppendTranscript(transcriptPath, JsonCompactSummaryLine());
    }

    // JSONL lines in the shapes TranscriptNormalizer parses. The boundary shape must stay in sync with
    // tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl (PINNED-BY: ClaudeCompactionCanaryTests).
    private static string JsonUserLine(string text) => JsonSerializer.Serialize(new
    {
        type = "user",
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        message = new { role = "user", content = text },
    });

    // The measured queue-operation shape (fixture tests/Antiphon.Tests/Agents/Fixtures/
    // queued-command.jsonl, and the CARD-0292 incident records): NO uuid, the operation, the
    // full content, and a timestamp that for enqueue is composer-accept time.
    private static string JsonQueueOperationLine(string operation, string content) => JsonSerializer.Serialize(new
    {
        type = "queue-operation",
        operation,
        content,
        timestamp = DateTime.UtcNow.ToString("o"),
    });

    // Real Claude writes message.model on every assistant record (CARD-0082). The synthetic
    // API-error stub below keeps model:"<synthetic>"; this is the real-model path.
    private const string FakeModelId = "claude-opus-4";

    private static string JsonAssistantLine(string text, string? apiCallId = null) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        message = new
        {
            // The Anthropic message id, always present on a real assistant record. Emitting it is
            // what lets anything below the service layer exercise same-response identity at all.
            id = apiCallId ?? NewApiCallId(),
            model = FakeModelId,
            role = "assistant",
            stop_reason = "end_turn",
            content = new object[] { new { type = "text", text } },
        },
    });

    /// <summary>
    /// The first record of a SPLIT turn-ending response (CARD-0046, measured from session 7f9d06a5,
    /// response <c>msg_011Ce2Xog1xCJs9P</c>): a thinking block with an EMPTY <c>thinking</c> string
    /// and a signature, stamped with the response's <c>stop_reason</c>. Because the text is empty,
    /// <c>TranscriptNormalizer.FromAssistant</c> emits no Thinking part and the record yields a bare
    /// <c>TurnEnd</c> and nothing else — which is the record settlement used to fire on while the
    /// report was still 0.01-1.17 s away. Shares its id with the text record that follows.
    /// </summary>
    private static string JsonAssistantThinkingLine(string apiCallId) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        message = new
        {
            id = apiCallId,
            model = FakeModelId,
            role = "assistant",
            stop_reason = "end_turn",
            content = new object[]
            {
                new { type = "thinking", thinking = "", signature = "FAKEsignature" },
            },
        },
    });

    // The synthetic user record compaction writes to carry the summary forward. The prefix is the
    // one TranscriptKinds.CompactionContinuationPromptPrefix matches; isCompactSummary is the
    // structural flag a future migration could key on instead (CARD-0041).
    private static string JsonCompactSummaryLine() => JsonSerializer.Serialize(new
    {
        type = "user",
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        isCompactSummary = true,
        message = new
        {
            role = "user",
            content = "This session is being continued from a previous conversation that ran out of "
                + "context. The conversation is summarized below:\nFAKE summary of the conversation.",
        },
    });

    // The measured text+status per error class — verbatim from the real records (CARD-0072 sweep):
    // 22× rate_limit/429 wall stubs, server_error 529 + no-status connection drop, 2× auth. An
    // unknown mode still emits a structurally valid stub (isApiErrorMessage:true, class verbatim,
    // no status) so classifier fall-through paths can be driven end to end.
    private static (string Text, int? Status) ApiErrorShape(string mode) => mode switch
    {
        "rate_limit" => ("You've hit your session limit · resets 6:10pm (Europe/London)", 429),
        "server_error" => ("API Error: 529 Overloaded. This is a server-side issue, usually temporary — try again in a moment.", 529),
        "authentication_failed" => ("Login expired · Please run /login", null),
        _ => ($"API Error: {mode}", null),
    };

    /// <summary>
    /// The ONE synthetic assistant record Claude Code writes when a turn is killed by the API
    /// (CARD-0072, shape verbatim from the 23 measured stubs): <c>model:"&lt;synthetic&gt;"</c>,
    /// top-level <c>error</c>/<c>isApiErrorMessage:true</c>/(optional) <c>apiErrorStatus</c>,
    /// <c>stop_reason:"stop_sequence"</c>, zeroed usage, and the error string as an ordinary text
    /// block. It carries its OWN TurnEnd — which is why detection is a consumer-side predicate
    /// (<c>TranscriptKinds.IsApiErrorStub</c>), never a working-rule change.
    /// </summary>
    private static string JsonApiErrorStubLine(string errorClass, int? status, string text)
    {
        var record = new Dictionary<string, object?>
        {
            ["type"] = "assistant",
            ["uuid"] = Guid.NewGuid().ToString(),
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["error"] = errorClass,
            ["isApiErrorMessage"] = true,
            ["message"] = new
            {
                id = Guid.NewGuid().ToString(),
                model = "<synthetic>",
                role = "assistant",
                stop_reason = "stop_sequence",
                stop_sequence = "",
                usage = new { input_tokens = 0, output_tokens = 0, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                content = new object[] { new { type = "text", text } },
            },
        };
        // Real stubs OMIT apiErrorStatus when the class has none (auth, connection drop) — model
        // the absence, not a null, so the normalizer's "field missing" path is what gets driven.
        if (status is not null)
            record["apiErrorStatus"] = status;
        return JsonSerializer.Serialize(record);
    }

    // isMeta:true user records (caveats, command output) are system-injected, not the user talking —
    // TranscriptNormalizer.FromUser drops them before any rule sees them.
    private static string JsonMetaUserLine(string text) => JsonSerializer.Serialize(new
    {
        type = "user",
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        isMeta = true,
        message = new { role = "user", content = text },
    });

    // Key set mirrors the pinned fixture (tests/Antiphon.Tests/Agents/Fixtures/compact-boundary.jsonl,
    // captured from claude 2.1.217 on 2026-07-22). PINNED-BY: ClaudeCompactionCanaryTests.
    private static string JsonCompactBoundaryLine(string trigger) => JsonSerializer.Serialize(new
    {
        parentUuid = (string?)null,
        logicalParentUuid = Guid.NewGuid().ToString(),
        isSidechain = false,
        type = "system",
        subtype = "compact_boundary",
        content = "Conversation compacted",
        level = "info",
        compactMetadata = new { trigger, preTokens = 1000, postTokens = 100 },
        uuid = Guid.NewGuid().ToString(),
        timestamp = DateTime.UtcNow.ToString("o"),
        userType = "external",
        entrypoint = "cli",
        cwd = Environment.CurrentDirectory,
        sessionId = Guid.NewGuid().ToString(),
        version = "fake",
        gitBranch = "",
        slug = "fake-claude",
    });

    private static void AppendTranscript(string? path, string jsonLine)
    {
        if (string.IsNullOrEmpty(path)) return;
        // RETRIED, not fire-and-forget: tests poll the file with File.ReadAllLines, whose read
        // handle (FileShare.Read) blocks a concurrent append — and a swallowed IOException here
        // loses the record FOREVER, which was the "last transcript line missing after a 10s poll"
        // flake shape in FakeClaudeContractTests. The real Claude never loses a JSONL line to our
        // readers; a fake that silently could was modelling a failure nothing has.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.AppendAllText(path, jsonLine + "\n");
                AppendTiming(path, attempt, jsonLine, gaveUp: false);
                return;
            }
            catch (IOException) when (attempt < 100) { Thread.Sleep(10); }
            catch
            {
                // Anything else stays best-effort test plumbing — but a DROPPED record must leave
                // evidence (CARD-0050: a full-suite flake presents as "one record short at the 10s
                // poll deadline" and lost-vs-late is the diagnosis that matters).
                AppendTiming(path, attempt, jsonLine, gaveUp: true);
                return;
            }
        }
    }

    /// <summary>
    /// CARD-0050 evidence trail: every transcript append also stamps a sidecar line
    /// (<c>&lt;path&gt;.timing</c>) with wall-clock time, retry count, and the record's head, so a
    /// captured flake shows whether a missing record was written late, starved by share-mode
    /// retries, or dropped entirely. Best-effort by design — the sidecar must never fail a test.
    /// </summary>
    private static void AppendTiming(string path, int retries, string jsonLine, bool gaveUp)
    {
        try
        {
            var head = jsonLine.Length > 80 ? jsonLine[..80] : jsonLine;
            File.AppendAllText(
                path + ".timing",
                $"{DateTime.UtcNow:O} retries={retries}{(gaveUp ? " GAVE-UP" : "")} {head}\n");
        }
        catch
        {
            // Never let diagnostics interfere with the record path.
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                match = haystack[i + j] == needle[j];
            if (match) return true;
        }
        return false;
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
            // We write UTF-8 bytes; without a UTF-8 console codepage ConPTY decodes them per the
            // legacy OEM codepage and non-ASCII (␟, em-dashes) reaches captures as mojibake.
            SetConsoleOutputCP(65001);
            SetConsoleCP(65001);

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
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);


    /// <summary>
    /// CARD-0101 / test-coverage plan P0-2: what a NATIVE child's own CRT would have built as its
    /// <c>argv</c>, as opposed to what .NET's parser handed <c>Main</c>.
    ///
    /// <para>This exists because the fake was lying, and that lie is blindness B1 in the coverage
    /// plan. <c>--echo-args</c> prints the vector .NET produced, and .NET's parser accepts a doubled
    /// <c>""</c> inside a quoted argument as ONE escaped quote. <c>CommandLineToArgvW</c> — which
    /// <c>claude.exe</c>, node and bun all use — SPLITS there instead. On the exact literal
    /// <c>SessionMessageQueuePtyIntegrationTests.Launch_args_reach_the_child_process</c> was
    /// sending, the real child would have seen NINE arguments where three were intended
    /// (<c>LaunchArgvGuardTests</c> measures it) — and that test passed for months on the failing
    /// shape, because the only child ever asked was this one.</para>
    ///
    /// <para><c>--echo-args</c> is deliberately KEPT and unchanged. The DIVERGENCE between the two
    /// lines on a hostile input is the assertion (<c>FakeClaudeContractTests</c>:
    /// <c>A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet</c>); a fake that
    /// only printed the strict vector could no longer show why the strict vector is needed, and the
    /// next person would "simplify" it back.</para>
    ///
    /// <para><c>argv[0]</c> is dropped so the two lines are directly comparable — that is the point
    /// of matching the format. It is the executable only when this process was launched AS one,
    /// which is how the harness stages it (<c>fakeclaude/fakeclaude.exe</c>); when it is not,
    /// <c>ARGVSTRICTWARN:</c> says so rather than letting a line that is offset by one read as a
    /// pass.</para>
    /// </summary>
    private static IEnumerable<string> StrictArgvLines()
    {
        // GetCommandLineW, not Environment.CommandLine: the latter is .NET's reconstruction and
        // would re-introduce exactly the parser this method exists to bypass.
        var raw = Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty;

        var handle = CommandLineToArgvW(raw, out var count);
        if (handle == IntPtr.Zero)
        {
            yield return "ARGVSTRICT:<unavailable:CommandLineToArgvW returned NULL>";
            yield break;
        }

        string[] argv;
        try
        {
            argv = new string[count];
            for (var i = 0; i < count; i++)
                argv[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(handle, i * IntPtr.Size)) ?? string.Empty;
        }
        finally
        {
            LocalFree(handle);
        }

        if (argv.Length == 0)
        {
            yield return "ARGVSTRICT:<empty>";
            yield break;
        }

        var self = Environment.ProcessPath;
        if (self is not null && !string.Equals(
                Path.GetFileName(argv[0]), Path.GetFileName(self), StringComparison.OrdinalIgnoreCase))
        {
            yield return "ARGVSTRICTWARN:argv[0] is '" + EscapeArgvLine(argv[0]) + "' but this process is '"
                + EscapeArgvLine(self) + "' — the strict vector below may be offset by one";
        }

        yield return "ARGVSTRICT:" + EscapeArgvLine(string.Join("\u241F", argv.Skip(1)));
    }

    /// <summary>The same escaping <c>--echo-args</c> uses, so the two lines compare directly.</summary>
    private static string EscapeArgvLine(string s) =>
        s.Replace("\r", "\\r").Replace("\n", "\\n");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetCommandLineW();

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

}
