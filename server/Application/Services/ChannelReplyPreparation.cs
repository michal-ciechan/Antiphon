using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure turn description. Paths are retained as descriptors, never opened here.</summary>
public sealed record ChannelReplyBodyDescriptor(string OriginalResponse, string Text,
    IReadOnlyList<string> AttachmentPaths)
{
    public IReadOnlyList<Guid> BundleTaskIds { get; init; } = [];
    public bool RequiresAttachment { get; init; }
    public int? MaxTextChars { get; init; }
}

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
/// Preparation runs only after capture. The materialization pump owns staging and retries.
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
            || capture.Body.BundleTaskIds is null || capture.Body.BundleTaskIds.Count > 64
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
        var paths = capture.Body.AttachmentPaths.ToList();
        string? manifest = null;
        foreach (var task in capture.Tasks.Where(t => t.BundleDirectory is not null
            && (t.TaskId == delivery.SourceTaskId || capture.Body.BundleTaskIds.Contains(t.TaskId))))
        {
            string? taskManifest;
            try
            {
                taskManifest = await reader.ReadTextAsync(
                    Path.Combine(task.BundleDirectory!, DeliverableBundleService.SourceManifestName),
                    [task.BundleDirectory!], MaxCaptureBytes, ct);
            }
            catch (FileNotFoundException) { taskManifest = null; }
            if (task.TaskId == delivery.SourceTaskId) manifest = taskManifest;
            if (!capture.Body.BundleTaskIds.Contains(task.TaskId)) continue;
            if (taskManifest is not null)
            {
                var sources = JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(taskManifest, JsonOptions);
                if (sources is not { Version: 1, Sources: not null })
                    throw new InvalidDataException("The captured bundle source manifest is invalid.");
                paths.AddRange(sources.Sources.Select(s => s.StoredFile).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(DeliverableBundleService.IsSafeStoredSourceName)
                    .Select(name => Path.Combine(task.BundleDirectory!, name)).Take(65));
            }
            else
            {
                // Legacy bundles retain the existing restricted extension fallback. All byte reads
                // still go through the captured-root/regular-file reader below.
                paths.AddRange(Directory.EnumerateFiles(task.BundleDirectory!)
                    .Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(p).EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(65));
            }
        }
        if (paths.Count > 64)
            throw new InvalidDataException("The captured bundle exceeds the attachment descriptor budget.");
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
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
        if (capture.Body.RequiresAttachment && attachments.Count == 0 && notes.Count == 0)
            throw new InvalidDataException("The machine origin requires an attachment; no source bytes were prepared.");
        if (capture.Body.MaxTextChars is > 0 and var max && text.Length > max)
            text = text[..max] + "…";
        var reply = capture.Route with { Text = text.Length == 0 ? null : text, Attachments = attachments };
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
