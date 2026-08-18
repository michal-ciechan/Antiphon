using System.Runtime.InteropServices;
using System.Text.Json;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Launch/observe helpers for headed canaries against the REAL Grok Build TUI (grok.exe), the
/// counterpart of <see cref="ClSession"/> for Claude. Grok's ground truth is the ACP update stream
/// the TUI persists live to <c>~/.grok/sessions/&lt;url-enc-cwd&gt;/&lt;session-id&gt;/updates.jsonl</c>
/// (verified layout, grok 1.0.5) — these helpers read that file the way S2's tailer will, so a
/// canary's verdict is about the exact rows production would consume.
/// </summary>
internal static class GkSession
{
    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(ClSession.EnvFlag) != "1")
            throw new SkipTestException($"Set {ClSession.EnvFlag}=1 to opt in to headed-grok tests");
        if (!File.Exists(GrokExePath))
            throw new SkipTestException($"grok.exe not found at {GrokExePath}");
    }

    public static string GrokExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");

    public static string DefaultGrokHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");

    /// <summary>
    /// The production launch shape: AgentRegistry's ArgsTemplate is
    /// <c>--always-approve --no-alt-screen</c> and AgentSessionService appends
    /// <c>--session-id &lt;id&gt;</c>. Canaries must measure the TUI Antiphon actually runs.
    /// </summary>
    public static string[] LaunchArgs(string sessionId, params string[] extra) =>
        new[] { "--always-approve", "--no-alt-screen", "--session-id", sessionId }
            .Concat(extra).ToArray();

    public static string SessionDirectory(string grokHome, string cwd, string sessionId) =>
        Path.Combine(grokHome, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId);

    public static string UpdatesPath(string grokHome, string cwd, string sessionId) =>
        Path.Combine(SessionDirectory(grokHome, cwd, sessionId), "updates.jsonl");

    /// <summary>
    /// Reads every complete row of an updates.jsonl that grok is still appending to. Grok keeps the
    /// file (plus an <c>updates.jsonl.lock</c>) open for the whole session, so the read must share
    /// write access; a half-written trailing line is skipped, not an error.
    /// </summary>
    public static List<GrokUpdateRow> ReadUpdates(string updatesPath)
    {
        var rows = new List<GrokUpdateRow>();
        if (!File.Exists(updatesPath)) return rows;
        using var stream = new FileStream(
            updatesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (Parse(line) is { } row) rows.Add(row);
        }
        return rows;
    }

    private static GrokUpdateRow? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            string? kind = null, text = null, stopReason = null;
            long? agentTsMs = null;
            if (root.TryGetProperty("params", out var p))
            {
                if (p.TryGetProperty("update", out var u))
                {
                    kind = u.TryGetProperty("sessionUpdate", out var k) ? k.GetString() : null;
                    if (u.TryGetProperty("content", out var c)
                        && c.ValueKind == JsonValueKind.Object
                        && c.TryGetProperty("text", out var t))
                    {
                        text = t.GetString();
                    }
                    if (u.TryGetProperty("stop_reason", out var sr)) stopReason = sr.GetString();
                }
                if (p.TryGetProperty("_meta", out var meta)
                    && meta.TryGetProperty("agentTimestampMs", out var ts)
                    && ts.TryGetInt64(out var tsv))
                {
                    agentTsMs = tsv;
                }
            }
            return new GrokUpdateRow(line, method, kind, text, stopReason, agentTsMs);
        }
        catch (JsonException)
        {
            return null; // trailing half-written line while grok appends
        }
    }

    /// <summary>
    /// Grok has no fixed ready banner we can key on, so ready is: some output, then the stream goes
    /// quiet, and the process is still alive. The canaries print the screen at that point — the
    /// verdicts themselves are made against updates.jsonl, not this heuristic.
    /// </summary>
    public static async Task WaitForReadyAsync(PtyAgentRunner runner)
    {
        (await runner.WaitForOutputAsync(s => s.Length > 50, TimeSpan.FromSeconds(30)))
            .ShouldBeTrueOrSkip("grok TUI produced no startup output");
        await runner.WaitForQuietAsync(TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(45));
        if (runner.Exited.IsCompleted)
            throw new SkipTestException(
                "grok exited during startup (exit " + runner.Exited.Result + "). Output:\n" + runner.SnapshotText());
    }

    private static void ShouldBeTrueOrSkip(this bool condition, string reason)
    {
        if (!condition) throw new SkipTestException(reason);
    }

    /// <summary>
    /// Measurements go to the console AND a per-test file under TestOutput/GrokCanary — the
    /// console capture of a PASSED headed test is not shown anywhere, and a canary whose
    /// measurements are unreadable has spent real Grok turns for nothing.
    /// </summary>
    public static Action<string> MeasurementLog(string testName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "GrokCanary");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, testName + ".log");
        File.WriteAllText(path, $"# {testName} {DateTime.UtcNow:O}{Environment.NewLine}");
        return line =>
        {
            Console.WriteLine(line);
            File.AppendAllText(path, line + Environment.NewLine);
        };
    }

    public static string TempCwd() =>
        Directory.CreateTempSubdirectory("antiphon-grok-canary").FullName;

    public static void BestEffortDelete(string? dir)
    {
        if (dir is null) return;
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    public static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];

    public static string Truncate(string? s, int n) =>
        s is null ? "<null>" : s.Length <= n ? s : s[..n] + "…";
}

internal sealed record GrokUpdateRow(
    string Raw, string? Method, string? Kind, string? Text, string? StopReason, long? AgentTimestampMs);
