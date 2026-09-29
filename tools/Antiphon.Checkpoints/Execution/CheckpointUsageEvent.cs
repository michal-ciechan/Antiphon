using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class CheckpointUsageEvent
{
    public static void Write(string kind, string path, long bytes = 0)
    {
        var target = Environment.GetEnvironmentVariable("C804_ROOT_EVENTS");
        if (string.IsNullOrWhiteSpace(target)) return;
        var line = JsonSerializer.Serialize(new
        {
            kind, path = Path.GetFullPath(path), bytes,
            at = DateTimeOffset.UtcNow, pid = Environment.ProcessId,
        }) + "\n";
        using var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var data = System.Text.Encoding.UTF8.GetBytes(line);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }
}
