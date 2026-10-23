using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

internal static class EffortFixtureCompletion
{
    internal static async Task<string> WaitAsync(Func<string> screen, Func<Task<string>> trace,
        Func<TimeSpan> elapsed, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var text = await trace();
        using var json = JsonDocument.Parse(text);
        if (!EmptyComposer(screen()) || json.RootElement.GetProperty("composer").GetString() != ""
            || json.RootElement.GetProperty("phase").GetString() != "clear")
            throw new InvalidOperationException("screen and coherent trace are not clear");
        return text;
    }

    internal static bool EmptyComposer(string screen) => screen.ReplaceLineEndings("\n").Split('\n')
        .Any(line => line.TrimEnd() == ">");
}
