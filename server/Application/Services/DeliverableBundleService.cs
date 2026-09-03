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
/// CARD-0337 S1: at settlement, a document-producing task gets a PDF + source copies (or a zip)
/// under <c>&lt;repo&gt;\.antiphon\deliverables\&lt;taskShort&gt;\</c>. Never throws into settlement.
/// </summary>
public sealed class DeliverableBundleService
{
    public const string RenderLogName = "render.log";
    public const long MaxInlineSourceBytes = 1024 * 1024;

    private static readonly Regex NamedDocPattern = new(
        "`?(?<path>docs/[\\w./-]+\\.md)`?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly MarkdownPdfRenderer _renderer;
    private readonly GitWorkspaceService _git;
    private readonly DeliverablesSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeliverableBundleService> _logger;

    public DeliverableBundleService(
        MarkdownPdfRenderer renderer,
        GitWorkspaceService git,
        IOptions<DeliverablesSettings> settings,
        TimeProvider clock,
        ILogger<DeliverableBundleService> logger)
    {
        _renderer = renderer;
        _git = git;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    public readonly record struct BundledDocument(string RepoRelativePath, string Markdown);

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
    /// Files S3 may attach: PDF first, then sources/zip. Skips <c>render.log</c> and leftover HTML.
    /// </summary>
    public static IReadOnlyList<string> ListAttachableFiles(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir)
            || !Directory.Exists(task.DeliverableBundleDir))
            return [];

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(task.DeliverablePdfPath)
            && File.Exists(task.DeliverablePdfPath)
            && seen.Add(task.DeliverablePdfPath))
        {
            files.Add(task.DeliverablePdfPath);
        }

        foreach (var path in Directory.EnumerateFiles(task.DeliverableBundleDir)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            if (name.Equals(RenderLogName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(path))
                continue;
            files.Add(path);
        }

        return files;
    }

