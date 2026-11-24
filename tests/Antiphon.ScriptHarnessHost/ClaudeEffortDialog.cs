using System.Text.Json;
using System.Text.Json.Nodes;

// CARD-0449 / CARD-1516: the test-owned console peer ClaudeAdapterEffortPromptTests runs in a real PTY in place of Claude's
// effort dialog. It replaced tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1 and its scripted-key driver
// claude-effort-trace-contract.ps1 (unit 35 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); it reads keys
// and paints exactly as the script did, through the same Console calls.
//
//   claude-effort-dialog [--effort <value>]   the dialog. C449_CAPTURE names the capture JSON ({ screen, composer }; the
//       Unicode provider captures are loaded, never retyped here), C449_TRACE the trace file, C449_REQUESTED the requested
//       effort (high starts on Keep, anything else on Switch), C449_STUCK=1 ignores Enter, C1516_CLEAR_DELAY_MS delays a
//       Ctrl+U clear and C1516_NEVER_CLEAR=1 ignores it.
//   claude-effort-deaf <screen-file>          paints the file's text once and then reads and ignores every key.
//   claude-effort-trace-contract              the C1516 trace contract: scripted keys through the same key handler, four
//       PASS C1516 rows and the C487 inventory on stdout; exit 0, or FAIL C1516 <reason> and exit 1.
//
// Trace publication is passive (CARD-1516 D-5): ordinary keys paint only; Enter and Ctrl+U paint and then publish one
// coherent snapshot (applied, highlight, keys, composer, pid, sequence, phase, clearReceived, clearPainted), written to a
// temporary file and moved over the trace. ASCII only.
internal sealed class ClaudeEffortDialog
{
    private readonly string _template;
    private readonly string _composerTemplate;
    private readonly string _trace;
    private readonly bool _stuck;
    private readonly bool _neverClear;
    private readonly int _clearDelayMs;
    private readonly TextWriter _console;
    private readonly List<string> _keys = [];
    private int _highlight;
    private bool _dialog = true;
    private string _composer = "";
    private string? _applied;
    private int _sequence;
    private string? _clearReceived;
    private string? _clearPainted;

    internal Action<string>? PaintSink { get; set; }

    internal Action<string>? PublishSink { get; set; }

