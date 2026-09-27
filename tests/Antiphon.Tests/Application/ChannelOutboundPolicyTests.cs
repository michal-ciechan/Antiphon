using System.Security.Cryptography;
using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundPolicyTests
{
    [Test]
    public async Task Profile_binding_defaults_and_validation()
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var directory = Path.Combine(Path.GetTempPath(), "antiphon-outbound-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var prompt = Path.Combine(directory, "convert.md");
        await File.WriteAllTextAsync(prompt, "Convert the supplied files.\n");
        var profile = new ChannelOutboundProfile
        {
            ProjectId = projectId, AgentId = converterId, PromptFile = "convert.md",
            Trigger = ChannelOutboundTrigger.EveryAgentReply,
        };
        var settings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile> { ["pdf-project"] = profile },
        });

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "outbound-" + projectId.ToString("N"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "outbound",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Agents.AddRange(
            new Agent { Id = inboundId, Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                WorkingDirectory = directory, BoardId = boardId },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                WorkingDirectory = directory, BoardId = boardId });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake", ExternalId = channelId.ToString("N"),
            AgentId = inboundId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        try
        {
            var service = new ChatChannelService(db, TimeProvider.System,
                new FakeAntiphonMessagingClient(), settings);
            var initial = (await service.GetAllAsync(CancellationToken.None)).Single(c => c.Id == channelId);
            initial.OutboundAgentProfile.ShouldBeNull();
            initial.OutboundProfile.ShouldBeNull();

            await Should.ThrowAsync<ValidationException>(() => service.UpdateAsync(channelId,
                new UpdateChatChannelRequest(OutboundAgentProfile: "unknown"), CancellationToken.None));
            (await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .OutboundAgentProfile.ShouldBeNull();

            var bound = await service.UpdateAsync(channelId,
                new UpdateChatChannelRequest(OutboundAgentProfile: "pdf-project"), CancellationToken.None);
            bound.OutboundAgentProfile.ShouldBe("pdf-project");
            bound.OutboundProfile!.ProjectId.ShouldBe(projectId);
            bound.OutboundProfile.AgentId.ShouldBe(converterId);
            bound.OutboundProfile.PromptRevision.ShouldBe(
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(prompt))).ToLowerInvariant());
            bound.OutboundProfile.Authorization.ShouldContain("metered");

            (await service.UpdateAsync(channelId, new UpdateChatChannelRequest(DigestEnabled: true),
                CancellationToken.None)).OutboundAgentProfile.ShouldBe("pdf-project");
            (await service.UpdateAsync(channelId, new UpdateChatChannelRequest(ClearOutboundAgentProfile: true),
                CancellationToken.None)).OutboundAgentProfile.ShouldBeNull();
            await service.UpdateAsync(channelId,
                new UpdateChatChannelRequest(OutboundAgentProfile: "pdf-project"), CancellationToken.None);
            (await service.UpdateAsync(channelId, new UpdateChatChannelRequest(UnbindAgent: true),
                CancellationToken.None)).OutboundAgentProfile.ShouldBeNull();

            await service.UpdateAsync(channelId,
                new UpdateChatChannelRequest(AgentId: inboundId, OutboundAgentProfile: "pdf-project"),
                CancellationToken.None);
            await db.Agents.Where(a => a.Id == converterId).ExecuteDeleteAsync();
            var producer = new FakeAntiphonMessagingClient();
            var outbound = new ChannelOutboundService(db,
                new ChannelOutboundFileStore(Path.Combine(directory, "outbound")), producer,
                settings, TimeProvider.System);
            var reply = new ChannelReply { Channel = "fake", ConversationId = channelId.ToString("N"),
                Text = "source text" };
            var source = new ChannelOutboundSource(Guid.NewGuid(), 1, 2, 3, "main", []);
            (await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply, source,
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            var degraded = await db.ChannelOutboundDeliveries.SingleAsync(d => d.ChannelId == channelId);
            degraded.State.ShouldBe(Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState.Ready);
            degraded.ConversionOutcome.ShouldBe("Fallback");
            producer.SentReplies.ShouldBeEmpty();

            await db.ChannelOutboundDeliveries.Where(d => d.Id == degraded.Id).ExecuteDeleteAsync();
            settings.Value.Profiles.Clear();
            (await outbound.SendAsync(reply with { Text = "new source-only message" },
                ChannelOutboundOrigin.AgentReply, source with { PromptSequence = 4 },
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Published);
            producer.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("new source-only message");
        }
        finally
        {
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundId || a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Profile_limits_refuse_out_of_range_values()
    {
        var validator = new ChannelOutboundSettingsValidator();
        var profile = new ChannelOutboundProfile
        {
            ProjectId = Guid.NewGuid(), AgentId = Guid.NewGuid(), PromptFile = "convert.md",
        };
        var settings = new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile> { ["conversion"] = profile },
        };
        validator.Validate(null, settings).Succeeded.ShouldBeTrue();
        foreach (var timeout in new[] { 9, 301 })
        {
            profile.TimeoutSeconds = timeout;
            validator.Validate(null, settings).Succeeded.ShouldBeFalse();
        }
        profile.TimeoutSeconds = 120;
        foreach (var maxPending in new[] { 0, 33 })
        {
            profile.MaxPending = maxPending;
            validator.Validate(null, settings).Succeeded.ShouldBeFalse();
        }
        profile.MaxPending = 8;
        profile.PromptFile = "../escape.md";
        validator.Validate(null, settings).Succeeded.ShouldBeFalse();
    }

    [Test]
    public void Text_only_conversion_failure_does_not_claim_source_attachments()
    {
        var text = ChannelOutboundService.AnnotateFallback("original answer", false);
        text.ShouldContain("original answer");
        text.ShouldContain("original reply sent");
        text.ShouldNotContain("files attached");
    }
}
