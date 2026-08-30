using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Shared skip / sidecar / D1-envelope / K9 helpers for CARD-0187 S3 real-CLI herdr canaries.
/// Live-environment tests: skip, never fail red, on herdr-down or a measured launch/boot stall.
/// </summary>
internal static class HerdrRealCliCanarySupport
{
    public const int LaunchStallSkipSeconds = 180;
    public const int CodexLaunchStallSkipSeconds = 180;

    /// <summary>Body→Enter gap; `ANTIPHON_STUB_ENTER_GAP_MS` overrides the production 20 ms for a measurement.</summary>
    public static int EnterGapMs =>
        int.TryParse(Environment.GetEnvironmentVariable("ANTIPHON_STUB_ENTER_GAP_MS"), out var ms) && ms >= 0 ? ms : 20;

    public static async Task<T> AwaitOrSkipAsync<T>(Task<T> task, TimeSpan budget, string reason)
    {
        var winner = await Task.WhenAny(task, Task.Delay(budget));
        if (winner != task)
            throw new SkipTestException(reason);
        return await task;
    }

    /// <summary>
    /// A skip that fires while StartAsync is still running must not abandon the pane.
    /// Cancel, wait briefly for a result, kill if we got one.
    /// </summary>
    public static async Task<T> AwaitOrSkipAndReapAsync<T>(
        Task<T> task,
        TimeSpan budget,
        string reason,
        Func<T, Task> reapAsync)
    {
        var winner = await Task.WhenAny(task, Task.Delay(budget));
        if (winner == task)
            return await task;

        try
        {
            var late = await task.WaitAsync(TimeSpan.FromSeconds(20));
            try { await reapAsync(late); }
            catch { /* teardown */ }
        }
        catch
        {
            // still running or faulted; nothing to reap
        }

        throw new SkipTestException(reason);
    }

    public static SkipTestException LaunchStallSkip(string kind, int seconds) =>
        new(
            $"Live herdr pipe answered ping but {kind} pane launch did not complete within {seconds}s. "
            + "CARD-0187 S3 skips rather than failing the slice"
            + (string.Equals(kind, "codex", StringComparison.Ordinal)
                ? " (CARD-0195 Codex MCP-boot hang risk)."
                : "."));

    public static SkipTestException BootStallSkip(string kind, string detail) =>
        new(
            $"CARD-0187 S3 {kind} canary: measured launch/boot stall — {detail}. "
            + "Skip-not-fail (CARD-0195); not this card's bug.");

    public static async Task AssertSidecarAndPaneAgentAsync(
        string sessionLogPath,
        Guid sessionId,
        HerdrClient herdrClient,
        string expectedKind,
        CancellationToken ct)
    {
        var sidecarPath = HerdrPaneSidecar.PathFor(sessionLogPath, sessionId);
        if (!File.Exists(sidecarPath))
            throw new InvalidOperationException($"herdr sidecar must exist at {sidecarPath}");

        var sidecar = HerdrPaneSidecar.TryLoad(sidecarPath)
            ?? throw new InvalidOperationException($"herdr sidecar at {sidecarPath} did not deserialize");
        if (string.IsNullOrWhiteSpace(sidecar.PaneId))
            throw new InvalidOperationException("herdr sidecar has no PaneId");

        var pane = await herdrClient.PaneGetAsync(sidecar.PaneId, ct);
        if (!string.Equals(pane.Agent, expectedKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"pane.get.agent == '{pane.Agent ?? "null"}' (expected '{expectedKind}') pane={sidecar.PaneId}");
        }
    }

    public static string BuildMultilineEnvelopeBody(int utf8Bytes, string head, string tail)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(utf8Bytes, 64);
        var prefix = head + "\n";
        var suffix = "\n" + tail;
        var used = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(suffix);
        if (used >= utf8Bytes)
            throw new ArgumentException("head+tail longer than envelope");

        var remaining = utf8Bytes - used;
        const int lineWidth = 80;
        var sb = new StringBuilder(utf8Bytes);
        sb.Append(prefix);
        while (remaining > 0)
        {
            var chunk = Math.Min(lineWidth, remaining);
            var last = remaining <= lineWidth;
            if (!last && chunk == remaining)
                chunk--; // leave room for a newline so we stay multi-line until the tail
            sb.Append('x', chunk);
            remaining -= chunk;
            if (remaining > 0)
            {
                sb.Append('\n');
                remaining--;
            }
        }

        sb.Append(suffix);
        var body = sb.ToString();
        var actual = Encoding.UTF8.GetByteCount(body);
        if (actual != utf8Bytes)
        {
            // Last-resort pad/trim on the payload (ASCII, so bytes==chars).
            if (actual < utf8Bytes)
                body = body.Insert(prefix.Length, new string('x', utf8Bytes - actual));
            else
                body = body.Remove(prefix.Length, actual - utf8Bytes);
        }