    private ClaudeEffortDialog(string capturePath, string trace, string? requested, bool stuck, bool neverClear, int clearDelayMs, TextWriter console)
    {
        var capture = JsonNode.Parse(File.ReadAllText(capturePath))!;
        _template = capture["screen"]!.GetValue<string>();
        _composerTemplate = capture["composer"]!.GetValue<string>();
        _trace = trace;
        _highlight = string.Equals(requested, "high", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        _stuck = stuck;
        _neverClear = neverClear;
        _clearDelayMs = clearDelayMs;
        _console = console;
    }

    private static ClaudeEffortDialog FromEnvironment(TextWriter console)
    {
        static string? Read(string name) => Environment.GetEnvironmentVariable(name);
        return new ClaudeEffortDialog(Read("C449_CAPTURE") ?? throw new InvalidOperationException("C449_CAPTURE is required."),
            Read("C449_TRACE") ?? throw new InvalidOperationException("C449_TRACE is required."), Read("C449_REQUESTED"),
            Read("C449_STUCK") == "1", Read("C1516_NEVER_CLEAR") == "1",
            int.TryParse(Read("C1516_CLEAR_DELAY_MS"), out var delay) ? delay : 0, console);
    }

    internal static int Run()
    {
        var dialog = FromEnvironment(Console.Out);
        dialog.Paint();
        while (true) dialog.Key(Console.ReadKey(true));
    }

    internal static int RunDeaf(string screenFile)
    {
        Console.Write(File.ReadAllText(screenFile));
        Console.Out.Flush();
        while (true) Console.ReadKey(true);
    }

    private void Paint()
    {
        string screen;
        if (_dialog)
        {
            screen = _template.Replace("> Keep", "  Keep").Replace("     Switch", "   > Switch");
            if (_highlight == 1) screen = _template;
        }
        else screen = "Fable 5.1 with " + _applied + " effort" + "\n" + _composerTemplate.Replace("{{composer}}", _composer);
        // The terminal needs an explicit carriage return on Unix as well as Windows.
        _console.Write("\u001b[2J\u001b[H" + screen.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        _console.Flush();
        PaintSink?.Invoke(screen);
    }

    private void Publish(string phase)
    {
        _sequence++;
        var json = new JsonObject
        {
            ["applied"] = _applied,
            ["highlight"] = _highlight,
            ["keys"] = new JsonArray(_keys.Select(key => (JsonNode)JsonValue.Create(key)!).ToArray()),
            ["composer"] = _composer,
            ["pid"] = Environment.ProcessId,
            ["sequence"] = _sequence,
            ["phase"] = phase,
            ["clearReceived"] = _clearReceived,
            ["clearPainted"] = _clearPainted,
        }.ToJsonString();
        var temporary = _trace + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _trace, true);
        PublishSink?.Invoke(phase);
    }

    internal void Key(ConsoleKeyInfo key)
    {
        var name = key.KeyChar.ToString();
        if (key.Key == ConsoleKey.Enter) name = "Enter";
        if (key.KeyChar == (char)21) name = "Ctrl+U";
        _keys.Add(name);
        if (_dialog)
        {
            if (char.ToLowerInvariant(key.KeyChar) == 'j' || key.Key == ConsoleKey.DownArrow || key.KeyChar == (char)14) _highlight = 3 - _highlight;
            if (key.Key == ConsoleKey.Enter && !_stuck)
            {
                _applied = _highlight == 1 ? "xhigh" : "high";
                _dialog = false;
            }
        }
        else if (key.KeyChar == (char)21)
        {
            _clearReceived = DateTime.UtcNow.ToString("O");
            if (_clearDelayMs > 0) Thread.Sleep(_clearDelayMs);
            if (!_neverClear) _composer = "";
        }
        else if (key.KeyChar != '\0') _composer += key.KeyChar;
        var phase = name == "Ctrl+U" ? "clear" : name == "Enter" ? "enter" : "key";
        Paint();
        if (name == "Ctrl+U") _clearPainted = DateTime.UtcNow.ToString("O");
        if (phase != "key") Publish(phase);
    }

    // The scripted-key driver: the same handler, sinks recording paint and publication order, the trace read back.
    internal static int RunTraceContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1516-trace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var capture = Path.Combine(root, "capture.json");
            var trace = Path.Combine(root, "trace.json");
            var ruler = new string((char)0x2500, 40);
            File.WriteAllText(capture, new JsonObject
            {
                ["screen"] = "> Keep xhigh\n     Switch high",
                ["composer"] = ruler + "\n> {{composer}}\n" + ruler + "\n? for shortcuts",
            }.ToJsonString());
            var dialog = new ClaudeEffortDialog(capture, trace, "xhigh", stuck: false, neverClear: false, clearDelayMs: 0, TextWriter.Null);
            var events = new List<string>();
            dialog.PaintSink = screen => events.Add("paint:" + screen);
            dialog.PublishSink = phase => events.Add("publish:" + phase);
            static void Assert(bool value, string message)
            {
                if (!value) throw new InvalidOperationException(message);
            }
            void Key(char character, ConsoleKey key) => dialog.Key(new ConsoleKeyInfo(character, key, false, false, false));
            JsonNode Read() => JsonNode.Parse(File.ReadAllText(trace))!;
            static string Keys(JsonNode json) => string.Join(",", json["keys"]!.AsArray().Select(k => k!.GetValue<string>()));

            Key('j', ConsoleKey.J);
            Assert(!File.Exists(trace), "ordinary j must not publish trace");
            Console.Out.WriteLine("PASS C1516 ordinary");
            events.Clear();
            Key('\r', ConsoleKey.Enter);
            var text = File.ReadAllText(trace);
            var json = JsonNode.Parse(text)!;
            Assert(json["applied"]?.GetValue<string>() == "xhigh" && json["composer"]!.GetValue<string>() == "" && json["sequence"]!.GetValue<int>() == 1
                && json["phase"]!.GetValue<string>() == "enter", "coherent Enter snapshot");
            Assert(Keys(json) == "j,Enter", "Enter exact key prefix");
            Assert(events.Count == 2 && events[0].StartsWith("paint:", StringComparison.Ordinal) && events[1] == "publish:enter", "Enter paints before publication");
            Console.Out.WriteLine("PASS C1516 enter");
            foreach (var character in "zz12345678")
            {
                Key(character, ConsoleKey.A);
                Assert(File.ReadAllText(trace) == text, "ordinary token characters must not publish trace");
            }
            Console.Out.WriteLine("PASS C1516 token");
            events.Clear();
            Key((char)21, ConsoleKey.U);
            json = Read();
            Assert(json["composer"]!.GetValue<string>() == "" && json["sequence"]!.GetValue<int>() == 2 && json["phase"]!.GetValue<string>() == "clear"
                && json["applied"]?.GetValue<string>() == "xhigh", "coherent clear snapshot");
            Assert(Keys(json) == "j,Enter,z,z,1,2,3,4,5,6,7,8,Ctrl+U", "every key retained");
            Assert(events.Count == 2 && events[0].StartsWith("paint:", StringComparison.Ordinal) && events[1] == "publish:clear", "clear paints before publication");
            Assert(json["clearPainted"] is not null, "clear publication includes paint observation");
            Console.Out.WriteLine("PASS C1516 clear");
            Console.Out.WriteLine("C487: 4 passed, 0 failed, 4 rows");
            Console.Out.WriteLine("C487 HARNESS EXIT CODE: 0");
            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException)
        {
            Console.Out.WriteLine("FAIL C1516 " + error.Message);
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
