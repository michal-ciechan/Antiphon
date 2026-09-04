using Antiphon.Messaging.Tests.FakeSlack;
using Antiphon.Messaging.Tests.FakeTelegram;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// CARD-0249: concurrent fake-server starts must not collide on a reserved-then-released port.
/// </summary>
public sealed class FakeServerPortTests
{
    [Test]
    public async Task Concurrent_telegram_and_slack_starts_bind_distinct_ports()
    {
        const int n = 24;
        var telegrams = Enumerable.Range(0, n).Select(_ => new FakeTelegramServer()).ToArray();
        var slacks = Enumerable.Range(0, n).Select(_ => new FakeSlackServer()).ToArray();
        try
        {
            await Task.WhenAll(
                telegrams.Select(s => s.StartAsync())
                    .Concat(slacks.Select(s => s.StartAsync())));

            var urls = telegrams.Select(s => s.BaseUrl)
                .Concat(slacks.Select(s => s.BaseUrl))
                .ToArray();
            urls.Distinct().Count().ShouldBe(n * 2);
            foreach (var url in urls)
                url.ShouldStartWith("http://127.0.0.1:");
        }
        finally
        {
            foreach (var s in telegrams)
                await s.DisposeAsync();
            foreach (var s in slacks)
                await s.DisposeAsync();
        }
    }
}
