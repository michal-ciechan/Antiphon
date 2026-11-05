using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class BuildSlotMaxHoldTests
{
    [Test]
    public void V1_Default_keeps_live_holder_until_48_hours_then_reaps_once()
    {
        using var f = new Fixture();
        var grant = f.Acquire();
        f.Time.Advance(TimeSpan.FromHours(48) - TimeSpan.FromTicks(1));
        f.Broker.Sweep().ShouldBe(0);
        f.Broker.List().Leases.Single().LeaseId.ShouldBe(grant.LeaseId!.Value);
        f.Time.Advance(TimeSpan.FromTicks(1));
        f.Broker.Sweep().ShouldBe(1);
        f.Broker.List().Occupied.ShouldBe(0);
        f.Broker.Sweep().ShouldBe(0);
        f.Messages.Count(m => m.Contains("absolute max hold 48h exceeded")).ShouldBe(1);
        f.Broker.Metrics().Reaps.ShouldBe(1);
        f.Broker.Release(grant.LeaseId!.Value).ShouldBeFalse();
    }

    [Test]
    public void V2_Configured_bound_uses_grant_age_and_cannot_be_extended_by_reacquire_or_renew()
    {
        using var f = new Fixture("72");
        var grant = f.Acquire();
        f.Time.Advance(TimeSpan.FromHours(60));
        f.Acquire().LeaseId.ShouldBe(grant.LeaseId);
        f.Broker.Renew(grant.LeaseId!.Value).ShouldBeTrue();
        f.Time.Advance(TimeSpan.FromHours(12));
        f.Broker.Renew(grant.LeaseId.Value).ShouldBeFalse();
        f.Broker.List().Occupied.ShouldBe(0);
        f.Messages.ShouldContain(m => m.Contains("absolute max hold 72h exceeded"));
        f.Acquire().LeaseId.ShouldNotBe(grant.LeaseId);
    }

    [Test]
    public void V3_Bound_applies_before_ttl_and_next_acquire_admits_a_waiter()
    {
        using var f = new Fixture("1");
        f.Acquire();
        f.Time.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
        f.Broker.TryAcquire(f.Request(102), out _).ShouldBeFalse();
        f.Time.Advance(TimeSpan.FromTicks(1));
        f.Broker.TryAcquire(f.Request(102), out var outcome).ShouldBeTrue();
        outcome.ShouldBeOfType<BuildSlotOutcome.Granted>();
        f.Broker.List().Leases.Single().Pid.ShouldBe(102);
    }

    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    public void V4_Nonpositive_bound_is_rejected_by_production_options(string value)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            using var f = new Fixture(value);
        });
        ex.Message.ShouldContain("SessionRunner:BuildSlots:LeaseMaxHoldHours");
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateTime T0 = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        private readonly ServiceProvider _provider;
        private readonly BuildSlotBrokerTests.FakeLiveness _liveness = new();
        public FakeTimeProvider Time { get; } = new(T0);
        public List<string> Messages { get; } = [];
        public BuildSlotBroker Broker { get; }

        public Fixture(string? maxHoldHours = null)
        {
            var values = new Dictionary<string, string?>
            {
                ["SessionRunner:BuildSlots:MaxConcurrent"] = "1",
                ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = "0",
            };
            if (maxHoldHours is not null)
                values["SessionRunner:BuildSlots:LeaseMaxHoldHours"] = maxHoldHours;
            else
                values.ContainsKey("SessionRunner:BuildSlots:LeaseMaxHoldHours").ShouldBeFalse();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<BuildSlotBroker>>(
                new ListLogger<BuildSlotBroker>(Messages));
            services.AddSingleton<IProcessLivenessProbe>(_liveness);
            services.AddSingleton<IHostMemoryProbe>(new BuildSlotBrokerTests.FakeMemory());
            services.AddSingleton<TimeProvider>(Time);
            services.AddBuildSlotBroker(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            _provider = services.BuildServiceProvider();
            try { Broker = _provider.GetRequiredService<BuildSlotBroker>(); }
            catch { _provider.Dispose(); throw; }
        }

        public BuildSlotRequest Request(int pid)
        {
            _liveness.Start(pid, T0);
            return new(pid, T0, "max-hold-" + pid, null, null);
        }

        public BuildSlotGrant Acquire()
        {
            Broker.TryAcquire(Request(101), out var result).ShouldBeTrue();
            return result.ShouldBeOfType<BuildSlotOutcome.Granted>().Grant;
        }

        public void Dispose() => _provider.Dispose();
    }
}
