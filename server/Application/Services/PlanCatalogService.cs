using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// A READ-ONLY projection over the plan files git already holds — <c>docs/superpowers/specs/*.md</c>
/// <c>docs/superpowers/plans/*.md</c>, and <c>docs/features/&lt;name&gt;/proposal.md</c> under a resolved repo root.
///
/// <para><b>Why there is no database artifact behind this.</b> Git already stores plans, versions
/// them, diffs them and survives a restart. A row mirroring each file would be a second durable
/// store for one fact — the exact defect CARD-0067 was created by and CLAUDE.md's two-stores rule
/// now forbids — and it would drift the moment an agent edited a file without re-POSTing it. It
/// would also orphan every plan written before it shipped. A projection is retroactive by
/// construction: the 23 specs that exist today are in the catalog because they are on disk.</para>
///
/// <para><b>Tolerance is the design, not a concession.</b> Those 23 files follow no enforced header
/// format — <c>- **Status**: Planned</c>, <c>**Status:** planned, not implemented. **Card:**
/// CARD-0019.</c>, a bare <c>Status: ...</c>, and one file with no header block at all. A parser
/// that dropped what it could not fully read would be a catalog of the plans written after it, so
/// every field but the path is best-effort and a file always appears with whatever was legible.</para>
///
/// <para><b>Serving file contents by name is the risky half</b>, so the resolver never trusts the
/// caller's string: the requested file is joined to the root, fully normalised, symlink-resolved,
/// and then required to sit inside one of the two plan roots. Anything else is refused as a
/// validation error — it is never a 404, because "no such plan" would tell a prober that a
/// different path might have worked.</para>
/// </summary>
public sealed class PlanCatalogService : IResettableCache
{
    /// <summary>
    /// Long enough that a phone polling the thread does not stat 25 files per tap, short enough
    /// that a plan an agent just wrote shows up while the operator is still looking for it.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>How far up from a requested directory to look for a repo root that holds plans.</summary>
    private const int MaxWalkUp = 12;

    /// <summary>Lines of a plan read for the card scan (the spec's own convention: header + citations).</summary>
    private const int ScannedLines = 200;

    /// <summary>Header fields sit above the first <c>##</c> section; this bounds a file with none.</summary>
    private const int MaxHeaderLines = 40;

    private const int MaxStatusChars = 300;

    /// <summary>A plan is prose. Anything larger than this is not one, and is refused rather than served.</summary>
    private const long MaxContentBytes = 4 * 1024 * 1024;

    private static readonly string[] SpecsRoot = ["docs", "superpowers", "specs"];
    private static readonly string[] PlansRoot = ["docs", "superpowers", "plans"];
    private static readonly string[] FeaturesRoot = ["docs", "features"];

    private readonly ILogger<PlanCatalogService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly GitWorkspaceService _git;

