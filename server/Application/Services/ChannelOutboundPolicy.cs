using System.Security.Cryptography;
using System.Text;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0418: opt-in binding validation and trigger matching.</summary>
public sealed class ChannelOutboundPolicy(
    AppDbContext db,
    IOptions<ChannelOutboundSettings> settings)
{
    private readonly ChannelOutboundSettings _settings = settings.Value;

    public ChannelOutboundProfileSettings? TryGetProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return _settings.Profiles.TryGetValue(name, out var profile) ? profile : null;
    }

    public IReadOnlyDictionary<string, ChannelOutboundProfileSettings> AllProfiles => _settings.Profiles;

    public async Task ValidateBindingAsync(ChatChannel channel, string profileName, CancellationToken ct)
    {
        if (!channel.Enabled)
            throw new ValidationException("outboundAgentProfile", "The channel must be enabled before an outbound profile can be bound.");
        if (channel.AgentId is null)
            throw new ValidationException("outboundAgentProfile", "The channel must be bound to an inbound agent in the same project.");

        var profile = TryGetProfile(profileName)
            ?? throw new ValidationException("outboundAgentProfile", $"Unknown outbound profile '{profileName}'.");

        var inbound = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == channel.AgentId, ct)
            ?? throw new ValidationException("outboundAgentProfile", "The inbound agent no longer exists.");
        var inboundProject = await ProjectIdForAgentAsync(inbound, ct);
        if (inboundProject != profile.ProjectId)
            throw new ValidationException("outboundAgentProfile", "The inbound agent and the conversion profile must share a project.");

        var converter = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == profile.AgentId, ct)
            ?? throw new ValidationException("outboundAgentProfile", "The conversion agent no longer exists.");
        if (!AgentTaskService.DelegatableKinds.Contains(converter.Kind))
            throw new ValidationException("outboundAgentProfile", "The conversion agent must use a delegatable kind.");
        var converterProject = await ProjectIdForAgentAsync(converter, ct);
        if (converterProject != profile.ProjectId)
            throw new ValidationException("outboundAgentProfile", "The conversion agent must belong to the profile project.");
        if (string.IsNullOrWhiteSpace(converter.WorkingDirectory)
            || !Directory.Exists(converter.WorkingDirectory))
        {
            throw new ValidationException("outboundAgentProfile", "The conversion agent must have a usable dedicated workspace.");
        }

        var promptPath = ResolveContainedPrompt(converter.WorkingDirectory, profile.PromptFile);
        if (promptPath is null || !File.Exists(promptPath))
            throw new ValidationException("outboundAgentProfile", "The conversion prompt file is missing or escapes the workspace.");

        var inboundBound = await db.ChatChannels.AsNoTracking()
            .AnyAsync(c => c.AgentId == converter.Id, ct);
        if (inboundBound)
            throw new ValidationException("outboundAgentProfile", "The conversion agent must not be any channel's inbound agent.");
    }

    public async Task<ChannelOutboundProfilePreviewDto?> PreviewAsync(ChatChannel channel, CancellationToken ct)
    {
        var profile = TryGetProfile(channel.OutboundAgentProfile);
        if (profile is null)
            return null;
        var converter = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == profile.AgentId, ct);
        var promptPath = converter is null
            ? null
            : ResolveContainedPrompt(converter.WorkingDirectory, profile.PromptFile);
        var revision = promptPath is not null && File.Exists(promptPath)
            ? Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(promptPath, ct)))
            : "missing";
        return new ChannelOutboundProfilePreviewDto(
            profile.ProjectId,
            profile.AgentId,
            converter?.Name,
            revision,
            profile.Trigger.ToString(),
            profile.TimeoutSeconds,
            "Enabling this profile authorizes one metered worker invocation per matching reply.");
    }

    public static string? ResolveContainedPrompt(string workingDirectory, string promptFile)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(promptFile))
            return null;
        if (Path.IsPathRooted(promptFile) || promptFile.Contains("..", StringComparison.Ordinal))
            return null;
        var root = Path.GetFullPath(workingDirectory);
        var full = Path.GetFullPath(Path.Combine(root, promptFile));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }

    public bool MatchesTrigger(ChannelOutboundProfileSettings profile, ChannelReply reply, bool hasSourceManifest)
    {
        var hasMarkdownAttachment = reply.Attachments.Any(a =>
            (a.Name?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(a.Mime, "text/markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Mime, "text/x-markdown", StringComparison.OrdinalIgnoreCase));
        var hasSourceZip = reply.Attachments.Any(a =>
            a.Name?.EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase) == true);
        return profile.Trigger switch
        {
            ChannelOutboundTrigger.MarkdownSources => hasSourceManifest || hasMarkdownAttachment || hasSourceZip,
            ChannelOutboundTrigger.EveryAgentReply => !string.IsNullOrWhiteSpace(reply.Text) || hasMarkdownAttachment || hasSourceManifest,
            _ => false,
        };
    }

    public async Task<Guid?> ProjectIdForAgentAsync(Agent agent, CancellationToken ct)
    {
        if (agent.BoardId is not Guid boardId)
            return agent.PoolProjectId;
        return await db.Boards.AsNoTracking()
            .Where(b => b.Id == boardId)
            .Select(b => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct) ?? agent.PoolProjectId;
    }

    public static string PromptRevisionOrEmpty(string? promptPath)
    {
        if (promptPath is null || !File.Exists(promptPath))
            return "";
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(promptPath)));
    }
}