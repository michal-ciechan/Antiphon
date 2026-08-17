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
/// CLI modelled (real grok 1.0.4):
///  * <c>--version</c> / <c>-v</c> / <c>version</c> — prints <c>grok 1.0.4 (fakegrok) [stable]</c>
///  * <c>models</c> — prints the measured prose catalogue (not one-id-per-line)
///  * <c>--session-id</c> / <c>-s</c>, <c>--resume</c> / <c>-r</c>, <c>--cwd</c>, <c>--model</c> / <c>-m</c>
///  * <c>--always-approve</c>, <c>--no-alt-screen</c>, <c>--permission-mode</c> accepted and ignored
///  * Session files under <c>GROK_HOME/sessions/&lt;encoded-cwd&gt;/&lt;session-id&gt;/</c>
///
/// PTY contract modelled (same as FakeClaude — Antiphon's queue depends on it for every TUI):
///  * Lone CR/LF burst submits; text+CR in one write is a paste
///  * Mid-body <c>\r</c> fragments; <c>\n</c> is literal
///  * Composer echo; opt-in clip / swallow-enter / paste-placeholder
///  * Turn-end: <c>Crunched for 1s</c> + idle OSC title, then quiet
/// </summary>
internal static class Program
{
    private const string IdleTitle = "\x1b]0;✳\x07";
    private const string VersionLine = "grok 1.0.4 (fakegrok) [stable]";
    private static readonly byte[] PasteStartBytes = Encoding.ASCII.GetBytes("\x1b[200~");

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
                ProcessBurst(burst);
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
                var pasted = text.Replace("\x1b[200~", "").Replace("\x1b[201~", "")
                    .Replace("\r\n", "\n").Replace('\r', '\n');
                composer.Append(pasted);
                if (placeholder is null)
                {
                    Write(pasted.Replace("\n", "\r\n"));
                    return;
                }

                pasteBuffer.Append(pasted);
                if (inPaste) return;
                Write(placeholder.Render(pasteBuffer.ToString()) + "\r\n");
                pasteBuffer.Clear();
                return;
            }

            var work = text.Replace("\r\n", "\r");
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
                        SubmitTurn(Write, sessionDir, sessionId, fragment);
                }

                work = segments[^1];
            }

            var composerText = work + (trailingCr ? "\n" : "");
            composer.Append(composerText);
            Write(composerText.Replace("\n", "\r\n"));
        }
    }

    private static void SubmitTurn(Action<string> write, string sessionDir, string sessionId, string text)
    {
        write("\r\n");
        var escaped = text.Replace("\n", "\\n");
        write($"SUBMITTED:{escaped}\r\n");
        var echo = escaped.Length > 60 ? escaped[..60] : escaped;
        write($"FAKE response to: {echo}\r\n");
        write("Crunched for 1s\r\n");
        write(IdleTitle);
        AppendSessionFiles(sessionDir, sessionId, text, $"FAKE response to: {echo}");
    }

    private static void AppendSessionFiles(string sessionDir, string sessionId, string user, string assistant)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var updates = Path.Combine(sessionDir, "updates.jsonl");
            var history = Path.Combine(sessionDir, "chat_history.jsonl");
            File.AppendAllText(updates,
                JsonSerializer.Serialize(new
                {
                    timestamp = now,
                    method = "session/update",
                    @params = new
                    {
                        sessionId,
                        update = new
                        {
                            sessionUpdate = "user_message_chunk",
                            content = new { type = "text", text = user }
                        }
                    }
                }) + "\n");
            File.AppendAllText(updates,
                JsonSerializer.Serialize(new
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
                        }
                    }
                }) + "\n");
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
}
