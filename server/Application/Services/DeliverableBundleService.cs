using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// At settlement, a document-producing task gets byte-preserving source copies (or a zip)
/// under <c>&lt;repo&gt;\.antiphon\deliverables\&lt;taskShort&gt;\</c>. Never throws into settlement.
/// </summary>
public sealed class DeliverableBundleService
{
    public const string SourceManifestName = "source-manifest.json";
    public const long MaxInlineSourceBytes = 1024 * 1024;

    private static readonly Regex NamedDocPattern = new(
        "`?(?<path>docs/[\\w./-]+\\.md)`?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GitWorkspaceService _git;
    private readonly DeliverablesSettings _settings;
    private readonly ILogger<DeliverableBundleService> _logger;

    public DeliverableBundleService(
        GitWorkspaceService git,
        IOptions<DeliverablesSettings> settings,
        ILogger<DeliverableBundleService> logger)
    {
        _git = git;
        _settings = settings.Value;
        _logger = logger;
    }

    public readonly record struct BundledDocument(string RepoRelativePath, byte[] Bytes);
    public sealed record SourceMember(string OriginalRelativePath, string StoredFile, string? ZipEntry,
        long Length, string Sha256);
    public sealed record OmittedSource(string OriginalRelativePath, long Length, string Reason);
    public sealed record SourceManifest(int Version, bool Complete, IReadOnlyList<SourceMember> Sources,
        IReadOnlyList<OmittedSource> Omitted);

    public async Task TryBuildAsync(
        AgentTask task,
        string report,
        AppDbContext? db,
        CancellationToken ct)
    {
        if (!_settings.Enabled)
            return;
        if (task.Status != AgentTaskStatus.Succeeded)
            return;

        try
        {
            await BuildCoreAsync(task, report, db, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not build a deliverable bundle for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// Only recorded source files are implicit. Legacy bundles have a restricted extension fallback.
    /// </summary>
    public static IReadOnlyList<string> ListAttachableFiles(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir)
            || !Directory.Exists(task.DeliverableBundleDir))
            return [];

        var dir = task.DeliverableBundleDir;
        var manifestPath = Path.Combine(dir, SourceManifestName);
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<SourceManifest>(File.ReadAllText(manifestPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (manifest is null || manifest.Version != 1 || manifest.Sources is null)
                    return [];
                return manifest.Sources.Select(s => s.StoredFile)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(IsSafeStoredSourceName)
                    .Select(name => Path.Combine(dir, name))
                    .Where(File.Exists)
                    .ToArray();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        return Directory.EnumerateFiles(dir)
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? FormatNoteBit(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir))
            return null;
        var md = task.DeliverableFileCount;
        var manifestPath = Path.Combine(task.DeliverableBundleDir, SourceManifestName);
        if (!File.Exists(manifestPath))
            return $"{md} md";
        try
        {
            var manifest = JsonSerializer.Deserialize<SourceManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var zipped = manifest?.Sources.Any(s => s.ZipEntry is not null) == true;
            var suffix = zipped ? ", sources zip" : "";
            if (manifest?.Complete == false)
                suffix += ", sources incomplete";
            return $"{md} md{suffix}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"{md} md, source manifest unavailable";
        }
    }

    private static bool IsSafeStoredSourceName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && !name.Contains("..", StringComparison.Ordinal)
        && (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase));

    private async Task BuildCoreAsync(
        AgentTask task,
        string report,
        AppDbContext? db,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        var (documents, omitted) = await CollectDocumentsAsync(task, report, log, ct);
        if (documents.Count == 0 && omitted.Count == 0)
            return;

        var root = FirstNonEmpty(task.RepoPath, task.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            log.AppendLine("no RepoPath or WorkingDirectory; skipped");
            return;
        }

        var shortId = DelegationReportFormatter.Short(task.Id);
        var bundleDir = Path.Combine(root, ".antiphon", "deliverables", shortId);
        Directory.CreateDirectory(bundleDir);

        var identifier = db is null ? null : await CardIdentifierAsync(db, task, ct);
        var firstPath = documents.Count > 0 ? documents[0].RepoRelativePath : omitted[0].OriginalRelativePath;
        var stem = $"{CoverStem(identifier, shortId)}-{Slug(firstPath)}";
        var sources = await WriteSourcesAsync(documents, bundleDir, stem, ct);
        var manifest = new SourceManifest(1, omitted.Count == 0, sources, omitted);
        var manifestJson = JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(bundleDir, SourceManifestName), manifestJson, ct);

        task.DeliverableBundleDir = bundleDir;
        task.DeliverablePdfPath = null;
        task.DeliverableFileCount = documents.Count;
        task.DeliverableRenderError = null;
    }

    private async Task<(List<BundledDocument> Documents, List<OmittedSource> Omitted)> CollectDocumentsAsync(
        AgentTask task,
        string report,
        StringBuilder log,
        CancellationToken ct)
    {
        var named = new List<string>();
        var seenNamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in NamedDocPattern.Matches(report ?? string.Empty))
        {
            var relative = NormalizeRelative(match.Groups["path"].Value);
            if (relative is null || !seenNamed.Add(relative))
                continue;
            named.Add(relative);
        }

        var worktreeDocs = new List<string>();
        var docsOnlyDiff = false;
        if (task.Workspace == WorkspaceMode.Worktree
            && !string.IsNullOrWhiteSpace(task.WorktreePath)
            && DelegationGitFacts.ResolveBase(task) is { } gitBase)
        {
            try
            {
                var changes = await _git.GetChangesSinceAsync(task.WorktreePath, gitBase, ct);
                var producing = changes
                    .Where(c => c.Status is GitFileStatus.Added or GitFileStatus.Modified
                        or GitFileStatus.Untracked or GitFileStatus.Renamed)
                    .ToList();
                docsOnlyDiff = producing.Count > 0
                    && producing.All(c => c.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                if (docsOnlyDiff)
                {
                    foreach (var change in producing)
                    {
                        var relative = NormalizeRelative(change.Path);
                        if (relative is not null)
                            worktreeDocs.Add(relative);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.AppendLine($"git diff failed: {ex.Message}");
            }
        }

        var producingByRole = task.Role is AgentTaskRole.Plan or AgentTaskRole.Docs;
        // Named paths that resolve (disk or git) make any role document-producing — the live
        // Custom-role cleanup task is this shape. Mixed code+docs Code tasks with no named
        // doc do not get a bundle.
        var documents = new List<BundledDocument>();
        var omitted = new List<OmittedSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long remaining = _settings.MaxTotalSourceBytes;
        var foundNamed = false;
        foreach (var relative in named.Concat(worktreeDocs))
        {
            if (!seen.Add(relative))
                continue;
            var read = await ReadContentAsync(task, relative, remaining, ct);
            if (read is null)
                continue;
            if (named.Contains(relative, StringComparer.OrdinalIgnoreCase))
                foundNamed = true;
            if (read.Value.Bytes is null)
                omitted.Add(new OmittedSource(relative, read.Value.Length, "64-MiB source budget exceeded"));
            else
            {
                documents.Add(new BundledDocument(relative, read.Value.Bytes));
                remaining -= read.Value.Bytes.LongLength;
            }
        }

        return !producingByRole && !foundNamed && !docsOnlyDiff
            ? ([], [])
            : (documents, omitted);
    }

    private readonly record struct SourceRead(byte[]? Bytes, long Length);

    private async Task<SourceRead?> ReadContentAsync(
        AgentTask task, string relative, long remaining, CancellationToken ct)
    {
        var diskRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in new[] { task.WorktreePath, task.WorkingDirectory, task.RepoPath }
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.Combine(root!, diskRelative);
            if (File.Exists(full))
            {
                try
                {
                    var length = new FileInfo(full).Length;
                    if (length > remaining)
                        return new SourceRead(null, length);
                    await using var stream = File.OpenRead(full);
                    using var output = new MemoryStream();
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) != 0)
                    {
                        if (read > remaining - output.Length)
                            return new SourceRead(null, new FileInfo(full).Length);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    return new SourceRead(output.ToArray(), output.Length);
                }
                catch (IOException) { }
            }
        }

        if (task.Workspace == WorkspaceMode.Worktree && !string.IsNullOrWhiteSpace(task.WorktreeBranch))
        {
            var repository = FirstNonEmpty(task.RepoPath, task.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(repository))
            {
                var length = await _git.GetContentLengthAtAsync(repository, relative, task.WorktreeBranch, ct);
                if (length is null)
                    return null;
                if (length > remaining)
                    return new SourceRead(null, length.Value);
                var bytes = await _git.GetContentBytesAtAsync(repository, relative, task.WorktreeBranch,
                    remaining, ct);
                return bytes is null ? null : new SourceRead(bytes, bytes.LongLength);
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<SourceMember>> WriteSourcesAsync(
        List<BundledDocument> documents,
        string bundleDir,
        string stem,
        CancellationToken ct)
    {
        var members = new List<SourceMember>(documents.Count);
        var anyOversize = documents.Any(d => d.Bytes.LongLength > MaxInlineSourceBytes);
        var zip = documents.Count > _settings.MaxSourceFilesInline || anyOversize;
        if (!zip)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in documents)
            {
                var fileName = UniqueFileName(doc.RepoRelativePath, names);
                var dest = Path.Combine(bundleDir, fileName);
                await File.WriteAllBytesAsync(dest, doc.Bytes, ct);
                members.Add(new SourceMember(doc.RepoRelativePath, fileName, null,
                    doc.Bytes.LongLength, Convert.ToHexString(SHA256.HashData(doc.Bytes)).ToLowerInvariant()));
            }

            return members;
        }

        var zipPath = Path.Combine(bundleDir, stem + "-sources.zip");
        await using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in documents)
            {
                var entryName = doc.RepoRelativePath.Replace('\\', '/').TrimStart('/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var writer = entry.Open();
                await writer.WriteAsync(doc.Bytes, ct);
                members.Add(new SourceMember(doc.RepoRelativePath, Path.GetFileName(zipPath), entryName,
                    doc.Bytes.LongLength, Convert.ToHexString(SHA256.HashData(doc.Bytes)).ToLowerInvariant()));
            }
        }

        return members;
    }

    private static async Task<string?> CardIdentifierAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (task.CardId is not Guid cardId)
            return null;
        return await db.Cards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => c.Identifier)
            .FirstOrDefaultAsync(ct);
    }

    private static string CoverStem(string? identifier, string shortId)
    {
        var raw = string.IsNullOrWhiteSpace(identifier) ? shortId : identifier;
        return SanitizeFileToken(raw);
    }

    private static string Slug(string relativePath)
    {
        var parent = Path.GetFileName(
            Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "");
        if (string.IsNullOrWhiteSpace(parent) || parent is "." or "..")
            return "document";
        return SanitizeFileToken(parent);
    }

    private static string SanitizeFileToken(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else if (c is ' ' or '/')
                sb.Append('-');
        }

        return sb.Length == 0 ? "document" : sb.ToString();
    }

    private static string UniqueFileName(string relative, HashSet<string> used)
    {
        var name = Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            name = "document.md";
        if (used.Add(name))
            return name;
        var parent = Path.GetFileName(
            Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar)) ?? "doc");
        var prefixed = SanitizeFileToken(parent) + "-" + name;
        if (used.Add(prefixed))
            return prefixed;
        var i = 2;
        while (!used.Add($"{i}-{name}"))
            i++;
        return $"{i}-{name}";
    }

    private static string? NormalizeRelative(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var n = raw.Replace('\\', '/').Trim();
        while (n.StartsWith("./", StringComparison.Ordinal))
            n = n[2..];
        n = n.TrimStart('/');
        if (n.Length == 0)
            return null;
        if (Path.IsPathRooted(raw))
            return null;
        if (n.Contains("..", StringComparison.Ordinal))
            return null;
        if (n.StartsWith("docs/cards/", StringComparison.OrdinalIgnoreCase)
            || n.Equals("docs/cards", StringComparison.OrdinalIgnoreCase))
            return null;
        if (n.StartsWith(".antiphon/", StringComparison.OrdinalIgnoreCase)
            || n.Equals(".antiphon", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;
        return n;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{Math.Max(1, (int)Math.Round(bytes / 1024.0))} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}
