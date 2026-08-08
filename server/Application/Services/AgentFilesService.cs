using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The data source for the agent Files review surface: merges (1) git working-tree changes vs a
/// baseline (HEAD, the latest "work completed" checkpoint, or an explicit commit), (2) files the
/// agent touched via Write/Edit tool calls in its session transcript, and (3) per-file
/// viewed/reviewed marks (hash-anchored — any content change makes a mark stale).
/// Content reads are workspace-rooted; the only paths served from outside the workspace are ones
/// the agent itself touched (they're listed flagged as external).
/// </summary>
public sealed class AgentFilesService
{
    private const long MaxContentBytes = 2 * 1024 * 1024;
    private const int MaxTreePaths = 20_000;
    private static readonly string[] FileToolNames = ["Write", "Edit", "NotebookEdit"];
    private static readonly string[] TreeWalkExcludedDirs =
        [".git", "node_modules", "bin", "obj", "dist", ".vite", ".next", "coverage", "__pycache__"];

    private readonly AppDbContext _db;
    private readonly GitWorkspaceService _git;
    private readonly AgentReviewCheckpointService _checkpoints;
    private readonly ILogger<AgentFilesService> _logger;

    public AgentFilesService(
        AppDbContext db,
        GitWorkspaceService git,
        AgentReviewCheckpointService checkpoints,
        ILogger<AgentFilesService> logger)
    {
        _db = db;
        _git = git;
        _checkpoints = checkpoints;
        _logger = logger;
    }

    private sealed record Baseline(string Kind, string? CommitSha, string? Reason, DateTime? At, bool TimestampFallback)
    {
        public static readonly Baseline Head = new("head", null, null, null, false);
    }

    /// <summary>
    /// Resolve the requested baseline ("head", "checkpoint", or a commit sha) to something git
    /// can diff against. A checkpoint whose commit is still in HEAD's history diffs against that
    /// commit; otherwise (rewritten history, or no sha captured) it falls back to the newest
    /// commit at or before the checkpoint's timestamp. Anything unresolvable degrades to HEAD.
    /// </summary>
    private async Task<Baseline> ResolveBaselineAsync(Guid agentId, string root, bool isRepo, string? since, CancellationToken ct)
    {
        if (!isRepo || string.IsNullOrWhiteSpace(since) || since.Equals("head", StringComparison.OrdinalIgnoreCase))
            return Baseline.Head;

        if (!since.Equals("checkpoint", StringComparison.OrdinalIgnoreCase))
        {
            return await _git.IsInHistoryAsync(root, since, ct)
                ? new Baseline("commit", since, null, null, false)
                : Baseline.Head;
        }

        var checkpoint = await _checkpoints.GetLatestAsync(agentId, ct);
        if (checkpoint is null)
            return Baseline.Head;

        if (checkpoint.CommitSha is not null && await _git.IsInHistoryAsync(root, checkpoint.CommitSha, ct))
            return new Baseline("checkpoint", checkpoint.CommitSha, checkpoint.Reason, checkpoint.CreatedAt, false);

        var fallback = await _git.GetLastCommitBeforeAsync(root, checkpoint.CreatedAt, ct);
        return fallback is null
            ? Baseline.Head
            : new Baseline("checkpoint", fallback, checkpoint.Reason, checkpoint.CreatedAt, true);
    }

    public async Task<AgentFilesDto?> GetFilesAsync(Guid agentId, string? since, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);

