using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Antiphon.FakeGrok;

/// <summary>
/// A deterministic stand-in for the Grok Build TUI's terminal and CLI contract — NOT a visual TUI
/// emulator. It models the behaviours Antiphon's PTY / session-runner stack depends on so tests can
/// lock them in without launching the real <c>grok</c> executable.
///
/// CLI modelled (real grok 1.0.5, measured by <c>GrokCanaryTests</c> 2026-08-18):
///  * <c>--version</c> / <c>-v</c> / <c>version</c> — prints <c>grok 1.0.5 (fakegrok) [stable]</c>
///  * <c>models</c> — prints the measured prose catalogue (not one-id-per-line)
///  * <c>--session-id</c> / <c>-s</c>, <c>--resume</c> / <c>-r</c>, <c>--cwd</c>, <c>--model</c> / <c>-m</c>
///  * <c>--always-approve</c>, <c>--no-alt-screen</c>, <c>--permission-mode</c> accepted and ignored
///  * Session files under <c>GROK_HOME/sessions/&lt;encoded-cwd&gt;/&lt;session-id&gt;/</c>
///
/// PTY contract modelled — the MEASURED grok 1.0.5 contract, which is NOT FakeClaude's
/// (the original "same as FakeClaude" assumption was wrong on every point the canaries checked):
///  * EVERY <c>\r</c> is Enter — text+CR in one write SUBMITS (no Claude-style paste window);
///    mid-body <c>\r</c> submits the fragment before it
///  * <c>\n</c> is DROPPED from composer input, typed and pasted alike — lines join with NO
///    separator (measured: 4450 sent → 4389 recorded, exactly the newline count)
///  * Bracketed paste content lands intact; no placeholder collapse at 4.4 KB; no stdin clip at
///    4.4 KB typed (the clip / swallow-enter / paste-placeholder models stay as opt-in harness
///    tooling for worst-case drills, not as measured Grok behaviour)
///  * Turn-end: <c>Worked for 1.7s</c> (decimal seconds — the <c> for \d+s</c> integer regex does
///    NOT match it) + idle OSC title <c>grok</c> (never Claude's <c>✳</c>), then quiet
///  * updates.jsonl per turn: user_message_chunk + agent_message_chunk (method
///    <c>session/update</c>) and turn_completed with stop_reason (method
///    <c>_x.ai/session/update</c>), flushed line-by-line as they happen
///  * Opt-in <c>ANTIPHON_FAKE_QUESTION_TOOL=1</c> (CARD-0241): first submit opens
///    <c>ask_user_question</c> (the three measured JSONL shapes) and holds the turn working;
///    a submit while open writes the completed update (not a user chunk); Esc does not complete
///    the tool. <c>ANTIPHON_FAKE_SUBMIT_WHILE_WORKING=cancel</c> still cancels a later
///    submit-while-working.
/// </summary>
internal static class Program
{
    // Real grok's idle title is plain "grok" (spinner/status titles while working); it never
    // sets Claude's ✳ — measured 1.0.5, and exactly why RunnerGrokAdapter's ✳ check never fires.
    private const string IdleTitle = "\x1b]0;grok\x07";
    private const string VersionLine = "grok 1.0.5 (fakegrok) [stable]";
    private static int _eventCounter;
    private static int _promptCounter;
    private static readonly byte[] PasteStartBytes = Encoding.ASCII.GetBytes("\x1b[200~");
    private static bool _turnInFlight;
    private static string? _inFlightPromptId;
    private static bool _questionOpen;
    private static string? _questionToolCallId;
    private static string? _questionText;

