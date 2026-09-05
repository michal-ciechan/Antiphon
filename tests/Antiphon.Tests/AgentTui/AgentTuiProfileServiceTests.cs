using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
// Each test owns a cloned database. The group key is kept so this class still serialises against
// itself (CARD-0110 S2 does not change NotInParallel attributes).
[NotInParallel("AgentTuiProfileServiceSchema")]
[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public class AgentTuiProfileServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private IsolatedTestSchema? _isolatedSchema;
    protected AppDbContext DbContext { get; private set; } = null!;

    public AgentTuiProfileServiceTests(TestDbFixture fixture)
    {
    }

    [Before(Test)]
    public async Task CreateIsolatedSchemaAsync()
    {
        _isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        DbContext = new AppDbContext(
            TestDbFixture.CreateDbContextOptions(_isolatedSchema.ConnectionString));
        await DbContext.Database.BeginTransactionAsync();
    }

    [After(Test)]
    public async Task DisposeIsolatedSchemaAsync()
    {
        try
        {
            if (DbContext.Database.CurrentTransaction is not null)
                await DbContext.Database.RollbackTransactionAsync();
        }
        finally
        {
            if (DbContext is not null)
                await DbContext.DisposeAsync();
            DbContext = null!;

            if (_isolatedSchema is not null)
            {
                await _isolatedSchema.DisposeAsync();
                _isolatedSchema = null;
            }
        }
    }

    [Test]
    public async Task Uses_a_fresh_cloned_database_for_each_test()
    {
        var database = new NpgsqlConnectionStringBuilder(DbContext.Database.GetConnectionString())
            .Database;

        (database?.StartsWith("test_", StringComparison.Ordinal) == true).ShouldBeTrue(
            "profile service tests must not write managed-profile state into the shared antiphon_test database");
        (await DbContext.AgentTuiProfiles.AnyAsync()).ShouldBeFalse();
        (await DbContext.Agents.AnyAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Editing_a_profile_Kind_resyncs_attached_agents_and_leaves_other_profiles_alone()
    {
        var service = CreateService();
        var codexRequest = NewRequest(UniqueName("Codex")) with { Kind = AgentKind.Codex };
        var grokRequest = NewRequest(UniqueName("Grok")) with { Kind = AgentKind.Grok };
        var codex = await service.CreateAsync(codexRequest, CancellationToken.None);
        var grok = await service.CreateAsync(grokRequest, CancellationToken.None);

        var attached = NewAgent(codex.Id, AgentModelLevel.High);
        attached.Kind = AgentKind.Codex;
        var other = NewAgent(grok.Id, AgentModelLevel.High);
        other.Kind = AgentKind.Grok;
        DbContext.Agents.AddRange(attached, other);
        await DbContext.SaveChangesAsync();

        await service.UpdateAsync(
            codex.Id,
            codexRequest with { ExpectedRevision = 1, Kind = AgentKind.OpenCode },
            CancellationToken.None);

        DbContext.ChangeTracker.Clear();
        var resynced = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == attached.Id);
        resynced.Kind.ShouldBe(AgentKind.OpenCode);
        resynced.TuiProfileId.ShouldBe(codex.Id);
        var untouched = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == other.Id);
        untouched.Kind.ShouldBe(AgentKind.Grok);
        untouched.TuiProfileId.ShouldBe(grok.Id);
    }

    [Test]
    public async Task Create_and_update_produce_immutable_monotonic_revisions()
    {
        var service = CreateService();
        var request = NewRequest(UniqueName("Revision"));

        var created = await service.CreateAsync(request, CancellationToken.None);
        var updated = await service.UpdateAsync(
            created.Id,
            request with { ExpectedRevision = 1, DisplayName = UniqueName("Changed") },
            CancellationToken.None);

        created.Revision.ShouldBe(1);
        updated.Revision.ShouldBe(2);
        await Should.ThrowAsync<ConflictException>(() => service.UpdateAsync(
            created.Id,
            request with { ExpectedRevision = 1 },
            CancellationToken.None));

        var revisions = await DbContext.AgentTuiProfileRevisions
            .AsNoTracking()
            .Where(revision => revision.ProfileId == created.Id)
            .OrderBy(revision => revision.RevisionNumber)
            .ToListAsync();
        revisions.Select(revision => revision.RevisionNumber).ShouldBe([1, 2]);
        revisions[0].Executable.ShouldBe(request.Executable);
        revisions[0].ArgumentsJson.ShouldBe(JsonSerializer.Serialize(request.Arguments));
    }

    [Test]
    public async Task Profile_mutations_preserve_exactly_one_installation_default()
    {
        var service = CreateService();
        var first = await service.CreateAsync(
            NewRequest(UniqueName("First")) with { IsDefault = false },
            CancellationToken.None);
        var secondRequest = NewRequest(UniqueName("Second")) with { IsDefault = true };
        var second = await service.CreateAsync(secondRequest, CancellationToken.None);

        var profiles = await service.ListAsync(CancellationToken.None);
        profiles.Count(profile => profile.IsDefault).ShouldBe(1);
        profiles.Single(profile => profile.IsDefault).Id.ShouldBe(second.Id);
        profiles.Single(profile => profile.Id == first.Id).IsDefault.ShouldBeFalse();

        var retainedDefault = await service.UpdateAsync(
            second.Id,
            secondRequest with { ExpectedRevision = 1, IsDefault = false, IsEnabled = false },
            CancellationToken.None);
        retainedDefault.IsDefault.ShouldBeTrue();
        retainedDefault.IsEnabled.ShouldBeTrue();
    }

    [Test]
    public async Task Duplicate_copies_the_active_revision_and_operator_models_but_not_secrets()
    {
        var service = CreateService();
        var source = await service.CreateAsync(
            NewRequest(UniqueName("Source")) with
            {
                AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
                SecretEnvironmentNames = ["SERVICE_TOKEN"],
                Models =
                [
                    new AgentTuiModelWriteDto(
                        "operator/provider-model",
                        "Operator model",
                        "operator",
                        true)
                ]
            },
            CancellationToken.None);
        await service.PutSecretAsync(
            source.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest("synthetic-duplicate-canary", 1, "duplicate-secret-set"),
            CancellationToken.None);

        var duplicate = await service.DuplicateAsync(
            source.Id,
            new DuplicateAgentTuiProfileRequest(UniqueName("Copy")),
            CancellationToken.None);

        duplicate.IsEnabled.ShouldBeFalse();
        duplicate.IsDefault.ShouldBeFalse();
        duplicate.Revision.ShouldBe(1);
        duplicate.RevisionDetails.Executable.ShouldBe(source.RevisionDetails.Executable);
        duplicate.RevisionDetails.Arguments.ShouldBe(source.RevisionDetails.Arguments);
        duplicate.Models.ShouldContain(model =>
            model.Identifier == "operator/provider-model" && model.Source == AgentTuiModelSource.Operator);
        duplicate.SecretEnvironment.Single(secret => secret.Name == "SERVICE_TOKEN").Configured.ShouldBeFalse();
        (await DbContext.AgentTuiSecrets.CountAsync(secret => secret.ProfileId == duplicate.Id)).ShouldBe(0);
    }

    [Test]
    public async Task Delete_rejects_default_agent_and_historical_session_references()
    {
        var service = CreateService();
        var installationDefault = await service.CreateAsync(
            NewRequest(UniqueName("Default")),
            CancellationToken.None);
        await Should.ThrowAsync<ConflictException>(() =>
            service.DeleteAsync(installationDefault.Id, CancellationToken.None));

        var assigned = await service.CreateAsync(
            NewRequest(UniqueName("Assigned")),
            CancellationToken.None);
        DbContext.Agents.Add(NewAgent(assigned.Id, AgentModelLevel.High));
        await DbContext.SaveChangesAsync();
        await Should.ThrowAsync<ConflictException>(() =>
            service.DeleteAsync(assigned.Id, CancellationToken.None));

        var historical = await service.CreateAsync(
            NewRequest(UniqueName("Historical")),
            CancellationToken.None);
        DbContext.AgentSessions.Add(new AgentSession
        {
            Id = Guid.NewGuid(),
            TuiProfileRevisionId = historical.RevisionId,
            DefinitionName = "synthetic",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Stopped,
            Cwd = Path.GetTempPath(),
            CreatedAt = FixedNow.UtcDateTime,
            StartedAt = FixedNow.UtcDateTime,
            LastSeenAt = FixedNow.UtcDateTime,
            EndedAt = FixedNow.UtcDateTime
        });
        await DbContext.SaveChangesAsync();
        await Should.ThrowAsync<ConflictException>(() =>
            service.DeleteAsync(historical.Id, CancellationToken.None));
    }

    [Test]
    public async Task Reads_expose_secret_metadata_only()
    {
        var protector = new HashingSecretProtector();
        var service = CreateService(protector);
        var created = await service.CreateAsync(
            NewRequest(UniqueName("Metadata")) with
            {
                AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
                SecretEnvironmentNames = ["SERVICE_TOKEN", "OPTIONAL_SECRET"]
            },
            CancellationToken.None);
        const string submittedCanary = "synthetic-read-canary";
        await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(submittedCanary, 1, "metadata-secret-set"),
            CancellationToken.None);

        var profile = await service.GetAsync(created.Id, CancellationToken.None);
        profile.SecretEnvironment.Count.ShouldBe(2);
        profile.SecretEnvironment.Single(secret => secret.Name == "SERVICE_TOKEN").Configured.ShouldBeTrue();
        profile.SecretEnvironment.Single(secret => secret.Name == "OPTIONAL_SECRET").Configured.ShouldBeFalse();

        var forbiddenNames = new[] { "Value", "Plaintext", "Ciphertext", "ProtectedValue" };
        typeof(AgentTuiSecretMetadataDto).GetProperties()
            .Select(property => property.Name)
            .ShouldAllBe(name => !forbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        var serialized = JsonSerializer.Serialize(profile);
        serialized.ShouldNotContain(submittedCanary);
        serialized.ShouldNotContain(protector.LastCiphertext!);
    }

    [Test]
    public async Task Secret_replace_and_clear_are_atomic_revision_checked_and_safely_audited()
    {
        var clock = new FakeTimeProvider(FixedNow);
        var protector = new HashingSecretProtector();
        var request = NewRequest(UniqueName("Secret atomicity")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            SecretEnvironmentNames = ["SERVICE_TOKEN"]
        };
        var actorId = Guid.NewGuid();
        DbContext.Users.Add(new User
        {
            Id = actorId,
            UserName = UniqueName("Secret actor"),
            Email = $"{Guid.NewGuid():N}@example.invalid",
            IsAdmin = true,
            CreatedAt = FixedNow.UtcDateTime
        });
        await DbContext.SaveChangesAsync();
        var service = CreateService(
            protector,
            clock,
            new TestCurrentUser(actorId, "trusted-actor", "203.0.113.42"));
        var created = await service.CreateAsync(request, CancellationToken.None);
        const string oldCanary = "synthetic-old-secret-canary";
        const string rejectedCanary = "synthetic-rejected-secret-canary";
        const string newCanary = "synthetic-new-secret-canary";

        var set = await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(oldCanary, 1, "secret-set-correlation"),
            CancellationToken.None);
        set.Revision.ShouldBe(1);
        var oldCiphertext = (await DbContext.AgentTuiSecrets.AsNoTracking()
            .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext;

        clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await service.UpdateAsync(
            created.Id,
            request with { ExpectedRevision = 1, DisplayName = UniqueName("Secret revised") },
            CancellationToken.None);
        updated.Revision.ShouldBe(2);

        await Should.ThrowAsync<ConflictException>(() => service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(rejectedCanary, 1, "secret-rejected-correlation"),
            CancellationToken.None));
        (await DbContext.AgentTuiSecrets.AsNoTracking()
            .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext.ShouldBe(oldCiphertext);

        clock.Advance(TimeSpan.FromMinutes(1));
        var replaced = await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(newCanary, 2, "secret-replace-correlation"),
            CancellationToken.None);
        replaced.Revision.ShouldBe(2);
        var newCiphertext = (await DbContext.AgentTuiSecrets.AsNoTracking()
            .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext;
        newCiphertext.ShouldNotBe(oldCiphertext);

        clock.Advance(TimeSpan.FromMinutes(1));
        var cleared = await service.ClearSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretClearRequest(2, "secret-clear-correlation"),
            CancellationToken.None);
        cleared.Configured.ShouldBeFalse();
        (await DbContext.AgentTuiSecrets.CountAsync(secret => secret.ProfileId == created.Id)).ShouldBe(0);

        var auditRecords = await DbContext.AuditRecords.AsNoTracking()
            .Where(record => record.Summary.Contains(created.Id.ToString()))
            .OrderBy(record => record.CreatedAt)
            .ToListAsync();
        auditRecords.Count.ShouldBe(3);
        auditRecords.ShouldAllBe(record =>
            record.Summary.Contains("environmentName=SERVICE_TOKEN", StringComparison.Ordinal)
            && record.Summary.Contains("result=succeeded", StringComparison.Ordinal)
            && record.Summary.Contains("occurredAt=", StringComparison.Ordinal)
            && record.UserId == actorId
            && record.ClientIp == "203.0.113.42"
            && record.CreatedAt != default);
        auditRecords.ShouldContain(record =>
            record.Summary.Contains("operation=set", StringComparison.Ordinal)
            && record.Summary.Contains("correlationId=secret-set-correlation", StringComparison.Ordinal));
        auditRecords.ShouldContain(record =>
            record.Summary.Contains("operation=replace", StringComparison.Ordinal)
            && record.Summary.Contains("correlationId=secret-replace-correlation", StringComparison.Ordinal));
        auditRecords.ShouldContain(record =>
            record.Summary.Contains("operation=clear", StringComparison.Ordinal)
            && record.Summary.Contains("correlationId=secret-clear-correlation", StringComparison.Ordinal));

        var auditText = string.Join("\n", auditRecords.Select(record => $"{record.Summary}\n{record.FullContent}"));
        auditText.ShouldNotContain(oldCanary);
        auditText.ShouldNotContain(rejectedCanary);
        auditText.ShouldNotContain(newCanary);
        auditText.ShouldNotContain(oldCiphertext);
        auditText.ShouldNotContain(newCiphertext);
        auditText.ShouldNotContain("secret-rejected-correlation");
    }

    [Test]
    public async Task Secret_protection_precedes_the_write_transaction()
    {
        var events = new List<string>();
        var protector = new RecordingSecretProtector(events);
        var transactionInterceptor = new RecordingTransactionInterceptor(events);
        await using var db = CreateIndependentContext(transactionInterceptor);
        Guid? profileId = null;

        try
        {
            var service = CreateService(db, protector);
            var managedRequest = NewRequest(UniqueName("Protection order")) with
            {
                AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
                SecretEnvironmentNames = ["SERVICE_TOKEN"]
            };
            var created = await service.CreateAsync(
                managedRequest,
                CancellationToken.None);
            profileId = created.Id;
            events.Clear();

            await service.PutSecretAsync(
                created.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest(
                    "synthetic-protection-order-canary",
                    1,
                    "protection-order"),
                CancellationToken.None);

            events.ShouldBe(["protect:SERVICE_TOKEN", "transaction-starting"]);
            var originalCiphertext = (await db.AgentTuiSecrets.AsNoTracking()
                .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext;
            events.Clear();
            transactionInterceptor.OnStartingAsync = async cancellationToken =>
            {
                await using var concurrentDb = CreateIndependentContext();
                var concurrentService = CreateService(
                    concurrentDb,
                    new RecordingSecretProtector([]));
                await concurrentService.UpdateAsync(
                    created.Id,
                    managedRequest with { ExpectedRevision = 1 },
                    cancellationToken);
            };

            await Should.ThrowAsync<ConflictException>(() => service.PutSecretAsync(
                created.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest(
                    "synthetic-authoritative-reread-canary",
                    1,
                    "authoritative-reread"),
                CancellationToken.None));

            events.ShouldBe(["protect:SERVICE_TOKEN", "transaction-starting"]);
            (await db.AgentTuiProfileRevisions.AsNoTracking()
                .Where(revision => revision.ProfileId == created.Id)
                .MaxAsync(revision => revision.RevisionNumber)).ShouldBe(2);
            (await db.AgentTuiSecrets.AsNoTracking()
                .SingleAsync(secret => secret.ProfileId == created.Id))
                .Ciphertext.ShouldBe(originalCiphertext);
            (await db.AuditRecords.AnyAsync(record =>
                record.Summary.Contains("authoritative-reread"))).ShouldBeFalse();
        }
        finally
        {
            if (profileId is not null)
                await DeleteIndependentProfileAsync(db, profileId.Value);
        }
    }

    [Test]
    public async Task Secret_preflight_rejections_do_not_protect_or_begin_writes()
    {
        var events = new List<string>();
        var protector = new RecordingSecretProtector(events);
        await using var db = CreateIndependentContext(new RecordingTransactionInterceptor(events));
        var profileIds = new List<Guid>();

        try
        {
            var service = CreateService(db, protector);
            var managedRequest = NewRequest(UniqueName("Stale preflight")) with
            {
                AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
                SecretEnvironmentNames = ["SERVICE_TOKEN"]
            };
            var managed = await service.CreateAsync(managedRequest, CancellationToken.None);
            profileIds.Add(managed.Id);
            await service.UpdateAsync(
                managed.Id,
                managedRequest with { ExpectedRevision = 1 },
                CancellationToken.None);
            events.Clear();

            await Should.ThrowAsync<ConflictException>(() => service.PutSecretAsync(
                managed.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest(
                    "synthetic-stale-preflight-canary",
                    1,
                    "stale-preflight"),
                CancellationToken.None));

            events.ShouldBeEmpty();
            (await db.AgentTuiSecrets.AnyAsync(secret => secret.ProfileId == managed.Id)).ShouldBeFalse();
            (await db.AuditRecords.AnyAsync(record => record.Summary.Contains("stale-preflight")))
                .ShouldBeFalse();

            var wrapper = await service.CreateAsync(
                NewRequest(UniqueName("Mode preflight")),
                CancellationToken.None);
            profileIds.Add(wrapper.Id);
            events.Clear();

            await Should.ThrowAsync<ValidationException>(() => service.PutSecretAsync(
                wrapper.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest(
                    "synthetic-mode-preflight-canary",
                    1,
                    "mode-preflight"),
                CancellationToken.None));

            events.ShouldBeEmpty();
            (await db.AgentTuiSecrets.AnyAsync(secret => secret.ProfileId == wrapper.Id)).ShouldBeFalse();
            (await db.AuditRecords.AnyAsync(record => record.Summary.Contains("mode-preflight")))
                .ShouldBeFalse();
        }
        finally
        {
            foreach (var profileId in profileIds)
                await DeleteIndependentProfileAsync(db, profileId);
        }
    }

    [Test]
    public async Task Windows_secret_replacement_canonicalizes_case_only_declaration_renames()
    {
        var protector = new RecordingSecretProtector([]);
        var service = CreateService(DbContext, protector, StringComparer.OrdinalIgnoreCase);
        var originalRequest = NewRequest(UniqueName("Canonical secret")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            SecretEnvironmentNames = ["Service_Token"]
        };
        var created = await service.CreateAsync(originalRequest, CancellationToken.None);
        await service.PutSecretAsync(
            created.Id,
            "service_token",
            new AgentTuiSecretWriteRequest(
                "synthetic-original-case-canary",
                1,
                "canonical-original"),
            CancellationToken.None);
        await service.UpdateAsync(
            created.Id,
            originalRequest with
            {
                ExpectedRevision = 1,
                SecretEnvironmentNames = ["SERVICE_TOKEN"]
            },
            CancellationToken.None);

        var replaced = await service.PutSecretAsync(
            created.Id,
            "service_token",
            new AgentTuiSecretWriteRequest(
                "synthetic-replacement-case-canary",
                2,
                "canonical-replacement"),
            CancellationToken.None);

        replaced.Name.ShouldBe("SERVICE_TOKEN");
        var persistedSecrets = await DbContext.AgentTuiSecrets.AsNoTracking()
            .Where(secret => secret.ProfileId == created.Id)
            .ToListAsync();
        var persistedSecret = persistedSecrets.ShouldHaveSingleItem();
        persistedSecret.Name.ShouldBe("SERVICE_TOKEN");
        protector.LastProtectedEnvironmentName.ShouldBe("SERVICE_TOKEN");
        protector.Unprotect(created.Id, persistedSecret.Name, persistedSecret.Ciphertext)
            .ShouldBe("synthetic-replacement-case-canary");
        protector.LastUnprotectedEnvironmentName.ShouldBe("SERVICE_TOKEN");

        var readMetadata = (await service.GetAsync(created.Id, CancellationToken.None))
            .SecretEnvironment.ShouldHaveSingleItem();
        readMetadata.Name.ShouldBe("SERVICE_TOKEN");
        readMetadata.Configured.ShouldBeTrue();

        var cleared = await service.ClearSecretAsync(
            created.Id,
            "service_token",
            new AgentTuiSecretClearRequest(2, "canonical-clear"),
            CancellationToken.None);
        cleared.Name.ShouldBe("SERVICE_TOKEN");
        var clearedMetadata = (await service.GetAsync(created.Id, CancellationToken.None))
            .SecretEnvironment.ShouldHaveSingleItem();
        clearedMetadata.Name.ShouldBe("SERVICE_TOKEN");
        clearedMetadata.Configured.ShouldBeFalse();

        var clearBeforeReplaceRequest = NewRequest(UniqueName("Canonical clear")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            SecretEnvironmentNames = ["Service_Token"]
        };
        var clearBeforeReplace = await service.CreateAsync(
            clearBeforeReplaceRequest,
            CancellationToken.None);
        await service.PutSecretAsync(
            clearBeforeReplace.Id,
            "service_token",
            new AgentTuiSecretWriteRequest(
                "synthetic-clear-before-replace-canary",
                1,
                "canonical-clear-original"),
            CancellationToken.None);
        await service.UpdateAsync(
            clearBeforeReplace.Id,
            clearBeforeReplaceRequest with
            {
                ExpectedRevision = 1,
                SecretEnvironmentNames = ["SERVICE_TOKEN"]
            },
            CancellationToken.None);

        var canonicalClear = await service.ClearSecretAsync(
            clearBeforeReplace.Id,
            "service_token",
            new AgentTuiSecretClearRequest(2, "canonical-clear-before-replace"),
            CancellationToken.None);

        canonicalClear.Name.ShouldBe("SERVICE_TOKEN");
        var canonicalClearedMetadata = (await service.GetAsync(
                clearBeforeReplace.Id,
                CancellationToken.None))
            .SecretEnvironment.ShouldHaveSingleItem();
        canonicalClearedMetadata.Name.ShouldBe("SERVICE_TOKEN");
        canonicalClearedMetadata.Configured.ShouldBeFalse();
    }

    [Test]
    public void Secret_write_contract_cannot_supply_an_audit_actor()
    {
        typeof(AgentTuiSecretWriteRequest).GetProperties()
            .ShouldNotContain(property => property.Name == "ActorId");
        typeof(AgentTuiSecretClearRequest).GetProperties()
            .ShouldNotContain(property => property.Name == "ActorId");
    }

    [Test]
    public async Task Wrapper_managed_profiles_reject_secret_declarations_and_puts()
    {
        var service = CreateService(new FailOnAccessSecretProtector());

        await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            NewRequest(UniqueName("Wrapper declaration")) with
            {
                SecretEnvironmentNames = ["SERVICE_TOKEN"]
            },
            CancellationToken.None));

        var wrapper = await service.CreateAsync(
            NewRequest(UniqueName("Wrapper put")),
            CancellationToken.None);
        await Should.ThrowAsync<ValidationException>(() => service.PutSecretAsync(
            wrapper.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest("must-not-be-protected", 1, "wrapper-put"),
            CancellationToken.None));
    }

    [Test]
    public async Task Configured_secrets_must_be_cleared_before_removal_or_wrapper_transition()
    {
        var service = CreateService();
        var managed = NewRequest(UniqueName("Managed transition")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            SecretEnvironmentNames = ["SERVICE_TOKEN"]
        };
        var created = await service.CreateAsync(managed, CancellationToken.None);
        await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest("synthetic-transition-canary", 1, "transition-set"),
            CancellationToken.None);

        await Should.ThrowAsync<ConflictException>(() => service.UpdateAsync(
            created.Id,
            managed with
            {
                ExpectedRevision = 1,
                SecretEnvironmentNames = []
            },
            CancellationToken.None));
        await Should.ThrowAsync<ConflictException>(() => service.UpdateAsync(
            created.Id,
            managed with
            {
                ExpectedRevision = 1,
                AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                SecretEnvironmentNames = []
            },
            CancellationToken.None));

        await service.ClearSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretClearRequest(1, "transition-clear"),
            CancellationToken.None);
        var switched = await service.UpdateAsync(
            created.Id,
            managed with
            {
                ExpectedRevision = 1,
                AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                SecretEnvironmentNames = []
            },
            CancellationToken.None);

        switched.Revision.ShouldBe(2);
        switched.RevisionDetails.AuthenticationMode.ShouldBe(AgentTuiAuthenticationMode.WrapperManaged);
    }

    [Test]
    public async Task Clear_removes_a_persisted_orphan_secret_even_when_no_longer_declared()
    {
        var service = CreateService();
        var managed = NewRequest(UniqueName("Orphan clear")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            SecretEnvironmentNames = ["SERVICE_TOKEN"]
        };
        var created = await service.CreateAsync(managed, CancellationToken.None);
        await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest("synthetic-orphan-canary", 1, "orphan-set"),
            CancellationToken.None);
        var revision = await DbContext.AgentTuiProfileRevisions
            .SingleAsync(candidate => candidate.Id == created.RevisionId);
        revision.SecretEnvironmentNamesJson = "[]";
        revision.AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged;
        await DbContext.SaveChangesAsync();

        var cleared = await service.ClearSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretClearRequest(1, "orphan-clear"),
            CancellationToken.None);

        cleared.Configured.ShouldBeFalse();
        (await DbContext.AgentTuiSecrets
            .AnyAsync(secret => secret.ProfileId == created.Id)).ShouldBeFalse();
    }

    [Test]
    public async Task Profile_validation_rejects_undefined_enums_and_all_bounded_fields()
    {
        var service = CreateService();
        var valid = NewRequest(UniqueName("Validation"));
        AgentTuiProfileWriteRequest[] invalidRequests =
        [
            valid with { Kind = (AgentKind)999 },
            valid with { AuthenticationMode = (AgentTuiAuthenticationMode)999 },
            valid with { WorkingDirectory = new string('w', 1001) },
            valid with { ModelArgumentName = new string('m', 101) },
            valid with { Arguments = [new string('a', 2001)] },
            valid with { DiscoveryArguments = [new string('d', 2001)] },
            valid with { VersionArguments = [new string('v', 2001)] },
            valid with
            {
                NonSecretEnvironment = new Dictionary<string, string>
                {
                    ["NORMAL_SETTING"] = new string('e', 4001)
                }
            },
            valid with { Guidance = new string('g', 4001) },
            valid with
            {
                Models = [new AgentTuiModelWriteDto("model", "Model", new string('f', 201))]
            }
        ];

        foreach (var request in invalidRequests)
        {
            await Should.ThrowAsync<ValidationException>(() =>
                service.CreateAsync(request, CancellationToken.None));
        }
    }

    [Test]
    public async Task Profile_environment_validation_uses_host_platform_name_equivalence()
    {
        var service = CreateService();
        var request = NewRequest(UniqueName("Environment comparer")) with
        {
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            NonSecretEnvironment = new Dictionary<string, string> { ["SERVICE_TOKEN"] = "ordinary" },
            SecretEnvironmentNames = ["service_token"]
        };

        if (OperatingSystem.IsWindows())
        {
            await Should.ThrowAsync<ValidationException>(() =>
                service.CreateAsync(request, CancellationToken.None));
        }
        else
        {
            (await service.CreateAsync(request, CancellationToken.None)).Revision.ShouldBe(1);
        }
    }

    [Test]
    public async Task Cached_profile_and_model_reads_require_no_secret_or_process_io()
    {
        var service = CreateService(new FailOnAccessSecretProtector());
        var created = await service.CreateAsync(
            NewRequest(UniqueName("Cached reads")),
            CancellationToken.None);

        var profile = await service.GetAsync(created.Id, CancellationToken.None);
        var models = await service.GetModelsAsync(created.Id, CancellationToken.None);

        profile.Id.ShouldBe(created.Id);
        models.ShouldContain(model => model.Identifier == "fable");
        models.ShouldContain(model => model.Identifier == "opus");
        models.ShouldContain(model => model.Identifier == "sonnet");
        models.ShouldContain(model => model.Identifier == "haiku");
    }

    [Test]
    public void Runner_catalogue_is_curated_and_truthful_without_probing()
    {
        var catalog = new AgentTuiRunnerCatalog();

        catalog.Get(AgentKind.ClaudeCode).CuratedModels.Select(model => model.Identifier)
            .ShouldBe(["fable", "opus", "sonnet", "haiku"]);
        catalog.Get(AgentKind.Codex).CuratedModels.Select(model => model.Identifier)
            .ShouldBe(["gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"]);

        var grok = catalog.Get(AgentKind.Grok, ["--always-approve", "--no-alt-screen"]);
        grok.CuratedModels.Select(model => model.Identifier).ShouldBe(["grok-4.6", "grok-4.5"]);
        Capability(grok, "modelArgument").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(grok, "modelDiscovery").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(grok, "structuredActivity").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(grok, "sessionResume").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(grok, "remoteControl").State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        catalog.SupportsRemoteControl(AgentKind.Grok).ShouldBeFalse();
        Capability(grok, "systemPromptAppend").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(grok, "permissionBypass").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(catalog.Get(AgentKind.Grok, ["--no-alt-screen"]), "permissionBypass").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);

        var openCode = catalog.Get(AgentKind.OpenCode, ["--auto", "--mini"]);
        openCode.CuratedModels.Select(model => model.Identifier).ShouldBe(["llmgateway/grok-4-5"]);
        Capability(openCode, "modelArgument").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(openCode, "modelDiscovery").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(openCode, "structuredActivity").State.ShouldBe(AgentTuiCapabilityState.Degraded);
        Capability(openCode, "structuredActivity").Reason.ShouldBe(
            ProviderContractCatalog.For(AgentKind.OpenCode).TurnCompletion.Reason);
        Capability(openCode, "sessionResume").State.ShouldBe(AgentTuiCapabilityState.Unknown);
        Capability(openCode, "remoteControl").State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        catalog.SupportsRemoteControl(AgentKind.OpenCode).ShouldBeFalse();
        Capability(openCode, "systemPromptAppend").State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        Capability(openCode, "permissionBypass").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(catalog.Get(AgentKind.OpenCode, ["--mini"]), "permissionBypass").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);

        var claude = catalog.Get(AgentKind.ClaudeCode);
        Capability(claude, "structuredActivity").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(claude, "sessionResume").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(claude, "remoteControl").State.ShouldBe(AgentTuiCapabilityState.Supported);
        catalog.SupportsRemoteControl(AgentKind.ClaudeCode).ShouldBeTrue();
        Capability(catalog.Get(AgentKind.Codex), "remoteControl").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);
        catalog.SupportsRemoteControl(AgentKind.Codex).ShouldBeFalse();
        Capability(catalog.Get(AgentKind.Raw), "remoteControl").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);
        catalog.SupportsRemoteControl(AgentKind.Raw).ShouldBeFalse();
        Capability(claude, "systemPromptAppend").State.ShouldBe(AgentTuiCapabilityState.Supported);
        // CARD-0099 S1: Codex's rollout tailer makes task_complete a real structured turn end, so
        // this row is now Supported — derived from TurnCompletion, like every other kind's.
        Capability(catalog.Get(AgentKind.Codex), "structuredActivity").State
            .ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(catalog.Get(AgentKind.Codex), "structuredActivity").Reason.ShouldBe(
            ProviderContractCatalog.For(AgentKind.Codex).TurnCompletion.Reason);
    }

    [Test]
    public async Task T10_blank_model_argument_name_reports_modelArgument_unsupported()
    {
        var service = CreateService();
        var blank = await service.CreateAsync(
            NewRequest(UniqueName("Blank arg")) with
            {
                Kind = AgentKind.Grok,
                ModelArgumentName = null
            },
            CancellationToken.None);
        var blankCaps = await service.GetCapabilitiesAsync(blank.Id, CancellationToken.None);
        var blankRow = blankCaps.Capabilities.Single(c => c.Name == "modelArgument");
        blankRow.State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        blankRow.Reason.ShouldBe("The active revision declares no model argument.");
        blank.Capabilities.Single(c => c.Name == "modelArgument").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);

        var declared = await service.CreateAsync(
            NewRequest(UniqueName("Declared arg")) with
            {
                Kind = AgentKind.Grok,
                ModelArgumentName = "--model"
            },
            CancellationToken.None);
        var declaredCaps = await service.GetCapabilitiesAsync(declared.Id, CancellationToken.None);
        declaredCaps.Capabilities.Single(c => c.Name == "modelArgument").State
            .ShouldBe(AgentTuiCapabilityState.Supported);
    }

    [Test]
    public async Task Import_is_two_pass_idempotent_and_preserves_exact_legacy_Claude_models()
    {
        var clock = new FakeTimeProvider(FixedNow);
        var protector = new HashingSecretProtector();
        var agents = Enum.GetValues<AgentModelLevel>()
            .Select(level => NewAgent(profileId: null, level))
            .ToArray();
        DbContext.Agents.AddRange(agents);
        await DbContext.SaveChangesAsync();
        var profileIdsBeforeImport = await DbContext.AgentTuiProfiles.AsNoTracking()
            .Select(profile => profile.Id)
            .ToListAsync();

        var originalSettings = new AgentRegistrySettings
        {
            DefaultDefinition = "claude-main",
            Definitions = new Dictionary<string, AgentDefinition>
            {
                ["zeta-raw"] = new()
                {
                    Kind = "Raw",
                    Exe = "synthetic-raw-wrapper",
                    ArgsTemplate = ["--raw"],
                    Env = new Dictionary<string, string>
                    {
                        ["RAW_PASSWORD"] = "synthetic-import-raw-canary"
                    },
                    SecretEnvironmentNames = ["RAW_PASSWORD"]
                },
                ["claude-main"] = new()
                {
                    Kind = "ClaudeCode",
                    Exe = "synthetic-claude-wrapper",
                    ArgsTemplate = ["--first", "{prompt}"],
                    Env = new Dictionary<string, string>
                    {
                        ["NORMAL_SETTING"] = "ordinary-value",
                        ["SERVICE_TOKEN"] = "synthetic-import-claude-canary"
                    },
                    NonSecretEnvironmentNames = ["NORMAL_SETTING"],
                    SecretEnvironmentNames = ["SERVICE_TOKEN"]
                }
            }
        };
        var importer = CreateImporter(originalSettings, protector, clock);

        var firstPass = await importer.ImportAsync(CancellationToken.None);

        firstPass.ProfilesCreated.ShouldBe(2);
        var importedProfiles = await DbContext.AgentTuiProfiles.AsNoTracking()
            .Where(profile => !profileIdsBeforeImport.Contains(profile.Id)
                && profile.Source == AgentTuiProfileSource.ImportedFile)
            .OrderBy(profile => profile.SourceDefinitionName)
            .ToListAsync();
        importedProfiles.Select(profile => profile.SourceDefinitionName)
            .ShouldBe(["claude-main", "zeta-raw"]);
        importedProfiles.Count(profile => profile.IsDefault).ShouldBe(1);
        var defaultProfile = importedProfiles.Single(profile => profile.IsDefault);
        defaultProfile.SourceDefinitionName.ShouldBe("claude-main");
        var importedRevision = await DbContext.AgentTuiProfileRevisions.AsNoTracking()
            .SingleAsync(revision => revision.Id == defaultProfile.ActiveRevisionId);
        importedRevision.Executable.ShouldBe("synthetic-claude-wrapper");
        JsonSerializer.Deserialize<string[]>(importedRevision.ArgumentsJson)
            .ShouldBe(["--first", "{prompt}"]);
        JsonSerializer.Deserialize<Dictionary<string, string>>(importedRevision.NonSecretEnvironmentJson)!
            .ShouldBe(new Dictionary<string, string> { ["NORMAL_SETTING"] = "ordinary-value" });
        JsonSerializer.Deserialize<string[]>(importedRevision.SecretEnvironmentNamesJson)
            .ShouldBe(["SERVICE_TOKEN"]);

        var assigned = await DbContext.Agents.AsNoTracking()
            .Where(agent => agents.Select(seed => seed.Id).Contains(agent.Id))
            .OrderBy(agent => agent.ModelLevel)
            .ToListAsync();
        assigned.Count.ShouldBe(agents.Length);
        assigned.ShouldAllBe(agent => agent.TuiProfileId == defaultProfile.Id);
        assigned.ToDictionary(agent => agent.ModelLevel, agent => agent.ModelId).ShouldBe(
            new Dictionary<AgentModelLevel, string?>
            {
                [AgentModelLevel.Frontier] = "fable",
                [AgentModelLevel.High] = "opus",
                [AgentModelLevel.Medium] = "sonnet",
                [AgentModelLevel.Low] = "haiku"
            });

        var lateAgent = NewAgent(profileId: null, AgentModelLevel.Medium);
        DbContext.Agents.Add(lateAgent);
        await DbContext.SaveChangesAsync();
        var rejectedSecondPassDefinitionName = $"new-default-{Guid.NewGuid():N}";
        var changedSettings = new AgentRegistrySettings
        {
            DefaultDefinition = rejectedSecondPassDefinitionName,
            Definitions = new Dictionary<string, AgentDefinition>
            {
                [rejectedSecondPassDefinitionName] = new()
                {
                    Kind = "Codex",
                    Exe = "must-not-overwrite-or-import",
                    ArgsTemplate = ["--changed"]
                },
                ["claude-main"] = new()
                {
                    Kind = "ClaudeCode",
                    Exe = "must-not-overwrite-existing",
                    ArgsTemplate = ["--changed"]
                }
            }
        };

        var secondPass = await CreateImporter(changedSettings, protector, clock)
            .ImportAsync(CancellationToken.None);

        secondPass.ProfilesCreated.ShouldBe(0);
        var importedProfileIds = importedProfiles.Select(profile => profile.Id).ToArray();
        var retainedImportedProfiles = await DbContext.AgentTuiProfiles.AsNoTracking()
            .Where(profile => importedProfileIds.Contains(profile.Id))
            .OrderBy(profile => profile.SourceDefinitionName)
            .Select(profile => new { profile.Id, profile.Source, profile.SourceDefinitionName })
            .ToListAsync();
        retainedImportedProfiles
            .Select(profile => (profile.Id, profile.Source, profile.SourceDefinitionName))
            .ShouldBe(importedProfiles.Select(profile =>
                (profile.Id, profile.Source, profile.SourceDefinitionName)));
        var secondPassDefinitionNames = changedSettings.Definitions.Keys.ToArray();
        var profilesMatchingSecondPassDefinitions = await DbContext.AgentTuiProfiles.AsNoTracking()
            .Where(profile => profile.Source == AgentTuiProfileSource.ImportedFile
                && profile.SourceDefinitionName != null
                && secondPassDefinitionNames.Contains(profile.SourceDefinitionName))
            .OrderBy(profile => profile.SourceDefinitionName)
            .Select(profile => new { profile.Id, profile.SourceDefinitionName })
            .ToListAsync();
        profilesMatchingSecondPassDefinitions
            .Select(profile => (profile.Id, profile.SourceDefinitionName))
            .ShouldBe([(defaultProfile.Id, defaultProfile.SourceDefinitionName)]);
        (await DbContext.AgentTuiProfileRevisions.AsNoTracking()
            .SingleAsync(revision => revision.Id == defaultProfile.ActiveRevisionId))
            .Executable.ShouldBe("synthetic-claude-wrapper");
        var backfilled = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == lateAgent.Id);
        backfilled.TuiProfileId.ShouldBe(defaultProfile.Id);
        backfilled.ModelId.ShouldBe("sonnet");
    }

    [Test]
    public async Task Import_rejects_unclassified_environment_without_exposing_its_value()
    {
        const string canary = "synthetic-import-unclassified-canary";
        var settings = new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions =
            {
                ["claude"] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = "synthetic-wrapper",
                    Env = new Dictionary<string, string> { ["NORMAL_SETTING"] = canary }
                }
            }
        };

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CreateImporter(settings, new HashingSecretProtector(), new FakeTimeProvider(FixedNow))
                .ImportAsync(CancellationToken.None));

        var validationText = string.Join(
            "\n",
            exception.Errors.SelectMany(error => error.Value));
        validationText.ShouldContain("NORMAL_SETTING");
        validationText.ShouldNotContain(canary);
        exception.Message.ShouldNotContain(canary);
        (await DbContext.AgentTuiProfiles.CountAsync()).ShouldBe(0);

        var maximumDefinitionName = new string('n', 200);
        var boundedSettings = new AgentRegistrySettings
        {
            DefaultDefinition = maximumDefinitionName,
            Definitions =
            {
                [maximumDefinitionName] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = "synthetic-wrapper"
                }
            }
        };
        var boundedResult = await CreateImporter(
                boundedSettings,
                new HashingSecretProtector(),
                new FakeTimeProvider(FixedNow))
            .ImportAsync(CancellationToken.None);
        boundedResult.ProfilesCreated.ShouldBe(1);
        (await DbContext.AgentTuiProfiles.AsNoTracking().ToListAsync()).ShouldHaveSingleItem()
            .SourceDefinitionName.ShouldBe(maximumDefinitionName);

        var overlongDefinitionName = new string('x', 201);
        var overlongSettings = new AgentRegistrySettings
        {
            DefaultDefinition = overlongDefinitionName,
            Definitions =
            {
                [overlongDefinitionName] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = "synthetic-wrapper"
                }
            }
        };
        var overlongException = await Should.ThrowAsync<ValidationException>(() =>
            CreateImporter(
                    overlongSettings,
                    new HashingSecretProtector(),
                    new FakeTimeProvider(FixedNow))
                .ImportAsync(CancellationToken.None));
        var overlongValidationText = string.Join(
            "\n",
            overlongException.Errors.SelectMany(error => error.Value));
        overlongValidationText.ShouldContain("200");
        overlongValidationText.ShouldNotContain(overlongDefinitionName);
        overlongException.Message.ShouldNotContain(overlongDefinitionName);
        (await DbContext.AgentTuiProfiles.CountAsync()).ShouldBe(1);
    }

    private AgentTuiProfileService CreateService(
        IAgentTuiSecretProtector? protector = null,
        TimeProvider? timeProvider = null,
        ICurrentUser? currentUser = null)
    {
        currentUser ??= new TestCurrentUser(
            new Guid("a0000000-0000-0000-0000-000000000001"),
            "admin",
            "203.0.113.42");
        return CreateService(
            DbContext,
            protector ?? new HashingSecretProtector(),
            timeProvider ?? new FakeTimeProvider(FixedNow),
            currentUser);
    }

    private static AgentTuiProfileService CreateService(
        AppDbContext db,
        IAgentTuiSecretProtector protector,
        TimeProvider? timeProvider = null,
        ICurrentUser? currentUser = null)
    {
        currentUser ??= new TestCurrentUser(
            new Guid("a0000000-0000-0000-0000-000000000001"),
            "admin",
            "203.0.113.42");
        var auditService = new AuditService(
            db,
            Options.Create(new AuditSettings { EnableFullContent = false, EnableIpLogging = true }));
        return new AgentTuiProfileService(
            db,
            protector,
            auditService,
            new AgentTuiRunnerCatalog(),
            timeProvider ?? new FakeTimeProvider(FixedNow),
            currentUser);
    }

    private static AgentTuiProfileService CreateService(
        AppDbContext db,
        IAgentTuiSecretProtector protector,
        IEqualityComparer<string> environmentNameComparer)
    {
        var currentUser = new TestCurrentUser(
            new Guid("a0000000-0000-0000-0000-000000000001"),
            "admin",
            "203.0.113.42");
        var auditService = new AuditService(
            db,
            Options.Create(new AuditSettings { EnableFullContent = false, EnableIpLogging = true }));
        return new AgentTuiProfileService(
            db,
            protector,
            auditService,
            new AgentTuiRunnerCatalog(),
            new FakeTimeProvider(FixedNow),
            currentUser,
            environmentNameComparer);
    }

    private AppDbContext CreateIndependentContext(params IInterceptor[] interceptors)
    {
        var isolatedSchema = _isolatedSchema
            ?? throw new InvalidOperationException("The test schema must be created before a context is opened.");
        var builder = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(isolatedSchema.ConnectionString));
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private static async Task DeleteIndependentProfileAsync(AppDbContext db, Guid profileId)
    {
        db.ChangeTracker.Clear();
        var profile = await db.AgentTuiProfiles.SingleOrDefaultAsync(candidate => candidate.Id == profileId);
        if (profile is null)
            return;

        profile.ActiveRevisionId = null;
        await db.SaveChangesAsync();
        var auditRecords = await db.AuditRecords
            .Where(record => record.Summary.Contains(profileId.ToString()))
            .ToListAsync();
        db.AuditRecords.RemoveRange(auditRecords);
        db.AgentTuiProfiles.Remove(profile);
        await db.SaveChangesAsync();
    }

    private AgentTuiProfileImporter CreateImporter(
        AgentRegistrySettings settings,
        IAgentTuiSecretProtector protector,
        TimeProvider timeProvider) =>
        new(
            DbContext,
            Options.Create(settings),
            protector,
            new AgentTuiRunnerCatalog(),
            timeProvider);

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

    private static Agent NewAgent(Guid? profileId, AgentModelLevel level) => new()
    {
        Id = Guid.NewGuid(),
        Name = UniqueName($"Agent {level}"),
        Slug = $"agent-{Guid.NewGuid():N}",
        WorkingDirectory = Path.GetTempPath(),
        Details = string.Empty,
        TuiProfileId = profileId,
        ModelLevel = level,
        CreatedAt = FixedNow.UtcDateTime,
        UpdatedAt = FixedNow.UtcDateTime
    };

    private static AgentTuiCapabilityDto Capability(AgentTuiRunnerTypeDto runner, string name) =>
        runner.Capabilities.Single(capability => capability.Name == name);

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private sealed class HashingSecretProtector : IAgentTuiSecretProtector
    {
        public string? LastCiphertext { get; private set; }

        public string Protect(Guid profileId, string environmentName, string plaintext)
        {
            LastCiphertext = $"test-v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))}";
            return LastCiphertext;
        }

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) =>
            throw new InvalidOperationException("Profile service reads must not unprotect secrets.");
    }

    private sealed class FailOnAccessSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) =>
            throw new InvalidOperationException("Cached reads must not invoke secret protection.");

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) =>
            throw new InvalidOperationException("Cached reads must not invoke secret unprotection.");
    }

    private sealed class RecordingSecretProtector(List<string> events) : IAgentTuiSecretProtector
    {
        public string? LastProtectedEnvironmentName { get; private set; }
        public string? LastUnprotectedEnvironmentName { get; private set; }

        public string Protect(Guid profileId, string environmentName, string plaintext)
        {
            events.Add($"protect:{environmentName}");
            LastProtectedEnvironmentName = environmentName;
            return $"test-v1:{environmentName}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext))}";
        }

        public string Unprotect(Guid profileId, string environmentName, string protectedValue)
        {
            LastUnprotectedEnvironmentName = environmentName;
            var prefix = $"test-v1:{environmentName}:";
            if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("The persisted secret name does not match its protection purpose.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[prefix.Length..]));
        }
    }

    private sealed class RecordingTransactionInterceptor(List<string> events) : DbTransactionInterceptor
    {
        public Func<CancellationToken, Task>? OnStartingAsync { get; set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            events.Add("transaction-starting");
            var callback = OnStartingAsync;
            OnStartingAsync = null;
            if (callback is not null)
                await callback(cancellationToken);
            return result;
        }
    }

    private sealed record TestCurrentUser(Guid UserId, string UserName, string IpAddress) : ICurrentUser;
}