        var isRepo = await _git.IsRepositoryAsync(root, ct);
        var baseline = await ResolveBaselineAsync(agentId, root, isRepo, since, ct);
        var gitChanges =
            !isRepo ? []
            : baseline.CommitSha is null
                ? await _git.GetChangesAsync(root, ct)
                : await _git.GetChangesSinceAsync(root, baseline.CommitSha, ct);
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
            files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            new FilesBaselineDto(
                baseline.Kind, baseline.CommitSha, baseline.Reason, baseline.At, baseline.TimestampFallback));
    }

    /// <summary>Recent workspace commits for the baseline picker, checkpoint commit flagged.</summary>
    public async Task<WorkspaceCommitsDto?> GetCommitsAsync(Guid agentId, int limit, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);

        var commits = await _git.GetRecentCommitsAsync(root, Math.Clamp(limit, 1, 200), ct);
        var isRepo = commits.Count > 0 || await _git.IsRepositoryAsync(root, ct);
        var baseline = await ResolveBaselineAsync(agentId, root, isRepo, "checkpoint", ct);
        return new WorkspaceCommitsDto(commits
            .Select(c => new WorkspaceCommitDto(
                c.Sha, c.ShortSha, c.Author, c.Date, c.Subject,
                string.Equals(c.Sha, baseline.CommitSha, StringComparison.OrdinalIgnoreCase)))
            .ToList());
    }

    /// <summary>
    /// Every file in the workspace for the "show all files" toggle. Repos list via git (tracked +
    /// untracked-not-ignored, so node_modules stays out for free); non-repos walk the filesystem
    /// skipping well-known junk dirs. Capped — the client shows a truncation notice.
    /// </summary>
    public async Task<WorkspaceTreeDto?> GetTreeAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);

        List<string> paths;
        if (await _git.IsRepositoryAsync(root, ct))
        {
            paths = (await _git.ListFilesAsync(root, ct)).ToList();
        }
        else
        {
            paths = [];
            try
            {
                WalkTree(root, root, paths);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Workspace tree walk failed in {Root}", root);
            }
        }

        var truncated = paths.Count > MaxTreePaths;
        return new WorkspaceTreeDto(
            (truncated ? paths.Take(MaxTreePaths) : paths)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
            truncated);
    }

    /// <summary>
    /// What adding <paramref name="pattern"/> to .gitignore would hide, without writing anything.
    /// The dialog shows this before the user commits to the line.
    /// </summary>
    public async Task<IgnorePreviewDto?> PreviewIgnoreAsync(
        Guid agentId, IgnorePreviewRequest request, CancellationToken ct)
    {
        var root = await ResolveRepoWorkspaceAsync(agentId, ct);
        if (root is null)
            return null;

        var sets = await _git.PreviewIgnoreAsync(root, request.Pattern, ct);
        var limit = Math.Clamp(request.Limit, 1, 1000);
        var ordered = sets.Disappears.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        return new IgnorePreviewDto(
            request.Pattern.Trim(),
            ordered.Take(limit).ToList(),
            ordered.Count > limit,
            sets.TrackedMatches.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(limit).ToList());
    }

    /// <summary>
    /// Appends the pattern to the workspace .gitignore. Reports how many paths actually left the
    /// listing, measured before the write, so the caller can confirm what happened.
    /// </summary>
    public async Task<AddIgnoreResultDto?> AddIgnoreAsync(
        Guid agentId, AddIgnoreRequest request, CancellationToken ct)
    {
        var root = await ResolveRepoWorkspaceAsync(agentId, ct);
        if (root is null)
            return null;

        var pattern = request.Pattern.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["pattern"] = ["An ignore pattern is required."],
            });

        var sets = await _git.PreviewIgnoreAsync(root, pattern, ct);
        var gitignore = await _git.AppendIgnoreAsync(root, pattern, ct);
        if (gitignore is null)
            return null;

        _logger.LogInformation(
            "Agent {AgentId}: ignored {Pattern} ({Count} path(s) leave the file view)",
            agentId, pattern, sets.Disappears.Count);
        return new AddIgnoreResultDto(pattern, gitignore, sets.Disappears.Count);
    }

    /// <summary>The agent's workspace, but only when it is a git repository (gitignore needs one).</summary>
    private async Task<string?> ResolveRepoWorkspaceAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory))
            return null;

        var root = Path.GetFullPath(agent.WorkingDirectory);
        return await _git.IsRepositoryAsync(root, ct) ? root : null;
    }

    private static void WalkTree(string root, string dir, List<string> paths)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (paths.Count > MaxTreePaths)
                return;
            paths.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (TreeWalkExcludedDirs.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                continue;
            if (paths.Count > MaxTreePaths)
                return;
            WalkTree(root, sub, paths);
        }
    }

    public async Task<AgentFileContentDto?> GetContentAsync(
        Guid agentId, string path, string rev, string? since, CancellationToken ct)
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
            var listing = await GetFilesAsync(agentId, since, ct);
            var normalized = Normalize(path);
            var listed = listing?.Files.Any(f =>
                f.External && string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)) ?? false;
            if (!listed)
                return null;
        }

        if (string.Equals(rev, "head", StringComparison.OrdinalIgnoreCase))
        {
            // "head" means "the baseline side of the diff" — with a since selection it serves the
            // file as of the resolved baseline commit rather than literal HEAD.
            if (external)
                return new AgentFileContentDto(path, "head", null, false, true);
            var isRepo = await _git.IsRepositoryAsync(root, ct);
            var baseline = await ResolveBaselineAsync(agentId, root, isRepo, since, ct);
            var text = baseline.CommitSha is null
                ? await _git.GetHeadContentAsync(root, Normalize(path), ct)
                : await _git.GetContentAtAsync(root, Normalize(path), baseline.CommitSha, ct);
            return new AgentFileContentDto(path, "head", text, false, text is null);
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

    public async Task<string?> GetDiffAsync(Guid agentId, string path, string? since, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || string.IsNullOrWhiteSpace(agent.WorkingDirectory) || Path.IsPathRooted(path))
            return null;
        var root = Path.GetFullPath(agent.WorkingDirectory);
        var baseline = await ResolveBaselineAsync(agentId, root, await _git.IsRepositoryAsync(root, ct), since, ct);
        return await _git.GetDiffAsync(root, Normalize(path), ct, baseline.CommitSha ?? "HEAD");
    }

    /// <summary>
    /// Upsert viewed/reviewed marks. Explicit paths, or every current file under a folder prefix
    /// (the right-click "mark all as viewed" flow). Level null clears the mark.
    /// </summary>
    public async Task<int> MarkAsync(
        Guid agentId, IReadOnlyList<string>? paths, string? prefix, FileReviewLevel? level, string? since,
        CancellationToken ct)
    {
        var listing = await GetFilesAsync(agentId, since, ct);
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

    /// <summary>Section marks for one file — the client compares hashes to derive fresh/stale.</summary>
    public async Task<IReadOnlyList<SectionReviewDto>?> GetSectionReviewsAsync(
        Guid agentId, string path, CancellationToken ct)
    {
        if (!await _db.Agents.AnyAsync(a => a.Id == agentId, ct))
            return null;

        var normalized = path.Replace('\\', '/');
        return await _db.FileSectionReviews.AsNoTracking()
            .Where(s => s.AgentId == agentId && s.Path == normalized)
            .OrderBy(s => s.SectionKey)
            .Select(s => new SectionReviewDto(s.SectionKey, s.ContentHash, s.UpdatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Batch upsert/clear of section marks for one file — one round-trip for "mark this subtree"
    /// and "mark all sections". A null hash clears that section's mark. Hashes and keys are
    /// client-derived and opaque here (see <see cref="FileSectionReview"/>).
    /// </summary>
    public async Task<int> MarkSectionsAsync(
        Guid agentId, MarkSectionReviewsRequest request, CancellationToken ct)
    {
        if (request.Sections.Count == 0 || !await _db.Agents.AnyAsync(a => a.Id == agentId, ct))
            return 0;

        var normalized = request.Path.Replace('\\', '/');
        var existing = await _db.FileSectionReviews
            .Where(s => s.AgentId == agentId && s.Path == normalized)
            .ToDictionaryAsync(s => s.SectionKey, StringComparer.Ordinal, ct);

        var now = DateTime.UtcNow;
        var touched = 0;
        foreach (var section in request.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Key))
                continue;
            existing.TryGetValue(section.Key, out var row);
            if (section.ContentHash is null)
            {
                if (row is not null)
                {
                    _db.FileSectionReviews.Remove(row);
                    touched++;
                }
                continue;
            }
            if (row is null)
            {
                row = new FileSectionReview
                {
                    Id = Guid.NewGuid(),
                    AgentId = agentId,
                    Path = normalized,
                    SectionKey = section.Key,
                };
                _db.FileSectionReviews.Add(row);
            }
            row.ContentHash = section.ContentHash;
            row.UpdatedAt = now;
            touched++;
        }
        await _db.SaveChangesAsync(ct);
        return touched;
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