    /// <summary>
    /// CARD-0159 S0: a prompt received while a turn is in flight emits
    /// <c>turn_completed stop_reason=cancelled</c> then the new
    /// <c>user_message_chunk</c>, 43 ms apart — the measured live order of submitting into a
    /// working Grok composer.
    /// </summary>
    private static bool SubmitWhileWorkingCancels =>
        string.Equals(
            Environment.GetEnvironmentVariable("ANTIPHON_FAKE_SUBMIT_WHILE_WORKING"),
            "cancel",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CARD-0241 S5: first submit opens <c>ask_user_question</c> (the three measured JSONL
    /// shapes; turn stays working). A submit while open writes the completed update, not a
    /// <c>user_message_chunk</c>. Esc does not complete the tool. The CARD-0159 S0 knob still
    /// cancels a submit-while-working that is not an answer to the open question.
    /// </summary>
    private static bool QuestionToolEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("ANTIPHON_FAKE_QUESTION_TOOL"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static int Main(string[] args)
    {
        if (IsHelp(args))
        {
            Console.WriteLine("Fake Grok — deterministic stand-in for grok.exe");
            Console.WriteLine("Usage: fakegrok [OPTIONS] [COMMAND]");
            Console.WriteLine("Commands: models, version");
            return 0;
        }

        if (IsVersion(args))
        {
            Console.WriteLine(VersionLine);
            return 0;
        }

        if (IsModels(args))
        {
            Console.WriteLine("You are logged in with grok.com.");
            Console.WriteLine();
            Console.WriteLine("Default model: grok-4.6");
            Console.WriteLine();
            Console.WriteLine("Available models:");
            Console.WriteLine("  * grok-4.6 (default)");
            Console.WriteLine("  - grok-4.5");
            return 0;
        }

        var banner = GetArg(args, "--banner") ?? "Fake Grok ready";
        var debugInput = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_DEBUG_INPUT") == "1";
        var burstGapMs = int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_BURST_MS"), out var g) ? g : 12;
        var clip = StdinClipModel.FromEnvironment();
        var placeholder = PastePlaceholderModel.FromEnvironment();
        var swallow = SwallowEnterModel.FromEnvironment();
        TryEnableRawConsole();

        var cwd = Path.GetFullPath(GetArg(args, "--cwd") ?? Environment.CurrentDirectory);
        var model = GetArg(args, "--model") ?? GetArg(args, "-m") ?? "grok-4.6";
        var sessionId = GetArg(args, "--session-id") ?? GetArg(args, "-s");
        var resumeId = GetArg(args, "--resume") ?? GetArg(args, "-r");
        var grokHome = Environment.GetEnvironmentVariable("GROK_HOME");
        if (string.IsNullOrWhiteSpace(grokHome))
            grokHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");

        if (!string.IsNullOrWhiteSpace(resumeId))
        {
            var resumeDir = SessionDirectory(grokHome, cwd, resumeId);
            if (!Directory.Exists(resumeDir))
            {
                Console.Error.WriteLine($"Session not found: {resumeId}");
                return 1;
            }

            sessionId = resumeId;
        }
        else
        {
            sessionId ??= Guid.NewGuid().ToString("D");
        }

        var sessionDir = SessionDirectory(grokHome, cwd, sessionId);
        Directory.CreateDirectory(sessionDir);
        WriteSummary(sessionDir, sessionId, cwd, model);
        // CARD-0050 S3: stamp process start into the updates.jsonl timing sidecar so a
        // captured flake can tell late-vs-lost-vs-starved (same trail FakeClaude gained in S1).
        AppendTiming(Path.Combine(sessionDir, "updates.jsonl"), 0, "\"process-start\"", gaveUp: false);

        var stdout = Console.OpenStandardOutput();
        void Write(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        Write(banner + "\r\n");
        if (clip is not null) Write(clip.Describe() + "\r\n");
        if (placeholder is not null) Write(placeholder.Describe() + "\r\n");
        if (swallow is not null) Write(swallow.Describe() + "\r\n");
        if (debugInput)
        {
            var h = GetStdHandle(STD_INPUT_HANDLE);
            Write(GetConsoleMode(h, out var m)
                ? $"INMODE:0x{m:X} vt={(m & ENABLE_VIRTUAL_TERMINAL_INPUT) != 0} cp={GetConsoleCP()}\r\n"
                : "INMODE:unavailable\r\n");
        }

        if (Array.IndexOf(args, "--echo-args") >= 0)
            Write("ARGS:" + string.Join("␟", args).Replace("\r", "\\r").Replace("\n", "\\n") + "\r\n");
        // --echo-argv-strict: the SAME command line, re-parsed the way a NATIVE child's CRT parses
        // it (CARD-0101 / test-coverage plan P0-2). Kept BESIDE --echo-args, never replacing it:
        // the divergence between the two lines on a hostile input is what pins why this exists.
        if (Array.IndexOf(args, "--echo-argv-strict") >= 0)
            foreach (var strictLine in StrictArgvLines())
                Write(strictLine + "\r\n");
        Write(IdleTitle);

        var gate = new object();
        var pending = new List<(long AtMs, byte[] Bytes)>();
        var clock = Stopwatch.StartNew();
        long lastByteMs = 0;
        var eof = false;
        var stdin = Console.OpenStandardInput();
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
                    lastByteMs = clock.ElapsedMilliseconds;
                    pending.Add((lastByteMs, buf[..n]));
                }
            }
        })
        { IsBackground = true, Name = "fakegrok-stdin" };
        reader.Start();

        var composer = new StringBuilder();
        var pasteBuffer = new StringBuilder();
        var inPaste = false;

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

            foreach (var burst in bursts)
            {
                var burstText = Encoding.UTF8.GetString(burst);
                // CARD-0241: Esc is not the answer path. A lone Esc while the question is open
                // must not complete the tool and must not submit.
                if (_questionOpen
                    && burstText.Length > 0
                    && burstText.All(c => c == '\x1b'))
                {
                    Write("QUESTION-ESC-IGNORED\r\n");
                    continue;
                }

                ProcessBurst(burst);
            }
        }

