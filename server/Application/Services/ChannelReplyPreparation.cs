using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure turn description. Paths are retained as descriptors, never opened here.</summary>
public sealed record ChannelReplyBodyDescriptor(string OriginalResponse, string Text,
    IReadOnlyList<string> AttachmentPaths);

public sealed record ChannelReplyTaskDescriptor(Guid TaskId, string? BundleDirectory);

public sealed record ChannelReplyProfileDescriptor(string Name, Guid ProjectId, Guid AgentId,
    string? PromptWorkspace, string PromptFile, string Trigger, int TimeoutSeconds, int MaxPending);

/// <summary>Versioned, bounded intent. Attachment bytes belong to the later file snapshot.</summary>
public sealed record ChannelReplyCapture(int Version, ChannelReply Route,
    ChannelReplyBodyDescriptor Body, IReadOnlyList<Guid> MemberIds,
    IReadOnlyList<ChannelReplyTaskDescriptor> Tasks, ChannelReplyProfileDescriptor? Profile,
    long MaxAttachmentBytes)
{
    public IReadOnlyList<string> AttachmentRoots { get; init; } = [];
}

public sealed record ChannelReplyPrepared(ChannelReply Reply, string PromptText,
    string PromptRevision, string? SourceManifestJson);

/// <summary>
/// S2 preparation primitive, deliberately not registered or invoked by live dispatch yet.
/// The materialization pump owns staging, retries and state transitions in S3.
/// </summary>
public sealed class ChannelReplyPreparation(IChannelReplyAttachmentReader reader)
{
    public const int MaxCaptureBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ChannelReplyBodyDescriptor Describe(string response, IReadOnlyList<string>? extraPaths = null)
    {
        var clean = ChannelPromptCorrelation.RemoveMarkers(response);
        var (text, explicitPaths) = ChannelContracts.ExtractAttachments(clean);
        var paths = explicitPaths.Concat(extraPaths ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Preserve the existing no-attachment behavior, including the full response text.
        return new(response, paths.Length == 0 ? clean : text, paths);
    }

    public static string Serialize(ChannelReplyCapture capture)
    {
        var json = JsonSerializer.Serialize(capture, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxCaptureBytes)
            throw new InvalidDataException("The outbound capture exceeds the descriptor budget.");
        return json;
    }

    public static ChannelReplyCapture Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxCaptureBytes)
            throw new InvalidDataException("The outbound capture exceeds the descriptor budget.");
        var capture = JsonSerializer.Deserialize<ChannelReplyCapture>(json, JsonOptions);
        if (capture is not { Version: 1, Route: not null, Body: not null,
                MemberIds: not null, Tasks: not null, MaxAttachmentBytes: > 0 }
            || capture.Body.AttachmentPaths is null || capture.Body.OriginalResponse is null
            || capture.Body.AttachmentPaths.Count > 64 || capture.AttachmentRoots is null || capture.AttachmentRoots.Count > 64
            || capture.Body.Text is null || capture.Route.Attachments is null || capture.Route.Attachments.Count != 0
            || string.IsNullOrWhiteSpace(capture.Route.Channel)
            || string.IsNullOrWhiteSpace(capture.Route.ConversationId))
            throw new InvalidDataException("The outbound capture has an unsupported or malformed descriptor.");
        return capture;
    }

    public async Task<ChannelReplyPrepared> PrepareAsync(ChannelOutboundDelivery delivery, CancellationToken ct,
        bool prepareConversion = true)
    {
        var capture = Deserialize(delivery.CaptureJson
            ?? throw new InvalidDataException("An original outbound capture is required."));
        var budget = capture.MaxAttachmentBytes;
        var attachments = new List<OutboundAttachment>();
        var notes = new List<string>();
        foreach (var path in capture.Body.AttachmentPaths)
        {
            // Read failures remain preparation failures; the pump, not extraction, owns retries.
            byte[] bytes;
            try { bytes = await reader.ReadAttachmentAsync(path, capture.AttachmentRoots, budget, ct); }
            catch (ChannelReplyFileTooLargeException ex)
            {
                notes.Add($"⚠️ attachment skipped — {Path.GetFileName(path)} is {ex.Length / (1024 * 1024)} MB, over the {capture.MaxAttachmentBytes / (1024 * 1024)} MB limit");
                continue;
            }
            attachments.Add(new OutboundAttachment { Kind = ChannelReplyDispatcher.InferAttachmentKind(Path.GetExtension(path)),
                Source = path, Content = bytes, Name = Path.GetFileName(path),
                Mime = ChannelReplyDispatcher.InferMime(Path.GetExtension(path)) });
            budget -= bytes.LongLength;
        }
        var text = capture.Body.Text;
        if (notes.Count > 0)
            text = text.Length == 0 ? string.Join("\n", notes) : text + "\n\n" + string.Join("\n", notes);
        var bundle = capture.Tasks.FirstOrDefault(t => t.TaskId == delivery.SourceTaskId)?.BundleDirectory;
        var manifest = bundle is null ? null
            : await reader.ReadTextAsync(Path.Combine(bundle, DeliverableBundleService.SourceManifestName), [bundle], MaxCaptureBytes, ct);
        var reply = capture.Route with { Text = text, Attachments = attachments };
        var prompt = "";
        if (prepareConversion && capture.Profile is { } profile
            && (profile.Trigger == "EveryAgentReply" || ChannelOutboundService.MatchesMarkdownSources(reply, manifest)))
        {
            // Workspace/path authorization may inspect the filesystem, so it belongs here,
            // after capture, never in descriptor extraction or the admission transaction.
            if (!ChatChannelService.TryGetPromptPath(new Agent { WorkingDirectory = profile.PromptWorkspace ?? "" },
                    profile.PromptFile, out var promptPath))
                throw new InvalidDataException("The captured conversion prompt is outside its workspace.");
            prompt = await reader.ReadTextAsync(promptPath, [profile.PromptWorkspace!], MaxCaptureBytes, ct);
        }
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
        return new(reply, prompt, revision, manifest);
    }
}
