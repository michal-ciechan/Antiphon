using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[Category("Integration")]
public sealed class ChannelOutboundEndpointTests
{
    [Test]
    public async Task Legacy_renderer_keys_warn_once_on_fresh_host_without_browser()
    {
        var root = Directory.CreateTempSubdirectory("c0418-legacy-startup-").FullName;
        try
        {
            await using var factory = new LegacyRendererKeysFactory(root);
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health");
            response.EnsureSuccessStatusCode();
            factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();

            var logs = Directory.GetFiles(root, "antiphon-*.log");
            logs.ShouldNotBeEmpty();
            var entries = logs.SelectMany(ReadLinesSharedWithWriter)
                .Where(line => line.Contains("Legacy Deliverables renderer settings are ignored;", StringComparison.Ordinal))
                .ToArray();
            entries.Length.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The live host's file sink still holds the log open; Windows refuses a non-sharing read.
    private static string[] ReadLinesSharedWithWriter(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n');
    }

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
        var companionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
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
        foreach (var companionId in companionIds)
            db.ChatChannels.Add(new ChatChannel { Id = companionId, Provider = "fake",
                ExternalId = companionId.ToString("N"), AgentId = inboundId,
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
                profiles.RootElement[0].GetProperty("agentId").GetGuid().ShouldBe(converterId);
                profiles.RootElement[0].GetProperty("promptRevision").GetString()!.Length.ShouldBe(64);
                profiles.RootElement[0].GetProperty("trigger").GetString().ShouldBe("MarkdownSources");
                profiles.RootElement[0].GetProperty("timeoutSeconds").GetInt32().ShouldBe(120);
                profiles.RootElement[0].GetProperty("authorization").GetString().ShouldContain("metered");
            }
            using (var bind = await client.PatchAsJsonAsync($"/api/channels/{channelId:D}",
                       new { outboundAgentProfile = "conversion" }))
            {
                bind.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await bind.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("outboundAgentProfile").GetString().ShouldBe("conversion");
                body.RootElement.GetProperty("outboundProfile").GetProperty("projectId").GetGuid()
                    .ShouldBe(projectId);
            }
            using (var list = await client.GetAsync("/api/channels"))
            {
                list.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
                foreach (var item in body.RootElement.EnumerateArray()
                             .Where(item => item.GetProperty("id").GetGuid() == channelId
                                 || companionIds.Contains(item.GetProperty("id").GetGuid())))
                {
                    var id = item.GetProperty("id").GetGuid();
                    if (id == channelId)
                        item.GetProperty("outboundAgentProfile").GetString().ShouldBe("conversion");
                    else
                        item.GetProperty("outboundAgentProfile").ValueKind.ShouldBe(JsonValueKind.Null);
                }
            }
            using (var invalid = await client.PatchAsJsonAsync($"/api/channels/{channelId:D}",
                       new { outboundAgentProfile = "unknown" }))
                invalid.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
            (await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .OutboundAgentProfile.ShouldBe("conversion");
            using (var clear = await client.PatchAsJsonAsync($"/api/channels/{channelId:D}",
                       new { clearOutboundAgentProfile = true }))
            {
                clear.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await clear.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("outboundAgentProfile").ValueKind.ShouldBe(JsonValueKind.Null);
            }
            using (var list = await client.GetAsync("/api/channels"))
            {
                list.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
                body.RootElement.EnumerateArray().Single(item =>
                    item.GetProperty("id").GetGuid() == channelId)
                    .GetProperty("outboundAgentProfile").ValueKind.ShouldBe(JsonValueKind.Null);
            }
            factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
        }
        finally
        {
            await db.ChatChannels.Where(c => c.Id == channelId || companionIds.Contains(c.Id))
                .ExecuteDeleteAsync();
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

    private sealed class LegacyRendererKeysFactory(string logRoot) : AntiphonWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Deliverables:BrowserPath"] = Path.Combine(logRoot, "missing-browser"),
                    ["Deliverables:RenderTimeoutSeconds"] = "1",
                    ["Serilog:LogPath"] = logRoot,
                }));
        }
    }
}
