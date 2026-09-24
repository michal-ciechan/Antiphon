using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 D-3: every dispatcher sweep runs under a cooperative budget, and a sweep that ignores
/// its cancelled token is abandoned only when it runs on its own service scope, never on the tick's
/// shared <see cref="AppDbContext"/> (the 912b195e defect, reverted by 587fd147).
/// </summary>
[Category("Integration")]
public class DispatcherSweepLifetimeTests
{
    [Test]
    public async Task Abandoned_sweep_runs_on_its_own_scope_and_that_scope_is_disposed_after_it_ends()
    {
        await using var g = await Graph.CreateAsync(scopeFactory: true);
        AgentTaskDispatcher? seen = null;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodyDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var wrapper = g.Tick.RunSweepAsync("probe", async (d, _) =>
        {
            seen = d;
            await hold.Task; // ignores its token on purpose: an uncancellable hang
            try
            {
                bodyDone.TrySetResult(await d.Db.Database.CanConnectAsync());
            }
            catch (Exception ex)
            {
                bodyDone.TrySetException(ex);
                throw;
            }

            return 0;
        }, CancellationToken.None);
        g.Clock.Advance(TimeSpan.FromSeconds(g.Settings.SweepBudgetSeconds + g.Settings.SweepAbandonGraceSeconds));

        (await wrapper.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(1, "an abandoned sweep counts as one sweep failure");
        seen.ShouldNotBeNull();
        ReferenceEquals(seen, g.Tick).ShouldBeFalse("the sweep must run on a dispatcher from its own scope");
        ReferenceEquals(seen.Db, g.Tick.Db).ShouldBeFalse("the sweep must not share the tick's DbContext");
        g.Logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("probe") && e.Message.Contains("abandoned"));

        hold.SetResult();
        (await bodyDone.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue(
            "the abandoned sweep's own scope must still be alive while it runs");
        await g.InFlight.WhenReleasedAsync("probe").WaitAsync(TimeSpan.FromSeconds(5));
        await Should.ThrowAsync<ObjectDisposedException>(() => seen.Db.AgentTasks.AnyAsync());
    }

    [Test]
    public async Task A_sweep_still_running_from_the_previous_tick_is_skipped_and_counted()
    {
        await using var g = await Graph.CreateAsync(scopeFactory: true);
        var calls = 0;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<AgentTaskDispatcher, CancellationToken, Task<int>> body = async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            await hold.Task;
            return 0;
        };

        var first = g.Tick.RunSweepAsync("probe", body, CancellationToken.None);
        g.Clock.Advance(TimeSpan.FromSeconds(g.Settings.SweepBudgetSeconds + g.Settings.SweepAbandonGraceSeconds));
        (await first.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(1);
        g.Clock.Advance(TimeSpan.FromSeconds(7));

        (await g.Tick.RunSweepAsync("probe", body, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(1);
        Volatile.Read(ref calls).ShouldBe(1, "a sweep still running from an earlier tick must not be started again");
        var age = g.Settings.SweepBudgetSeconds + g.Settings.SweepAbandonGraceSeconds + 7;
        g.Logger.Entries.ShouldContain(e => e.Level == LogLevel.Error
            && e.Message.Contains("probe") && e.Message.Contains("still running") && e.Message.Contains($"{age}s"));

        hold.SetResult();
        await g.InFlight.WhenReleasedAsync("probe").WaitAsync(TimeSpan.FromSeconds(5));
        (await g.Tick.RunSweepAsync("probe", body, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(0);
        Volatile.Read(ref calls).ShouldBe(2);
    }

    [Test]
    public async Task Without_a_scope_factory_the_budget_cancels_but_never_abandons()
    {
        await using var g = await Graph.CreateAsync(scopeFactory: false);
        AgentTaskDispatcher? seenA = null;
        var a = g.Tick.RunSweepAsync("cooperative", async (d, ct) =>
        {
            seenA = d;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }, CancellationToken.None);
        g.Clock.Advance(TimeSpan.FromSeconds(g.Settings.SweepBudgetSeconds));
        (await a.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(1, "the budget must cancel the sweep's token");
        ReferenceEquals(seenA, g.Tick).ShouldBeTrue();

        AgentTaskDispatcher? seenB = null;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = g.Tick.RunSweepAsync("uncooperative", async (d, _) =>
        {
            seenB = d;
            await hold.Task;
            return 0;
        }, CancellationToken.None);
        g.Clock.Advance(TimeSpan.FromSeconds(g.Settings.SweepBudgetSeconds + g.Settings.SweepAbandonGraceSeconds + 60));
        await Task.Delay(250);
        b.IsCompleted.ShouldBeFalse("a sweep on the tick's own context must never be abandoned");

        hold.SetResult();
        (await b.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(1, "a sweep that overran its budget is still a sweep failure");
        ReferenceEquals(seenB, g.Tick).ShouldBeTrue();
    }

    [Test]
    [Arguments(ScopeFault.CreateThrows)]
    [Arguments(ScopeFault.ResolveAndDisposeThrow)]
    [Arguments(ScopeFault.DisposeThrowsAfterSweep)]
    public async Task A_failing_owned_scope_still_releases_the_sweep_claim(ScopeFault fault)
    {
        await using var g = await Graph.CreateAsync(scopeFactory: true, fault: fault);
        var calls = 0;
        Func<AgentTaskDispatcher, CancellationToken, Task<int>> body = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(0);
        };

        var result = await g.Tick.RunSweepAsync("probe", body, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        g.InFlight.IsRunning("probe").ShouldBeFalse("a scope that failed to build or dispose must not strand the sweep's claim");
        if (fault == ScopeFault.DisposeThrowsAfterSweep)
        {
            result.ShouldBe(0, "the sweep itself succeeded; only its scope's disposal failed");
            Volatile.Read(ref calls).ShouldBe(1);
        }
        else
        {
            result.ShouldBe(1, "a sweep that could not get its own scope is one sweep failure");
            Volatile.Read(ref calls).ShouldBe(0);
        }

        g.Logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("probe"));
        g.Logger.Entries.ShouldNotContain(e => e.Message.Contains("still running"));
    }

    public enum ScopeFault
    {
        None,
        CreateThrows,
        ResolveAndDisposeThrow,
        DisposeThrowsAfterSweep,
    }

    private sealed class Graph : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required AsyncServiceScope TickScope { get; init; }
        public required AgentTaskDispatcher Tick { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required SweepInFlightState InFlight { get; init; }
        public required RecordingLogger<AgentTaskDispatcher> Logger { get; init; }
        public required DelegationSettings Settings { get; init; }

        public static async Task<Graph> CreateAsync(bool scopeFactory, ScopeFault fault = ScopeFault.None)
        {
            // The shared database may take a minute to start. Awaiting its one fixture task
            // leaves pool threads available for the other CP-1 classes' short network waits.
            var connection = (await TestDbFixture.Lifecycle.EnsureReadyAsync()).SharedConnectionString;
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var settings = new DelegationSettings();
            var logger = new RecordingLogger<AgentTaskDispatcher>();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(settings));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-sweep-lifetime-wt"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<SweepInFlightState>();
            services.AddSingleton<ILogger<AgentTaskDispatcher>>(logger);
            if (scopeFactory && fault != ScopeFault.None)
                services.AddScoped(sp => ActivatorUtilities.CreateInstance<AgentTaskDispatcher>(
                    new WithFaultyScopeFactory(sp, new FaultyScopeFactory(sp.GetRequiredService<IServiceScopeFactory>(), fault))));
            else if (scopeFactory)
                services.AddScoped<AgentTaskDispatcher>();
            else
                services.AddScoped(sp => ActivatorUtilities.CreateInstance<AgentTaskDispatcher>(new WithoutScopeFactory(sp)));

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            return new Graph
            {
                Provider = provider,
                TickScope = scope,
                Tick = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
                Clock = clock,
                InFlight = provider.GetRequiredService<SweepInFlightState>(),
                Logger = logger,
                Settings = settings,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await TickScope.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>Resolves everything except the scope factory, so the dispatcher gets <c>scopeFactory: null</c>.</summary>
    private sealed class WithoutScopeFactory(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) || serviceType == typeof(IServiceProviderIsService)
                ? null
                : inner.GetService(serviceType);
    }

    /// <summary>Hands the dispatcher <paramref name="factory"/> in place of the real scope factory.</summary>
    private sealed class WithFaultyScopeFactory(IServiceProvider inner, IServiceScopeFactory factory) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? factory : inner.GetService(serviceType);
    }

    /// <summary>A scope factory whose scopes fail to build, resolve or dispose, per <see cref="ScopeFault"/>.</summary>
    private sealed class FaultyScopeFactory(IServiceScopeFactory inner, ScopeFault fault) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            if (fault == ScopeFault.CreateThrows)
                throw new InvalidOperationException("scope creation failed");
            return new FaultyScope(inner.CreateScope(), fault);
        }
    }

    private sealed class FaultyScope(IServiceScope inner, ScopeFault fault) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = fault == ScopeFault.ResolveAndDisposeThrow
            ? new ThrowingProvider()
            : inner.ServiceProvider;

        public void Dispose()
        {
            inner.Dispose();
            throw new InvalidOperationException("scope disposal failed");
        }

        public async ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable a)
                await a.DisposeAsync();
            else
                inner.Dispose();
            throw new InvalidOperationException("scope disposal failed");
        }
    }

    private sealed class ThrowingProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("resolve failed");
    }
}
