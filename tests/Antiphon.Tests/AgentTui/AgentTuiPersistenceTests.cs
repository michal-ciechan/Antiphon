using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Antiphon.Tests;

[Category("Integration")]
public class AgentTuiPersistenceTests
{
    [Test]
    public void Enums_keep_their_persisted_numeric_contracts()
    {
        AgentKind.Raw.ShouldBe((AgentKind)0);
        AgentKind.ClaudeCode.ShouldBe((AgentKind)1);
        AgentKind.Codex.ShouldBe((AgentKind)2);
        AgentKind.OpenCode.ShouldBe((AgentKind)3);
        AgentKind.Grok.ShouldBe((AgentKind)4);

        AgentTuiAuthenticationMode.WrapperManaged.ShouldBe((AgentTuiAuthenticationMode)0);
        AgentTuiAuthenticationMode.ManagedEnvironment.ShouldBe((AgentTuiAuthenticationMode)1);
        AgentTuiProfileSource.ImportedFile.ShouldBe((AgentTuiProfileSource)0);
        AgentTuiProfileSource.Operator.ShouldBe((AgentTuiProfileSource)1);
        AgentTuiModelSource.Curated.ShouldBe((AgentTuiModelSource)0);
        AgentTuiModelSource.Discovered.ShouldBe((AgentTuiModelSource)1);
        AgentTuiModelSource.Operator.ShouldBe((AgentTuiModelSource)2);
        AgentTuiModelAvailability.Unverified.ShouldBe((AgentTuiModelAvailability)0);
        AgentTuiModelAvailability.Verified.ShouldBe((AgentTuiModelAvailability)1);
        AgentTuiModelAvailability.Stale.ShouldBe((AgentTuiModelAvailability)2);
        AgentTuiModelAvailability.Unavailable.ShouldBe((AgentTuiModelAvailability)3);
        AgentTuiCapabilityState.Supported.ShouldBe((AgentTuiCapabilityState)0);
        AgentTuiCapabilityState.Unsupported.ShouldBe((AgentTuiCapabilityState)1);
        AgentTuiCapabilityState.Degraded.ShouldBe((AgentTuiCapabilityState)2);
        AgentTuiCapabilityState.Unknown.ShouldBe((AgentTuiCapabilityState)3);
        AgentTuiValidationStatus.NeverRun.ShouldBe((AgentTuiValidationStatus)0);
        AgentTuiValidationStatus.Running.ShouldBe((AgentTuiValidationStatus)1);
        AgentTuiValidationStatus.Succeeded.ShouldBe((AgentTuiValidationStatus)2);
        AgentTuiValidationStatus.Partial.ShouldBe((AgentTuiValidationStatus)3);
        AgentTuiValidationStatus.Failed.ShouldBe((AgentTuiValidationStatus)4);
        AgentTuiValidationStatus.TimedOut.ShouldBe((AgentTuiValidationStatus)5);
    }

