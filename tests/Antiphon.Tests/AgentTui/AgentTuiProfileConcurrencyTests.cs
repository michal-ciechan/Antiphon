using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

[Category("Integration")]
[NotInParallel]
[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public sealed class AgentTuiProfileConcurrencyTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 12, 13, 0, 0, TimeSpan.Zero);
    private IsolatedTestSchema? _isolatedSchema;

    public AgentTuiProfileConcurrencyTests(TestDbFixture fixture)
    {
    }

    [Before(Test)]
    public async Task CreateIsolatedSchemaAsync()
    {
        _isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
    }

    [After(Test)]
    public async Task DisposeIsolatedSchemaAsync()
    {
        if (_isolatedSchema is not null)
        {
            await _isolatedSchema.DisposeAsync();
            _isolatedSchema = null;
        }
    }

    [Test]
    public async Task Concurrent_contexts_share_a_disposable_test_schema()
    {
        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();

        var firstDatabase = new NpgsqlConnectionStringBuilder(firstDb.Database.GetConnectionString())
            .Database;
        var secondDatabase = new NpgsqlConnectionStringBuilder(secondDb.Database.GetConnectionString())
            .Database;

        (firstDatabase?.StartsWith("test_", StringComparison.Ordinal) == true).ShouldBeTrue(
            "profile concurrency tests must not use the shared antiphon_test database");
        secondDatabase.ShouldBe(firstDatabase);
        (await firstDb.AgentTuiProfiles.AnyAsync()).ShouldBeFalse(
            "a new profile concurrency schema must not inherit profiles from another test");
        (await firstDb.Agents.AnyAsync()).ShouldBeFalse(
            "a new profile concurrency schema must not inherit agents from another test");
    }

    [Test]
    public async Task Concurrent_default_profile_creates_translate_commit_serialization_failure_to_conflict()
    {
        var rendezvous = new AsyncRendezvous(2);
        await using var firstDb = CreateContext(new ProfileReadBarrierInterceptor(2, rendezvous));
        await using var secondDb = CreateContext(new ProfileReadBarrierInterceptor(2, rendezvous));
        var first = CreateService(firstDb);
        var second = CreateService(secondDb);

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => first.CreateAsync(
                NewRequest($"Concurrent first {Guid.NewGuid():N}"),
                CancellationToken.None)),
            CaptureAsync(() => second.CreateAsync(
                NewRequest($"Concurrent second {Guid.NewGuid():N}"),
                CancellationToken.None)));

        outcomes.Count(outcome => outcome.Result is not null).ShouldBe(1);
        var failure = outcomes.Single(outcome => outcome.Error is not null).Error!;
        failure.ShouldBeOfType<ConflictException>(failure.ToString());
        FindPostgresException(failure).SqlState.ShouldBe(PostgresErrorCodes.SerializationFailure);
        outcomes.ShouldAllBe(outcome =>
            outcome.Error == null || outcome.Error.GetType() == typeof(ConflictException));

        await using var verification = CreateContext();
        (await verification.AgentTuiProfiles.CountAsync()).ShouldBe(1);
        (await verification.AgentTuiProfiles.CountAsync(profile => profile.IsDefault)).ShouldBe(1);
    }

    [Test]
    public async Task Concurrent_startup_imports_retry_reread_and_backfill_safely()
    {
        var agentId = Guid.NewGuid();
        await using (var seedDb = CreateContext())
        {
            seedDb.Agents.Add(NewAgent(agentId));
            await seedDb.SaveChangesAsync();
        }

        var settings = new AgentRegistrySettings
        {
            DefaultDefinition = "claude-main",
            Definitions =
            {
                ["claude-main"] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = "synthetic-claude-wrapper"
                },
                ["raw-secondary"] = new AgentDefinition
                {
                    Kind = "Raw",
                    Exe = "synthetic-raw-wrapper"
                }
            }
        };
        var rendezvous = new AsyncRendezvous(2);
        await using var firstDb = CreateContext(new ProfileReadBarrierInterceptor(2, rendezvous));
        await using var secondDb = CreateContext(new ProfileReadBarrierInterceptor(2, rendezvous));
        var first = CreateImporter(firstDb, settings);
        var second = CreateImporter(secondDb, settings);

        var results = await Task.WhenAll(
            first.ImportAsync(CancellationToken.None),
            second.ImportAsync(CancellationToken.None));

        results.Sum(result => result.ProfilesCreated).ShouldBe(2);
        await using var verification = CreateContext();
        (await verification.AgentTuiProfiles.CountAsync()).ShouldBe(2);
        (await verification.AgentTuiProfiles.CountAsync(profile => profile.IsDefault)).ShouldBe(1);
        var agent = await verification.Agents.AsNoTracking().SingleAsync(candidate => candidate.Id == agentId);
        agent.TuiProfileId.ShouldNotBeNull();
        agent.ModelId.ShouldBe("sonnet");
    }

    [Test]
    public async Task Secret_and_audit_rows_roll_back_together_when_real_transaction_commit_fails()
    {
        AgentTuiProfileDto created;
        await using (var seedDb = CreateContext())
        {
            created = await CreateService(seedDb).CreateAsync(
                NewRequest($"Rollback {Guid.NewGuid():N}") with
                {
                    AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
                    SecretEnvironmentNames = ["SERVICE_TOKEN"]
                },
                CancellationToken.None);
        }

        var correlationId = $"rollback-{Guid.NewGuid():N}";
        await using (var failingDb = CreateContext(new ThrowOnCommitInterceptor()))
        {
            var service = CreateService(failingDb);
            await Should.ThrowAsync<InvalidOperationException>(() => service.PutSecretAsync(
                created.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest("synthetic-rollback-canary", 1, correlationId),
                CancellationToken.None));
        }

        await using var verification = CreateContext();
        (await verification.AgentTuiSecrets.AnyAsync(secret => secret.ProfileId == created.Id))
            .ShouldBeFalse();
        (await verification.AuditRecords.AnyAsync(record => record.Summary.Contains(correlationId)))
            .ShouldBeFalse();
    }

    private static AgentTuiProfileService CreateService(AppDbContext db)
    {
        var audit = new AuditService(
            db,
            Options.Create(new AuditSettings { EnableFullContent = false, EnableIpLogging = true }));
        return new AgentTuiProfileService(
            db,
            new HashingSecretProtector(),
            audit,
            new AgentTuiRunnerCatalog(),
            new FakeTimeProvider(FixedNow),
            new TestCurrentUser(
                new Guid("a0000000-0000-0000-0000-000000000001"),
                "admin",
                "203.0.113.42"));
    }

    private static AgentTuiProfileImporter CreateImporter(
        AppDbContext db,
        AgentRegistrySettings settings) => new(
        db,
        Options.Create(settings),
        new HashingSecretProtector(),
        new AgentTuiRunnerCatalog(),
        new FakeTimeProvider(FixedNow));

    private AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var isolatedSchema = _isolatedSchema
            ?? throw new InvalidOperationException("The test schema must be created before a context is opened.");
        var builder = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(isolatedSchema.ConnectionString));
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private static AgentTuiProfileWriteRequest NewRequest(string displayName) => new(
        DisplayName: displayName,
        Kind: AgentKind.ClaudeCode,
        IsEnabled: true,
        IsDefault: false,
        Executable: "synthetic-claude-wrapper",
        Arguments: ["--synthetic"],
        DiscoveryArguments: [],
        VersionArguments: ["--version"],
        WorkingDirectory: null,
        AuthenticationMode: AgentTuiAuthenticationMode.WrapperManaged,
        NonSecretEnvironment: new Dictionary<string, string>(),
        SecretEnvironmentNames: [],
        ModelArgumentName: "--model",
        Guidance: "Synthetic test profile",
        Models: []);

    private static Agent NewAgent(Guid id) => new()
    {
        Id = id,
        Name = $"Import agent {Guid.NewGuid():N}",
        Slug = $"agent-tui-concurrency-{Guid.NewGuid():N}",
        WorkingDirectory = Path.GetTempPath(),
        Details = string.Empty,
        ModelLevel = AgentModelLevel.Medium,
        CreatedAt = FixedNow.UtcDateTime,
        UpdatedAt = FixedNow.UtcDateTime
    };

    private static async Task<OperationOutcome> CaptureAsync(
        Func<Task<AgentTuiProfileDto>> operation)
    {
        try
        {
            return new OperationOutcome(await operation(), null);
        }
        catch (Exception exception)
        {
            return new OperationOutcome(null, exception);
        }
    }

    private sealed record OperationOutcome(AgentTuiProfileDto? Result, Exception? Error);

    private static PostgresException FindPostgresException(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgres)
                return postgres;
            current = current.InnerException;
        }

        throw new InvalidOperationException(
            "Expected a PostgreSQL concurrency failure in the exception chain.");
    }

    private sealed record TestCurrentUser(Guid UserId, string UserName, string IpAddress) : ICurrentUser;

    private sealed class HashingSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) =>
            $"test-v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))}";

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) =>
            throw new InvalidOperationException("Concurrency tests do not decrypt secrets.");
    }

    private sealed class ProfileReadBarrierInterceptor(
        int targetRead,
        AsyncRendezvous rendezvous) : DbCommandInterceptor
    {
        private int _profileReads;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("AgentTuiProfiles", StringComparison.Ordinal)
                && Interlocked.Increment(ref _profileReads) == targetRead)
            {
                await rendezvous.SignalAndWaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ThrowOnCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult>(
                new InvalidOperationException("Synthetic commit failure after database writes."));
    }

    private sealed class AsyncRendezvous(int participants)
    {
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == participants)
                _ready.TrySetResult();
            return _ready.Task.WaitAsync(cancellationToken);
        }
    }
}
