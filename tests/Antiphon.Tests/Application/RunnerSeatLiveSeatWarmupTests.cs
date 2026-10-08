using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using LiveSeat = Antiphon.Tests.Application.RunnerSeatReleaseFixture.LiveSeat;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1137: an HTTP-arm seat must pay its app's first-request endpoint build before
/// a test can spend its 10 s loopback budget, including after either transport restart.</summary>
[Category("Integration")]
public class RunnerSeatLiveSeatWarmupTests
{
    private static readonly string[] Warmed = ["GET /sessions 200"];

    [Test]
    public async Task Http_seat_serves_its_warmup_before_handing_out_the_client()
    {
        var generation = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
        await using var first = await LiveSeat.CreateAsync(Guid.NewGuid(), generation, "Codex", phoneHome: false);
        first.ServedBeforeClient.ShouldBe(Warmed, "the first app in this process");
        var coldFirst = first.WarmupElapsed;

        await using var second = await LiveSeat.CreateAsync(Guid.NewGuid(), generation, "Claude", phoneHome: false);
        second.ServedBeforeClient.ShouldBe(Warmed, "a second app in the same process");
        var coldSecond = second.WarmupElapsed;

        await second.RestartServerTransportAsync();
        second.ServedBeforeClient.ShouldBe(Warmed, "the recreated app after a server transport restart");
        var serverRestart = second.WarmupElapsed;
        await second.RestartRunnerAsync();
        second.ServedBeforeClient.ShouldBe(Warmed, "the recreated app after a runner restart");

        // The handed-out client still reaches the same seat through its own budget.
        var started = Stopwatch.GetTimestamp();
        (await second.Client.ListAsync(default)).ShouldHaveSingleItem().SessionId.ShouldBe(second.SessionId);
        var firstTestCall = Stopwatch.GetElapsedTime(started);
        await second.SubmitAsync("after warm-up");
        second.NativeSubmissions.ShouldBe(["after warm-up"]);

        Console.WriteLine($"C1137-WARMUP firstApp={coldFirst.TotalMilliseconds:F0}ms secondApp={coldSecond.TotalMilliseconds:F0}ms " +
            $"serverRestartApp={serverRestart.TotalMilliseconds:F0}ms runnerRestartApp={second.WarmupElapsed.TotalMilliseconds:F0}ms " +
            $"firstTestCallAfterWarmup={firstTestCall.TotalMilliseconds:F0}ms");
    }
}
