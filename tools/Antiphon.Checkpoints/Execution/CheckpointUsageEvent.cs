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
        var data = System.Text.Encoding.UTF8.GetBytes(line);
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // FileShare.None serializes separate TUnit child hosts as well as
                // parallel tests in one host. A single append is one complete event.
                using var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.None);
                stream.Write(data);
                stream.Flush(flushToDisk: true);
                return;
            }
            catch (IOException) when (wait.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(2);
            }
        }
    }
}
