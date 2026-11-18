using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1744 W-6, W-7 and W-8. A non-default direct key starts a new ingest cursor and
/// is what the pipeline reads for the local host and the desktop catalogue row.
/// </summary>
[Category("Integration")]
public sealed class C1744G2HostLoadKeyTests
{
    [Test]
    public async Task Direct_key_change_starts_a_new_cursor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using (var seed = Context(schema))
        {
            seed.HostLoadIngestCursors.Add(new HostLoadIngestCursor
            {
                RunnerId = "desktop",
                RunnerStoreId = storeA,
                GapsJson = "[]",
                CommittedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var settings = new HostLoadHistorySettings { DirectRunnerKey = "server2-direct" };
        await using var db = Context(schema);
        var service = new HostLoadIngestService(
            db, Options.Create(settings), new HostLoadCounterCache(), TimeProvider.System);
        var receipt = await service.IngestAsync(Batch(storeB), CancellationToken.None);
        receipt.Inserted.ShouldBeGreaterThan(0);

        await using var verify = Context(schema);
        var cursor = await verify.HostLoadIngestCursors.SingleAsync(c => c.RunnerId == "server2-direct");
        cursor.RunnerStoreId.ShouldBe(storeB);
    }

    [Test]
    public async Task Pipeline_local_host_reads_the_direct_key()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 });
        var directory = new PhoneHomeRunnerDirectory(
            new SilentClient(),
            Options.Create(new PhoneHomeRunnerSettings { Enabled = false }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        var budgets = new HostBudgetService(db, directory, options, TimeProvider.System);
        var shadow = new HostLoadShadowCache(TimeProvider.System);
        shadow.Set("desktop", Advice(1));
        shadow.Set("server2-direct", Advice(2));
        var phone = new PhoneHomeRunnerSettings { Enabled = true };
        phone.Runners["server2"] = new PhoneHomeRunnerEntry { Enabled = true, AllowDelegatedTasks = true };
        phone.Runners["desktop-win"] = new PhoneHomeRunnerEntry { Enabled = true, AllowDelegatedTasks = true };
        var keys = new HostLoadRunnerKeys(
            Options.Create(new HostLoadHistorySettings { DirectRunnerKey = "server2-direct" }),
            Options.Create(phone));
        var pipeline = new AgentTaskPipelineStatusService(
            db, options, new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance), TimeProvider.System,
            budgets, directory: directory, hostLoad: shadow, runnerKeys: keys);
        var dto = await pipeline.GetAsync(CancellationToken.None);
        dto.Hosts.Single(h => h.HostId == "local").Load!.Recommendation.RecommendedAdditionalTasks.ShouldBe(2);
        dto.Runners.Single(r => r.RunnerId == "desktop").Load!.Recommendation.RecommendedAdditionalTasks.ShouldBe(2);
    }

    [Test]
    public void Default_direct_key_is_desktop_and_unchanged()
    {
        var settings = new HostLoadHistorySettings();
        settings.DirectRunnerKey.ShouldBe("desktop");
        settings.Validate();
        var keys = HostLoadRunnerKeys.Default;
        keys.Canonical("local").ShouldBe("desktop");
        keys.Canonical("desktop").ShouldBe("desktop");
        keys.Canonical("server2").ShouldBe("server2");

        Should.Throw<InvalidOperationException>(() =>
            new HostLoadHistorySettings { DirectRunnerKey = "local" }.Validate());
        Should.Throw<InvalidOperationException>(() =>
            new HostLoadHistorySettings { DirectRunnerKey = "LOCAL" }.Validate());
        Should.Throw<InvalidOperationException>(() =>
            new HostLoadHistorySettings { DirectRunnerKey = "Desktop" }.Validate());

        var mapped = new PhoneHomeRunnerSettings { Enabled = true };
        mapped.Runners["server2"] = new PhoneHomeRunnerEntry { Enabled = true, AllowDelegatedTasks = true };
        mapped.Runners["desktop-win"] = new PhoneHomeRunnerEntry { Enabled = false, AllowDelegatedTasks = true };
        Should.Throw<InvalidOperationException>(() => Keys("server2", mapped));
        Should.Throw<InvalidOperationException>(() => Keys("SERVER2", mapped));
        Should.Throw<InvalidOperationException>(() => Keys("desktop-win", mapped));

        var legacy = new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "server2" };
        Should.Throw<InvalidOperationException>(() => Keys("server2", legacy));

        var direct = Keys("server2-direct", mapped);
        direct.Canonical("local").ShouldBe("server2-direct");
        direct.Canonical("desktop").ShouldBe("server2-direct");
        direct.IsDirect("server2-direct").ShouldBeTrue();
    }

    private static HostLoadRunnerKeys Keys(string key, PhoneHomeRunnerSettings phone) => new(
        Options.Create(new HostLoadHistorySettings { DirectRunnerKey = key }),
        Options.Create(phone));

    private static HostLoadAdvice Advice(int recommended) => new(
        new HostLoadShadowDecision(null, null, "unknown", "unknown", [], []),
        new HostLoadRecommendationDto(recommended, "fleet", [], null, 0, 0, null, null, null, null, null, []));

    private static HostLoadIngestBatch Batch(Guid store)
    {
        var boot = Guid.NewGuid();
        var source = new HostLoadSource("local", store, boot, 1);
        var sample = new HostLoadSample
        {
            Source = source,
            SampledAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero),
            IntervalSeconds = 5,
            CpuPercent = 1,
        };
        return new HostLoadIngestBatch("local", null, new HostLoadHistoryPage(1, source, [sample], [], null, false, true));
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class SilentClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
