using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RunStateStoreTests
{
    [Test]
    public async Task readers_never_see_a_partial_file()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "state.json");
        var store = new RunStateStore();
        var failures = 0;
        var reads = 0;
        var stop = false;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                string text;
                try
                {
                    if (!File.Exists(path))
                        continue;
                    using var stream = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var streamReader = new StreamReader(stream);
                    text = streamReader.ReadToEnd();
                }
                catch (IOException)
                {
                    continue;
                }

                Interlocked.Increment(ref reads);
                if (!IsComplete(text))
                    Interlocked.Increment(ref failures);
            }
        });
        for (var i = 1; i <= 300; i++)
        {
            store.Write(path, new RunState { RunId = i.ToString(), Marker = new string('x', i * 20), Phase = "running" });
            if (i % 4 == 0)
                await Task.Yield();
        }
        Volatile.Write(ref stop, true);
        await reader;
        failures.ShouldBe(0);
        reads.ShouldBeGreaterThan(0);
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").ShouldBeEmpty();
    }

    private static bool IsComplete(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            var state = System.Text.Json.JsonSerializer.Deserialize<RunState>(text, RunStateStore.Json);
            return state is not null && int.TryParse(state.RunId, out var n) && state.Marker.Length == n * 20;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
