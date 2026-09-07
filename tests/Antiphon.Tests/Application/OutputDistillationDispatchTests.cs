using System.Data.Common;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class OutputDistillationDispatchTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Dispatch_rechecks_expiry_under_claim(bool afterSelection)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scratch = Directory.CreateTempSubdirectory("antiphon-c432-dispatch").FullName;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var probe = new ClaimProbe();
        await using var provider = Build(schema.ConnectionString, clock, probe);
        try
        {
            var (agent, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(schema.ConnectionString, scratch);
            var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(schema.ConnectionString, scratch,
                agent, AgentModelLevel.Low, "optional deadline");
            probe.TaskId = task.Id;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Role, AgentTaskRole.Distill)
                .SetProperty(t => t.ExecutionDeadlineAt, clock.GetUtcNow().AddSeconds(afterSelection ? 5 : -1).UtcDateTime));
            await using var blocker = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await using var transaction = await blocker.Database.BeginTransactionAsync();
            if (afterSelection)
                await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {task.Id} FOR UPDATE");
            await using var scope = provider.CreateAsyncScope();
            var sweep = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            if (afterSelection)
            {
                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                clock.Advance(TimeSpan.FromSeconds(6));
            }
            await transaction.RollbackAsync();
            await sweep.WaitAsync(TimeSpan.FromSeconds(10));
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
            (await db.SessionQueuedMessages.AnyAsync(m => m.ExecutionTaskId == task.Id)).ShouldBeFalse();
            // Reconstructed services cannot launch this expired durable row.
            await using var restarted = Build(schema.ConnectionString, clock, new ClaimProbe());
            await using var secondScope = restarted.CreateAsyncScope();
            await secondScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static ServiceProvider Build(string connectionString, TimeProvider clock, ClaimProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString).AddInterceptors(probe));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock);
        services.AddOptions<SupervisionSettings>();
        services.AddOptions<ChannelBridgeSettings>();
        services.AddOptions<AgentSessionSettings>();
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        { s.DefaultDefinition = "claude"; s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" }; });
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

    private sealed class ClaimProbe : DbCommandInterceptor
    {
        public Guid TaskId;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE") && command.CommandText.Contains("AgentTasks")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == TaskId))
                Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
