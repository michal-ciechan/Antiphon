using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-5 (endpoint half): binding a conversion profile to a channel is an explicit,
/// validated, reversible act over the real HTTP surface.
///
/// <para>The whole point of the feature is that it is OFF unless someone turned it on for one
/// conversation, so the tests that matter most here are the ones about NOT changing a binding:
/// an unrelated PATCH must leave it alone, and every invalid change must leave the row exactly as
/// it was rather than half-applied.</para>
/// </summary>
[NotInParallel]
[ClassDataSource<ChannelOutboundWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ChannelOutboundEndpointTests(ChannelOutboundWebAppFactory factory)
{
    private const string ProfileName = "pdf";

    [Test]
    [Timeout(240_000)]
    public async Task Profile_patch_clear_and_rebind(CancellationToken ct)
    {
        var world = await SeedAsync(ct);
        using var client = factory.CreateClient();

        // A newly discovered channel defaults to no profile at all.
        (await ReadChannelAsync(world.ChannelX, ct)).OutboundAgentProfile.ShouldBeNull();

        // Bind X.
        var bind = await PatchAsync(client, world.ChannelX, new { outboundAgentProfile = ProfileName }, ct);
        bind.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadChannelAsync(world.ChannelX, ct)).OutboundAgentProfile.ShouldBe(ProfileName);
        (await ReadChannelAsync(world.ChannelY, ct)).OutboundAgentProfile.ShouldBeNull();

        // The bound channel's DTO exposes the pinned converter and the prompt revision, so an
        // operator can see WHAT they authorized, not just that they authorized something.
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/channels", ct);
        var x = listed.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == world.ChannelX);
        x.GetProperty("outboundAgentProfile").GetString().ShouldBe(ProfileName);
        var preview = x.GetProperty("outboundPreview");
        preview.GetProperty("agentId").GetGuid().ShouldBe(world.ConverterId);
        preview.GetProperty("agentName").GetString().ShouldBe("converter-c");
        preview.GetProperty("promptRevision").GetString().ShouldNotBe("missing");
        preview.GetProperty("trigger").GetString().ShouldBe(nameof(ChannelOutboundTrigger.MarkdownSources));
        preview.GetProperty("authorizationNote").GetString()
            .ShouldContain("one metered worker invocation per matching reply");

        // An unrelated PATCH must not disturb the binding.
        var unrelated = await PatchAsync(client, world.ChannelX, new { digestEnabled = true }, ct);
        unrelated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterUnrelated = await ReadChannelAsync(world.ChannelX, ct);
        afterUnrelated.OutboundAgentProfile.ShouldBe(ProfileName);
        afterUnrelated.DigestEnabled.ShouldBeTrue();

        // Explicit clear.
        var clear = await PatchAsync(client, world.ChannelX, new { clearOutboundAgentProfile = true }, ct);
        clear.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadChannelAsync(world.ChannelX, ct)).OutboundAgentProfile.ShouldBeNull();

        // Rebind, then unbind the inbound agent: the outbound binding goes with it, because a
        // profile without an inbound agent in the same project is meaningless.
        (await PatchAsync(client, world.ChannelX, new { outboundAgentProfile = ProfileName }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadChannelAsync(world.ChannelX, ct)).OutboundAgentProfile.ShouldBe(ProfileName);
        (await PatchAsync(client, world.ChannelX, new { unbindAgent = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterUnbind = await ReadChannelAsync(world.ChannelX, ct);
        afterUnbind.AgentId.ShouldBeNull();
        afterUnbind.OutboundAgentProfile.ShouldBeNull();

        // Rebinding the inbound agent also clears it, so a channel that changes hands never
        // inherits the previous owner's conversion authorization.
        (await PatchAsync(client, world.ChannelX, new { agentId = world.InboundId }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadChannelAsync(world.ChannelX, ct)).OutboundAgentProfile.ShouldBeNull();
    }

    /// <summary>
    /// Every validation refusal, each one asserted to be ATOMIC: the channel row after the refusal
    /// is byte-identical to the row before it, including any binding it already had.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    [Arguments("unknown-profile")]
    [Arguments("disabled-channel")]
    [Arguments("no-inbound-agent")]
    [Arguments("project-mismatch")]
    [Arguments("converter-missing")]
    [Arguments("converter-not-delegatable")]
    [Arguments("converter-workspace-missing")]
    [Arguments("prompt-file-missing")]
    [Arguments("prompt-escapes-workspace")]
    [Arguments("converter-is-inbound-bound")]
    public async Task Invalid_bindings_are_atomic_failures(string shape, CancellationToken ct)
    {
        var world = await SeedAsync(ct);
        using var client = factory.CreateClient();

        // Start from a channel that already has a valid binding, so a refusal that "helpfully"
        // cleared it would be caught too.
        (await PatchAsync(client, world.ChannelX, new { outboundAgentProfile = ProfileName }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await ReadChannelAsync(world.ChannelX, ct);

        var profileName = "bad-" + shape;
        var profile = new ChannelOutboundProfileSettings
        {
            ProjectId = world.ProjectP,
            AgentId = world.ConverterId,
            PromptFile = "prompts/pdf.md",
        };
        var requestProfileName = profileName;

        switch (shape)
        {
            case "unknown-profile":
                requestProfileName = "no-such-profile";
                break;
            case "disabled-channel":
                await MutateChannelAsync(world.ChannelX, c => c.Enabled = false, ct);
                break;
            case "no-inbound-agent":
                await MutateChannelAsync(world.ChannelX, c => c.AgentId = null, ct);
                break;
            case "project-mismatch":
                profile.ProjectId = world.ProjectQ;
                break;
            case "converter-missing":
                profile.AgentId = Guid.NewGuid();
                break;
            case "converter-not-delegatable":
                profile.AgentId = await SeedAgentAsync("raw-converter", world.ProjectP, AgentKind.Raw, ct);
                break;
            case "converter-workspace-missing":
                profile.AgentId = await SeedAgentAsync(
                    "no-workspace", world.ProjectP, AgentKind.ClaudeCode, ct,
                    workspace: Path.Combine(Path.GetTempPath(), "antiphon-absent-" + Guid.NewGuid().ToString("N")));
                break;
            case "prompt-file-missing":
                profile.PromptFile = "prompts/does-not-exist.md";
                break;
            case "prompt-escapes-workspace":
                profile.PromptFile = "../outside.md";
                break;
            case "converter-is-inbound-bound":
                // The converter becomes some channel's inbound agent: it can no longer be the
                // converter, or a reply could convert itself.
                await MutateChannelAsync(world.ChannelZ, c => c.AgentId = world.ConverterId, ct);
                break;
        }

        factory.Configure(s => s.Profiles[profileName] = profile);
        try
        {
            var response = await PatchAsync(
                client, world.ChannelX, new { outboundAgentProfile = requestProfileName }, ct);
            // 422 is this repository's answer to a ValidationException, not 400.
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, shape);
            (await response.Content.ReadAsStringAsync(ct)).ShouldContain("outboundAgentProfile");

            var after = await ReadChannelAsync(world.ChannelX, ct);
            after.OutboundAgentProfile.ShouldBe(before.OutboundAgentProfile, shape + ": binding changed");
            after.AgentId.ShouldBe(
                shape is "no-inbound-agent" ? null : before.AgentId, shape + ": inbound binding changed");
            after.Enabled.ShouldBe(shape is not "disabled-channel", shape + ": enabled changed");
        }
        finally
        {
            factory.Configure(s => s.Profiles.Remove(profileName));
            await MutateChannelAsync(world.ChannelX, c =>
            {
                c.Enabled = true;
                c.AgentId = world.InboundId;
            }, ct);
            await MutateChannelAsync(world.ChannelZ, c => c.AgentId = null, ct);
        }
    }

    /// <summary>
    /// The profile catalog is a read of configuration, not of channel state: it lists what an
    /// operator may choose, with the converter each choice pins.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Outbound_profile_catalog_lists_configured_choices(CancellationToken ct)
    {
        var world = await SeedAsync(ct);
        using var client = factory.CreateClient();

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/channels/outbound-profiles", ct);
        var row = listed.EnumerateArray().Single(p => p.GetProperty("name").GetString() == ProfileName);
        row.GetProperty("projectId").GetGuid().ShouldBe(world.ProjectP);
        row.GetProperty("agentId").GetGuid().ShouldBe(world.ConverterId);
        row.GetProperty("agentName").GetString().ShouldBe("converter-c");
        row.GetProperty("trigger").GetString().ShouldBe(nameof(ChannelOutboundTrigger.MarkdownSources));
        row.GetProperty("timeoutSeconds").GetInt32().ShouldBe(120);
        row.GetProperty("maxPending").GetInt32().ShouldBe(8);
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private sealed record SeededWorld(
        Guid ProjectP, Guid ProjectQ, Guid InboundId, Guid ConverterId,
        Guid ChannelX, Guid ChannelY, Guid ChannelZ);

    private static SeededWorld? _world;

    private async Task<SeededWorld> SeedAsync(CancellationToken ct)
    {
        if (_world is { } existing)
        {
            await ResetChannelsAsync(existing, ct);
            return existing;
        }

        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-endpoints-" + Guid.NewGuid().ToString("N"));
        var converterWorkspace = Path.Combine(root, "converter");
        Directory.CreateDirectory(Path.Combine(converterWorkspace, "prompts"));
        await File.WriteAllTextAsync(
            Path.Combine(converterWorkspace, "prompts", "pdf.md"), "# convert\n", ct);

        var projectP = Guid.NewGuid();
        var projectQ = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.Projects.AddRange(
            new Project { Id = projectP, Name = "P", GitRepositoryUrl = "https://example.invalid/p.git", CreatedAt = now, UpdatedAt = now },
            new Project { Id = projectQ, Name = "Q", GitRepositoryUrl = "https://example.invalid/q.git", CreatedAt = now, UpdatedAt = now });

        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(root, "inbound"));
        db.Agents.AddRange(
            NewAgent(inboundId, "inbound-a", projectP, Path.Combine(root, "inbound")),
            NewAgent(converterId, "converter-c", projectP, converterWorkspace));

        var channelX = Guid.NewGuid();
        var channelY = Guid.NewGuid();
        var channelZ = Guid.NewGuid();
        db.ChatChannels.AddRange(
            NewChannel(channelX, "endpoint-X", inboundId),
            NewChannel(channelY, "endpoint-Y", inboundId),
            NewChannel(channelZ, "endpoint-Z", null));
        await db.SaveChangesAsync(ct);

        factory.Configure(s => s.Profiles[ProfileName] = new ChannelOutboundProfileSettings
        {
            ProjectId = projectP,
            AgentId = converterId,
            PromptFile = "prompts/pdf.md",
            Trigger = ChannelOutboundTrigger.MarkdownSources,
            TimeoutSeconds = 120,
            MaxPending = 8,
        });

        _world = new SeededWorld(projectP, projectQ, inboundId, converterId, channelX, channelY, channelZ);
        return _world;
    }

    private async Task ResetChannelsAsync(SeededWorld world, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ChatChannels.Where(c => c.Id == world.ChannelX || c.Id == world.ChannelY)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.OutboundAgentProfile, (string?)null)
                .SetProperty(c => c.AgentId, world.InboundId)
                .SetProperty(c => c.DigestEnabled, false)
                .SetProperty(c => c.Enabled, true), ct);
        await db.ChatChannels.Where(c => c.Id == world.ChannelZ)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.OutboundAgentProfile, (string?)null)
                .SetProperty(c => c.AgentId, (Guid?)null), ct);
    }

    private async Task<Guid> SeedAgentAsync(
        string name, Guid projectId, AgentKind kind, CancellationToken ct, string? workspace = null)
    {
        var id = Guid.NewGuid();
        workspace ??= Path.Combine(Path.GetTempPath(), "antiphon-agent-" + id.ToString("N"));
        if (!workspace.Contains("antiphon-absent-", StringComparison.Ordinal))
            Directory.CreateDirectory(workspace);
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = NewAgent(id, name, projectId, workspace);
        agent.Kind = kind;
        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);
        return id;
    }

    private async Task MutateChannelAsync(Guid channelId, Action<ChatChannel> change, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = await db.ChatChannels.FirstAsync(c => c.Id == channelId, ct);
        change(channel);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ChatChannel> ReadChannelAsync(Guid channelId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChatChannels.AsNoTracking().FirstAsync(c => c.Id == channelId, ct);
    }

    private static Task<HttpResponseMessage> PatchAsync(
        HttpClient client, Guid channelId, object body, CancellationToken ct) =>
        client.PatchAsJsonAsync($"/api/channels/{channelId}", body, ct);

    private static Agent NewAgent(Guid id, string name, Guid projectId, string workspace)
    {
        var now = DateTime.UtcNow;
        return new Agent
        {
            Id = id,
            Name = name,
            Slug = name,
            WorkingDirectory = workspace,
            PoolProjectId = projectId,
            Kind = AgentKind.ClaudeCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static ChatChannel NewChannel(Guid id, string externalId, Guid? agentId)
    {
        var now = DateTime.UtcNow;
        return new ChatChannel
        {
            Id = id,
            Provider = "slack",
            ExternalId = externalId,
            Kind = ChatChannelKind.Group,
            Title = externalId,
            AgentId = agentId,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
