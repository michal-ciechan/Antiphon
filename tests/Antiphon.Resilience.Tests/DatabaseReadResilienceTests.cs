using System.Data.Common;
using System.Transactions;
using Antiphon.Resilience;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Resilience;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Integration")]
public class DatabaseReadResilienceTests
{
    [Test]
    public async Task Transient_read_fault_succeeds_on_a_new_context_against_postgres()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var fault = new OneShotFault(Postgres("08006"));
        await using var provider = Host(schema.ConnectionString, fault, interceptor: null, out var time);
        var id = await SeedProvider(provider);
        var service = provider.GetRequiredService<LlmProviderService>();
        var read = service.GetByIdAsync(id, CancellationToken.None);
        await Task.Delay(30);
        time.Advance(TimeSpan.FromMilliseconds(300));
        var dto = await ResilienceTestHost.Pump(time, read, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        dto.Id.ShouldBe(id);
        dto.Name.ShouldBe("probe");
    }

    [Test]
    public async Task Ef_wrapper_around_a_transient_postgres_error_is_retried()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var interceptor = new OneShotCommandInterceptor();
        await using var provider = Host(schema.ConnectionString, fault: null, interceptor, out var time);
        var id = await SeedProvider(provider);
        interceptor.Arm(Postgres("08006"));
        var service = provider.GetRequiredService<LlmProviderService>();
        var dto = await ResilienceTestHost.Pump(
            time, service.GetByIdAsync(id, CancellationToken.None), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        dto.Name.ShouldBe("probe");
        interceptor.Executions.ShouldBeGreaterThan(1);
    }