        return body;
    }

    /// <summary>
    /// Answers the Codex trust prompt if it renders within ~5 s. Returns whether Enter was sent, so a
    /// caller can answer ONCE: the detector reads cumulative RawOutput and stays true after the
    /// dialog is gone, and a second Enter lands on whatever follows it — on an isolated CODEX_HOME
    /// that is the Windows sandbox NUX prompt, whose default runs elevated setup and disables the
    /// composer (CARD-0133 S0).
    /// </summary>
    public static async Task<bool> AcceptCodexTrustIfVisibleAsync(
        SessionRunnerRuntime runtime, Guid sessionId, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var snap = runtime.GetSnapshot(sessionId);
            if (CodexTrustPromptDetector.IsVisible(snap.RawOutput, snap.RenderedScreen))
            {
                await runtime.SendInputAsync(sessionId, "\r", ct);
                await Task.Delay(500, ct);
                return true;
            }

            await Task.Delay(250, ct);
        }

        return false;
    }

    public static async Task SendWrappedBodyAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string body, CancellationToken ct)
    {
        var normalized = PtyInputEncoding.NormalizeBody(body);
        var payload = PtyInputEncoding.WrapIfMultiline(normalized);
        await runtime.SendInputAsync(sessionId, payload, ct);
        // 20 ms mirrors SessionMessageQueueService's body→Enter gap. CARD-0133 S0 measurement knob:
        // Codex's PasteBurst turns an Enter that lands within 120 ms of a typed burst into a
        // newline (codex-rs/tui/src/bottom_pane/paste_burst.rs, PASTE_ENTER_SUPPRESS_WINDOW).
        await Task.Delay(EnterGapMs, ct);
        await runtime.SendInputAsync(sessionId, "\r", ct);
    }

    public static EnvelopeMeasurement MeasureRecord(string sentBody, string? recordText)
    {
        var sentBytes = Encoding.UTF8.GetByteCount(sentBody);
        if (recordText is null)
            return new EnvelopeMeasurement(sentBytes, RecordBytes: 0, Exact: false, Complete: false, EscInRecord: false, JoinedNewlines: false, RecordText: null);

        var recordBytes = Encoding.UTF8.GetByteCount(recordText);
        var exact = string.Equals(recordText, sentBody, StringComparison.Ordinal);
        var joined = string.Equals(recordText, sentBody.Replace("\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        var complete = PromptSubmissionMatch.IsCompleteIn(sentBody, recordText);
        var esc = recordText.Contains('\u001b');
        return new EnvelopeMeasurement(sentBytes, recordBytes, exact, complete, esc, joined, recordText);
    }

    public static string? TryFindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    public static void WriteProbeResults(string markdown)
    {
        var names = new List<string>();
        var root = TryFindRepoRoot();
        if (root is not null)
            names.Add(Path.Combine(root, ".antiphon", "card-0187-d1-envelope-results.md"));

        var shared = Path.Combine(@"C:\src\Antiphon", ".antiphon", "card-0187-d1-envelope-results.md");
        if (!names.Contains(shared, StringComparer.OrdinalIgnoreCase))
            names.Add(shared);

        foreach (var path in names)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var merged = markdown;
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path);
                    if (existing.Contains("D1 grok", StringComparison.Ordinal)
                        && !markdown.Contains("D1 grok", StringComparison.Ordinal))
                    {
                        merged = MergeProbeMarkdown(existing, markdown);
                    }
                }

                File.WriteAllText(path, merged);
            }
            catch
            {
                // Probe dump is best-effort; the test output still carries the numbers.
            }
        }
    }

    private static string MergeProbeMarkdown(string existing, string incoming)
    {
        var d1Existing = existing.Split("## D1 — composer-input envelope", 2);
        var d1Incoming = incoming.Split("## D1 — composer-input envelope", 2);
        var k9Incoming = incoming.Contains("K9 Codex", StringComparison.Ordinal) ? incoming : existing;
        var header = k9Incoming.Split("## D1 — composer-input envelope", 2)[0];
        var bullets = new List<string>();
        foreach (var part in new[] { d1Existing.ElementAtOrDefault(1), d1Incoming.ElementAtOrDefault(1) })
        {
            if (part is null) continue;
            foreach (var line in part.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("- `", StringComparison.Ordinal) && !bullets.Contains(t))
                    bullets.Add(t);
            }
        }

        return header + "## D1 — composer-input envelope" + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, bullets) + Environment.NewLine;
    }

    public sealed record EnvelopeMeasurement(
        int SentBytes,
        int RecordBytes,
        bool Exact,
        bool Complete,
        bool EscInRecord,
        bool JoinedNewlines,
        string? RecordText);

    public sealed class ProbeLog
    {
        private readonly List<string> _lines = [];
        private readonly object _gate = new();

        public void Add(string line)
        {
            lock (_gate)
            {
                _lines.Add(line);
                Console.WriteLine(line);
            }
        }

        public void AddMeasurement(string kind, int size, EnvelopeMeasurement m, long elapsedMs)
        {
            Add(
                $"D1 {kind} {size}B: sent={m.SentBytes} record={m.RecordBytes} exact={m.Exact} "
                + $"complete={m.Complete} joinedNewlines={m.JoinedNewlines} escInRecord={m.EscInRecord} "
                + $"elapsedMs={elapsedMs}");
        }

        public string RenderMarkdown(string k9Line)
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# CARD-0187 S3 probe D1 envelope + K9 timeout");
                sb.AppendLine();
                sb.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z · **herdr:** 0.8.2 · **lane:** pane.send_text");
                sb.AppendLine("**Bodies:** multi-line ASCII, LF endings, production wrap (`ESC[200~`…`ESC[201~`) then a separate Enter.");
                sb.AppendLine("**Oracle:** UserPrompt transcript record vs sent body. Stub-proxy so no real model turn is spent.");
                sb.AppendLine();
                sb.AppendLine("## K9 — Codex cold-boot / launch-detect");
                sb.AppendLine();
                sb.AppendLine(k9Line);
                sb.AppendLine();
                sb.AppendLine("## D1 — composer-input envelope");
                sb.AppendLine();
                foreach (var line in _lines)
                    sb.AppendLine($"- `{line}`");
                sb.AppendLine();
                return sb.ToString();
            }
        }
    }

    public static readonly ProbeLog Log = new();
    public static string K9Line { get; set; } = "K9 not measured this run.";
}
