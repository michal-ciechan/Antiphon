using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// This locally inherited child runs before test bodies, with the executable's own
// dependency/runtime context. The parent always kills/joins the serving peer.
internal static class HerdrOwnedPeerWorker
{
    [Before(Assembly)]
    public static async Task RunAsync()
    {
        var mode = Environment.GetEnvironmentVariable(HerdrTestProcess.OwnedFixtureMode);
        if (mode is null) return;
        try
        {
            if (mode == "reclaim")
            {
                FakeHerdrEndpoint.ReclaimDeadLeases();
                Environment.Exit(0);
            }
            if (mode != "serve") throw new ArgumentException("Unknown owned Herdr worker mode.");

            await using var endpoint = new FakeHerdrEndpoint();
            await using var transport = new FakeHerdrTransport(endpoint);
            transport.Bind();
            var announced = false;
            while (true)
            {
                await using var stream = await transport.AcceptAsync(CancellationToken.None, () =>
                {
                    if (announced) return;
                    // Bypass TUnit's console capture: this is the parent/child readiness wire.
                    using var output = Console.OpenStandardOutput();
                    output.Write(Encoding.UTF8.GetBytes("READY " + endpoint.Path + "\n"));
                    output.Flush();
                    announced = true;
                });
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
                if (await reader.ReadLineAsync() is { Length: > 0 } line)
                {
                    using var request = JsonDocument.Parse(line);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        id = request.RootElement.GetProperty("id").GetString(),
                        result = new { type = "pong", version = "0.8.2", protocol = 20 }
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            using var error = Console.OpenStandardError();
            error.Write(Encoding.UTF8.GetBytes(ex + "\n"));
            error.Flush();
            Environment.Exit(1);
        }
    }
}