    public static string? FormatNoteBit(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.DeliverableBundleDir))
            return null;
        var md = task.DeliverableFileCount;
        if (!string.IsNullOrWhiteSpace(task.DeliverablePdfPath)
            && File.Exists(task.DeliverablePdfPath))
        {
            return $"{md} md, pdf {FormatSize(new FileInfo(task.DeliverablePdfPath).Length)}";
        }

        return $"{md} md, pdf failed";
    }

    private async Task BuildCoreAsync(
        AgentTask task,
        string report,
        AppDbContext? db,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        var documents = await CollectDocumentsAsync(task, report, log, ct);
        if (documents.Count == 0)
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

        var collected = documents.Count;
        var truncated = collected > _settings.MaxDocuments;
        if (truncated)
        {
            log.AppendLine(
                $"capped documents at {_settings.MaxDocuments} of {collected}; PDF skipped, sources zipped");
            documents = documents.Take(_settings.MaxDocuments).ToList();
        }

        var identifier = db is null ? null : await CardIdentifierAsync(db, task, ct);
        var stem = $"{CoverStem(identifier, shortId)}-{Slug(documents[0])}";
        await WriteSourcesAsync(documents, bundleDir, stem, zipOnly: truncated, log, ct);

        string? pdfPath = null;
        string? renderError = null;
        if (!truncated)
        {
            var cover = await BuildCoverLineAsync(task, identifier, shortId, log, ct);
            var pdfName = stem + ".pdf";
            pdfPath = Path.Combine(bundleDir, pdfName);
            var html = _renderer.ToHtml(
                cover,
                documents.Select(d => new MarkdownPdfRenderer.DocumentSection(d.RepoRelativePath, d.Markdown)).ToList());
            var rendered = await _renderer.RenderToPdfAsync(html, pdfPath, ct);
            log.AppendLine($"pdf {rendered.DurationMs}ms: {(rendered.Succeeded ? "ok" : rendered.Error)}");
            if (!string.IsNullOrWhiteSpace(rendered.Log))
                log.AppendLine(rendered.Log);
            if (!rendered.Succeeded)
            {
                renderError = rendered.Error;
                pdfPath = null;
                try { if (File.Exists(Path.Combine(bundleDir, pdfName))) File.Delete(Path.Combine(bundleDir, pdfName)); }
                catch (IOException) { }
            }
        }
        else
        {
            renderError = $"too many documents ({collected}); PDF skipped";
        }

        await File.WriteAllTextAsync(Path.Combine(bundleDir, RenderLogName), log.ToString(), ct);

        task.DeliverableBundleDir = bundleDir;
        task.DeliverablePdfPath = pdfPath;
        task.DeliverableFileCount = documents.Count;
        task.DeliverableRenderError = renderError is null
            ? null
            : renderError.Length <= 300 ? renderError : renderError[..300];
    }

    private async Task<List<BundledDocument>> CollectDocumentsAsync(
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
        var resolvedNamed = new List<BundledDocument>();
        foreach (var relative in named)
        {
            var content = await ReadContentAsync(task, relative, ct);
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
            var content = await ReadContentAsync(task, relative, ct);
            if (content is not null)
                documents.Add(new BundledDocument(relative, content));
        }

        return documents;
    }

    private async Task<string?> ReadContentAsync(AgentTask task, string relative, CancellationToken ct)
    {
        var diskRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in new[] { task.WorktreePath, task.WorkingDirectory, task.RepoPath }
                     .Where(r => !string.IsNullOrWhiteSpace(r))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.Combine(root!, diskRelative);
            if (File.Exists(full))
            {
                try { return await File.ReadAllTextAsync(full, ct); }
                catch (IOException) { }
            }
        }

        if (task.Workspace == WorkspaceMode.Worktree && !string.IsNullOrWhiteSpace(task.WorktreeBranch))
        {
            var repository = FirstNonEmpty(task.RepoPath, task.WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(repository))
                return await _git.GetContentAtAsync(repository, relative, task.WorktreeBranch, ct);
        }

        return null;
    }

    private async Task WriteSourcesAsync(
        List<BundledDocument> documents,
        string bundleDir,
        string stem,
        bool zipOnly,
        StringBuilder log,
        CancellationToken ct)
    {
        var anyOversize = documents.Any(d => Encoding.UTF8.GetByteCount(d.Markdown) > MaxInlineSourceBytes);
        var zip = zipOnly || documents.Count > _settings.MaxSourceFilesInline || anyOversize;
        if (!zip)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in documents)
            {
                var fileName = UniqueFileName(doc.RepoRelativePath, names);
                var dest = Path.Combine(bundleDir, fileName);
                await File.WriteAllTextAsync(dest, doc.Markdown, ct);
                log.AppendLine($"copied {doc.RepoRelativePath} -> {fileName}");
            }

            return;
        }

        var zipPath = Path.Combine(bundleDir, stem + "-sources.zip");
        await using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in documents)
            {
                var entryName = doc.RepoRelativePath.Replace('\\', '/').TrimStart('/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync(doc.Markdown.AsMemory(), ct);
            }
        }

        log.AppendLine($"zipped {documents.Count} sources -> {Path.GetFileName(zipPath)}");
    }

    private async Task<string> BuildCoverLineAsync(
        AgentTask task,
        string? identifier,
        string shortId,
        StringBuilder log,
        CancellationToken ct)
    {
        var title = string.IsNullOrWhiteSpace(task.Title) ? null : task.Title.Trim();
        var sha = await TryHeadShaAsync(task, ct);
        var date = _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd");
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(title))
            bits.Add($"{identifier} {title}");
        else if (!string.IsNullOrWhiteSpace(identifier))
            bits.Add(identifier);
        else if (!string.IsNullOrWhiteSpace(title))
            bits.Add(title);
        bits.Add(shortId);
        if (!string.IsNullOrWhiteSpace(sha))
            bits.Add(sha.Length > 12 ? sha[..12] : sha);
        bits.Add(date);
        log.AppendLine("cover: " + string.Join(" · ", bits));
        return string.Join(" · ", bits);
    }

    private async Task<string?> TryHeadShaAsync(AgentTask task, CancellationToken ct)
    {
        foreach (var dir in new[] { task.WorktreePath, task.WorkingDirectory, task.RepoPath }
                     .Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var sha = await _git.GetHeadShaAsync(dir!, ct);
            if (!string.IsNullOrWhiteSpace(sha))
                return sha;
        }

        return string.IsNullOrWhiteSpace(task.WorktreeBaseSha) ? null : task.WorktreeBaseSha;
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

    internal static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{Math.Max(1, (int)Math.Round(bytes / 1024.0))} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}
