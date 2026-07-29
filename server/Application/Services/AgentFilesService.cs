using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The data source for the agent Files review surface: merges (1) git working-tree changes vs
/// HEAD, (2) files the agent touched via Write/Edit tool calls in its session transcript, and
/// (3) per-file viewed/reviewed marks (hash-anchored — any content change makes a mark stale).
/// Content reads are workspace-rooted; the only paths served from outside the workspace are ones
/// the agent itself touched (they're listed flagged as external).
/// </summary>
public sealed class AgentFilesService
{
    private const long MaxContentBytes = 2 * 1024 * 1024;
    private static readonly string[] FileToolNames = ["Write", "Edit", "NotebookEdit"];

    private readonly AppDbContext _db;
    private readonly GitWorkspaceService _git;
    private readonly ILogger<AgentFilesService> _logger;

    public AgentFilesService(AppDbContext db, GitWorkspaceService git, ILogger<AgentFilesService> logger)
    {
        _db = db;
        _git = git;
        _logger = logger;
    }

    public async Task<AgentFilesDto?> GetFilesAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);

        var isRepo = await _git.IsRepositoryAsync(root, ct);
        var gitChanges = isRepo ? await _git.GetChangesAsync(root, ct) : [];
        var activity = await GetAgentActivityAsync(agent, root, ct);
        var reviews = await _db.FileReviewStates.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .ToDictionaryAsync(r => r.Path, ct);

        // Union: everything git says changed + everything the agent touched. A git change with a
        // ROOTED path is a repo change outside a subdirectory workspace — listed as external.
        var byPath = new Dictionary<string, AgentFileDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in gitChanges)
        {
            var rel = Normalize(change.Path);
            var external = Path.IsPathRooted(rel);
            byPath[rel] = new AgentFileDto(
                rel, change.Status.ToString(), External: external,
                AgentEdits: 0, LastAgentEditAt: null,
                ContentHash: null, ReviewLevel: null, ReviewStale: false,
                SizeBytes: null, IsMarkdown: IsMarkdown(rel));
        }
        foreach (var (path, info) in activity)
        {
            if (byPath.TryGetValue(path, out var existing))
                byPath[path] = existing with { AgentEdits = info.Edits, LastAgentEditAt = info.LastEditAt };
            else
                byPath[path] = new AgentFileDto(
                    path, GitFileStatus.None.ToString(), External: info.External,
                    AgentEdits: info.Edits, LastAgentEditAt: info.LastEditAt,
                    ContentHash: null, ReviewLevel: null, ReviewStale: false,
                    SizeBytes: null, IsMarkdown: IsMarkdown(path));
        }

        // Hashes + review staleness (deleted files have no content to hash).
        var files = new List<AgentFileDto>(byPath.Count);
        foreach (var dto in byPath.Values)
        {
            var abs = Resolve(root, dto.Path, dto.External);
            string? hash = null;
            long? size = null;
            if (abs is not null && File.Exists(abs))
            {
                try
                {
                    var info = new FileInfo(abs);
                    size = info.Length;
                    hash = HashFile(abs);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Hashing {Path} failed", abs);
                }
            }

            string? level = null;
            var stale = false;
            if (reviews.TryGetValue(dto.Path, out var review))
            {
                level = review.Level.ToString();
                stale = hash is null || !string.Equals(review.ContentHash, hash, StringComparison.Ordinal);
            }

            files.Add(dto with { ContentHash = hash, SizeBytes = size, ReviewLevel = level, ReviewStale = stale });
        }

        return new AgentFilesDto(
            agentId,
            root,
            isRepo,
            files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<AgentFileContentDto?> GetContentAsync(
        Guid agentId, string path, string rev, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);

        var external = Path.IsPathRooted(path);
        if (external)
        {
            // Absolute paths are served ONLY when they appear in the agent's file listing —
            // agent-touched files or repo changes outside a subdirectory workspace.
            var listing = await GetFilesAsync(agentId, ct);
            var normalized = Normalize(path);
            var listed = listing?.Files.Any(f =>
                f.External && string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)) ?? false;
            if (!listed)
                return null;
        }

        if (string.Equals(rev, "head", StringComparison.OrdinalIgnoreCase))
        {
            if (external)
                return new AgentFileContentDto(path, "head", null, false, true);
            var head = await _git.GetHeadContentAsync(root, Normalize(path), ct);
            return new AgentFileContentDto(path, "head", head, false, head is null);
        }

        var abs = external ? Normalize(path) : Resolve(root, path, external: false);
        if (abs is null || !File.Exists(abs))
            return new AgentFileContentDto(path, "work", null, false, true);

        var fileInfo = new FileInfo(abs);
        var truncated = fileInfo.Length > MaxContentBytes;
        var bytes = truncated
            ? await ReadPrefixAsync(abs, MaxContentBytes, ct)
            : await File.ReadAllBytesAsync(abs, ct);
        if (LooksBinary(bytes))
            return new AgentFileContentDto(path, "work", null, false, false, IsBinary: true);
        return new AgentFileContentDto(path, "work", Encoding.UTF8.GetString(bytes), truncated, false);
    }

    public async Task<string?> GetDiffAsync(Guid agentId, string path, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory) || Path.IsPathRooted(path))
            return null;
        return await _git.GetDiffAsync(Path.GetFullPath(agent.WorkingDirectory), Normalize(path), ct);
    }

    /// <summary>
    /// Upsert viewed/reviewed marks. Explicit paths, or every current file under a folder prefix
    /// (the right-click "mark all as viewed" flow). Level null clears the mark.
    /// </summary>
    public async Task<int> MarkAsync(
        Guid agentId, IReadOnlyList<string>? paths, string? prefix, FileReviewLevel? level, CancellationToken ct)
    {
        var listing = await GetFilesAsync(agentId, ct);
        if (listing is null)
            return 0;

        var targets = listing.Files
            .Where(f => paths is { Count: > 0 }
                ? paths.Contains(f.Path, StringComparer.OrdinalIgnoreCase)
                : prefix is not null
                    && (prefix.Length == 0
                        || f.Path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(f.Path, prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (targets.Count == 0)
            return 0;

        var existing = await _db.FileReviewStates
            .Where(r => r.AgentId == agentId)
            .ToDictionaryAsync(r => r.Path, ct);

        var now = DateTime.UtcNow;
        foreach (var target in targets)
        {
            existing.TryGetValue(target.Path, out var row);
            if (level is null)
            {
                if (row is not null)
                    _db.FileReviewStates.Remove(row);
                continue;
            }
            if (row is null)
            {
                row = new FileReviewState { Id = Guid.NewGuid(), AgentId = agentId, Path = target.Path };
                _db.FileReviewStates.Add(row);
            }
            row.ContentHash = target.ContentHash ?? "";
            row.Level = level.Value;
            row.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return targets.Count;
    }

    private sealed record ActivityInfo(int Edits, DateTime LastEditAt, bool External);

    private async Task<Dictionary<string, ActivityInfo>> GetAgentActivityAsync(
        Agent agent, string root, CancellationToken ct)
    {
        var result = new Dictionary<string, ActivityInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return result;

        var calls = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.ToolCall
                && t.ToolName != null && FileToolNames.Contains(t.ToolName)
                && t.ToolInput != null)
            .Select(t => new { t.ToolInput, t.CreatedAt })
            .ToListAsync(ct);

        foreach (var call in calls)
        {
            string? filePath = null;
            try
            {
                using var doc = JsonDocument.Parse(call.ToolInput!);
                if (doc.RootElement.TryGetProperty("file_path", out var fp))
                    filePath = fp.GetString();
                else if (doc.RootElement.TryGetProperty("notebook_path", out var np))
                    filePath = np.GetString();
            }
            catch (JsonException)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(filePath))
                continue;

            var full = Normalize(Path.GetFullPath(filePath));
            var normalizedRoot = Normalize(root).TrimEnd('/') + "/";
            var external = !full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            var key = external ? full : full[normalizedRoot.Length..];

            result.TryGetValue(key, out var prior);
            result[key] = new ActivityInfo(
                (prior?.Edits ?? 0) + 1,
                call.CreatedAt > (prior?.LastEditAt ?? DateTime.MinValue) ? call.CreatedAt : prior!.LastEditAt,
                external);
        }
        return result;
    }

    private static string? Resolve(string root, string relativeOrAbsolute, bool external)
    {
        if (external)
            return Path.IsPathRooted(relativeOrAbsolute) ? relativeOrAbsolute : null;
        var combined = Path.GetFullPath(Path.Combine(root, relativeOrAbsolute));
        // Traversal guard: a relative path must stay inside the workspace.
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        return Convert.ToHexString(SHA256.HashData(stream))[..16].ToLowerInvariant();
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var probe = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0)
                return true;
        return false;
    }

    private static async Task<byte[]> ReadPrefixAsync(string path, long count, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, (int)(count - read)), ct);
            if (n == 0) break;
            read += n;
        }
        return buffer.AsSpan(0, read).ToArray();
    }
}
