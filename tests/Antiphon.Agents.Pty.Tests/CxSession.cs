using System.Runtime.InteropServices;
using System.Text.Json;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Launch/observe helpers for headed canaries against the REAL Codex CLI, the counterpart of
/// <see cref="ClSession"/> (Claude) and <see cref="GkSession"/> (Grok). Codex's ground truth is the
/// rollout JSONL it writes per session under <c>CODEX_HOME/sessions/YYYY/MM/DD/</c>; these helpers
/// read it the way <c>CodexTranscriptTailer</c> does, so a canary's verdict is about the exact rows
/// production consumes.
///
/// <para><b>Finding the CLI.</b> The registry definition still names <c>cx.ps1</c>, a wrapper that
/// does not exist on this machine — which is why every pre-existing headed-Codex test skips
/// (CARD-0099 plan, section 0; the definition is S3's to fix). This gate prefers <c>cx.ps1</c> when
/// it is there and falls back to the npm shim <c>%APPDATA%\npm\codex.cmd</c>, so S1's canary can
/// run against the real CLI today without waiting for S3.</para>
/// </summary>
internal static class CxSession
{
    public const string EnvFlag = "ANTIPHON_CODEX_HEADED_TESTS";

    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed Codex tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed-codex tests");
        if (ResolveCli() is null)
            throw new SkipTestException("No Codex CLI found (cx.ps1 or %APPDATA%\\npm\\codex.cmd)");
    }

    /// <summary>cx.ps1 first (the registry definition's shape), then the npm shim.</summary>
    public static string? ResolveCli()
    {
        var wrapper = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "cx.ps1");
        if (File.Exists(wrapper))
            return wrapper;

        var shim = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
        return File.Exists(shim) ? shim : null;
    }

    /// <summary>
    /// The production launch shape: the registry's ArgsTemplate is
    /// <c>--no-alt-screen --dangerously-bypass-approvals-and-sandbox</c>. A <c>.ps1</c> wrapper runs
    /// under pwsh and a <c>.cmd</c> shim under cmd.exe, the same resolution
    /// <c>HeadedCodexGate.BuildLaunch</c> uses.
    /// </summary>
    public static (string App, string[] Args) BuildLaunch(string cli, params string[] extra)
    {
        var codexArgs = new[] { "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox" }
            .Concat(extra).ToArray();

        if (cli.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return ("pwsh.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", cli }.Concat(codexArgs).ToArray());
        }

        if (cli.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (cli, codexArgs);

        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            new[] { "/d", "/c", cli }.Concat(codexArgs).ToArray());
    }

    public static string DefaultSessionsRoot => Path.Combine(
        Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
        "sessions");

    public static string[] Rollouts() =>
        Directory.Exists(DefaultSessionsRoot)
            ? Directory.GetFiles(DefaultSessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
            : [];

    /// <summary>
    /// Reads every complete row of a rollout Codex is still appending to. Codex keeps the file open
    /// for the whole session, so the read MUST share write access — a plain File.ReadLines on a live
    /// rollout throws "being used by another process" (measured 0.147.0). A half-written trailing
    /// line is skipped, not an error.
    /// </summary>
    public static List<CodexRolloutRow> ReadRollout(string path)
    {
        var rows = new List<CodexRolloutRow>();
        if (!File.Exists(path))
            return rows;

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (Parse(line) is { } row)
                rows.Add(row);
        }

        return rows;
    }

    private static CodexRolloutRow? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var type = Str(root, "type");
            var payload = root.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object
                ? p
                : default;
            var eventType = payload.ValueKind == JsonValueKind.Object ? Str(payload, "type") : null;

            string? itemType = null, text = null;
            if (eventType == "item_completed"
                && payload.TryGetProperty("item", out var item)
                && item.ValueKind == JsonValueKind.Object)
            {
                itemType = Str(item, "type");
                text = ContentText(item);
            }
            else if (eventType is "user_message" or "agent_message")
            {
                text = Str(payload, "message");
            }

            string? cwd = null, originator = null;
            if (type == "session_meta" && payload.ValueKind == JsonValueKind.Object)
            {
                cwd = Str(payload, "cwd");
                originator = Str(payload, "originator");
            }

            return new CodexRolloutRow(line, type, eventType, itemType, text, cwd, originator);
        }
        catch (JsonException)
        {
            return null; // partial line mid-write
        }
    }

    private static string? ContentText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        var parts = content.EnumerateArray()
            .Where(b => b.ValueKind == JsonValueKind.Object)
            .Select(b => Str(b, "text"))
            .Where(t => !string.IsNullOrEmpty(t));
        var joined = string.Concat(parts);
        return joined.Length == 0 ? null : joined;
    }

    private static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Waits for the composer to render, answering the startup modals Codex can park on — and only
    /// those, by shape.
    ///
    /// <para>Two were measured, and they can appear one after the other, so this loop keeps
    /// answering rather than dismissing once: the per-cwd <b>trust dialog</b> on a first launch into
    /// an unseen directory (answered with Enter, which is the default "Yes, continue" — the same
    /// answer production's <see cref="CodexTrustPromptDetector"/> gives), and an <b>"Update
    /// available"</b> prompt whose DEFAULT option runs <c>npm install -g @openai/codex</c>, so it is
    /// answered with "2" (Skip) and never with a bare Enter. Any other modal is left standing: a
    /// numbered menu is far too weak a shape to type a digit into blindly.</para>
    ///
    /// <para>Why this matters beyond test plumbing: a modal swallows keystrokes with no output of
    /// its own, so a canary that started typing on a quiet period alone silently lost its leading
    /// characters (measured — "Reply with exactly…" reached the composer as "with exactly…" after
    /// the trust dialog ate the first five). That is the CARD-0047 shape, in a different TUI.</para>
    /// </summary>
    public static async Task<bool> WaitForComposerAsync(PtyAgentRunner runner, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lastAnswer = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            var screen = runner.SnapshotScreen();
            var modal = screen.Contains("Press enter to continue", StringComparison.Ordinal);

            if (screen.Contains("OpenAI Codex", StringComparison.Ordinal) && !modal)
                return true;

            if (modal && DateTime.UtcNow - lastAnswer > TimeSpan.FromSeconds(1))
            {
                lastAnswer = DateTime.UtcNow;
                if (screen.Contains("Update available", StringComparison.OrdinalIgnoreCase))
                {
                    // NOT Enter: option 1 upgrades the CLI out from under the session.
                    await runner.WriteAsync("2");
                    await Task.Delay(200);
                    await runner.WriteAsync("\r");
                }
                else if (CodexTrustPromptDetector.IsVisible(runner.SnapshotText(), screen))
                {
                    await runner.WriteAsync("\r");
                }
            }

            await Task.Delay(250);
        }

        return false;
    }

    public static async Task<string?> WaitForNewRolloutAsync(
        HashSet<string> before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var fresh = Rollouts().FirstOrDefault(f => !before.Contains(f));
            if (fresh is not null)
                return fresh;
            await Task.Delay(250);
        }

        return null;
    }

    public static async Task<CodexRolloutRow?> WaitForRowAsync(
        string rollout, Func<CodexRolloutRow, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ReadRollout(rollout).FirstOrDefault(predicate) is { } row)
                return row;
            await Task.Delay(250);
        }

        return null;
    }

    public static Action<string> MeasurementLog(string testName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestOutput", "CodexCanary");
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
        Directory.CreateTempSubdirectory("antiphon-codex-canary").FullName;

    public static void BestEffortDelete(string? dir)
    {
        if (dir is null) return;
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    public static string Tail(string s, int n) => s.Length <= n ? s : s[^n..];
}

internal sealed record CodexRolloutRow(
    string Raw,
    string? Type,
    string? EventType,
    string? ItemType,
    string? Text,
    string? Cwd,
    string? Originator);
