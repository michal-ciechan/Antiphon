namespace Antiphon.PtyHost;

/// <summary>
/// Minimal append-only file logger. The host is detached with no console; this file
/// (<c>logs/pty-hosts/&lt;sessionId&gt;.log</c>) is its only diagnostic surface.
/// </summary>
public sealed class HostLog(string? path)
{
    private readonly object _gate = new();

    public void Info(string message) => Write("INF", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERR", ex is null ? message : $"{message}: {ex}");

    private void Write(string level, string message)
    {
        if (path is null)
            return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTime.UtcNow:O} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the host down.
        }
    }
}
