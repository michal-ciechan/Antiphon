using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0212 — create/PATCH refuse <c>RemoteControlEnabled = true</c> on a kind whose catalog
/// row is not Supported. Positive Claude create is
/// <see cref="AgentCreateSupervisionTests.CreateAsync_persists_create_time_always_on_and_remote_control"/>.
/// </summary>
public class AgentRemoteControlGateTests
{
    [Test]
    [Category("Integration")]
    public async Task create_grok_with_remote_control_true_is_refused_and_persists_no_row()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");
        var name = Unique("Grok RC");

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.CreateAsync(
                new CreateAgentRequest(
                    name,
                    $"D:/src/{Guid.NewGuid():N}",
                    TuiProfileId: grok.Id,
                    RemoteControlEnabled: true),
                CancellationToken.None));
        ex.Code.ShouldBe("remote_control_refused");
        ex.Message.ShouldContain("Grok");
        ex.Message.ShouldContain("remoteControlEnabled: false");

        await using var verify = CreateContext();
        (await verify.Agents.CountAsync(a => a.Name == name)).ShouldBe(0);
    }

    [Test]
    [Category("Integration")]
    public async Task create_grok_with_flag_omitted_stores_false()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");

        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Grok No RC"),
                $"D:/src/{Guid.NewGuid():N}",
                TuiProfileId: grok.Id),
            CancellationToken.None);

        created.Kind.ShouldBe(AgentKind.Grok);
        created.RemoteControlEnabled.ShouldBeFalse();

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == created.Id))
            .RemoteControlEnabled.ShouldBeFalse();
    }

    [Test]
    [Category("Integration")]
    public async Task patch_claude_rc_on_to_grok_profile_omitting_flag_is_refused_and_row_unchanged()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var claude = await SeedProfileAsync(db, AgentKind.ClaudeCode, "Claude");
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Claude Then Grok"),
                $"D:/src/{Guid.NewGuid():N}",
                TuiProfileId: claude.Id,
                RemoteControlEnabled: true),
            CancellationToken.None);
        created.Kind.ShouldBe(AgentKind.ClaudeCode);
        created.RemoteControlEnabled.ShouldBeTrue();

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateAgentRequest(
                    created.Name,
                    created.WorkingDirectory,
                    created.Details,
                    created.DefaultWorkflowTemplateId,
                    created.AssignmentPolicy,
                    BoardId: created.BoardId,
                    TuiProfileId: grok.Id),
                CancellationToken.None));
        ex.Code.ShouldBe("remote_control_refused");

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.TuiProfileId.ShouldBe(claude.Id);
        stored.RemoteControlEnabled.ShouldBeTrue();
        stored.Kind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_claude_rc_on_to_grok_profile_with_flag_false_succeeds()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var claude = await SeedProfileAsync(db, AgentKind.ClaudeCode, "Claude");
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Claude To Grok"),
                $"D:/src/{Guid.NewGuid():N}",
                TuiProfileId: claude.Id,
                RemoteControlEnabled: true),
            CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                BoardId: created.BoardId,
                TuiProfileId: grok.Id,
                RemoteControlEnabled: false),
            CancellationToken.None);

        updated.Kind.ShouldBe(AgentKind.Grok);
        updated.RemoteControlEnabled.ShouldBeFalse();
        updated.TuiProfileId.ShouldBe(grok.Id);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_grok_agent_with_remote_control_true_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Grok Patch RC"),
                $"D:/src/{Guid.NewGuid():N}",
                TuiProfileId: grok.Id),
            CancellationToken.None);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.UpdateAsync(
                created.Id,
                new UpdateAgentRequest(
                    created.Name,
                    created.WorkingDirectory,
                    created.Details,
                    created.DefaultWorkflowTemplateId,
                    created.AssignmentPolicy,
                    BoardId: created.BoardId,
                    RemoteControlEnabled: true),
                CancellationToken.None));
        ex.Code.ShouldBe("remote_control_refused");

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == created.Id))
            .RemoteControlEnabled.ShouldBeFalse();
    }

    private static async Task<AgentTuiProfile> SeedProfileAsync(
        AppDbContext db, AgentKind kind, string namePrefix)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"{namePrefix} {Guid.NewGuid():N}",
            Kind = kind,
            IsEnabled = true,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = "synthetic-wrapper",
            ArgumentsJson = "[]",
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            Guidance = string.Empty,
            CreatedAt = now,
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;
        await db.SaveChangesAsync();
        return profile;
    }

    private static AgentService CreateService(AppDbContext db) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";
}