    private readonly ConcurrentDictionary<string, (DateTime At, PlanCatalogDto Catalog)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public PlanCatalogService(
        TimeProvider timeProvider,
        ILogger<PlanCatalogService> logger,
        GitWorkspaceService git)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _git = git;
    }

    /// <summary>
    /// Every plan under the resolved root, newest first. A root that cannot be resolved comes back
    /// with <see cref="PlanCatalogDto.RootResolved"/> false and an empty list — absent, not empty.
    /// </summary>
    public async Task<PlanCatalogDto> ListAsync(string? requestedRoot, CancellationToken ct)
    {
        var root = ResolveRoot(requestedRoot);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (root is null)
            return new PlanCatalogDto(null, RootResolved: false, now, []);

        if (_cache.TryGetValue(root, out var hit) && now - hit.At < Ttl)
            return hit.Catalog;

        var plans = new List<PlanSummaryDto>();
        foreach (var (file, kind) in EnumeratePlanFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            var summary = await TryReadSummaryAsync(root, file, kind, ct);
            if (summary is not null)
                plans.Add(summary);
        }

        plans.Sort(Newest);
        var catalog = new PlanCatalogDto(root, RootResolved: true, now, plans);
        _cache[root] = (now, catalog);
        return catalog;
    }

    /// <summary>
    /// One plan's raw markdown. <paramref name="file"/> is the root-relative path the catalog
    /// handed out; anything that does not resolve inside a plan root is a
    /// <see cref="ValidationException"/>, and a well-formed path with no file behind it is a
    /// <see cref="NotFoundException"/>.
    /// </summary>
    public async Task<PlanContentDto> ReadAsync(string? requestedRoot, string file, string? gitRef, CancellationToken ct)
    {
        var root = ResolveRoot(requestedRoot)
            ?? throw new NotFoundException("PlanRoot", requestedRoot ?? "(server root)");

        if (!TryResolvePlanFile(root, file, out var fullPath, out var refusal))
            throw new ValidationException(nameof(file), refusal);

        if (!string.IsNullOrWhiteSpace(gitRef))
        {
            var contentAtRef = await _git.GetContentAtAsync(root, file, gitRef.Trim(), ct);
            if (contentAtRef is null)
                throw new NotFoundException("Plan", $"{file} not on {gitRef.Trim()}");

            return new PlanContentDto(
                SummaryFromContent(root, file, contentAtRef, _timeProvider.GetUtcNow().UtcDateTime),
                contentAtRef);
        }

        if (!File.Exists(fullPath))
            throw new NotFoundException("Plan", file);

        var info = new FileInfo(fullPath);
        if (info.Length > MaxContentBytes)
        {
            throw new ValidationException(
                nameof(file), $"'{file}' is {info.Length} bytes; plans are prose and are capped at {MaxContentBytes}.");
        }

        var kind = KindOf(root, fullPath);
        var summary = await TryReadSummaryAsync(root, fullPath, kind, ct)
            ?? throw new NotFoundException("Plan", file);
        var content = await File.ReadAllTextAsync(fullPath, ct);
        return new PlanContentDto(summary, content);
    }

    /// <summary>
    /// The nearest directory at or above <paramref name="requested"/> that actually holds plans,
    /// or null. Walking up is what makes a worktree subdirectory — or the server's own bin folder,
    /// when no root was requested — resolve to the checkout it belongs to.
    /// </summary>
    public string? ResolveRoot(string? requested)
    {
        var start = string.IsNullOrWhiteSpace(requested) ? AppContext.BaseDirectory : requested.Trim();
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(Path.GetFullPath(start));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Plan root {Root} is not a usable path", start);
            return null;
        }

        for (var depth = 0; dir is not null && depth < MaxWalkUp; depth++, dir = dir.Parent)
        {
            if (HasPlanRoot(dir.FullName))
                return dir.FullName;
        }
        return null;
    }

    public void Clear() => _cache.Clear();

    private static bool HasPlanRoot(string root) =>
        Directory.Exists(Combine(root, SpecsRoot))
        || Directory.Exists(Combine(root, PlansRoot))
        || Directory.Exists(Combine(root, FeaturesRoot));

    private static string Combine(string root, string[] segments) =>
        Path.Combine([root, .. segments]);

    private IEnumerable<(string File, PlanKind Kind)> EnumeratePlanFiles(string root)
    {
        var specs = Combine(root, SpecsRoot);
        if (Directory.Exists(specs))
        {
            foreach (var file in SafeEnumerateFiles(specs, "*.md"))
                yield return (file, PlanKind.Spec);
        }

        var plans = Combine(root, PlansRoot);
        if (Directory.Exists(plans))
        {
            foreach (var file in SafeEnumerateFiles(plans, "*.md"))
                yield return (file, PlanKind.Plan);
        }

        var features = Combine(root, FeaturesRoot);
        if (!Directory.Exists(features))
            yield break;

        foreach (var folder in SafeEnumerateDirectories(features))
        {
            var proposal = Path.Combine(folder, "proposal.md");
            if (File.Exists(proposal))
                yield return (proposal, PlanKind.Proposal);
        }
    }

    private IReadOnlyList<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return [.. Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not list plans in {Directory}", directory);
            return [];
        }
    }

    private IReadOnlyList<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(directory)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not list feature folders in {Directory}", directory);
            return [];
        }
    }

    private async Task<PlanSummaryDto?> TryReadSummaryAsync(
        string root, string fullPath, PlanKind kind, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
                return null;

            var lines = await ReadHeadLinesAsync(fullPath, ScannedLines, ct);
            var fallbackName = kind == PlanKind.Proposal
                ? Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? info.Name
                : Path.GetFileNameWithoutExtension(info.Name);

            var header = ParseHeader(info.Name, fallbackName, lines);
            return new PlanSummaryDto(
                RelativePath: ToRelative(root, fullPath),
                FileName: info.Name,
                Kind: kind,
                Title: header.Title,
                Date: header.Date,
                Status: header.Status,
                Cards: header.Cards,
                MentionedCards: header.MentionedCards,
                SizeBytes: info.Length,
                ModifiedAt: info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that vanished mid-scan costs the caller that row, never the catalog.
            _logger.LogDebug(ex, "Could not read plan {Path}", fullPath);
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>> ReadHeadLinesAsync(string path, int max, CancellationToken ct)
    {
        var lines = new List<string>(Math.Min(max, 64));
        using var reader = new StreamReader(path);
        while (lines.Count < max && await reader.ReadLineAsync(ct) is { } line)
            lines.Add(line);
        return lines;
    }

    private static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static PlanKind KindOf(string root, string fullPath) =>
        IsUnder(Combine(root, FeaturesRoot), fullPath) ? PlanKind.Proposal
        : IsUnder(Combine(root, PlansRoot), fullPath) ? PlanKind.Plan
        : PlanKind.Spec;

    private static PlanSummaryDto SummaryFromContent(string root, string file, string content, DateTime modifiedAt)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, file));
        var kind = KindOf(root, fullPath);
        var fileName = Path.GetFileName(file);
        var fallbackName = kind == PlanKind.Proposal
            ? Path.GetFileName(Path.GetDirectoryName(file)) ?? fileName
            : Path.GetFileNameWithoutExtension(fileName);
        var header = ParseHeader(
            fileName,
            fallbackName,
            content.ReplaceLineEndings("\n").Split('\n').Take(ScannedLines).ToList());
        return new PlanSummaryDto(
            file.Replace('\\', '/'), fileName, kind, header.Title, header.Date, header.Status,
            header.Cards, header.MentionedCards, System.Text.Encoding.UTF8.GetByteCount(content), modifiedAt);
    }

    private static int Newest(PlanSummaryDto a, PlanSummaryDto b)
    {
        // Undated plans (every proposal) sort after dated ones rather than to the top.
        if (a.Date != b.Date)
            return (b.Date ?? DateOnly.MinValue).CompareTo(a.Date ?? DateOnly.MinValue);
        return string.Compare(b.RelativePath, a.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    // ---- path safety ---------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="file"/> names a plan file inside one of the two roots. The check is
    /// on the FULLY RESOLVED path, not on the string: rejecting <c>..</c> by inspection alone would
    /// miss every other way out of a directory, and the symlink resolution is there because a
    /// symlink inside the roots is a path that passes a prefix test and reads a file that doesn't.
    /// </summary>
    internal static bool TryResolvePlanFile(
        string root, string? file, out string fullPath, out string refusal)
    {
        fullPath = string.Empty;
        refusal = string.Empty;

        var candidate = (file ?? string.Empty).Trim().Replace('\\', '/');
        if (candidate.Length == 0)
        {
            refusal = "A plan file is required — pass the catalog's relativePath.";
            return false;
        }

        if (Path.IsPathRooted(candidate) || candidate.Contains(':'))
        {
            refusal = $"'{file}' is an absolute path; plan files are named relative to the repo root.";
            return false;
        }

        if (candidate.Split('/').Any(segment => segment is ".."))
        {
            refusal = $"'{file}' leaves the plan directories.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, candidate));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            refusal = $"'{file}' is not a usable path.";
            return false;
        }

        // A symlink's own path can sit inside the roots while its target does not. Resolved only
        // when something is actually there — ResolveLinkTarget throws on a path that does not
        // exist, and a missing plan is a 404, not a refusal.
        var resolved = fullPath;
        try
        {
            if (File.Exists(fullPath))
                resolved = File.ResolveLinkTarget(fullPath, returnFinalTarget: true)?.FullName ?? fullPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A broken or cyclic link is not a plan; refuse rather than guess at its target.
            refusal = $"'{file}' does not resolve to a readable plan.";
            fullPath = string.Empty;
            return false;
        }

        if (!IsInPlanRoots(root, fullPath) || !IsInPlanRoots(root, resolved))
        {
            refusal = $"'{file}' is outside docs/superpowers/specs, docs/superpowers/plans and docs/features.";
            fullPath = string.Empty;
            return false;
        }

        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            refusal = $"'{file}' is not a markdown plan.";
            fullPath = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsInPlanRoots(string root, string fullPath) =>
        IsUnder(Combine(root, SpecsRoot), fullPath)
        || IsUnder(Combine(root, PlansRoot), fullPath)
        || IsUnder(Combine(root, FeaturesRoot), fullPath);

    private static bool IsUnder(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(prefix, comparison);
    }

    // ---- header parsing ------------------------------------------------------------------------

    internal sealed record PlanHeader(
        string Title,
        DateOnly? Date,
        string? Status,
        IReadOnlyList<string> Cards,
        IReadOnlyList<string> MentionedCards);

    /// <summary>
    /// Reads what the file actually says. Everything is optional; nothing here can fail a file out
    /// of the catalog.
    /// </summary>
    internal static PlanHeader ParseHeader(string fileName, string fallbackName, IReadOnlyList<string> lines)
    {
        var titleIndex = -1;
        string? title = null;
        for (var i = 0; i < lines.Count && i < MaxHeaderLines; i++)
        {
            var match = TitleLine.Match(lines[i]);
            if (!match.Success)
                continue;
            titleIndex = i;
            title = match.Groups["title"].Value.Trim().Trim('#', ' ');
            break;
        }

        var fields = ReadFields(lines, titleIndex + 1);

        var status = fields
            .Where(f => IsLabel(f.Label, "status"))
            .Select(f => Clamp(f.Value, MaxStatusChars))
            .FirstOrDefault(v => v.Length > 0);

        var date = ReadDate(fileName)
            ?? fields.Where(f => IsLabel(f.Label, "date"))
                .Select(f => ReadDate(f.Value))
                .FirstOrDefault(d => d is not null);

        // The card the plan is ABOUT: its filename, its title, and any Card(s) header field. The
        // "Relates to" / "Supersedes" citations most specs carry are deliberately not in here —
        // folding them in would put every plan on every neighbour's thread.
        var subject = new List<string>();
        subject.AddRange(CardsIn(fileName));
        if (title is not null)
            subject.AddRange(CardsIn(title));
        foreach (var field in fields.Where(f => CardLabels.Contains(f.Label)))
            subject.AddRange(CardsIn(field.Value));

        var cards = subject.Distinct(StringComparer.Ordinal).ToList();
        var mentioned = lines
            .SelectMany(CardsIn)
            .Distinct(StringComparer.Ordinal)
            .Where(c => !cards.Contains(c, StringComparer.Ordinal))
            .ToList();

        return new PlanHeader(
            string.IsNullOrWhiteSpace(title) ? Humanise(fallbackName) : title!,
            date,
            string.IsNullOrWhiteSpace(status) ? null : status,
            cards,
            mentioned);
    }

    private static bool IsLabel(string label, string known) =>
        string.Equals(label, known, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> CardLabels =
        new(StringComparer.OrdinalIgnoreCase) { "card", "cards", "cards reconciled", "tracking card" };

    private sealed record Field(string Label, string Value);

    /// <summary>
    /// Label/value pairs out of the header block, in the three shapes the corpus actually uses:
    /// <c>- **Status**: x</c>, <c>**Status:** x. **Card:** y.</c> (several on one line) and a plain
    /// <c>Status: x</c>. The plain form is tried FIRST because a value may itself be bold
    /// (<c>Status: **Implemented** (…)</c>) and reading that line as a bold field would name the
    /// value and lose the label.
    /// </summary>
    private static List<Field> ReadFields(IReadOnlyList<string> lines, int start)
    {
        var fields = new List<Field>();

        // A header field that WRAPS is the common case, not an edge one: half the specs in this
        // repo carry a Card or Cards line long enough to run onto an indented continuation, and
        // reading only the first line silently drops most of what it names (the mobile-thread
        // spec's own `Cards reconciled` list loses four of its seven identifiers that way).
        var open = false;

        for (var i = Math.Max(0, start); i < lines.Count && i < start + MaxHeaderLines; i++)
        {
            var raw = lines[i];
            if (raw.StartsWith("## ", StringComparison.Ordinal))
                break;

            var line = ListMarker.Replace(raw, string.Empty).Trim();
            if (line.Length == 0)
            {
                // A blank line ends the wrap; prose below it is not part of the field above it.
                open = false;
                continue;
            }

            var plain = PlainField.Match(line);
            if (plain.Success)
            {
                fields.Add(new Field(plain.Groups["label"].Value.Trim(), CleanValue(plain.Groups["value"].Value)));
                open = true;
                continue;
            }

            var bold = BoldField.Matches(line);
            if (bold.Count == 0)
            {
                // Indented, no label of its own, and a field is open above it: a continuation.
                if (open && fields.Count > 0 && char.IsWhiteSpace(raw[0]))
                {
                    var last = fields[^1];
                    fields[^1] = last with { Value = CleanValue($"{last.Value} {line}") };
                }
                else
                {
                    open = false;
                }
                continue;
            }

            for (var m = 0; m < bold.Count; m++)
            {
                var here = bold[m];
                var valueStart = here.Index + here.Length;
                var valueEnd = m + 1 < bold.Count ? bold[m + 1].Index : line.Length;
                if (valueEnd < valueStart)
                    continue;
                fields.Add(new Field(
                    here.Groups["label"].Value.Trim(),
                    CleanValue(line[valueStart..valueEnd])));
            }
            open = fields.Count > 0;
        }
        return fields;
    }

    private static string CleanValue(string value) =>
        value.Trim().TrimStart(':').Replace("**", string.Empty).Trim().TrimEnd('.', ',', ' ');

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private static DateOnly? ReadDate(string text)
    {
        var match = IsoDate.Match(text);
        return match.Success
            && DateOnly.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }

    /// <summary>
    /// Every <c>CARD-nnnn</c> in the text, canonicalised. The trailing <c>(?![0-9])</c> is the
    /// no-false-positive guard the thread depends on: without it <c>CARD-0067</c> matches inside
    /// <c>CARD-00670</c> and a plan lands on a card it never mentions.
    /// </summary>
    internal static IEnumerable<string> CardsIn(string text)
    {
        foreach (Match match in CardRef.Matches(text))
        {
            if (int.TryParse(match.Groups["n"].Value, out var number))
                yield return $"CARD-{number:0000}";
        }
    }

    private static string Humanise(string name)
    {
        var stripped = LeadingIndex.Replace(Path.GetFileNameWithoutExtension(name), string.Empty);
        var words = stripped.Replace('-', ' ').Replace('_', ' ').Trim();
        if (words.Length == 0)
            return name;
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static readonly Regex TitleLine =
        new(@"^\s{0,3}#\s+(?<title>.+?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ListMarker =
        new(@"^\s*(?:[-*+]\s+|>\s*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlainField =
        new(@"^(?<label>[A-Za-z][A-Za-z ]{0,30}?)\s*:\s*(?<value>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoldField =
        new(@"\*\*\s*(?<label>[^*\r\n:]{1,40}?)\s*:?\s*\*\*\s*:?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsoDate =
        new(@"\d{4}-\d{2}-\d{2}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CardRef =
        new(@"(?<![A-Za-z0-9])CARD-(?<n>\d{1,4})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingIndex =
        new(@"^\d{2,4}(-\d{2}-\d{2})?-", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
