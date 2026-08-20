using System.Net;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

/// <summary>
/// The entry point is invoked once per <c>WebApplicationFactory</c>, and this assembly holds
/// several of them, so two invocations can be inside startup at the same time. Startup writes
/// process-global state — Serilog's static <c>Log.Logger</c>, a ReloadableLogger that
/// <c>builder.Build()</c> FREEZES — and interleaved invocations used to make the loser's Build()
/// throw "The logger is already frozen."
///
/// That throw was then swallowed by <c>Program</c>'s catch-all, so the entry point returned
/// without ever starting a host: the factory cached an unstarted <c>TestServer</c> and every
/// later test sharing it failed with "The server has not been started or no web application was
/// configured", naming nothing. Two full-suite runs on 2026-08-20 lost 32 tests each to exactly
/// that, while every one of those classes passed in isolation.
///
/// Both halves of the fix are pinned here: the startup gate (nothing throws) and the rethrow (a
/// failure that DOES happen surfaces instead of leaving a silently dead factory behind).
/// </summary>
[Category("Integration")]
public class ProgramStartupConcurrencyTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Two_entry_point_invocations_racing_on_startup_both_serve(CancellationToken ct)
    {
        // Two rounds: the failure was an interleaving, so one sample is weak evidence.
        for (var round = 0; round < 2; round++)
        {
            await using var first = new AntiphonWebAppFactory();
            await using var second = new AntiphonWebAppFactory();

            // Line both invocations up on the same instant — the race needs them overlapping,
            // not merely both present.
            using var startLine = new Barrier(2);

            async Task<HttpStatusCode> BuildAndCallAsync(AntiphonWebAppFactory factory)
            {
                await Task.Yield();
                startLine.SignalAndWait(ct);
                using var client = factory.CreateClient();
                var response = await client.GetAsync("/api/boards", ct);
                return response.StatusCode;
            }

            var statuses = await Task.WhenAll(
                Task.Run(() => BuildAndCallAsync(first), ct),
                Task.Run(() => BuildAndCallAsync(second), ct));

            statuses[0].ShouldBe(HttpStatusCode.OK, $"round {round}: first host did not serve");
            statuses[1].ShouldBe(HttpStatusCode.OK, $"round {round}: second host did not serve");
        }
    }
}