        return 0;

        void ProcessBurst(byte[] burst)
        {
            if (clip is not null && !(inPaste || Contains(burst, PasteStartBytes)))
            {
                burst = clip.Apply(burst, out var note);
                if (note is not null) Write(note + "\r\n");
            }

            var text = Encoding.UTF8.GetString(burst);
            var wasBracketedPaste = inPaste || Contains(burst, PasteStartBytes);
            if (text.Contains("\x1b[200~", StringComparison.Ordinal))
                inPaste = true;
            if (text.Contains("\x1b[201~", StringComparison.Ordinal))
                inPaste = false;

            var isLoneEnter = !wasBracketedPaste
                && text.Length > 0
                && text.All(c => c is '\r' or '\n');

            if (isLoneEnter)
            {
                var submitted = composer.ToString().Trim();
                if (submitted.Length == 0)
                {
                    composer.Clear();
                    return;
                }

                if (swallow is not null && swallow.ShouldSwallow())
                {
                    Write($"SWALLOWED-ENTER:remaining={swallow.Remaining} held={submitted.Length}\r\n");
                    return;
                }

                composer.Clear();
                SubmitTurn(Write, sessionDir, sessionId, submitted);
                return;
            }

            if (wasBracketedPaste)
            {
                // Measured 1.0.5: pasted newlines are DROPPED — lines join with no separator.
                // The raw (newline-bearing) text still feeds the opt-in placeholder model so its
                // "+M lines" arithmetic stays meaningful as harness tooling.
                var pastedRaw = text.Replace("\x1b[200~", "").Replace("\x1b[201~", "")
                    .Replace("\r\n", "\n").Replace('\r', '\n');
                var pasted = pastedRaw.Replace("\n", "");
                composer.Append(pasted);
                if (placeholder is null)
                {
                    Write(pasted);
                    return;
                }

                pasteBuffer.Append(pastedRaw);
                if (inPaste) return;
                Write(placeholder.Render(pasteBuffer.ToString()) + "\r\n");
                pasteBuffer.Clear();
                return;
            }

            // Measured 1.0.5: every \r is Enter — including one trailing a text burst — and \n is
            // dropped from typed input (no Claude-style paste window, no literal newline).
            var work = text.Replace("\r\n", "\r");
            var trailingCr = work.EndsWith('\r');
            if (trailingCr) work = work[..^1];
            var segments = work.Split('\r');
            for (var s = 0; s < segments.Length; s++)
            {
                var piece = segments[s].Replace("\n", "");
                composer.Append(piece);
                Write(piece);
                var submitHere = s < segments.Length - 1 || trailingCr;
                if (!submitHere) continue;
                var fragment = composer.ToString().Trim();
                composer.Clear();
                Write("\r\n");
                if (fragment.Length > 0)
                    SubmitTurn(Write, sessionDir, sessionId, fragment);
            }
        }
    }

    private static void SubmitTurn(Action<string> write, string sessionDir, string sessionId, string text)
    {
        // Real grok sets WORKING titles mid-turn ("⠹ - Waiting for response… - grok", "Thinking -
        // grok", "Responding - grok" — measured 1.0.5) before re-idling to plain "grok". Modelled
        // here for fidelity AND necessity: conhost only re-emits an OSC title that CHANGED, so
        // without the working title the closing IdleTitle (same "grok" the session started with)
        // dedups away and never reaches the pty output at all.
        write("\x1b]0;Responding - grok\x07");
        // Do not collapse the working and idle title mutations into one console scheduler turn.
        // OpenConsole coalesces that zero-duration transition and can omit the closing OSC record
        // from its pty output. A real turn has response work between these states; this short,
        // deterministic fake turn gives the terminal a distinct working state to publish before
        // we emit its measured completion record below.
        Thread.Sleep(100);
        write("\r\n");
        var escaped = text.Replace("\n", "\\n");
        write($"SUBMITTED:{escaped}\r\n");

        if (QuestionToolEnabled && _questionOpen)
        {
            // Measured: the overlay answer is a completed tool_call_update, not a user_message_chunk.
            AppendQuestionCompleted(sessionDir, sessionId, text);
            _questionOpen = false;
            _questionToolCallId = null;
            _questionText = null;
            write("QUESTION-ANSWERED\r\n");
            // Same turn stays in flight; a later submit-while-working still cancels (S0).
            return;
        }

        if (QuestionToolEnabled && !_questionOpen)
        {
            var echoQ = escaped.Length > 60 ? escaped[..60] : escaped;
            _inFlightPromptId = AppendPartialTurn(sessionDir, sessionId, text, $"FAKE response to: {echoQ}");
            _questionToolCallId = $"call-{Guid.NewGuid():D}-25";
            _questionText = "Any preference before I start?";
            AppendQuestionOpening(sessionDir, sessionId, _questionToolCallId, _questionText);
            _questionOpen = true;
            _turnInFlight = true;
            write("QUESTION-OPEN\r\n");
            return;
        }

        // Measured 1.0.5 (GrokCanaryTests): a typed /compact writes compaction_checkpoint +
        // auto_compact_completed and NO turn_completed / user_message_chunk (CARD-0157).
        if (IsCompactCommand(text))
        {
            write("Compaction completed\r\n");
            write(IdleTitle);
            AppendCompactFiles(sessionDir, sessionId);
            return;
        }

        var echo = escaped.Length > 60 ? escaped[..60] : escaped;
        write($"FAKE response to: {echo}\r\n");

        if (SubmitWhileWorkingCancels && _turnInFlight)
        {
            // Measured live order (CARD-0159): cancelled turn_completed, then the new
            // user_message_chunk 43 ms later. No Esc is involved — submitting into a working
            // composer is itself the cancel.
            AppendTurnCompleted(sessionDir, sessionId, _inFlightPromptId, "cancelled", withUsage: false);
            Thread.Sleep(43);
            _turnInFlight = false;
            _inFlightPromptId = null;
            write("Worked for 1.7s\r\n");
            write(IdleTitle);
            AppendSessionFiles(sessionDir, sessionId, text, $"FAKE response to: {echo}");
            return;
        }

        if (SubmitWhileWorkingCancels && !_turnInFlight)
        {
            // Hold the turn open so the next prompt is the measured mid-turn submit.
            _inFlightPromptId = AppendPartialTurn(sessionDir, sessionId, text, $"FAKE response to: {echo}");
            _turnInFlight = true;
            return;
        }

        // The real turn-end line, measured 1.0.5: decimal seconds ("Worked for 1.7s"), which the
        // integer " for \d+s" regex does NOT match — do not "fix" this to an integer.
        write("Worked for 1.7s\r\n");
        write(IdleTitle);
        AppendSessionFiles(sessionDir, sessionId, text, $"FAKE response to: {echo}");
    }

    private static bool IsCompactCommand(string text) =>
        text.StartsWith("/compact", StringComparison.OrdinalIgnoreCase)
        && (text.Length == 8 || char.IsWhiteSpace(text[8]));

    /// <summary>
    /// The measured per-turn updates.jsonl emission (grok 1.0.5): user_message_chunk and
    /// agent_message_chunk as <c>session/update</c>, then turn_completed with a stop_reason as
    /// <c>_x.ai/session/update</c>, each row flushed as it happens (the real file is line-buffered
    /// — turn_completed landed ~1.5 s after Enter on a trivial turn, no Claude-style flush stall).
    /// </summary>
    private static void AppendSessionFiles(string sessionDir, string sessionId, string user, string assistant)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var promptId = Guid.NewGuid().ToString("D");
            var promptIndex = _promptCounter++;
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            var history = Path.Combine(sessionDir, "chat_history.jsonl");
            object Meta() => new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs };
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "user_message_chunk",
                        content = new { type = "text", text = user },
                        _meta = new { modelId = "grok-4.6", promptIndex }
                    },
                    _meta = Meta()
                }
            }));
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "agent_message_chunk",
                        content = new { type = "text", text = assistant }
                    },
                    _meta = Meta()
                }
            }));
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "_x.ai/session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "turn_completed",
                        prompt_id = promptId,
                        stop_reason = "end_turn",
                        usage = BuildTurnUsage()
                    },
                    _meta = Meta()
                }
            }));
            File.AppendAllText(history,
                JsonSerializer.Serialize(new { role = "user", content = user }) + "\n");
            File.AppendAllText(history,
                JsonSerializer.Serialize(new { role = "assistant", content = assistant }) + "\n");
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }
    }

    /// <summary>User + agent chunks of a turn, no <c>turn_completed</c> — the in-flight half of S0.</summary>
    private static string AppendPartialTurn(string sessionDir, string sessionId, string user, string assistant)
    {
        var promptId = Guid.NewGuid().ToString("D");
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var promptIndex = _promptCounter++;
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            object Meta() => new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs };
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "user_message_chunk",
                        content = new { type = "text", text = user },
                        _meta = new { modelId = "grok-4.6", promptIndex }
                    },
                    _meta = Meta()
                }
            }));
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "agent_message_chunk",
                        content = new { type = "text", text = assistant }
                    },
                    _meta = Meta()
                }
            }));
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }

        return promptId;
    }

    /// <summary>
    /// Incident lines 86/87 shape: opening <c>tool_call</c> titled ask_user_question with
    /// <c>_meta["x.ai/tool"]</c>, then a rendering <c>tool_call_update</c> that is not completed.
    /// </summary>
    private static void AppendQuestionOpening(
        string sessionDir, string sessionId, string toolCallId, string question)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            object XaiTool() => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["x.ai/tool"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["version"] = 1,
                    ["name"] = "ask_user_question",
                    ["kind"] = "ask_user",
                    ["namespace"] = "grok_build",
                    ["label"] = "Ask User",
                    ["read_only"] = true,
                },
            };
            object Meta() => new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs };
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "tool_call",
                        toolCallId,
                        title = "ask_user_question",
                        rawInput = new
                        {
                            questions = new[]
                            {
                                new
                                {
                                    question,
                                    options = new[]
                                    {
                                        new { label = "Proceed as planned (Recommended)", description = "Go ahead." },
                                        new { label = "Hold - I have a change", description = "Wait." },
                                    },
                                },
                            },
                        },
                        _meta = XaiTool(),
                    },
                    _meta = Meta(),
                },
            }));
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "tool_call_update",
                        toolCallId,
                        kind = "other",
                        title = "Ask: " + question,
                        locations = Array.Empty<object>(),
                        _meta = XaiTool(),
                    },
                    _meta = Meta(),
                },
            }));
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }
    }

    /// <summary>
    /// Incident line 91 shape: completed update, empty title, no <c>_meta["x.ai/tool"]</c>,
    /// <c>content[0].content.text</c> wrapping the typed answer.
    /// </summary>
    private static void AppendQuestionCompleted(string sessionDir, string sessionId, string answer)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            var wrapper =
                $"User has answered your questions: \"{_questionText ?? "question"}\"=\"{answer}\". "
                + "You can now continue with the user's answers in mind.";
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "tool_call_update",
                        toolCallId = _questionToolCallId,
                        status = "completed",
                        content = new object[]
                        {
                            new
                            {
                                type = "content",
                                content = new { type = "text", text = wrapper },
                            },
                        },
                    },
                    _meta = new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs },
                },
            }));
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }
    }

    private static void AppendTurnCompleted(
        string sessionDir, string sessionId, string? promptId, string stopReason, bool withUsage)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            object update = withUsage
                ? new
                {
                    sessionUpdate = "turn_completed",
                    prompt_id = promptId,
                    stop_reason = stopReason,
                    usage = BuildTurnUsage()
                }
                : new
                {
                    sessionUpdate = "turn_completed",
                    prompt_id = promptId,
                    stop_reason = stopReason
                };
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "_x.ai/session/update",
                @params = new
                {
                    sessionId,
                    update,
                    _meta = new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs }
                }
            }));
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }
    }

    /// <summary>
    /// CARD-0157: the measured /compact pair. Tokens from
    /// <c>ANTIPHON_FAKE_COMPACT_TOKENS="before,after"</c> (default 106112,34833 — session
    /// 1636e434's first compact). No user_message_chunk, no turn_completed.
    /// </summary>
    private static void AppendCompactFiles(string sessionDir, string sessionId)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            var (before, after) = CompactTokens();
            var checkpointId = Guid.NewGuid().ToString("D");
            object Meta() => new { eventId = $"{sessionId}-{++_eventCounter}", agentTimestampMs = nowMs };
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "_x.ai/session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "compaction_checkpoint",
                        checkpoint_id = checkpointId,
                        prompt_index_at_compaction = 1,
                        checkpoint_file = $"compaction_checkpoints/{checkpointId}.json",
                        schema_version = 1,
                        created_at = DateTimeOffset.UtcNow.ToString("o")
                    },
                    _meta = Meta()
                }
            }));
            AppendShared(updates, JsonSerializer.Serialize(new
            {
                timestamp = now,
                method = "_x.ai/session/update",
                @params = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "auto_compact_completed",
                        tokens_before = before,
                        tokens_after = after,
                        summary_preview = (string?)null
                    },
                    _meta = Meta()
                }
            }));
        }
        catch
        {
            // Session files are test plumbing; a write failure must not kill the TUI contract.
        }
    }

    private static object BuildTurnUsage()
    {
        var modelCalls = FakeModelCalls();
        return new
        {
            inputTokens = 1,
            outputTokens = 1,
            totalTokens = 2,
            modelCalls,
            numTurns = modelCalls,
            modelUsage = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["grok-4.6-build"] = new
                {
                    inputTokens = 1,
                    outputTokens = 1,
                    totalTokens = 2,
                    modelCalls,
                }
            }
        };
    }

    private static int FakeModelCalls() =>
        int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_FAKE_MODELCALLS"), out var n) && n > 0
            ? n
            : 1;

    private static (int Before, int After) CompactTokens()
    {
        var spec = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_COMPACT_TOKENS") ?? "106112,34833";
        var parts = spec.Split(',');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var before)
            && int.TryParse(parts[1], out var after))
        {
            return (before, after);
        }

        return (106112, 34833);
    }

    /// <summary>
    /// RETRIED, not fire-and-forget: a test poll that opens with <c>FileShare.Read</c> blocks a
    /// concurrent append, and a swallowed <see cref="IOException"/> here loses the row forever —
    /// the "updates.jsonl not yet written" flake in CARD-0050. Same shape as FakeClaude's
    /// transcript append (slice 1).
    /// </summary>
    private static void AppendShared(string path, string jsonLine)
    {
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
                AppendTiming(path, attempt, jsonLine, gaveUp: true);
                return;
            }
        }
    }

    /// <summary>
    /// CARD-0050 evidence trail: every updates.jsonl append also stamps a sidecar line
    /// (<c>&lt;path&gt;.timing</c>) with wall-clock time, retry count, and the record's head, so a
    /// captured flake shows whether a missing row was written late, starved by share-mode
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

    private static void WriteSummary(string sessionDir, string sessionId, string cwd, string model)
    {
        try
        {
            File.WriteAllText(Path.Combine(sessionDir, "summary.json"), JsonSerializer.Serialize(new
            {
                info = new { id = sessionId, cwd },
                current_model_id = model,
                created_at = DateTime.UtcNow.ToString("o"),
                grok_home = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(sessionDir)))
            }));
        }
        catch
        {
            // Same as AppendSessionFiles — best-effort test plumbing.
        }
    }

    private static string SessionDirectory(string grokHome, string cwd, string sessionId) =>
        Path.Combine(grokHome, "sessions", Uri.EscapeDataString(cwd), sessionId);

    private static bool IsHelp(string[] args) =>
        args.Any(a => a is "-h" or "--help" or "help");

    private static bool IsVersion(string[] args) =>
        args.Any(a => a is "-v" or "--version" or "version");

    private static bool IsModels(string[] args) =>
        args.Any(a => string.Equals(a, "models", StringComparison.OrdinalIgnoreCase));

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

    private static void TryEnableRawConsole()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
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
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

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
