using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Observes commands and compilation from the actual service methods, never copied LINQ.</summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<EfClockQueryStore>(Shared = SharedType.PerClass)]
public sealed class EfProductionClockQueryTests(EfClockQueryStore store)
{
    [Test]
    public async Task Dispatcher_expiry_parameters_change_without_recompiling_and_tokens_stay_per_row()
    {
        var probe = new QueryProbe("UPDATE \"AgentTasks\"");
        await using var db = await store.OpenAsync(probe);
        var clock = Clock();
        var dispatcher = new AgentTaskDispatcher(db, null!, null!, null!, null!, null!, null!,
            Options.Create(new DelegationSettings { Enabled = true, MaxConcurrentTasks = 512 }),
            new MockEventBus(), clock, NullLogger<AgentTaskDispatcher>.Instance);
        var first = clock.GetUtcNow().UtcDateTime;
        (await dispatcher.DispatchQueuedAsync(CancellationToken.None)).Dispatched.ShouldBe(0);
        clock.Advance(TimeSpan.FromMinutes(7));
        (await dispatcher.DispatchQueuedAsync(CancellationToken.None)).Dispatched.ShouldBe(0);
        probe.AssertStable(first, clock.GetUtcNow().UtcDateTime);
        foreach (var command in probe.Commands)
            command.Sql.ShouldContain("gen_random_uuid()");
    }

