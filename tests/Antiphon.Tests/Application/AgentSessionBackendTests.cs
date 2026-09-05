using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0160 / CARD-0186 / CARD-0187 — SessionBackend as a separate, defaulted dimension + the
/// Kind gate (ClaudeCode/Grok/Codex allowed; OpenCode/Raw refused by name). AlwaysOn and
/// channel-bound pairings are allowed.
/// </summary>
[Category("Integration")]
public class AgentSessionBackendTests
{
    [Test]
    public void pty_host_is_zero_so_existing_rows_stay_on_the_only_lane_that_existed()
    {
        ((int)SessionBackend.PtyHost).ShouldBe(0);
        new Agent().SessionBackend.ShouldBe(SessionBackend.PtyHost);
        new AgentSession().SessionBackend.ShouldBe(SessionBackend.PtyHost);
    }

    [Test]
    public void create_request_null_means_pty_host_and_update_null_means_leave_unchanged()
    {
        new CreateAgentRequest("A", "C:\\tmp").SessionBackend.ShouldBeNull();
        new CreateAgentRequest("A", "C:\\tmp", SessionBackend: SessionBackend.Herdr)
            .SessionBackend.ShouldBe(SessionBackend.Herdr);
        new UpdateAgentRequest("A", "C:\\tmp", null, null, AgentAssignmentPolicy.AutoPick)
            .SessionBackend.ShouldBeNull();
    }

