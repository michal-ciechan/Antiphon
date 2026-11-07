using System.Net;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(10_000)]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadAuthorityTests(C1514HostLoadFixture fixture)
{
    [Test]
    public async Task OverrideReadsSharedAuthority()
    {
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.AuthorityId = "shared";
        settings.AuthorityEndpoint = await fixture.AuthorityAsync(c => c.Response.WriteAsJsonAsync(Known(clock), c.RequestAborted));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var shared = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(0));
        var answer = await shared.ReadAsync(CancellationToken.None);
        answer.Id.ShouldBe("shared"); answer.Occupied.ShouldBe(4); answer.State.ShouldBe("known");
        settings.AuthorityEndpoint = null;
        using var local = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(2));
        (await local.ReadAsync(CancellationToken.None)).Occupied.ShouldBe(2);
    }
    [Test]
    public async Task AuthorityFailureRemainsUnknown()
    {
        foreach (var malformed in new[] { false, true })
        {
            var clock = new FakeTimeProvider(C1514HostLoadFixture.T0); var calls = 0;
            var settings = C1514HostLoadFixture.Config(fixture.PrivatePath());
            settings.AuthorityEndpoint = await fixture.AuthorityAsync(async c =>
            { Interlocked.Increment(ref calls); c.Response.StatusCode = malformed ? 200 : 503; await c.Response.WriteAsync("{bad", c.RequestAborted); });
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var reader = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(0));
            var answer = await reader.ReadAsync(CancellationToken.None);
            answer.State.ShouldBe("unknown"); answer.Occupied.ShouldBeNull(); calls.ShouldBe(1);
        }
        foreach (var callerCancellation in new[] { false, true })
        {
            var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var settings = C1514HostLoadFixture.Config(fixture.PrivatePath());
            settings.AuthorityEndpoint = await fixture.AuthorityAsync(async c =>
            {
                c.Response.ContentType = "application/json"; await c.Response.StartAsync(c.RequestAborted);
                await c.Response.WriteAsync("{", c.RequestAborted); await c.Response.Body.FlushAsync(c.RequestAborted);
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, c.RequestAborted); } catch (OperationCanceledException) { }
            });
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var reader = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(0));
            using var caller = new CancellationTokenSource();
            var read = reader.ReadAsync(caller.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (callerCancellation) caller.Cancel(); else clock.Advance(TimeSpan.FromSeconds(3));
            if (callerCancellation) await Should.ThrowAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(2)));
            else
            {
                var answer = await read.WaitAsync(TimeSpan.FromSeconds(2));
                answer.State.ShouldBe("unknown"); answer.Occupied.ShouldBeNull();
            }
        }
    }
    [Test]
    public async Task AuthorityIdentityAndFreshnessAreChecked()
    {
        foreach (var variant in new[] { "host", "id", "stale", "boundary" })
        {
            var clock = new FakeTimeProvider(C1514HostLoadFixture.T0);
            var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.AuthorityId = "shared";
            var answer = Known(clock) with
            {
                Id = variant == "id" ? "foreign" : "shared", PhysicalHostKey = variant == "host" ? "foreign" : "owned-host",
                ObservedAt = clock.GetUtcNow() - TimeSpan.FromMilliseconds(variant == "stale" ? 15001 : 15000),
            };
            settings.AuthorityEndpoint = await fixture.AuthorityAsync(c => c.Response.WriteAsJsonAsync(answer, c.RequestAborted));
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var reader = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(0));
            var actual = await reader.ReadAsync(CancellationToken.None);
            if (variant == "boundary") { actual.State.ShouldBe("known"); actual.Occupied.ShouldBe(4); }
            else { actual.State.ShouldNotBe("known"); actual.Occupied.ShouldBeNull(); }
        }
    }
    [Test]
    public async Task AuthorityReadsAreCachedPerInterval()
    {
        var clock = new FakeTimeProvider(C1514HostLoadFixture.T0); var calls = 0;
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.AuthorityId = "shared";
        settings.AuthorityEndpoint = await fixture.AuthorityAsync(c =>
        { Interlocked.Increment(ref calls); return c.Response.WriteAsJsonAsync(Known(clock), c.RequestAborted); });
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var reader = new HostAdmissionSnapshotClient(http, settings, clock, () => Local(0));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => reader.ReadAsync(CancellationToken.None)));
        results.ShouldAllBe(r => r.Occupied == 4); calls.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(5));
        (await reader.ReadAsync(CancellationToken.None)).Occupied.ShouldBe(4); calls.ShouldBe(2);
    }
    private static HostAdmissionSnapshot Known(TimeProvider clock) => new("shared", "owned-host", 1, clock.GetUtcNow(), "known", "Lease", 4, 6, 1);
    private static BuildSlotListing Local(int occupied) => new(true, 6, 2, occupied, new(null, 0), [], []);
}
