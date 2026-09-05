using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[NotInParallel]
[Category("Integration")]
public sealed class AgentTuiSecretMigratorTests
{
    [Test]
    public async Task conversion_preserves_the_resolved_environment_and_creates_an_immutable_revision()
    {
        const string canary = "sk-canary-migrator-0114";
        var name = $"MIGRATOR_KEY_{Guid.NewGuid():N}";
        await using var db = NewDb();
        var profile = await AddManagedProfileAsync(db, name, canary);
        // ManagedEnvironment first carries the persisted ordinary values, then overlays secrets.
        var oldEnvironment = new Dictionary<string, string> { ["ORDINARY"] = "value", [name] = canary };

        var migrator = NewMigrator(db);
        var result = await migrator.MigrateAsync(CancellationToken.None);

        result.ProfilesConverted.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = NewDb();
        var migrated = await verify.AgentTuiProfiles.Include(p => p.ActiveRevision)
            .SingleAsync(p => p.Id == profile.Id);
        migrated.ActiveRevision!.AuthenticationMode.ShouldBe(AgentTuiAuthenticationMode.WrapperManaged);
        migrated.ActiveRevision.SecretEnvironmentNamesJson.ShouldBe("[]");
        migrated.ActiveRevision.RevisionNumber.ShouldBe(2);
        (await verify.AgentTuiSecrets.AnyAsync(secret => secret.ProfileId == profile.Id)).ShouldBeFalse();
        (await verify.AgentTuiProfileRevisions.CountAsync(revision => revision.ProfileId == profile.Id)).ShouldBe(2);

        var storedEnvironment = JsonSerializer.Deserialize<Dictionary<string, string>>(
            migrated.ActiveRevision.NonSecretEnvironmentJson)!;
        storedEnvironment[name].ShouldBe($"{{{{key:{name}}}}}");
        var resolved = await new ApiKeyEnvResolver(
            verify, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<ApiKeyEnvResolver>.Instance)
            .ResolveAsync(storedEnvironment, null, "migration parity", CancellationToken.None);

        // This is the migration gate: process environments are byte-identical before and after.
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resolved))
            .ShouldBe(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(oldEnvironment)));
        (await verify.ApiKeys.SingleAsync(key => key.Name == name)).Ciphertext.ShouldNotContain(canary);
    }

    [Test]
    public async Task a_global_name_collision_refuses_the_entire_profile_without_writes()
    {
        const string canary = "sk-canary-refusal-0114";
        var name = $"MIGRATOR_COLLISION_{Guid.NewGuid():N}";
        await using var db = NewDb();
        var profile = await AddManagedProfileAsync(db, name, canary);
        var existingId = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = existingId, Name = name, Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(existingId, "other"),
            ProtectionVersion = "v1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await NewMigrator(db).MigrateAsync(CancellationToken.None);

        result.ProfilesRefused.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = NewDb();
        var active = await verify.AgentTuiProfiles.Include(p => p.ActiveRevision).SingleAsync(p => p.Id == profile.Id);
        active.ActiveRevision!.AuthenticationMode.ShouldBe(AgentTuiAuthenticationMode.ManagedEnvironment);
        (await verify.AgentTuiSecrets.CountAsync(secret => secret.ProfileId == profile.Id)).ShouldBe(1);
        (await verify.ApiKeys.CountAsync(key => key.Name == name)).ShouldBe(1);
    }

    private static AgentTuiSecretMigrator NewMigrator(AppDbContext db) => new(
        db, new FakeSecretProtector(), new ApiKeyStoreTests.FakeApiKeyProtector(),
        NullLogger<AgentTuiSecretMigrator>.Instance);

    private static async Task<AgentTuiProfile> AddManagedProfileAsync(AppDbContext db, string name, string value)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(), DisplayName = $"Migrator {Guid.NewGuid():N}", Kind = AgentKind.ClaudeCode,
            IsEnabled = true, Source = AgentTuiProfileSource.Operator, CreatedAt = now, UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1, Executable = "runner",
            AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment,
            NonSecretEnvironmentJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["ORDINARY"] = "value" }),
            SecretEnvironmentNamesJson = JsonSerializer.Serialize(new[] { name }), CreatedAt = now,
        };
        profile.ActiveRevisionId = revision.Id;
        db.AgentTuiProfileRevisions.Add(revision);
        db.AgentTuiSecrets.Add(new AgentTuiSecret
        {
            Id = Guid.NewGuid(), ProfileId = profile.Id, Name = name,
            Ciphertext = new FakeSecretProtector().Protect(profile.Id, name, value), ProtectionVersion = "v1",
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return profile;
    }

    private static AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class FakeSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string Unprotect(Guid profileId, string environmentName, string protectedValue) => Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
    }
}
