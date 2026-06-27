using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Optional per-session PTY audit dump for post-mortem debugging without a debugger.
///
/// <para>
/// DISABLED by default. It captures every raw PTY output chunk plus periodic screen snapshots, which for
/// a long-running, continuously-repainting TUI (e.g. Claude Code) grows without bound — a runaway dump
/// here once filled a disk with ~894 GB. So it is now: (1) opt-in via <c>ANTIPHON_PTY_AUDIT=1</c>;
/// (2) hard-capped per session (<c>ANTIPHON_PTY_AUDIT_MAX_MB</c>, default 20 MB); and (3) self-cleaning —
/// audit directories older than <c>ANTIPHON_PTY_AUDIT_RETAIN_DAYS</c> (default 2) are pruned.
/// </para>
///
/// Layout:  %TEMP%\antiphon-pty-audits\&lt;timestamp&gt;-&lt;shortid&gt;\
///   meta.txt, timeline.txt (capped), snapshots.txt (capped), final.txt
/// </summary>
public sealed class PtySessionAudit : IAsyncDisposable
{
    private const string RootName = "antiphon-pty-audits";

    private static readonly bool s_enabled =
        Environment.GetEnvironmentVariable("ANTIPHON_PTY_AUDIT") == "1";
    private static readonly long s_maxBytes =
        Math.Max(1, ReadInt("ANTIPHON_PTY_AUDIT_MAX_MB", 20)) * 1024L * 1024L;
    private static readonly int s_retainDays = Math.Max(0, ReadInt("ANTIPHON_PTY_AUDIT_RETAIN_DAYS", 2));
    private static int s_prunedOnce;

    private readonly string _dir;
    private readonly StreamWriter _timeline;
    private readonly Func<string> _getSnapshot;
    private long _remainingBytes = s_maxBytes;
    private int _capNoted;
    private Timer? _snapshotTimer;

    public string Directory => _dir;

    /// <summary>Creates an audit for this session, or returns <c>null</c> when auditing is disabled.</summary>
    public static PtySessionAudit? Create(
        string app,
        string[] args,
        string? cwd,
        IDictionary<string, string>? env,
        Func<string> getSnapshot,
        TimeSpan? snapshotInterval = null)
    {
        if (!s_enabled)
            return null;

        // Prune stale audit directories once per process (best-effort, off the hot path).
        if (Interlocked.Exchange(ref s_prunedOnce, 1) == 0)
            _ = Task.Run(() => PruneOldAudits(TimeSpan.FromDays(s_retainDays)));

        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
        var dir = Path.Combine(Path.GetTempPath(), RootName, id);
        System.IO.Directory.CreateDirectory(dir);

        var meta = new StringBuilder();
        meta.AppendLine($"StartedAt : {DateTime.UtcNow:O}");
        meta.AppendLine($"App       : {app}");
        meta.AppendLine($"Args      : {string.Join(" ", args.Select(a => $"\"{a}\""))}");
        meta.AppendLine($"Cwd       : {cwd ?? Environment.CurrentDirectory}");
        meta.AppendLine($"Pid       : {Environment.ProcessId}");
        meta.AppendLine($"CapBytes  : {s_maxBytes}");
        if (env is { Count: > 0 })
        {
            meta.AppendLine("Env:");
            foreach (var kv in env)
                meta.AppendLine($"  {kv.Key}={kv.Value}");
        }
        File.WriteAllText(Path.Combine(dir, "meta.txt"), meta.ToString(), Encoding.UTF8);

        var audit = new PtySessionAudit(dir, getSnapshot);
        var interval = snapshotInterval ?? TimeSpan.FromSeconds(2);
        audit._snapshotTimer = new Timer(_ => audit.WriteSnapshot(), null, interval, interval);
        return audit;
    }

    /// <summary>
    /// Deletes audit directories under <c>%TEMP%\antiphon-pty-audits</c> last written more than
    /// <paramref name="maxAge"/> ago. Safe to call on startup even when auditing is disabled, so old
    /// dumps get cleaned up regardless. Best-effort: never throws.
    /// </summary>
    public static void PruneOldAudits(TimeSpan maxAge) =>
        PruneOldAudits(Path.Combine(Path.GetTempPath(), RootName), maxAge);

    /// <summary>Prunes audit directories under an explicit <paramref name="root"/> (for tests).</summary>
    public static void PruneOldAudits(string root, TimeSpan maxAge)
    {
        try
        {
            if (!System.IO.Directory.Exists(root))
                return;

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var dir in System.IO.Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (System.IO.Directory.GetLastWriteTimeUtc(dir) < cutoff)
                        System.IO.Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // Skip dirs that are locked / in use.
                }
            }
        }
        catch
        {
            // Never let cleanup crash the host.
        }
    }

    private PtySessionAudit(string dir, Func<string> getSnapshot)
    {
        _dir = dir;
        _getSnapshot = getSnapshot;
        _timeline = new StreamWriter(Path.Combine(dir, "timeline.txt"), append: false, Encoding.UTF8)
            { AutoFlush = true };
    }

    public void RecordChunk(string raw)
    {
        if (!TryReserve(raw.Length + 16))
            return;
        var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        // Escape newlines so each timeline entry stays on one "record" line, while staying readable.
        try { _timeline.WriteLine($"[{ts}] {raw}"); } catch { }
    }

    private void WriteSnapshot()
    {
        try
        {
            var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            var clean = AnsiStripper.Clean(_getSnapshot()) ?? "";
            var entry = $"\n=== {ts} ===\n{clean}\n";
            if (!TryReserve(entry.Length))
                return;
            File.AppendAllText(Path.Combine(_dir, "snapshots.txt"), entry, Encoding.UTF8);
        }
        catch { /* never let audit I/O crash the runner */ }
    }

    // Returns true while the per-session byte budget allows another write; once exhausted, writes a single
    // "[capped]" marker to the timeline and refuses all further writes.
    private bool TryReserve(int bytes)
    {
        if (Interlocked.Add(ref _remainingBytes, -bytes) >= 0)
            return true;

        if (Interlocked.Exchange(ref _capNoted, 1) == 0)
        {
            try { _timeline.WriteLine($"[audit capped at {s_maxBytes / (1024 * 1024)} MB — further output omitted]"); }
            catch { }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        try { _snapshotTimer?.Dispose(); } catch { }
        try
        {
            var clean = AnsiStripper.Clean(_getSnapshot()) ?? "";
            await File.WriteAllTextAsync(Path.Combine(_dir, "final.txt"), clean, Encoding.UTF8);
        }
        catch { }
        try { await _timeline.DisposeAsync(); } catch { }
    }

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;
}
