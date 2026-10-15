using Antiphon.HostCleanup.Checkpoints;
using System.Text.Json;

namespace Antiphon.Checkpoints;

public static class CheckpointUsageEvent
{
    public static void Write(string kind, string path, long bytes = 0, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        var target = context.Io.EnvironmentVariable("C804_ROOT_EVENTS");
        if (string.IsNullOrWhiteSpace(target)) return;
        var line = JsonSerializer.Serialize(new
        {
            kind, path = Path.GetFullPath(path), bytes,
            at = context.Time.GetUtcNow(), pid = context.Io.CurrentProcessId,
        }) + "\n";
        var data = System.Text.Encoding.UTF8.GetBytes(line);
        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (string.IsNullOrEmpty(directory))
            return;
        var ownership = write.Under(directory);
        var started = context.Time.GetTimestamp();
        while (true)
        {
            try
            {
                // FileShare.None serializes separate TUnit child hosts as well as
                // parallel tests in one host. A single append is one complete event.
                var reason = write.OpenAppend(target, ownership, out var stream, FileShare.None);
                if (reason is not null || stream is null)
                    return;
                using (stream)
                {
                    stream.Write(data);
                    if (stream is FileStream file) file.Flush(flushToDisk: true); else stream.Flush();
                }
                return;
            }
            catch (IOException) when (context.Time.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
            {
                context.Delay(TimeSpan.FromMilliseconds(2));
            }
        }
    }
}
