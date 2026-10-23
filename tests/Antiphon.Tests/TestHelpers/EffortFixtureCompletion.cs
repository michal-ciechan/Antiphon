using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

internal static class EffortFixtureCompletion
{
    internal static async Task<string> WaitAsync(Func<string> screen, Func<Task<string>> trace,
        Func<TimeSpan> elapsed, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        while (elapsed() < TimeSpan.FromSeconds(16))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var text = await trace();
                using var json = JsonDocument.Parse(text);
                if (EmptyComposer(screen()) && json.RootElement.GetProperty("composer").GetString() == ""
                    && json.RootElement.GetProperty("phase").GetString() == "clear") return text;
            }
            catch (Exception ex) when (ex is FileNotFoundException or JsonException) { }
            var remaining = TimeSpan.FromSeconds(16) - elapsed();
            if (remaining > TimeSpan.Zero)
                await delay(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25), ct);
        }
        throw new TimeoutException("screen and coherent clear trace did not agree within the original 16-second watchdog");
    }

    internal static async Task<string> ReadTraceAsync(string path)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return await reader.ReadToEndAsync();
    }

    internal static bool EmptyComposer(string screen) => screen.ReplaceLineEndings("\n").Split('\n')
        .Any(line => line.TrimEnd() == ">");
}
