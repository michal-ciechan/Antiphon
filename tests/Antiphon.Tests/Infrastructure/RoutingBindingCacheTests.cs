using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1987 S1. The directory stub counts binding reads and availability checks.
/// No database: the production binding query is tagged separately.
/// </summary>
[Category("Unit")]
public sealed class RoutingBindingCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task C1987_route_reads_the_binding_once_per_ttl()
    {
        var clock = new FakeTimeProvider(Start);
        var directory = new CountingDirectory
        {
            Binding = () => SessionRunnerBinding.Local.Instance,
        };
        var client = new RoutingSessionRunnerClient(directory, clock);
        var id = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await client.GetAsync(id, CancellationToken.None);

        directory.BindingReads.ShouldBe(1, "binding reads, budget 1");
        clock.Advance(TimeSpan.FromSeconds(61));
        await client.GetAsync(id, CancellationToken.None);
        directory.BindingReads.ShouldBe(2, "binding reads after the ttl, budget 2");
    }

    [Test]
    public async Task C1987_missing_binding_is_never_cached()
    {
        var clock = new FakeTimeProvider(Start);
        var missing = true;
        var directory = new CountingDirectory
        {
            Binding = () => missing
                ? SessionRunnerBinding.Missing.Instance
                : SessionRunnerBinding.Local.Instance,
        };
        var client = new RoutingSessionRunnerClient(directory, clock);
        var id = Guid.NewGuid();
        await Should.ThrowAsync<NotFoundException>(() => client.GetAsync(id, CancellationToken.None));
        missing = false;
        var routed = await client.GetAsync(id, CancellationToken.None);
        routed.SessionId.ShouldBe(id);
    }

    [Test]
    public async Task C1987_resolve_runs_on_every_call()
    {
        var clock = new FakeTimeProvider(Start);
        var owner = new SessionRunnerOwner("grok-linux", Guid.NewGuid(), "/work");
        var directory = new CountingDirectory
        {
            Binding = () => new SessionRunnerBinding.Remote(owner),
        };
        var client = new RoutingSessionRunnerClient(directory, clock);
        var id = Guid.NewGuid();
        await client.GetAsync(id, CancellationToken.None);
        directory.Unavailable = true;
        await Should.ThrowAsync<ServiceUnavailableException>(() => client.GetAsync(id, CancellationToken.None));
        directory.ResolveCalls.ShouldBe(2);
    }

    private sealed class CountingDirectory : ISessionRunnerDirectory
    {
        private readonly FakeSessionRunnerClient _local = new();

        public int BindingReads { get; private set; }
        public int ResolveCalls { get; private set; }
        public bool Unavailable { get; set; }
        public Func<SessionRunnerBinding> Binding { get; init; } = () => SessionRunnerBinding.Local.Instance;

        public ISessionRunnerClient Local => _local;

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            ResolveCalls++;
            if (Unavailable)
                throw new ServiceUnavailableException("runner unavailable", PhoneHomeProblemTypes.Unavailable);
            return _local;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);

        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct)
        {
            BindingReads++;
            return Task.FromResult(Binding());
        }

        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable(RunnerInventoryReasons.PhoneHomeUnavailable));

        public IReadOnlyList<string> KnownRunnerIds => [];

        public Guid? GetLiveStoreId(string? runnerId) => null;
    }
}