    [Test]
    public void Model_has_owned_profile_revisions_and_effective_session_contracts()
    {
        using var db = NewModelContext();

        var profile = RequiredEntity(db, typeof(AgentTuiProfile));
        var revision = RequiredEntity(db, typeof(AgentTuiProfileRevision));
        var secret = RequiredEntity(db, typeof(AgentTuiSecret));
        var model = RequiredEntity(db, typeof(AgentTuiModel));
        var validation = RequiredEntity(db, typeof(AgentTuiValidationRun));
        var agent = RequiredEntity(db, typeof(Agent));
        var session = RequiredEntity(db, typeof(AgentSession));

        AssertIndex(profile, [nameof(AgentTuiProfile.DisplayName)],
            "IX_AgentTuiProfiles_DisplayName", unique: true);
        AssertIndex(profile, [nameof(AgentTuiProfile.Id), nameof(AgentTuiProfile.ActiveRevisionId)],
            "IX_AgentTuiProfiles_Id_ActiveRevisionId", unique: false);
        AssertIndex(revision,
            [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.RevisionNumber)],
            "IX_AgentTuiProfileRevisions_ProfileId_RevisionNumber", unique: true);
        AssertIndex(secret, [nameof(AgentTuiSecret.ProfileId), nameof(AgentTuiSecret.Name)],
            "IX_AgentTuiSecrets_ProfileId_Name", unique: true);
        AssertIndex(model, [nameof(AgentTuiModel.ProfileId), nameof(AgentTuiModel.Identifier)],
            "IX_AgentTuiModels_ProfileId_Identifier", unique: true);
        AssertIndex(validation, [nameof(AgentTuiValidationRun.ProfileId), nameof(AgentTuiValidationRun.CreatedAt)],
            "IX_AgentTuiValidationRuns_ProfileId_CreatedAt", unique: false);
        AssertIndex(validation,
            [nameof(AgentTuiValidationRun.ProfileId), nameof(AgentTuiValidationRun.ProfileRevisionId)],
            "IX_AgentTuiValidationRuns_ProfileId_ProfileRevisionId", unique: false);
        AssertIndex(agent, [nameof(Agent.TuiProfileId)], "IX_Agents_TuiProfileId", unique: false);
        AssertIndex(session, [nameof(AgentSession.TuiProfileRevisionId)],
            "IX_AgentSessions_TuiProfileRevisionId", unique: false);