    [Test]
    public async Task Outbound_claim_uses_clock_parameters_without_recompiling()
    {
        var probe = new QueryProbe("UPDATE \"ChannelOutboundDeliveries\"");
        await using var db = await store.OpenAsync(probe);
        var clock = Clock();
        var pump = new ChannelOutboundDeliveryPump(db, null!, null!, null!,
            Options.Create(new AntiphonMessagingOptions()), clock,
            NullLogger<ChannelOutboundDeliveryPump>.Instance);
        var id = Guid.NewGuid();
        var first = clock.GetUtcNow().UtcDateTime;
        (await Invoke<bool>(pump, "ClaimAsync", id, 0L, CancellationToken.None)).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMinutes(7));
        (await Invoke<bool>(pump, "ClaimAsync", id, 0L, CancellationToken.None)).ShouldBeFalse();
        probe.AssertStable(first, clock.GetUtcNow().UtcDateTime);
        probe.Commands[0].Values.ShouldContain(first.AddSeconds(300));
        probe.Commands[1].Values.ShouldContain(clock.GetUtcNow().UtcDateTime.AddSeconds(300));
    }

    [Test]
    public async Task Queue_expiry_completion_uses_clock_parameters_without_recompiling()
    {
        var probe = new QueryProbe("UPDATE \"AgentTasks\"");
        await using var db = await store.OpenAsync(probe);
        var clock = Clock();
        var queue = new SessionMessageQueueService(null!, null!, new MockEventBus(), clock,
            NullLogger<SessionMessageQueueService>.Instance);
        var first = clock.GetUtcNow().UtcDateTime;
        var taskId = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
        {
            var message = new SessionQueuedMessage
            {
                ExecutionTaskId = taskId, ExecutionDeadlineAt = first.AddMinutes(-1),
                Status = QueuedMessageStatus.Pending, DeliveryAttempts = 0,
            };
            (await Invoke<bool>(queue, "CancelExpiredBriefsAsync", db,
                new[] { message }, CancellationToken.None)).ShouldBeTrue();
            message.Status.ShouldBe(QueuedMessageStatus.Canceled);
            message.CanceledAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
            if (i == 0) clock.Advance(TimeSpan.FromMinutes(7));
        }
        probe.AssertStable(first, clock.GetUtcNow().UtcDateTime);
        foreach (var command in probe.Commands)
            command.Values.OfType<Guid>().ShouldNotBeEmpty();
    }

    [Test]
    public async Task Conversion_fence_uses_database_now_in_the_production_command()
    {
        var probe = new QueryProbe("UPDATE \"ChannelOutboundDeliveries\"");
        await using var db = await store.OpenAsync(probe);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var root = Directory.CreateTempSubdirectory("c1925-clock-").FullName;
        var request = Path.Combine(root, "request.json");
        await File.WriteAllTextAsync(request, "{}");
        try
        {
            var project = new Project { Id = Guid.NewGuid(), Name = "clock-witness" };
            var board = new Board { Id = Guid.NewGuid(), Name = "clock-witness", ProjectId = project.Id };
            var agent = new Agent { Id = Guid.NewGuid(), Name = "converter", Slug = Guid.NewGuid().ToString("N"),
                BoardId = board.Id, WorkingDirectory = root };
            db.AddRange(project, board, agent);
            await db.SaveChangesAsync();
            // CreateAsync owns its transaction; commit only into this class's disposable clone.
            await transaction.CommitAsync();
            var delivery = new ChannelOutboundDelivery
            {
                Id = Guid.NewGuid(), ConverterAgentId = agent.Id, ProjectId = project.Id,
                InputPath = Path.Combine(root, "reply.json"), LeaseOwner = Guid.NewGuid(),
            };
            var runner = new OutboundConversionTaskRunner(db, null!);
            for (var i = 0; i < 2; i++)
                await Should.ThrowAsync<DbUpdateConcurrencyException>(() =>
                    runner.CreateAsync(delivery, CancellationToken.None));
            probe.Commands.Count.ShouldBe(2);
            probe.Compilations.ShouldBe(1, probe.Describe());
            foreach (var command in probe.Commands)
            {
                command.Sql.ShouldContain("now()");
                command.Sql.ShouldNotContain("TIMESTAMPTZ '");
                command.Values.OfType<DateTime>().ShouldBeEmpty();
            }
        }
        finally
        {
            File.Delete(request);
            Directory.Delete(root);
        }
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2036, 4, 5, 6, 7, 8, TimeSpan.Zero));

    // Reflection reaches existing private operations without introducing a production seam.
    private static Task<T> Invoke<T>(object service, string method, params object[] arguments) =>
        (Task<T>)(service.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(service.GetType().Name, method)).Invoke(service, arguments)!;

    public sealed class QueryProbe(string commandPrefix) : DbCommandInterceptor, IQueryExpressionInterceptor
    {
        public sealed record Command(string Sql, object[] Values);
        public List<Command> Commands { get; } = [];
        public int Compilations { get; private set; }

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (queryExpression.ToString().Contains("ExecuteUpdate", StringComparison.Ordinal))
                Compilations++;
            return queryExpression;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith(commandPrefix, StringComparison.Ordinal))
            {
                Commands.Add(new Command(command.CommandText,
                    command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));
                Console.WriteLine("C1925 SQL " + command.CommandText);
            }
            return ValueTask.FromResult(result);
        }

        public void AssertStable(DateTime first, DateTime second)
        {
            second.ShouldNotBe(first);
            Commands.Count.ShouldBe(2, Describe());
            Compilations.ShouldBe(1, Describe());
            // SQL equality alone is insufficient: require actual bound clock values on both calls.
            Commands[0].Values.ShouldContain(first);
            Commands[1].Values.ShouldContain(second);
            foreach (var command in Commands)
            {
                command.Sql.ShouldContain("@__");
                command.Sql.ShouldNotContain("TIMESTAMPTZ '");
                command.Sql.ShouldNotContain("now()");
                command.Sql.ShouldNotContain(first.ToString("yyyy-MM-dd"));
                command.Sql.ShouldNotContain(second.ToString("yyyy-MM-dd"));
            }
            Commands[1].Sql.ShouldBe(Commands[0].Sql);
        }

        public string Describe() => $"compilations={Compilations}, commands={Commands.Count}\n"
            + string.Join("\n", Commands.Select(c => c.Sql));
    }
}

/// <summary>One standard TestDbFixture clone for the class; each probe owns a fresh EF cache.</summary>
public sealed class EfClockQueryStore : IAsyncDisposable
{
    private readonly Lazy<Task<IsolatedTestSchema>> _schema = new(TestDbFixture.CreateIsolatedSchemaAsync);

    public async Task<AppDbContext> OpenAsync(EfProductionClockQueryTests.QueryProbe probe)
    {
        var schema = await _schema.Value;
        var options = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(schema.ConnectionString, probe))
            .EnableServiceProviderCaching(false).Options;
        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_schema.IsValueCreated) await (await _schema.Value).DisposeAsync();
    }
}