    [Test]
    public async Task Syntax_and_constraint_faults_run_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var interceptor = new OneShotCommandInterceptor();
        await using var provider = Host(schema.ConnectionString, fault: null, interceptor, out _);
        var id = await SeedProvider(provider);
        interceptor.Arm(Postgres("42601"));
        var service = provider.GetRequiredService<LlmProviderService>();
        await Should.ThrowAsync<Exception>(() => service.GetByIdAsync(id, CancellationToken.None));
        interceptor.Executions.ShouldBe(1);
    }

    [Test]
    public async Task Failed_context_is_disposed_before_the_retry_delay()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var observed = new List<bool>();
        await using var provider = Host(schema.ConnectionString, new OneShotFault(Postgres("08006")), null, out var time, observed);
        var executor = provider.GetRequiredService<DatabaseResilienceExecutor>();
        var read = executor.ExecuteReadAsync(
            ResilienceOperations.LlmProvidersList,
            (db, ct) => db.LlmProviders.AsNoTracking().CountAsync(ct),
            CancellationToken.None);
        await Task.Delay(50);
        observed.Count.ShouldBe(1);
        observed[0].ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(1));
        var until = DateTime.UtcNow.AddSeconds(3);
        while (observed.Count < 2 && DateTime.UtcNow < until)
            await Task.Delay(20);
        observed.Count.ShouldBe(2);
        (await read).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Both_services_four_getter_paths_read_the_seeded_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = Host(schema.ConnectionString, fault: null, interceptor: null, out _);
        var providerId = await SeedProvider(provider);
        var templateId = await SeedTemplate(provider);
        var providers = provider.GetRequiredService<LlmProviderService>();
        var templates = provider.GetRequiredService<WorkflowTemplateService>();
        (await providers.GetAllAsync(CancellationToken.None)).ShouldContain(row => row.Id == providerId);
        (await providers.GetByIdAsync(providerId, CancellationToken.None)).Name.ShouldBe("probe");
        (await templates.GetAllAsync(CancellationToken.None)).ShouldContain(row => row.Id == templateId);
        (await templates.GetByIdAsync(templateId, CancellationToken.None)).Name.ShouldBe("template");
    }

    [Test]
    public async Task Active_transaction_non_read_and_external_effects_are_rejected()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = Host(schema.ConnectionString, fault: null, interceptor: null, out _);
        var executor = provider.GetRequiredService<DatabaseResilienceExecutor>();
        var effects = 0;
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Should.ThrowAsync<ResilienceAdmissionException>(() => executor.ExecuteReadAsync(
                ResilienceOperations.LlmProvidersList,
                (_, _) =>
                {
                    effects++;
                    return Task.FromResult(0);
                },
                CancellationToken.None));
            scope.Complete();
        }

        effects.ShouldBe(0);
        var calls = 0;
        await Should.ThrowAsync<ResilienceAdmissionException>(() => executor.ExecuteReadAsync(
            "external-send",
            (_, _) =>
            {
                calls++;
                return Task.FromResult(0);
            },
            CancellationToken.None));
        calls.ShouldBe(0);
        var writes = 0;
        await Should.ThrowAsync<ResilienceAdmissionException>(() => executor.ExecuteReadAsync(
            ResilienceOperations.LlmProvidersList,
            async (db, ct) =>
            {
                writes++;
                await db.Database.BeginTransactionAsync(ct);
                return 0;
            },
            CancellationToken.None));
        writes.ShouldBe(1);
    }

    [Test]
    public async Task Serialization_failure_rolls_back_and_replays_on_a_fresh_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = Host(schema.ConnectionString, fault: null, interceptor: null, out var time);
        await SeedProvider(provider);
        var executor = provider.GetRequiredService<DatabaseResilienceExecutor>();
        var contexts = new List<string>();
        var read = executor.ExecuteReadAsync(
            ResilienceOperations.LlmProvidersList,
            async (db, ct) =>
            {
                contexts.Add(db.ContextId.ToString());
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                if (contexts.Count == 1)
                {
                    db.LlmProviders.Add(new LlmProvider
                    {
                        Id = Guid.NewGuid(),
                        Name = "rolled-back",
                        ProviderType = ProviderType.OpenAI,
                        ApiKey = "sk-test",
                        BaseUrl = "http://127.0.0.1:9",
                        IsEnabled = true,
                        DefaultModel = "m",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                    throw Postgres("40001");
                }

                (await db.LlmProviders.AsNoTracking().AnyAsync(row => row.Name == "rolled-back", ct)).ShouldBeFalse();
                await tx.RollbackAsync(ct);
                return await db.LlmProviders.AsNoTracking().CountAsync(ct);
            },
            CancellationToken.None);
        var count = await ResilienceTestHost.Pump(time, read, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        count.ShouldBeGreaterThan(0);
        contexts.Count.ShouldBe(2);
        contexts[0].ShouldNotBe(contexts[1]);
    }

    [Test]
    public async Task Cancellation_during_the_delay_does_not_start_another_database_command()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var interceptor = new OneShotCommandInterceptor();
        await using var provider = Host(schema.ConnectionString, new OneShotFault(Postgres("08006")), interceptor, out var time);
        var executor = provider.GetRequiredService<DatabaseResilienceExecutor>();
        using var cts = new CancellationTokenSource();
        var read = executor.ExecuteReadAsync(
            ResilienceOperations.LlmProvidersList,
            (db, ct) => db.LlmProviders.AsNoTracking().CountAsync(ct),
            cts.Token);
        await Task.Delay(40);
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => read);
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(40);
        interceptor.Executions.ShouldBe(0);
    }

    private static ServiceProvider Host(
        string connectionString,
        IDatabaseReadFault? fault,
        OneShotCommandInterceptor? interceptor,
        out FakeTimeProvider time,
        List<bool>? disposed = null)
    {
        time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        var clock = time;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IResilienceJitter>(new FixedResilienceJitter(1));
        services.AddAntiphonResilience(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.PostConfigure<ResilienceSettings>(settings =>
        {
            settings.BaseDelayMilliseconds = 200;
            settings.MaxDelayMilliseconds = 200;
            settings.MaxRetryAttempts = 2;
            settings.AttemptTimeoutSeconds = 5;
            settings.TotalTimeoutSeconds = 30;
            settings.CircuitBreaker.SamplingDurationSeconds = 10;
            settings.CircuitBreaker.MinimumThroughput = 100;
        });
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        services.AddSingleton(new DatabaseResilienceName(database));
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
        });
        if (fault is not null)
            services.AddSingleton(fault);
        if (disposed is not null)
            services.AddSingleton<IDatabaseReadObserver>(new RecordingObserver(disposed));
        services.AddHttpClient();
        services.AddSingleton(sp => new DatabaseResilienceExecutor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<DatabaseAttemptExecutor>(),
            sp.GetRequiredService<IOptionsMonitor<ResilienceSettings>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<DatabaseResilienceName>(),
            sp.GetService<IDatabaseReadFault>(),
            sp.GetService<IDatabaseReadObserver>()));
        services.AddScoped(sp => new LlmProviderService(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            NullLogger<LlmProviderService>.Instance,
            sp.GetRequiredService<DatabaseResilienceExecutor>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptionsMonitor<ResilienceSettings>>()));
        services.AddScoped(sp => new WorkflowTemplateService(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<DatabaseResilienceExecutor>()));
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedProvider(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.LlmProviders.Add(new LlmProvider
        {
            Id = id,
            Name = "probe",
            ProviderType = ProviderType.OpenAI,
            ApiKey = "sk-test",
            BaseUrl = "http://127.0.0.1:9",
            IsEnabled = true,
            DefaultModel = "m",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTemplate(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.WorkflowTemplates.Add(new WorkflowTemplate
        {
            Id = id,
            Name = "template",
            Description = "d",
            YamlDefinition = "stages: []\n",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static PostgresException Postgres(string state) => new("marker", "ERROR", "ERROR", state);

    private sealed class OneShotFault(Exception exception) : IDatabaseReadFault
    {
        private int _remaining = 1;
        public Exception? Consume() => Interlocked.Decrement(ref _remaining) >= 0 ? exception : null;
    }

    private sealed class OneShotCommandInterceptor : DbCommandInterceptor
    {
        private Exception? _exception;
        private int _remaining;
        public int Executions { get; private set; }

        public void Arm(Exception exception)
        {
            _exception = exception;
            _remaining = 1;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_exception is null)
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            Executions++;
            if (Interlocked.Decrement(ref _remaining) >= 0)
                throw _exception;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RecordingObserver(List<bool> disposed) : IDatabaseReadObserver
    {
        public void OnAttemptEnded(AppDbContext context)
        {
            var thrown = false;
            try
            {
                _ = context.LlmProviders.Local.Count;
            }
            catch (ObjectDisposedException)
            {
                thrown = true;
            }

            disposed.Add(thrown);
        }
    }
}