        var ownershipKey = revision.GetKeys().Single(key => PropertyNames(key.Properties).SequenceEqual(
            [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.Id)]));
        ownershipKey.GetName().ShouldBe("AK_AgentTuiProfileRevisions_ProfileId_Id");

        AssertForeignKey(
            profile,
            [nameof(AgentTuiProfile.Id), nameof(AgentTuiProfile.ActiveRevisionId)],
            [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.Id)],
            DeleteBehavior.NoAction,
            DeleteBehavior.Restrict);
        AssertForeignKey(
            validation,
            [nameof(AgentTuiValidationRun.ProfileId), nameof(AgentTuiValidationRun.ProfileRevisionId)],
            [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.Id)],
            DeleteBehavior.NoAction,
            DeleteBehavior.Restrict);
        AssertForeignKey(revision, [nameof(AgentTuiProfileRevision.ProfileId)],
            [nameof(AgentTuiProfile.Id)], DeleteBehavior.Cascade);
        AssertForeignKey(secret, [nameof(AgentTuiSecret.ProfileId)],
            [nameof(AgentTuiProfile.Id)], DeleteBehavior.Cascade);
        AssertForeignKey(model, [nameof(AgentTuiModel.ProfileId)],
            [nameof(AgentTuiProfile.Id)], DeleteBehavior.Cascade);
        AssertForeignKey(validation, [nameof(AgentTuiValidationRun.ProfileId)],
            [nameof(AgentTuiProfile.Id)], DeleteBehavior.Cascade);
        AssertForeignKey(agent, [nameof(Agent.TuiProfileId)],
            [nameof(AgentTuiProfile.Id)], DeleteBehavior.Restrict);
        AssertForeignKey(session, [nameof(AgentSession.TuiProfileRevisionId)],
            [nameof(AgentTuiProfileRevision.Id)], DeleteBehavior.Restrict);

        AssertProperty(profile, nameof(AgentTuiProfile.Id), nullable: false);
        profile.FindProperty(nameof(AgentTuiProfile.Id))!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        AssertProperty(profile, nameof(AgentTuiProfile.DisplayName), nullable: false, maxLength: 200);
        AssertProperty(profile, nameof(AgentTuiProfile.Kind), nullable: false);
        AssertProperty(profile, nameof(AgentTuiProfile.IsEnabled), nullable: false);
        AssertProperty(profile, nameof(AgentTuiProfile.IsDefault), nullable: false);
        AssertProperty(profile, nameof(AgentTuiProfile.Source), nullable: false);
        AssertProperty(profile, nameof(AgentTuiProfile.SourceDefinitionName), nullable: true, maxLength: 200);
        AssertProperty(profile, nameof(AgentTuiProfile.ActiveRevisionId), nullable: true);
        AssertProperty(profile, nameof(AgentTuiProfile.CreatedAt), nullable: false);
        AssertProperty(profile, nameof(AgentTuiProfile.UpdatedAt), nullable: false);

        AssertProperty(revision, nameof(AgentTuiProfileRevision.Id), nullable: false);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.ProfileId), nullable: false);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.RevisionNumber), nullable: false);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.Executable), nullable: false, maxLength: 2000);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.ArgumentsJson), nullable: false, columnType: "jsonb");
        AssertProperty(revision, nameof(AgentTuiProfileRevision.DiscoveryArgumentsJson), nullable: false,
            columnType: "jsonb");
        AssertProperty(revision, nameof(AgentTuiProfileRevision.VersionArgumentsJson), nullable: false,
            columnType: "jsonb");
        AssertProperty(revision, nameof(AgentTuiProfileRevision.WorkingDirectory), nullable: true, maxLength: 1000);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.AuthenticationMode), nullable: false);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.NonSecretEnvironmentJson), nullable: false,
            columnType: "jsonb");
        AssertProperty(revision, nameof(AgentTuiProfileRevision.SecretEnvironmentNamesJson), nullable: false,
            columnType: "jsonb");
        AssertProperty(revision, nameof(AgentTuiProfileRevision.ModelArgumentName), nullable: true, maxLength: 100);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.Guidance), nullable: false, maxLength: 4000);
        AssertProperty(revision, nameof(AgentTuiProfileRevision.CreatedAt), nullable: false);

        AssertProperty(secret, nameof(AgentTuiSecret.Id), nullable: false);
        AssertProperty(secret, nameof(AgentTuiSecret.ProfileId), nullable: false);
        AssertProperty(secret, nameof(AgentTuiSecret.Name), nullable: false, maxLength: 200);
        AssertProperty(secret, nameof(AgentTuiSecret.Ciphertext), nullable: false);
        AssertProperty(secret, nameof(AgentTuiSecret.ProtectionVersion), nullable: false, maxLength: 100);
        AssertProperty(secret, nameof(AgentTuiSecret.CreatedAt), nullable: false);
        AssertProperty(secret, nameof(AgentTuiSecret.UpdatedAt), nullable: false);

        AssertProperty(model, nameof(AgentTuiModel.Id), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.ProfileId), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.Identifier), nullable: false, maxLength: 500);
        AssertProperty(model, nameof(AgentTuiModel.DisplayName), nullable: false, maxLength: 200);
        AssertProperty(model, nameof(AgentTuiModel.Family), nullable: true, maxLength: 200);
        AssertProperty(model, nameof(AgentTuiModel.Source), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.Availability), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.DiscoveredAt), nullable: true);
        AssertProperty(model, nameof(AgentTuiModel.RunnerVersion), nullable: true, maxLength: 200);
        AssertProperty(model, nameof(AgentTuiModel.IsSuggestedDefault), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.CreatedAt), nullable: false);
        AssertProperty(model, nameof(AgentTuiModel.UpdatedAt), nullable: false);

        AssertProperty(validation, nameof(AgentTuiValidationRun.Id), nullable: false);
        AssertProperty(validation, nameof(AgentTuiValidationRun.ProfileId), nullable: false);
        AssertProperty(validation, nameof(AgentTuiValidationRun.ProfileRevisionId), nullable: false);
        AssertProperty(validation, nameof(AgentTuiValidationRun.Operation), nullable: false, maxLength: 50);
        AssertProperty(validation, nameof(AgentTuiValidationRun.Status), nullable: false);
        AssertProperty(validation, nameof(AgentTuiValidationRun.ResultsJson), nullable: false, columnType: "jsonb");
        AssertProperty(validation, nameof(AgentTuiValidationRun.CapabilitiesJson), nullable: false,
            columnType: "jsonb");
        AssertProperty(validation, nameof(AgentTuiValidationRun.RunnerVersion), nullable: true, maxLength: 200);
        AssertProperty(validation, nameof(AgentTuiValidationRun.Summary), nullable: true, maxLength: 4000);
        AssertProperty(validation, nameof(AgentTuiValidationRun.CreatedAt), nullable: false);
        AssertProperty(validation, nameof(AgentTuiValidationRun.StartedAt), nullable: true);
        AssertProperty(validation, nameof(AgentTuiValidationRun.CompletedAt), nullable: true);

        AssertProperty(agent, nameof(Agent.TuiProfileId), nullable: true);
        AssertProperty(agent, nameof(Agent.ModelId), nullable: true, maxLength: 500);
        AssertProperty(session, nameof(AgentSession.TuiProfileRevisionId), nullable: true);
        AssertProperty(session, nameof(AgentSession.EffectiveModelId), nullable: true, maxLength: 500);

        var designRevision = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(AgentTuiProfileRevision));
        designRevision.ShouldNotBeNull();
        designRevision.GetCheckConstraints()
            .Single(check => check.Name == "CK_AgentTuiProfileRevisions_RevisionNumber_Positive")
            .Sql.ShouldContain("RevisionNumber");
        designRevision.GetCheckConstraints()
            .Single(check => check.Name == "CK_AgentTuiProfileRevisions_AuthenticationMode_Valid")
            .Sql.ShouldContain("AuthenticationMode");
        var designProfile = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(AgentTuiProfile));
        designProfile.ShouldNotBeNull();
        designProfile.GetCheckConstraints()
            .Single(check => check.Name == "CK_AgentTuiProfiles_Kind_Valid")
            .Sql.ShouldContain("Kind");
    }

    [Test]
    public async Task PostgreSql_rejects_cross_profile_active_revision()
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await SeedTwoProfilesAsync(db);

        graph.ProfileA.ActiveRevisionId = graph.RevisionB.Id;

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task PostgreSql_rejects_cross_profile_validation_revision()
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await SeedTwoProfilesAsync(db);
        var now = DateTime.UtcNow;

        db.AgentTuiValidationRuns.Add(new AgentTuiValidationRun
        {
            Id = Guid.NewGuid(),
            ProfileId = graph.ProfileA.Id,
            ProfileRevisionId = graph.RevisionB.Id,
            Operation = "validate",
            Status = AgentTuiValidationStatus.Failed,
            ResultsJson = "{}",
            CapabilitiesJson = "{}",
            CreatedAt = now,
            CompletedAt = now
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task PostgreSql_rejects_deleting_an_active_revision()
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await SeedTwoProfilesAsync(db);

        var exception = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "AgentTuiProfileRevisions" WHERE "Id" = {graph.RevisionA.Id}"""));
        exception.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task PostgreSql_rejects_non_positive_revision_numbers(int revisionNumber)
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var profile = NewProfile("Revision constraint", now);
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = NewRevision(profile.Id, now);
        revision.RevisionNumber = revisionNumber;
        db.AgentTuiProfileRevisions.Add(revision);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        postgresException.ShouldNotBeNull();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgresException.ConstraintName.ShouldBe("CK_AgentTuiProfileRevisions_RevisionNumber_Positive");
    }

    [Test]
    public async Task PostgreSql_rejects_undefined_profile_kind()
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var profile = NewProfile("Kind constraint", DateTime.UtcNow);
        profile.Kind = (AgentKind)999;
        db.AgentTuiProfiles.Add(profile);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        postgresException.ShouldNotBeNull();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgresException.ConstraintName.ShouldBe("CK_AgentTuiProfiles_Kind_Valid");
    }

    [Test]
    public async Task PostgreSql_rejects_undefined_profile_authentication_mode()
    {
        await using var db = NewDatabaseContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var profile = NewProfile("Authentication constraint", now);
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        var revision = NewRevision(profile.Id, now);
        revision.AuthenticationMode = (AgentTuiAuthenticationMode)999;
        db.AgentTuiProfileRevisions.Add(revision);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresException = exception.InnerException as PostgresException;
        postgresException.ShouldNotBeNull();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgresException.ConstraintName.ShouldBe("CK_AgentTuiProfileRevisions_AuthenticationMode_Valid");
    }

    [Test]
    public async Task Migration_applies_five_tables_four_columns_and_no_seed_operations()
    {
        await using var db = NewDatabaseContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.ShouldContain(migration => migration.EndsWith("_AddAgentTuiProfiles", StringComparison.Ordinal));
        applied.ShouldContain(migration =>
            migration.EndsWith("_EnforceAgentTuiProfileEnums", StringComparison.Ordinal));

        var tables = await db.Database.SqlQueryRaw<string>(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name LIKE 'AgentTui%'
                ORDER BY table_name
                """)
            .ToListAsync();
        tables.ShouldBe(
            [
                "AgentTuiModels",
                "AgentTuiProfileRevisions",
                "AgentTuiProfiles",
                "AgentTuiSecrets",
                "AgentTuiValidationRuns"
            ]);

        var columns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND ((table_name = 'Agents' AND column_name IN ('TuiProfileId', 'ModelId'))
                    OR (table_name = 'AgentSessions' AND column_name IN ('TuiProfileRevisionId', 'EffectiveModelId')))
                ORDER BY column_name
                """)
            .ToListAsync();
        columns.ShouldBe(["EffectiveModelId", "ModelId", "TuiProfileId", "TuiProfileRevisionId"]);

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        var migrationType = migrationsAssembly.Migrations
            .Single(pair => pair.Key.EndsWith("_AddAgentTuiProfiles", StringComparison.Ordinal))
            .Value;
        var migration = migrationsAssembly.CreateMigration(migrationType, db.Database.ProviderName!);

        migration.UpOperations.OfType<CreateTableOperation>().Count().ShouldBe(5);
        migration.UpOperations.OfType<AddColumnOperation>().Count().ShouldBe(4);
        migration.UpOperations.ShouldNotContain(operation => operation is InsertDataOperation);
        migration.DownOperations.OfType<DropTableOperation>().Count().ShouldBe(5);
        migration.DownOperations.OfType<DropColumnOperation>().Count().ShouldBe(4);
        migration.DownOperations.ShouldNotContain(operation => operation is DeleteDataOperation);

        var enumMigrationType = migrationsAssembly.Migrations
            .Single(pair => pair.Key.EndsWith("_EnforceAgentTuiProfileEnums", StringComparison.Ordinal))
            .Value;
        var enumMigration = migrationsAssembly.CreateMigration(enumMigrationType, db.Database.ProviderName!);
        enumMigration.UpOperations.OfType<AddCheckConstraintOperation>().Count().ShouldBe(2);
        enumMigration.UpOperations.OfType<AlterColumnOperation>().Count().ShouldBe(1);
        enumMigration.DownOperations.OfType<DropCheckConstraintOperation>().Count().ShouldBe(2);
    }

    [Test]
    [NotInParallel]
    public async Task Migration_downgrades_the_five_tables_and_four_columns_cleanly()
    {
        await using var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("antiphon_downgrade")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await database.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var agentTuiMigrationIndex = applied.FindIndex(migration =>
            migration.EndsWith("_AddAgentTuiProfiles", StringComparison.Ordinal));
        agentTuiMigrationIndex.ShouldBeGreaterThan(0);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[agentTuiMigrationIndex - 1]);

        var tables = await db.Database.SqlQueryRaw<string>(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name LIKE 'AgentTui%'
                """)
            .ToListAsync();
        tables.ShouldBeEmpty();

        var columns = await db.Database.SqlQueryRaw<string>(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND ((table_name = 'Agents' AND column_name IN ('TuiProfileId', 'ModelId'))
                    OR (table_name = 'AgentSessions' AND column_name IN ('TuiProfileRevisionId', 'EffectiveModelId')))
                """)
            .ToListAsync();
        columns.ShouldBeEmpty();

        var remainingMigrations = await db.Database.GetAppliedMigrationsAsync();
        remainingMigrations.ShouldNotContain(migration =>
            migration.EndsWith("_AddAgentTuiProfiles", StringComparison.Ordinal));
    }

    private static AppDbContext NewModelContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=agent_tui_model;Username=unused;Password=unused")
            .Options;

        return new AppDbContext(options);
    }

    private static AppDbContext NewDatabaseContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task<ProfileGraph> SeedTwoProfilesAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var profileA = NewProfile("A", now);
        var profileB = NewProfile("B", now);
        db.AgentTuiProfiles.AddRange(profileA, profileB);
        await db.SaveChangesAsync();

        var revisionA = NewRevision(profileA.Id, now);
        var revisionB = NewRevision(profileB.Id, now);
        db.AgentTuiProfileRevisions.AddRange(revisionA, revisionB);
        await db.SaveChangesAsync();

        profileA.ActiveRevisionId = revisionA.Id;
        profileB.ActiveRevisionId = revisionB.Id;
        await db.SaveChangesAsync();

        return new ProfileGraph(profileA, profileB, revisionA, revisionB);
    }

    private static AgentTuiProfile NewProfile(string suffix, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = $"Persistence {suffix} {Guid.NewGuid():N}",
        Kind = AgentKind.OpenCode,
        IsEnabled = true,
        Source = AgentTuiProfileSource.Operator,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static AgentTuiProfileRevision NewRevision(Guid profileId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        RevisionNumber = 1,
        Executable = "ocg.ps1",
        ArgumentsJson = "[]",
        DiscoveryArgumentsJson = "[]",
        VersionArgumentsJson = "[]",
        AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
        NonSecretEnvironmentJson = "{}",
        SecretEnvironmentNamesJson = "[]",
        Guidance = string.Empty,
        CreatedAt = now
    };

    private static IEntityType RequiredEntity(AppDbContext db, Type type)
    {
        var entity = db.Model.FindEntityType(type);
        entity.ShouldNotBeNull();
        return entity;
    }

    private static void AssertIndex(
        IEntityType entity,
        string[] propertyNames,
        string databaseName,
        bool unique)
    {
        var index = entity.GetIndexes().Single(candidate =>
            PropertyNames(candidate.Properties).SequenceEqual(propertyNames));
        index.IsUnique.ShouldBe(unique);
        index.GetDatabaseName().ShouldBe(databaseName);
    }

    private static void AssertForeignKey(
        IEntityType entity,
        string[] propertyNames,
        string[] principalPropertyNames,
        params DeleteBehavior[] expectedDeleteBehaviors)
    {
        var foreignKey = entity.GetForeignKeys().Single(candidate =>
            PropertyNames(candidate.Properties).SequenceEqual(propertyNames));
        PropertyNames(foreignKey.PrincipalKey.Properties).ShouldBe(principalPropertyNames);
        expectedDeleteBehaviors.ShouldContain(foreignKey.DeleteBehavior);
    }

    private static void AssertProperty(
        IEntityType entity,
        string propertyName,
        bool nullable,
        int? maxLength = null,
        string? columnType = null)
    {
        var property = entity.FindProperty(propertyName);
        property.ShouldNotBeNull();
        property.IsNullable.ShouldBe(nullable);
        property.GetMaxLength().ShouldBe(maxLength);
        if (columnType is not null)
        {
            property.GetColumnType().ShouldBe(columnType);
        }
    }

    private static IEnumerable<string> PropertyNames(IEnumerable<IReadOnlyProperty> properties) =>
        properties.Select(property => property.Name);

    private sealed record ProfileGraph(
        AgentTuiProfile ProfileA,
        AgentTuiProfile ProfileB,
        AgentTuiProfileRevision RevisionA,
        AgentTuiProfileRevision RevisionB);
}
