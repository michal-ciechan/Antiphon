using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
    [Arguments("disabled")]
    [Arguments("unbound")]
    [Arguments("inbound_project")]
    [Arguments("converter_project")]
    [Arguments("same_agent")]
    [Arguments("raw_converter")]
    [Arguments("pool_converter")]
    [Arguments("always_on_converter")]
    [Arguments("converter_is_inbound_elsewhere")]
    [Arguments("missing_prompt")]
    [Arguments("escaping_prompt")]
    [Arguments("missing_workspace")]
    public async Task Invalid_profile_bindings_are_atomic_failures(string fault)
    {
        var root = Directory.CreateTempSubdirectory("c0418-policy-matrix-").FullName;
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var otherBoardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var extraChannelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert source Markdown.");
        var profile = new ChannelOutboundProfile
        {
            ProjectId = projectId, AgentId = converterId, PromptFile = "convert.md",
        };
        var settings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile> { ["conversion"] = profile },
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.AddRange(
            new Project { Id = projectId, Name = "policy-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now },
            new Project { Id = otherProjectId, Name = "policy-" + otherProjectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
        db.Boards.AddRange(
            new Board { Id = boardId, ProjectId = projectId, Name = "policy",
                CreatedAt = now, UpdatedAt = now },
            new Board { Id = otherBoardId, ProjectId = otherProjectId, Name = "other",
                CreatedAt = now, UpdatedAt = now });
        db.Agents.AddRange(
            new Agent { Id = inboundId, Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                BoardId = fault == "inbound_project" ? otherBoardId : boardId,
                WorkingDirectory = root },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                BoardId = fault == "converter_project" ? otherBoardId : boardId,
                WorkingDirectory = fault == "missing_workspace" ? Path.Combine(root, "absent") : root,
                Kind = fault == "raw_converter" ? AgentKind.Raw : AgentKind.ClaudeCode,
                IsPoolDelegate = fault == "pool_converter", AlwaysOn = fault == "always_on_converter" });
        db.ChatChannels.Add(new ChatChannel
        {
            Id = channelId, Provider = "fake", ExternalId = channelId.ToString("N"),
            AgentId = fault == "unbound" ? null : inboundId, Enabled = fault != "disabled",
            CreatedAt = now, UpdatedAt = now,
        });
        if (fault == "converter_is_inbound_elsewhere")
            db.ChatChannels.Add(new ChatChannel
            {
                Id = extraChannelId, Provider = "fake", ExternalId = extraChannelId.ToString("N"),
                AgentId = converterId, CreatedAt = now, UpdatedAt = now,
            });
        if (fault == "same_agent") profile.AgentId = inboundId;
        if (fault == "missing_prompt") profile.PromptFile = "absent.md";
        if (fault == "escaping_prompt") profile.PromptFile = "../escape.md";
        await db.SaveChangesAsync();
        // Raw is enum zero, the EF store-generated sentinel for the ClaudeCode default.
        // Force the persisted kind so this row exercises the nondelegatable binding guard.
        if (fault == "raw_converter")
            await db.Agents.Where(a => a.Id == converterId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Kind, AgentKind.Raw));
        db.ChangeTracker.Clear();
        try
        {
            var service = new ChatChannelService(db, TimeProvider.System,
                new FakeAntiphonMessagingClient(), settings);
            await Should.ThrowAsync<ValidationException>(() => service.UpdateAsync(channelId,
                new UpdateChatChannelRequest(OutboundAgentProfile: "conversion"), CancellationToken.None));
            db.ChangeTracker.Clear();
            (await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .OutboundAgentProfile.ShouldBeNull();
        }
        finally
        {
            await db.ChatChannels.Where(c => c.Id == channelId || c.Id == extraChannelId)
                .ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundId || a.Id == converterId)
                .ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId || b.Id == otherBoardId)
                .ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId || p.Id == otherProjectId)
                .ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }

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
        foreach (var timeout in new[] { 10, 120, 300 })
        {
            profile.TimeoutSeconds = timeout;
            validator.Validate(null, settings).Succeeded.ShouldBeTrue();
        }
        foreach (var timeout in new[] { 9, 301 })
        {
            profile.TimeoutSeconds = timeout;
            validator.Validate(null, settings).Succeeded.ShouldBeFalse();
        }
        profile.TimeoutSeconds = 120;
        foreach (var maxPending in new[] { 1, 8, 32 })
        {
            profile.MaxPending = maxPending;
            validator.Validate(null, settings).Succeeded.ShouldBeTrue();
        }
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

    [Test]
    public void Markdown_source_trigger_requires_an_attached_manifested_zip()
    {
        var manifest = JsonSerializer.Serialize(new DeliverableBundleService.SourceManifest(1, true,
            [new("docs/source.md", "sources.zip", "docs/source.md", 7, new string('a', 64))], []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        ChannelReply Reply(string name) => new()
        {
            Channel = "fake", ConversationId = "C1",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                Name = name, Mime = "application/zip", Content = [1, 2, 3] }],
        };
        ChannelOutboundService.MatchesMarkdownSources(Reply("unrelated.zip"), manifest).ShouldBeFalse();
        ChannelOutboundService.MatchesMarkdownSources(Reply("sources.zip"), manifest).ShouldBeTrue();
        ChannelOutboundService.MatchesMarkdownSources(Reply("source.md"), null).ShouldBeTrue();
        ChannelOutboundService.MatchesMarkdownSources(new ChannelReply
            { Channel = "fake", ConversationId = "C1", Text = "plain" }, manifest).ShouldBeFalse();
    }

    [Test]
    [Arguments("inline_markdown", true)]
    [Arguments("uppercase_markdown", true)]
    [Arguments("missing_inline_bytes", false)]
    [Arguments("manifested_zip", true)]
    [Arguments("unlisted_zip", false)]
    [Arguments("zip_without_bytes", false)]
    [Arguments("zip_without_manifest", false)]
    [Arguments("wrong_manifest_version", false)]
    [Arguments("non_source_zip_member", false)]
    [Arguments("text_only", false)]
    public void Markdown_trigger_matches_only_authorized_source_shapes(string shape, bool expected)
    {
        var source = new DeliverableBundleService.SourceMember("docs/source.md",
            "sources.zip", "docs/source.md", 3, new string('a', 64));
        var manifest = JsonSerializer.Serialize(new DeliverableBundleService.SourceManifest(1, true,
            [source], []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (shape == "wrong_manifest_version")
            manifest = JsonSerializer.Serialize(new DeliverableBundleService.SourceManifest(2, true,
                [source], []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (shape == "non_source_zip_member")
            manifest = JsonSerializer.Serialize(new DeliverableBundleService.SourceManifest(1, true,
                [source with { ZipEntry = null }], []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var (name, bytes) = shape switch
        {
            "inline_markdown" => ("source.md", (byte[]?)[1, 2, 3]),
            "uppercase_markdown" => ("SOURCE.MD", (byte[]?)[1, 2, 3]),
            "missing_inline_bytes" => ("source.md", null),
            "manifested_zip" or "wrong_manifest_version" or "non_source_zip_member"
                => ("sources.zip", (byte[]?)[1, 2, 3]),
            "unlisted_zip" => ("unlisted.zip", (byte[]?)[1, 2, 3]),
            "zip_without_manifest" => ("sources.zip", (byte[]?)[1, 2, 3]),
            "zip_without_bytes" => ("sources.zip", null),
            _ => ("answer.txt", (byte[]?)null),
        };
        var reply = new ChannelReply
        {
            Channel = "fake", ConversationId = "C1", Text = "source task finished",
            Attachments = shape == "text_only" ? [] : [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = name, Mime = "application/octet-stream",
                Content = bytes,
            }],
        };
        ChannelOutboundService.MatchesMarkdownSources(reply,
            shape == "zip_without_manifest" ? null : manifest).ShouldBe(expected);
    }
}