    [Test]
    public void herdr_on_always_on_is_allowed_both_directions()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, AgentKind.ClaudeCode));
    }

    [Test]
    public void herdr_while_channel_bound_is_allowed()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, AgentKind.ClaudeCode));
    }

    [Test]
    [Arguments(AgentKind.Grok)]
    [Arguments(AgentKind.Codex)]
    public void herdr_on_grok_and_codex_is_allowed(AgentKind kind)
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, kind));
    }

    [Test]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Raw)]
    public void herdr_on_opencode_and_raw_is_refused_naming_the_kind(AgentKind kind)
    {
        var ex = Should.Throw<ConflictException>(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, kind));
        ex.Code.ShouldBe("herdr_refused");
        ex.Message.ShouldContain(kind.ToString());
        ex.Message.ShouldContain("ClaudeCode, Grok, Codex");
    }

    [Test]
    public void pty_host_accepts_always_on_channel_bound_and_any_kind()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.PtyHost, AgentKind.Grok));
    }

    [Test]
    public void herdr_on_claude_non_always_on_unbound_is_allowed()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(SessionBackend.Herdr, AgentKind.ClaudeCode));
    }

    [Test]
    [Category("Integration")]
    public async Task create_without_field_defaults_to_pty_host()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(Unique("Default Backend"), "D:/src/app"),
            CancellationToken.None);

        created.SessionBackend.ShouldBe(SessionBackend.PtyHost);

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == created.Id))
            .SessionBackend.ShouldBe(SessionBackend.PtyHost);
    }

    [Test]
    [Category("Integration")]
    public async Task create_herdr_persists_when_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr Agent"),
                "D:/src/herdr",
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);

        created.SessionBackend.ShouldBe(SessionBackend.Herdr);
    }

    [Test]
    [Category("Integration")]
    public async Task create_herdr_with_always_on_is_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr AlwaysOn"),
                "D:/src/herdr-ao",
                SessionBackend: SessionBackend.Herdr,
                AlwaysOn: true),
            CancellationToken.None);

        created.SessionBackend.ShouldBe(SessionBackend.Herdr);
        created.AlwaysOn.ShouldBeTrue();
    }

    [Test]
    [Category("Integration")]
    public async Task patch_null_leaves_backend_unchanged_and_herdr_applies_when_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(Unique("Patch Backend"), "D:/src/patch-backend"),
            CancellationToken.None);

        var unchanged = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                BoardId: created.BoardId,
                SessionBackend: null),
            CancellationToken.None);
        unchanged.SessionBackend.ShouldBe(SessionBackend.PtyHost);

        var herdr = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                BoardId: created.BoardId,
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);
        herdr.SessionBackend.ShouldBe(SessionBackend.Herdr);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_always_on_onto_herdr_agent_is_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr Then AO"),
                "D:/src/herdr-then-ao",
                SessionBackend: SessionBackend.Herdr),
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
                AlwaysOn: true),
            CancellationToken.None);

        updated.AlwaysOn.ShouldBeTrue();
        updated.SessionBackend.ShouldBe(SessionBackend.Herdr);

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AlwaysOn.ShouldBeTrue();
        stored.SessionBackend.ShouldBe(SessionBackend.Herdr);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_herdr_onto_always_on_agent_is_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("AO Then Herdr"),
                "D:/src/ao-then-herdr",
                AlwaysOn: true),
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
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);

        updated.SessionBackend.ShouldBe(SessionBackend.Herdr);
        updated.AlwaysOn.ShouldBeTrue();
    }

    [Test]
    [Category("Integration")]
    public async Task channel_bind_onto_herdr_agent_is_allowed()
    {
        await using var db = CreateContext();
        var agents = CreateService(db);
        var created = await agents.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr Channel"),
                "D:/src/herdr-channel",
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);

        var channel = new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = $"chat-{Guid.NewGuid():N}",
            Kind = ChatChannelKind.Group,
            Title = "probe",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ChatChannels.Add(channel);
        await db.SaveChangesAsync();

        var channels = new ChatChannelService(db, TimeProvider.System, new FakeAntiphonMessagingClient());
        var bound = await channels.UpdateAsync(
            channel.Id,
            new UpdateChatChannelRequest(AgentId: created.Id),
            CancellationToken.None);

        bound.AgentId.ShouldBe(created.Id);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_kind_to_raw_on_a_herdr_agent_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr Then Raw"),
                "D:/src/herdr-then-raw",
                SessionBackend: SessionBackend.Herdr),
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
                    Kind: AgentKind.Raw),
                CancellationToken.None));
        ex.Code.ShouldBe("herdr_refused");
        ex.Message.ShouldContain("Raw");
        ex.Message.ShouldContain("ClaudeCode, Grok, Codex");

        await using var verify = CreateContext();
        (await verify.Agents.SingleAsync(a => a.Id == created.Id)).Kind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    [Category("Integration")]
    public async Task channel_bind_onto_a_grok_herdr_agent_is_allowed()
    {
        await using var db = CreateContext();
        var agents = CreateService(db);
        var grok = await SeedProfileAsync(db, AgentKind.Grok, "Grok");
        var created = await agents.CreateAsync(
            new CreateAgentRequest(
                Unique("Grok Herdr Channel"),
                "D:/src/grok-herdr-channel",
                TuiProfileId: grok.Id,
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);
        created.Kind.ShouldBe(AgentKind.Grok);
        created.SessionBackend.ShouldBe(SessionBackend.Herdr);

        var channel = new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = $"chat-{Guid.NewGuid():N}",
            Kind = ChatChannelKind.Group,
            Title = "probe",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ChatChannels.Add(channel);
        await db.SaveChangesAsync();

        var channels = new ChatChannelService(db, TimeProvider.System, new FakeAntiphonMessagingClient());
        var bound = await channels.UpdateAsync(
            channel.Id,
            new UpdateChatChannelRequest(AgentId: created.Id),
            CancellationToken.None);

        bound.AgentId.ShouldBe(created.Id);
    }

    [Test]
    [Category("Integration")]
    public async Task herdr_while_a_channel_names_the_agent_is_allowed()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(Unique("Bound Then Herdr"), "D:/src/bound-herdr"),
            CancellationToken.None);

        db.ChatChannels.Add(new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = $"chat-{Guid.NewGuid():N}",
            Kind = ChatChannelKind.Direct,
            Title = "bound",
            AgentId = created.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var updated = await service.UpdateAsync(
            created.Id,
            new UpdateAgentRequest(
                created.Name,
                created.WorkingDirectory,
                created.Details,
                created.DefaultWorkflowTemplateId,
                created.AssignmentPolicy,
                BoardId: created.BoardId,
                SessionBackend: SessionBackend.Herdr),
            CancellationToken.None);

        updated.SessionBackend.ShouldBe(SessionBackend.Herdr);
    }

    [Test]
    [Category("Integration")]
    public async Task interactive_session_row_stamps_the_agents_backend()
    {
        await using var db = CreateContext();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = Unique("Stamp"),
            Slug = $"stamp-{Guid.NewGuid():N}"[..20],
            WorkingDirectory = "D:/src/stamp",
            Details = string.Empty,
            SessionBackend = SessionBackend.Herdr,
            Kind = AgentKind.ClaudeCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = agent.SessionBackend,
            Status = SessionStatus.Starting,
            Cwd = agent.WorkingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();

        agent.SessionBackend = SessionBackend.PtyHost;
        await db.SaveChangesAsync();

        await using var verify = CreateContext();
        var storedSession = await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        storedSession.SessionBackend.ShouldBe(
            SessionBackend.Herdr,
            "a PATCH after launch must not change the live session's snapshot");
        (await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id))
            .SessionBackend.ShouldBe(SessionBackend.PtyHost);
    }

    [Test]
    [Category("Integration")]
    [NotInParallel("AgentControl")]
    public async Task resume_restamps_session_backend_from_the_agent()
    {
        var tempRoot = AgentControlServiceIntegrationTests.NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "restamp-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest(Unique("Restamp Backend"), workspace),
                CancellationToken.None);
            agent.SessionBackend.ShouldBe(SessionBackend.PtyHost);

            var first = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            first.PersistentSessionId.ShouldNotBeNull();

            await using (var launched = CreateContext())
            {
                var row = await launched.AgentSessions.SingleAsync(
                    s => s.Id.ToString() == first.PersistentSessionId);
                row.SessionBackend.ShouldBe(SessionBackend.PtyHost);
            }

            await AgentControlServiceIntegrationTests.MarkSessionEndedAsync(
                first.PersistentSessionId!, SessionStatus.Failed);

            using var scope = harness.Provider.CreateScope();
            var agents = scope.ServiceProvider.GetRequiredService<AgentService>();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            await agents.UpdateAsync(
                agent.Id,
                new UpdateAgentRequest(
                    agent.Name,
                    agent.WorkingDirectory,
                    agent.Details,
                    agent.DefaultWorkflowTemplateId,
                    agent.AssignmentPolicy,
                    BoardId: agent.BoardId,
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None);

            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            await using var verify = CreateContext();
            var resumed = await verify.AgentSessions.SingleAsync(
                s => s.Id.ToString() == first.PersistentSessionId);
            resumed.SessionBackend.ShouldBe(SessionBackend.Herdr);
        }
        finally
        {
            await AgentControlServiceIntegrationTests.CleanupProjectsByTempRootAsync(tempRoot);
            AgentControlServiceIntegrationTests.DeleteDirectoryBestEffort(tempRoot);
        }
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
