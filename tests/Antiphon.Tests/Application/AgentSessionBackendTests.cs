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
/// CARD-0160 — SessionBackend as a separate, defaulted dimension + the three refusal gates.
/// </summary>
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
    public void herdr_on_always_on_is_refused_both_directions()
    {
        Should.Throw<ConflictException>(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.Herdr, alwaysOn: true, AgentKind.ClaudeCode, channelBound: false));

        // Mirrored: AlwaysOn requested while already Herdr — same pairing check.
        Should.Throw<ConflictException>(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.Herdr, alwaysOn: true, AgentKind.ClaudeCode, channelBound: false))
            .Code.ShouldBe("herdr_refused");
    }

    [Test]
    public void herdr_while_channel_bound_is_refused()
    {
        Should.Throw<ConflictException>(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.Herdr, alwaysOn: false, AgentKind.ClaudeCode, channelBound: true))
            .Message.ShouldContain("Channel-bound");
    }

    [Test]
    public void herdr_on_non_claude_is_refused()
    {
        Should.Throw<ConflictException>(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.Herdr, alwaysOn: false, AgentKind.Grok, channelBound: false))
            .Message.ShouldContain("Claude Code");
    }

    [Test]
    public void pty_host_accepts_always_on_channel_bound_and_any_kind()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.PtyHost, alwaysOn: true, AgentKind.Grok, channelBound: true));
    }

    [Test]
    public void herdr_on_claude_non_always_on_unbound_is_allowed()
    {
        Should.NotThrow(() =>
            AgentService.ValidateSessionBackendPairing(
                SessionBackend.Herdr, alwaysOn: false, AgentKind.ClaudeCode, channelBound: false));
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
    public async Task create_herdr_with_always_on_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            service.CreateAsync(
                new CreateAgentRequest(
                    Unique("Herdr AlwaysOn"),
                    "D:/src/herdr-ao",
                    SessionBackend: SessionBackend.Herdr,
                    AlwaysOn: true),
                CancellationToken.None));

        ex.Code.ShouldBe("herdr_refused");
        (await db.Agents.CountAsync(a => a.Name.StartsWith("Herdr AlwaysOn"))).ShouldBe(0);
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
    public async Task patch_always_on_onto_herdr_agent_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("Herdr Then AO"),
                "D:/src/herdr-then-ao",
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
                    AlwaysOn: true),
                CancellationToken.None));

        ex.Code.ShouldBe("herdr_refused");

        await using var verify = CreateContext();
        var stored = await verify.Agents.SingleAsync(a => a.Id == created.Id);
        stored.AlwaysOn.ShouldBeFalse();
        stored.SessionBackend.ShouldBe(SessionBackend.Herdr);
    }

    [Test]
    [Category("Integration")]
    public async Task patch_herdr_onto_always_on_agent_is_refused()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            new CreateAgentRequest(
                Unique("AO Then Herdr"),
                "D:/src/ao-then-herdr",
                AlwaysOn: true),
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
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None));

        ex.Code.ShouldBe("herdr_refused");
    }

    [Test]
    [Category("Integration")]
    public async Task channel_bind_onto_herdr_agent_is_refused()
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

        var channels = new ChatChannelService(db, TimeProvider.System);
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            channels.UpdateAsync(
                channel.Id,
                new UpdateChatChannelRequest(AgentId: created.Id),
                CancellationToken.None));

        ex.Code.ShouldBe("herdr_refused");
        ex.Message.ShouldContain("Bind refused");
    }

    [Test]
    [Category("Integration")]
    public async Task herdr_while_a_channel_names_the_agent_is_refused()
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
                    SessionBackend: SessionBackend.Herdr),
                CancellationToken.None));

        ex.Code.ShouldBe("herdr_refused");
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
