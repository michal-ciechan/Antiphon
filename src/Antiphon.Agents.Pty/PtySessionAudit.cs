using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Writes a self-contained audit directory for every PTY session so that test
/// failures can be post-mortemed without a debugger.
///
/// Layout:  %TEMP%\antiphon-pty-audits\&lt;timestamp&gt;-&lt;shortid&gt;\
///   meta.txt       — app, args, cwd, env, start time
///   timeline.txt   — every PTY output chunk with a UTC timestamp prefix
///   snapshots.txt  — periodic (default 1 s) clean-text snapshots of the screen
///   final.txt      — last clean-text snapshot written on dispose
/// </summary>
public sealed class PtySessionAudit : IAsyncDisposable
{
    private readonly string _dir;
    private readonly StreamWriter _timeline;
    private readonly Func<string> _getSnapshot;
    private Timer? _snapshotTimer;

    public string Directory => _dir;

    public static PtySessionAudit Create(
        string app,
        string[] args,
        string? cwd,
        IDictionary<string, string>? env,
        Func<string> getSnapshot,
        TimeSpan? snapshotInterval = null)
    {
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
        var dir = Path.Combine(Path.GetTempPath(), "antiphon-pty-audits", id);
        System.IO.Directory.CreateDirectory(dir);

        var meta = new StringBuilder();
        meta.AppendLine($"StartedAt : {DateTime.UtcNow:O}");
        meta.AppendLine($"App       : {app}");
        meta.AppendLine($"Args      : {string.Join(" ", args.Select(a => $"\"{a}\""))}");
        meta.AppendLine($"Cwd       : {cwd ?? Environment.CurrentDirectory}");
        meta.AppendLine($"Pid       : {Environment.ProcessId}");
        if (env is { Count: > 0 })
        {
            meta.AppendLine("Env:");
            foreach (var kv in env)
                meta.AppendLine($"  {kv.Key}={kv.Value}");
        }
        File.WriteAllText(Path.Combine(dir, "meta.txt"), meta.ToString(), Encoding.UTF8);

        var audit = new PtySessionAudit(dir, getSnapshot);
        audit._snapshotTimer = new Timer(
            _ => audit.WriteSnapshot(),
            null,
            snapshotInterval ?? TimeSpan.FromSeconds(1),
            snapshotInterval ?? TimeSpan.FromSeconds(1));
        return audit;
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
        var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        // Escape newlines so each timeline entry stays on one "record" line,
        // while still being human-readable.
        _timeline.WriteLine($"[{ts}] {raw}");
    }

    private void WriteSnapshot()
    {
        try
        {
            var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            var clean = AnsiStripper.Clean(_getSnapshot()) ?? "";
            File.AppendAllText(
                Path.Combine(_dir, "snapshots.txt"),
                $"\n=== {ts} ===\n{clean}\n",
                Encoding.UTF8);
        }
        catch { /* never let audit I/O crash the runner */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { _snapshotTimer?.Dispose(); } catch { }
        // Final clean snapshot
        try
        {
            var clean = AnsiStripper.Clean(_getSnapshot()) ?? "";
            await File.WriteAllTextAsync(Path.Combine(_dir, "final.txt"), clean, Encoding.UTF8);
        }
        catch { }
        try { await _timeline.DisposeAsync(); } catch { }
    }
}
