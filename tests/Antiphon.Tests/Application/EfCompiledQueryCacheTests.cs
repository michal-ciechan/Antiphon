using System.Linq.Expressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1912 / CARD-1913. Counts <see cref="CoreEventId.QueryCompilationStarting"/> (10111).
/// A stable SQL shape compiles once across distinct key sets and distinct clock values.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class EfCompiledQueryCacheTests
{
    [Test]
    public async Task Identity_lookup_compiles_once_for_distinct_key_sets()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var probe = new CompileProbe(static text =>
            text.Contains("identity.RunnerStoreId", StringComparison.Ordinal)
            || text.Contains("unnest(", StringComparison.Ordinal)
            || text.Contains("HostLoadSampleIdentities", StringComparison.Ordinal));
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString, probe);
        await using var db = new AppDbContext(options);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        var service = new HostLoadIngestService(db, Options.Create(new HostLoadHistorySettings()),
            new HostLoadCounterCache(), clock);
        var store = Guid.NewGuid();
        string? cursor = null;
        var inserted = 0;
        for (var i = 1; i <= 3; i++)
        {
            var boot = Guid.NewGuid();
            var sample = Sample(store, boot, i);
            var page = new HostLoadHistoryPage(1, new HostLoadSource("desktop", store, boot, i),
                [sample], [], "c" + i, false, i == 3);
            var receipt = await service.IngestAsync(new HostLoadIngestBatch("desktop", cursor, page), CancellationToken.None);
            inserted += receipt.Inserted;
            cursor = receipt.CommittedCursor;
        }

        inserted.ShouldBe(3);
        probe.Total.ShouldBeGreaterThan(0);
        probe.Count.ShouldBe(1, probe.Describe());
    }

    [Test]
    public async Task Optional_work_expiry_update_compiles_once_across_ticks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        var probe = new CompileProbe(static text =>
            text.Contains("Optional work expired before execution.", StringComparison.Ordinal));
        await using var provider = Build(schema.ConnectionString, clock, probe);
        for (var i = 0; i < 3; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .DispatchQueuedAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        probe.Total.ShouldBeGreaterThan(0);
        probe.Count.ShouldBe(1, probe.Describe());
    }

    [Test]
    public async Task Delivery_failure_recovery_compiles_once_across_sweeps()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        var probe = new CompileProbe(static text => text.Contains("NextAttemptAt", StringComparison.Ordinal));
        await using var provider = Build(schema.ConnectionString, clock, probe);
        for (var i = 0; i < 3; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .RemindUnacknowledgedFailuresAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        probe.Total.ShouldBeGreaterThan(0);
        probe.Count.ShouldBe(1, probe.Describe());
    }

    private static HostLoadSample Sample(Guid store, Guid boot, long sequence) => new()
    {
        Source = new HostLoadSource("desktop", store, boot, sequence),
        SampledAt = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero),
        IntervalSeconds = 5,
        CpuPercent = 1,
    };

    private static ServiceProvider Build(string connectionString, TimeProvider clock, CompileProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.SetPostgresVersion(16, 0))
            .AddInterceptors(probe));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock);
        services.AddOptions<SupervisionSettings>();
        services.AddOptions<ChannelBridgeSettings>();
        services.AddOptions<AgentSessionSettings>();
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512, CheckEnabled = false }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.GetTempPath() });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed class CompileProbe(Func<string, bool> match) : IQueryExpressionInterceptor
    {
        public int Count;
        public int Total;
        private readonly List<string> _seen = [];

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            var text = queryExpression.ToString();
            if (_seen.Count < 6)
                _seen.Add(text.Length > 240 ? text[..240] : text);
            if (match(text))
                Interlocked.Increment(ref Count);
            Interlocked.Increment(ref Total);
            return queryExpression;
        }

        public string Describe() => $"count={Count} total={Total}\n" + string.Join("\n---\n", _seen);
    }
}
