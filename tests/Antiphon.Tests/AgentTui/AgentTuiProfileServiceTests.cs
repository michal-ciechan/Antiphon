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
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests;

[Category("Integration")]
[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public class AgentTuiProfileServiceTests : TransactionalTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    public AgentTuiProfileServiceTests(TestDbFixture fixture) : base(fixture)
    {
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
        var service = CreateService(protector, clock);
        var request = NewRequest(UniqueName("Secret atomicity")) with
        {
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
        var created = await service.CreateAsync(request, CancellationToken.None);
        const string oldCanary = "synthetic-old-secret-canary";
        const string rejectedCanary = "synthetic-rejected-secret-canary";
        const string newCanary = "synthetic-new-secret-canary";

        var set = await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(oldCanary, 1, "secret-set-correlation", actorId),
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
            new AgentTuiSecretWriteRequest(rejectedCanary, 1, "secret-rejected-correlation", actorId),
            CancellationToken.None));
        (await DbContext.AgentTuiSecrets.AsNoTracking()
            .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext.ShouldBe(oldCiphertext);

        clock.Advance(TimeSpan.FromMinutes(1));
        var replaced = await service.PutSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretWriteRequest(newCanary, 2, "secret-replace-correlation", actorId),
            CancellationToken.None);
        replaced.Revision.ShouldBe(2);
        var newCiphertext = (await DbContext.AgentTuiSecrets.AsNoTracking()
            .SingleAsync(secret => secret.ProfileId == created.Id)).Ciphertext;
        newCiphertext.ShouldNotBe(oldCiphertext);

        clock.Advance(TimeSpan.FromMinutes(1));
        var cleared = await service.ClearSecretAsync(
            created.Id,
            "SERVICE_TOKEN",
            new AgentTuiSecretClearRequest(2, "secret-clear-correlation", actorId),
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
            .ShouldBe(["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"]);

        var openCode = catalog.Get(AgentKind.OpenCode, ["--auto", "--mini"]);
        openCode.CuratedModels.Select(model => model.Identifier).ShouldBe(["llmgateway/grok-4-5"]);
        Capability(openCode, "modelArgument").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(openCode, "modelDiscovery").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(openCode, "structuredActivity").State.ShouldBe(AgentTuiCapabilityState.Degraded);
        Capability(openCode, "structuredActivity").Reason.ShouldBe(
            "PTY quiet-time fallback; ACP/event integration not active");
        Capability(openCode, "sessionResume").State.ShouldBe(AgentTuiCapabilityState.Unknown);
        Capability(openCode, "remoteControl").State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        Capability(openCode, "systemPromptAppend").State.ShouldBe(AgentTuiCapabilityState.Unsupported);
        Capability(openCode, "permissionBypass").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(catalog.Get(AgentKind.OpenCode, ["--mini"]), "permissionBypass").State
            .ShouldBe(AgentTuiCapabilityState.Unsupported);

        var claude = catalog.Get(AgentKind.ClaudeCode);
        Capability(claude, "structuredActivity").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(claude, "sessionResume").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(claude, "remoteControl").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(claude, "systemPromptAppend").State.ShouldBe(AgentTuiCapabilityState.Supported);
        Capability(catalog.Get(AgentKind.Codex), "structuredActivity").State
            .ShouldNotBe(AgentTuiCapabilityState.Supported);
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
                    }
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
                    }
                }
            }
        };
        var importer = CreateImporter(originalSettings, protector, clock);

        var firstPass = await importer.ImportAsync(CancellationToken.None);

        firstPass.ProfilesCreated.ShouldBe(2);
        firstPass.AgentsAssigned.ShouldBe(4);
        var importedProfiles = await DbContext.AgentTuiProfiles.AsNoTracking()
            .Where(profile => profile.Source == AgentTuiProfileSource.ImportedFile)
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
        var changedSettings = new AgentRegistrySettings
        {
            DefaultDefinition = "new-default",
            Definitions = new Dictionary<string, AgentDefinition>
            {
                ["new-default"] = new()
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
        secondPass.AgentsAssigned.ShouldBe(1);
        (await DbContext.AgentTuiProfiles.CountAsync()).ShouldBe(2);
        (await DbContext.AgentTuiProfileRevisions.AsNoTracking()
            .SingleAsync(revision => revision.Id == defaultProfile.ActiveRevisionId))
            .Executable.ShouldBe("synthetic-claude-wrapper");
        var backfilled = await DbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == lateAgent.Id);
        backfilled.TuiProfileId.ShouldBe(defaultProfile.Id);
        backfilled.ModelId.ShouldBe("sonnet");
    }

    private AgentTuiProfileService CreateService(
        IAgentTuiSecretProtector? protector = null,
        TimeProvider? timeProvider = null)
    {
        var auditService = new AuditService(
            DbContext,
            Options.Create(new AuditSettings { EnableFullContent = false, EnableIpLogging = false }));
        return new AgentTuiProfileService(
            DbContext,
            protector ?? new HashingSecretProtector(),
            auditService,
            new AgentTuiRunnerCatalog(),
            timeProvider ?? new FakeTimeProvider(FixedNow));
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
}
