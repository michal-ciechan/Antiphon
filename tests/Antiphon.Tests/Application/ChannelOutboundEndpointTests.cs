using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[Category("Integration")]
public sealed class ChannelOutboundEndpointTests
{
    [Test]
    public async Task Profile_patch_clear_and_rebind()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "conversion.md"), "Convert only staged sources.");
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        await using var factory = new OutboundEndpointFactory(projectId, converterId);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.Projects.Add(new Project { Id = projectId, Name = "outbound-http-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "outbound",
            CreatedAt = now, UpdatedAt = now });
        db.Agents.AddRange(
            new Agent { Id = inboundId, Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
            ExternalId = channelId.ToString("N"), AgentId = inboundId,
            CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        try
        {
            using (var profilesResponse = await client.GetAsync("/api/channels/outbound-profiles"))
            {
                profilesResponse.EnsureSuccessStatusCode();
                using var profiles = JsonDocument.Parse(await profilesResponse.Content.ReadAsStringAsync());
                profiles.RootElement.GetArrayLength().ShouldBe(1);
                profiles.RootElement[0].GetProperty("name").GetString().ShouldBe("conversion");
                profiles.RootElement[0].GetProperty("projectId").GetGuid().ShouldBe(projectId);
            }
            using (var bind = await client.PatchAsJsonAsync($"/api/channels/{channelId:D}",
                       new { outboundAgentProfile = "conversion" }))
            {
                bind.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await bind.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("outboundAgentProfile").GetString().ShouldBe("conversion");
            }
            using (var clear = await client.PatchAsJsonAsync($"/api/channels/{channelId:D}",
                       new { clearOutboundAgentProfile = true }))
            {
                clear.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await clear.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("outboundAgentProfile").ValueKind.ShouldBe(JsonValueKind.Null);
            }
            factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
        }
        finally
        {
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundId || a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class OutboundEndpointFactory(Guid projectId, Guid converterId) : AntiphonWebAppFactory
    {
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.PostConfigure<ChannelOutboundSettings>(settings =>
                settings.Profiles["conversion"] = new ChannelOutboundProfile
                {
                    ProjectId = projectId, AgentId = converterId,
                    PromptFile = "conversion.md", Trigger = ChannelOutboundTrigger.MarkdownSources,
                });
        }
    }
}
