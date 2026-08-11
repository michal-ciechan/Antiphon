using System.Text;
using System.Text.Json;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Drives <c>probes/stdin-probe.js</c> — a Node peer on the real ConPTY path — and returns what it
/// received. The point of a NODE peer: the real Claude is Node, and Node's Windows stdin goes
/// through libuv's TTY reader, not the Console/ReadFile path the .NET fake Claude uses. A shortfall
/// measured here is the platform losing bytes; no shortfall here puts the loss in the TUI's own JS.
/// </summary>
public sealed record ProbeResult(
    int Bytes,
    int Chunks,
    int Turns,
    int MaxChunksPerTurn,
    int[] Sizes,
    int[] DistinctSizes,
    int LinesSeen,
    int HighestLine,
    int MissingCount,
    int[][] MissingRuns,
    bool HasPasteStart,
    bool HasPasteEnd,
    int CrCount)
{
    public override string ToString() =>
        $"bytes={Bytes} chunks={Chunks} lines={LinesSeen}/{HighestLine + 1} missing={MissingCount} "
        + $"sizes=[{string.Join(",", DistinctSizes)}]";
}

public sealed class NodeStdinProbe : IAsyncDisposable
{
    private readonly PtyAgentRunner _runner;

    private NodeStdinProbe(PtyAgentRunner runner) => _runner = runner;

    public PtyAgentRunner Runner => _runner;

    public static string ProbePath =>
        Path.Combine(AppContext.BaseDirectory, "probes", "stdin-probe.js");

    public static bool NodeAvailable => ResolveNode() is not null;

    /// <summary>
    /// The JS runtime to drive. Defaults to node.exe, but the real Claude CLI is a <b>Bun</b>
    /// single-file executable (<c>claude.exe</c> is Bun v1.4.0), and Bun does not share Node's
    /// Windows console reader — so the runtime is a variable, not a detail. Point
    /// <c>CARD27_RUNTIME</c> at a bun.exe to measure the one that matters.
    /// </summary>
    public static string? RuntimeOverride =>
        Environment.GetEnvironmentVariable("CARD27_RUNTIME") is { Length: > 0 } p && File.Exists(p)
            ? p
            : null;

    public static string RuntimeName =>
        Path.GetFileNameWithoutExtension(RuntimeOverride ?? ResolveNode() ?? "node");

    private static string? ResolveNode()
    {
        if (RuntimeOverride is { } over) return over;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir, "node.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    public static async Task<NodeStdinProbe> StartAsync(
        bool raw = true,
        int blockMs = 0,
        bool chunkLog = true,
        int cols = 120,
        int rows = 30)
    {
        var node = ResolveNode() ?? throw new InvalidOperationException("node.exe not on PATH");
        var runner = new PtyAgentRunner();
        var env = new Dictionary<string, string>
        {
            ["PROBE_RAW"] = raw ? "1" : "0",
            ["PROBE_BLOCK_MS"] = blockMs.ToString(),
            ["PROBE_CHUNKLOG"] = chunkLog ? "1" : "0",
        };
        await runner.StartAsync(node, [ProbePath], cwd: AppContext.BaseDirectory, env: env, cols: cols, rows: rows);
        var ready = await runner.WaitForOutputAsync(s => s.Contains("PROBE-READY"), TimeSpan.FromSeconds(30));
        if (!ready)
            throw new InvalidOperationException("probe never announced readiness: " + runner.SnapshotText());
        await Task.Delay(150);
        runner.ClearLiveBuffer();
        return new NodeStdinProbe(runner);
    }

    /// <summary>Every line carries its index, so a gap names the exact span that vanished.</summary>
    public static string MarkedBody(int lines, int pad = 20) =>
        string.Join("\n", Enumerable.Range(0, lines).Select(i => $"L{i:D4} {new string('x', pad)}"));

    /// <summary>A body of approximately <paramref name="targetBytes"/> ASCII bytes.</summary>
    public static string MarkedBodyOfBytes(int targetBytes)
    {
        const int lineLen = 27; // "L0000 " + 20x + "\n"
        var lines = Math.Max(1, targetBytes / lineLen);
        var body = MarkedBody(lines);
        return body.Length < targetBytes ? body + new string('y', targetBytes - body.Length) : body;
    }

    /// <summary>Writes raw text straight through, no encoding applied.</summary>
    public Task WriteRawAsync(string data) => _runner.WriteAsync(data);

    /// <summary>
    /// Sends a body exactly the way production does (LF-normalize, bracket-wrap, delayed separate
    /// CR) and returns what the probe reports it received.
    /// </summary>
    public async Task<ProbeResult> DeliverAsync(
        string body,
        bool wrap = true,
        int delayBeforeCrMs = 20,
        TimeSpan? timeout = null)
    {
        _runner.ClearLiveBuffer();
        var payload = wrap
            ? PtyInputEncoding.EncodeBody(body)
            : PtyInputEncoding.NormalizeBody(body);
        await _runner.WriteAsync(payload);
        if (delayBeforeCrMs > 0) await Task.Delay(delayBeforeCrMs);
        // The sentinel triggers the report; sent as its own write so it is never part of the body.
        await _runner.WriteAsync("\nPROBE-REPORT\r");
        return await ReadReportAsync(timeout ?? TimeSpan.FromSeconds(30));
    }

    public async Task<ProbeResult> ReadReportAsync(TimeSpan timeout)
    {
        var got = await _runner.WaitForOutputAsync(s => s.Contains("PROBE-SUMMARY "), timeout);
        var text = _runner.SnapshotText();
        if (!got) throw new TimeoutException("no PROBE-SUMMARY within timeout. Output:\n" + Tail(text, 2000));
        return Parse(text);
    }

    public string Output => _runner.SnapshotText();

    public void Clear() => _runner.ClearLiveBuffer();

    private static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];

    private static ProbeResult Parse(string output)
    {
        var idx = output.LastIndexOf("PROBE-SUMMARY ", StringComparison.Ordinal);
        var tail = output[(idx + "PROBE-SUMMARY ".Length)..];
        // ConPTY wraps at the terminal width and injects CR/LF + erase sequences into the echoed
        // line; strip everything that is not part of the JSON before parsing.
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in tail)
        {
            if (ch == '{') depth++;
            if (depth > 0 && ch is not ('\r' or '\n' or '\x1b')) sb.Append(ch);
            if (ch == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        var json = sb.ToString();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new ProbeResult(
            r.GetProperty("bytes").GetInt32(),
            r.GetProperty("chunks").GetInt32(),
            r.GetProperty("turns").GetInt32(),
            r.GetProperty("maxChunksPerTurn").GetInt32(),
            r.GetProperty("sizes").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
            r.GetProperty("distinctSizes").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
            r.GetProperty("linesSeen").GetInt32(),
            r.GetProperty("highestLine").GetInt32(),
            r.GetProperty("missingCount").GetInt32(),
            r.GetProperty("missingRuns").EnumerateArray()
                .Select(e => e.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray(),
            r.GetProperty("hasPasteStart").GetBoolean(),
            r.GetProperty("hasPasteEnd").GetBoolean(),
            r.GetProperty("crCount").GetInt32());
    }

    public async ValueTask DisposeAsync()
    {
        try { await _runner.KillAsync(TimeSpan.FromSeconds(2)); } catch { }
        await _runner.DisposeAsync();
    }
}
