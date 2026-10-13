using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Infrastructure.Files;

/// <summary>Preserves the full report; never copies raw worktree evidence.</summary>
public sealed class CardDoneArtifactPreservation(IAgentReportStore reports)
{
    // C1032_AdmissionArtifactSurvival: verify existing external bytes without copying or
    // opening remote paths. ChannelReplyAttachmentReader retains no-follow regular-file handles.
    public async Task<(bool Preserved, string Digest, IReadOnlyList<string> Pointers)> VerifyMirrorAsync(
        AgentTask task, IReadOnlyList<string> roots, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(task.Result) || task.ResultFilePath is null) return (false, "", []);
            var pointers = new[] { task.ResultFilePath, task.DeliverablePath, task.DeliverablePdfPath, task.DeliverableBundleDir }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().ToArray();
            var reader = new ChannelReplyAttachmentReader();
            var facts = new List<string>();
            foreach (var pointer in pointers)
            {
                if (!Outside(pointer, roots) || roots.Any(root => pointer.StartsWith(root + ".cleanup-", StringComparison.Ordinal)))
                    return (false, "", []);
                var bytes = await reader.ReadAttachmentAsync(pointer, [Path.GetDirectoryName(pointer)!], 20 * 1024 * 1024, ct);
                if (pointer == task.ResultFilePath && !bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(task.Result)))
                    return (false, "", []);
                facts.Add(pointer + "\n" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }
            return (true, Digest(string.Join("\n", facts)), pointers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or ChannelReplyFileTooLargeException) { return (false, "", []); }
    }

    public async Task<string?> EnsureAsync(AgentTask task, IReadOnlyList<string> roots, bool missingReviewed, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Result)) return missingReviewed && await CheckDeliverablesAsync(task, roots, ct) ? null : "artifact_unpreserved";
        // A recorded artifact is evidence, not permission to silently repair or relocate it.
        if (task.ResultFilePath is { } existing && (!Outside(existing, roots)
            || !await reports.IsUsableAsync(existing, task.Result, ct))) return "artifact_unpreserved";
        var stored = await reports.StoreAsync(task, ct);
        if (!stored.Succeeded || !Outside(stored.Path!, roots)
            || !await reports.IsUsableAsync(stored.Path, task.Result, ct)) return "artifact_unpreserved";
        if (!await CheckDeliverablesAsync(task, roots, ct)) return "artifact_unpreserved";
        task.ResultFilePath = stored.Path;
        return null;
    }

    public async Task<bool> VerifyAsync(AgentTask task, IReadOnlyList<string> roots, bool missingReviewed, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Result)) return missingReviewed && await CheckDeliverablesAsync(task, roots, ct);
        return task.ResultFilePath is { } path && Outside(path, roots)
            && await reports.IsUsableAsync(path, task.Result, ct)
            && await CheckDeliverablesAsync(task, roots, ct);
    }

    private static async Task<bool> CheckDeliverablesAsync(AgentTask task, IReadOnlyList<string> roots, CancellationToken ct)
    {
        try
        {
            var pointers = new[] { task.DeliverablePath, task.DeliverablePdfPath, task.DeliverableBundleDir };
            foreach (var pointer in pointers.Where(p => !string.IsNullOrWhiteSpace(p)))
                if (!Outside(pointer!, roots) || !await ReadableAsync(pointer!, ct)) return false;
            // An author's task report can contain detail absent from the canonical Result.
            // Distinct bytes need an existing external deliverable, never a retention copy.
            foreach (var root in roots)
            foreach (var id in new[] { task.Id.ToString("D"), task.Id.ToString("N"), task.Id.ToString("N")[..8] })
            {
                var copy = Path.Combine(root, ".antiphon", "task-" + id + ".md");
                if (!File.Exists(copy)) continue;
                if (!NoLinks(copy)) return false;
                var bytes = await File.ReadAllBytesAsync(copy, ct);
                if (bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(task.Result ?? ""))) continue;
                var preserved = false;
                foreach (var pointer in pointers.Where(p => p is not null && File.Exists(p)))
                {
                    var saved = await File.ReadAllBytesAsync(pointer!, ct);
                    if (bytes.AsSpan().SequenceEqual(saved)) { preserved = true; break; }
                }
                if (!preserved) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static string Digest(string? value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));

    private static async Task<bool> ReadableAsync(string path, CancellationToken ct)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                if (!await ReadableAsync(entry, ct)) return false;
            return true;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.CopyToAsync(Stream.Null, ct);
        return true;
    }

    private static bool Outside(string path, IReadOnlyList<string> roots)
    {
        try
        {
        if (!Path.IsPathFullyQualified(path) || !NoLinks(path)) return false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return roots.All(root => !full.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), comparison)
            && !full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparison));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static bool NoLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return false; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return true;
    }
}
