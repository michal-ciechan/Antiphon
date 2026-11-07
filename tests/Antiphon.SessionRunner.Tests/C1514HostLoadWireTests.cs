using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(10_000), NotInParallel("C1514RunnerWire")]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadWireTests(C1514HostLoadFixture fixture)
{
    [Test]
    public async Task HistoryRoutePreservesCursorAndBounds()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var spool = new HostLoadSpool(settings.HistoryPath!, settings, clock);
        for (var sequence = 1; sequence <= 121; sequence++) spool.Append(C1514HostLoadFixture.Sample(spool, sequence));
        await fixture.SetHistoryAsync(spool);
        using var http = new HttpClient { BaseAddress = await fixture.AddressAsync() };
        var direct = (await http.GetFromJsonAsync<HostLoadHistoryPage>("host-stats/history?limit=120", PhoneHomeFraming.Json))!;
        direct.Samples.Count.ShouldBe(120); direct.More.ShouldBeTrue();
        direct.Samples.First().Source.Sequence.ShouldBe(1); direct.Samples.Last().Source.Sequence.ShouldBe(120);
        var dispatcher = Dispatcher(spool);
        var frame = await dispatcher.DispatchAsync(Request(new(null, 120)), CancellationToken.None);
        frame.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        var phone = frame.Payload!.Value.Deserialize<HostLoadHistoryPage>(PhoneHomeFraming.Json)!;
        phone.NextCursor.ShouldBe(direct.NextCursor); phone.Samples.Select(s => s.Source).ShouldBe(direct.Samples.Select(s => s.Source));
        (await http.GetAsync("host-stats/history?limit=121")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await http.GetAsync("host-stats/history?after=foreign")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await dispatcher.DispatchAsync(Request(new("foreign", 120)), CancellationToken.None)).StatusCode.ShouldBe(400);
        Should.Throw<ArgumentException>(() => spool.Append(C1514HostLoadFixture.Sample(spool, 122) with { SchemaVersion = 2 }));
        var next = (await http.GetFromJsonAsync<HostLoadHistoryPage>("host-stats/history?after=" + Uri.EscapeDataString(direct.NextCursor!), PhoneHomeFraming.Json))!;
        next.Samples.Single().Source.Sequence.ShouldBe(121);
        var largeSettings = C1514HostLoadFixture.Config(fixture.PrivatePath());
        var large = new HostLoadSpool(largeSettings.HistoryPath!, largeSettings, clock);
        var processes = Enumerable.Range(0, 7000).Select(_ => new HostLoadProcess("session", null, 250, 1024)).ToArray();
        large.Append(C1514HostLoadFixture.Sample(large, 1) with { Processes = processes });
        large.Append(C1514HostLoadFixture.Sample(large, 2) with { Processes = processes });
        var bounded = large.ReadHistory(null, 120);
        bounded.Samples.Count.ShouldBe(1); bounded.More.ShouldBeTrue();
        JsonSerializer.SerializeToUtf8Bytes(bounded, PhoneHomeFraming.Json).Length.ShouldBeLessThanOrEqualTo(1048576);
    }
    [Test]
    public async Task CapabilitiesAndOrdinalsStayCompatible()
    {
        var enabled = HostStatsRoutes.CapabilityFeatures([], new HostStatsSettings());
        enabled.ShouldContain("hostStatsV1"); enabled.ShouldContain("hostLoadHistoryV1");
        HostStatsRoutes.CapabilityFeatures([], new HostStatsSettings { HistoryEnabled = false }).ShouldNotContain("hostLoadHistoryV1");
        HostStatsRoutes.CapabilityFeatures([], new HostStatsSettings { Enabled = false }).ShouldBeEmpty();
        ((int)PhoneHomeOperation.HostStats).ShouldBe(29); ((int)PhoneHomeOperation.HostStatsSeries).ShouldBe(30);
        ((int)PhoneHomeOperation.DecisionPathInspect).ShouldBe(49); ((int)PhoneHomeOperation.HostLoadHistory).ShouldBe(50);
        var unsupported = Dispatcher(null);
        unsupported.Capabilities().Features!.ShouldNotContain("hostLoadHistoryV1");
        (await unsupported.DispatchAsync(Request(new()), CancellationToken.None)).ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath());
        var spool = new HostLoadSpool(settings.HistoryPath!, settings, new FakeTimeProvider(C1514HostLoadFixture.T0));
        Dispatcher(spool).Capabilities().Features!.ShouldContain("hostLoadHistoryV1");
        await fixture.SetHistoryAsync(spool, enabled: false);
        using var http = new HttpClient { BaseAddress = await fixture.AddressAsync() };
        (await http.GetAsync("host-stats/history")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.Settings.HistoryEnabled = true;
    }
    private static PhoneHomeCommandDispatcher Dispatcher(IHostLoadHistorySource? source) => new(
        new PhoneHomeCommandDispatcherTests.RecordingRuntime(), new PhoneHomeSettings(), hostLoad: source);
    private static PhoneHomeFrame Request(PhoneHomeHostLoadHistoryRequest body) => new(PhoneHomeFrameKind.Request, 1,
        Guid.NewGuid(), PhoneHomeOperation.HostLoadHistory, JsonSerializer.SerializeToElement(body, PhoneHomeFraming.Json));
}
