using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0418: at settlement, a document-producing task gets source copies (or a zip) under
/// <c>&lt;repo&gt;\.antiphon\deliverables\&lt;taskShort&gt;\</c>. Never throws into settlement.
/// Does not render PDF.
/// </summary>
public sealed class DeliverableBundleService
{
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
        if (task.OutboundDeliveryId is not null)
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
    /// Files a channel turn may imply-attach: manifest-listed markdown and source zip only.
    /// Historical PDF paths stay on the task row but are not implicitly attached.
    /// </summary>
    public static IReadOnlyList<string> ListAttachableFiles(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir)
            || !Directory.Exists(task.DeliverableBundleDir))
            return [];

        var manifestPath = Path.Combine(task.DeliverableBundleDir, SourceBundleManifest.FileName);
        if (File.Exists(manifestPath))
        {
            try
            {
                return SourceBundleManifest.Read(manifestPath).ListExistingFiles(task.DeliverableBundleDir);
            }
            catch (Exception)
            {
                return [];
            }
        }

        return Directory.EnumerateFiles(task.DeliverableBundleDir)
            .Where(IsLegacyImpliedSource)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? FormatNoteBit(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir))
            return null;
        var md = task.DeliverableFileCount;
        var zip = HasSourcesZip(task);
        var incomplete = IsIncomplete(task);
        var bit = zip ? $"{md} md, sources zip" : $"{md} md";
        return incomplete ? bit + ", incomplete" : bit;
    }

    private async Task BuildCoreAsync(
        AgentTask task,
        string report,
        AppDbContext? db,
        CancellationToken ct)
    {
        var documents = await CollectDocumentsAsync(task, report, ct);
        if (documents.Count == 0)
            return;

        var root = FirstNonEmpty(task.RepoPath, task.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(root))
            return;

        var shortId = DelegationReportFormatter.Short(task.Id);
        var bundleDir = Path.Combine(root, ".antiphon", "deliverables", shortId);
        Directory.CreateDirectory(bundleDir);

        var budget = _settings.MaxUncompressedSourceBytes <= 0
            ? 64L * 1024 * 1024
            : _settings.MaxUncompressedSourceBytes;
        var retained = new List<BundledDocument>();
        var omissions = new List<SourceBundleOmission>();
        long used = 0;
        foreach (var doc in documents)
        {
            var length = doc.Bytes.LongLength;
            if (used > 0 && used + length > budget)
            {
                omissions.Add(new SourceBundleOmission
                {
                    RelativePath = doc.RepoRelativePath,
                    Length = length,
                    Reason = "uncompressed-source-budget",
                });
                continue;
            }

            if (used == 0 && length > budget)
            {
                omissions.Add(new SourceBundleOmission
                {
                    RelativePath = doc.RepoRelativePath,
                    Length = length,
                    Reason = "uncompressed-source-budget",
                });
                continue;
            }

            retained.Add(doc);
            used += length;
        }

        if (retained.Count == 0)
            return;

        var identifier = db is null ? null : await CardIdentifierAsync(db, task, ct);
        var stem = $"{CoverStem(identifier, shortId)}-{Slug(retained[0])}";
        var members = await WriteSourcesAsync(retained, bundleDir, stem, ct);

        var manifest = new SourceBundleManifest
        {
            Version = SourceBundleManifest.CurrentVersion,
            Members = members,
            Omissions = omissions,
            Incomplete = omissions.Count > 0,
        };
        await SourceBundleManifest.WriteAsync(
            Path.Combine(bundleDir, SourceBundleManifest.FileName), manifest, ct);

        task.DeliverableBundleDir = bundleDir;
        task.DeliverablePdfPath = null;
        task.DeliverableFileCount = retained.Count;
        task.DeliverableRenderError = null;
    }

    private async Task<List<BundledDocument>> CollectDocumentsAsync(
        AgentTask task,
        string report,
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
                _logger.LogDebug(ex, "git diff failed while collecting deliverable sources");
            }
        }

        var producingByRole = task.Role is AgentTaskRole.Plan or AgentTaskRole.Docs;
        var resolvedNamed = new List<BundledDocument>();
        foreach (var relative in named)
        {
            var content = await ReadBytesAsync(task, relative, ct);
            if (content is not null)
                resolvedNamed.Add(new BundledDocument(relative, content));
        }

        if (!producingByRole && resolvedNamed.Count == 0 && !docsOnlyDiff)
            return [];

        var documents = new List<BundledDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in resolvedNamed)
        {
            if (seen.Add(doc.RepoRelativePath))
                documents.Add(doc);
        }

        foreach (var relative in worktreeDocs)
        {
            if (!seen.Add(relative))
                continue;
            var content = await ReadBytesAsync(task, relative, ct);
            if (content is not null)
                documents.Add(new BundledDocument(relative, content));
        }

        return documents;
    }

    private async Task<byte[]?> ReadBytesAsync(AgentTask task, string relative, CancellationToken ct)
    {
        var diskRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in new[] { task.WorktreePath, task.WorkingDirectory, task.RepoPath }
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.Combine(root!, diskRelative);
            if (!File.Exists(full))
                continue;
            try { return await File.ReadAllBytesAsync(full, ct); }
            catch (IOException) { }
        }

        if (task.Workspace == WorkspaceMode.Worktree && !string.IsNullOrWhiteSpace(task.WorktreeBranch))
        {
            var repository = FirstNonEmpty(task.RepoPath, task.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(repository))
                return await _git.GetBytesAtAsync(repository, relative, task.WorktreeBranch, ct);
        }

        return null;
    }

    private async Task<List<SourceBundleMember>> WriteSourcesAsync(
        List<BundledDocument> documents,
        string bundleDir,
        string stem,
        CancellationToken ct)
    {
        var anyOversize = documents.Any(d => d.Bytes.LongLength > MaxInlineSourceBytes);
        var zip = documents.Count > _settings.MaxSourceFilesInline || anyOversize;
        var members = new List<SourceBundleMember>();
        if (!zip)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in documents)
            {
                var fileName = UniqueFileName(doc.RepoRelativePath, names);
                var dest = Path.Combine(bundleDir, fileName);
                await File.WriteAllBytesAsync(dest, doc.Bytes, ct);
                members.Add(new SourceBundleMember
                {
                    RelativePath = doc.RepoRelativePath,
                    StoredName = fileName,
                    Kind = SourceBundleMemberKinds.Markdown,
                    Length = doc.Bytes.LongLength,
                    Sha256 = SourceBundleManifest.Sha256Hex(doc.Bytes),
                });
            }

            return members;
        }

        var zipName = stem + "-sources.zip";
        var zipPath = Path.Combine(bundleDir, zipName);
        await using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in documents)
            {
                var entryName = doc.RepoRelativePath.Replace('\\', '/').TrimStart('/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var dest = entry.Open();
                await dest.WriteAsync(doc.Bytes, ct);
                members.Add(new SourceBundleMember
                {
                    RelativePath = doc.RepoRelativePath,
                    StoredName = zipName,
                    Kind = SourceBundleMemberKinds.Zip,
                    Length = doc.Bytes.LongLength,
                    Sha256 = SourceBundleManifest.Sha256Hex(doc.Bytes),
                });
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

    private static string Slug(BundledDocument first)
    {
        var parent = Path.GetFileName(
            Path.GetDirectoryName(first.RepoRelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "");
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

    private static bool IsLegacyImpliedSource(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSourcesZip(AgentTask task)
    {
        foreach (var file in ListAttachableFiles(task))
        {
            if (file.EndsWith("-sources.zip", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsIncomplete(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir))
            return false;
        var manifestPath = Path.Combine(task.DeliverableBundleDir, SourceBundleManifest.FileName);
        if (!File.Exists(manifestPath))
            return false;
        try
        {
            return SourceBundleManifest.Read(manifestPath).Incomplete;
        }
        catch (Exception)
        {
            return false;
        }
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
