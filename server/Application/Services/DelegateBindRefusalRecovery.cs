using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0085: positive-only evidence that a delegated task whose session ingested nothing still
/// did the work. Called from the delivery watchdog / dead-session sweep immediately before they
/// would write Failed on an empty <c>TranscriptEntries</c> table. Does not bind a transcript,
/// does not ingest, and does not change C1–C4.
/// </summary>
public sealed class DelegateBindRefusalRecovery
{
    /// <summary>Same slack <c>TranscriptTailer.EpochOk</c> uses for rule C3.</summary>
    internal static readonly TimeSpan EpochSkewSlack = TimeSpan.FromSeconds(2);

    private const int CommitLimit = 50;

    /// <summary>
    /// Card identifiers only — the same shape <see cref="GitWorkspaceService.ListCommitsByGrepAsync"/>
    /// greps for. Generic Goal prose is not a needle.
    /// </summary>
    private static readonly Regex CardId = new(
        @"CARD-\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly GitWorkspaceService _git;
    private readonly ILogger<DelegateBindRefusalRecovery> _logger;
    private readonly string _projectsRoot;

    public DelegateBindRefusalRecovery(
        GitWorkspaceService git,
        ILogger<DelegateBindRefusalRecovery> logger,
        IOptions<DelegateBindRefusalRecoverySettings>? settings = null)
    {
        _git = git;
        _logger = logger;
        _projectsRoot = ResolveProjectsRoot(settings?.Value.ClaudeProjectsRoot);
    }

    /// <summary>
    /// Either arm is enough. Null means "no positive evidence" — including "could not ask git"
    /// and "projects root missing". Absence is not evidence the work did not happen, but it is
    /// also not evidence that it did, so the caller still Fails.
    /// </summary>
    public async Task<DelegateBindRefusalEvidence?> TryFindAsync(
        AgentTask task, AgentSession? session, IReadOnlySet<Guid> knownSessionIds, CancellationToken ct)
    {
        var commits = await TryGitAsync(task, ct);
        var jsonl = TryScanJsonl(task, session, knownSessionIds);

        if (commits.Count == 0 && jsonl is null)
            return null;

        return new DelegateBindRefusalEvidence(commits, jsonl);
    }

    private async Task<IReadOnlyList<string>> TryGitAsync(AgentTask task, CancellationToken ct)
    {
        var directory = task.WorktreePath ?? task.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];
        if (!await _git.IsRepositoryAsync(directory, ct))
            return [];

        IReadOnlyList<string>? lines;
        var worktree = task.Workspace == WorkspaceMode.Worktree
            && task.WorktreeBranch is { Length: > 0 }
            && task.MergeTargetRef is { Length: > 0 };

        // Same LogOnelineAsync reads DelegateCheckProbe.GatherGitAsync already uses.
        // null from git means "could not ask", not "no commits" — that is not evidence.
        if (worktree)
        {
            lines = await _git.LogOnelineAsync(
                directory, task.MergeTargetRef, task.WorktreeBranch!, CommitLimit, since: null, ct);
            if (lines is null)
                return [];
            // The branch is the task's: any commit counts.
            return [.. lines.Select(ParseSha).Where(s => s.Length > 0)];
        }

        lines = await _git.LogOnelineAsync(
            directory, from: null, to: "HEAD", CommitLimit, task.DispatchedAt, ct);
        if (lines is null)
            return [];

        var needles = DistinctiveNeedles(task);
        if (needles.Count == 0)
            return [];

        return [.. lines
            .Where(line => needles.Any(n => n.IsMatch(line)))
            .Select(ParseSha)
            .Where(s => s.Length > 0)];
    }

    private string? TryScanJsonl(AgentTask task, AgentSession? session, IReadOnlySet<Guid> knownSessionIds)
    {
        // This root contains Claude Code transcripts only. A hit for any other tool is therefore
        // wrong-tool evidence, while the tool-agnostic git arm above remains available to all.
        if (task.AgentKind != AgentKind.ClaudeCode)
            return null;

        // C3 cannot be evaluated without a start time; recovering from a file we cannot date is
        // how the 2026-08-09 operator-collision would come back.
        if (session?.StartedAt is not { } startedAt)
            return null;

        var cwd = session.Cwd;
        if (string.IsNullOrWhiteSpace(cwd))
            return null;

        var needles = JsonlNeedles(task);
        if (needles.Count == 0)
            return null;

        if (!Directory.Exists(_projectsRoot))
            return null;

        foreach (var file in EnumerateCandidates(cwd))
        {
            try
            {
                // Claude launches its sessions with AgentSessionId as the transcript filename.
                // A known session other than this task's is categorically somebody else's work.
                if (IsAnotherSessionTranscript(file, task.AgentSessionId, knownSessionIds))
                    continue;

                if (TryMatchJsonl(file, cwd, startedAt, needles))
                    return file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogDebug(ex, "CARD-0085: skipped unreadable transcript {Path}", file);
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateCandidates(string cwd)
    {
        // Prefer Claude's encoded project dir so a production sweep does not walk every
        // conversation on the box. Fall back to a recursive walk only when that dir is missing
        // (encoding drift); C2 still has to match before any later record is treated as evidence.
        var encoded = Path.Combine(_projectsRoot, EncodeClaudeProjectDir(cwd));
        if (Directory.Exists(encoded))
            return Directory.EnumerateFiles(encoded, "*.jsonl", SearchOption.TopDirectoryOnly);

        try
        {
            return Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "CARD-0085: cannot enumerate {Root}", _projectsRoot);
            return [];
        }
    }

    /// <summary>
    /// C2 then C3 then a distinctive-needle search of user records. A C3 refusal stops the read
    /// — using that file would undermine CARD-0006.
    /// </summary>
    private static bool TryMatchJsonl(
        string path, string sessionCwd, DateTime startedAt, IReadOnlyList<Needle> needles)
    {
        using var reader = new StreamReader(path);
        string? cwd = null;
        DateTimeOffset? firstTimestamp = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            var isUserRecord = false;
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                isUserRecord = root.TryGetProperty("type", out var typeEl)
                    && typeEl.ValueKind == JsonValueKind.String
                    && string.Equals(typeEl.GetString(), "user", StringComparison.Ordinal);

                if (cwd is null
                    && root.TryGetProperty("cwd", out var cwdEl)
                    && cwdEl.ValueKind == JsonValueKind.String)
                {
                    cwd = cwdEl.GetString();
                    // C2: no match → skip (never read past the lead).
                    if (!CwdMatches(cwd, sessionCwd))
                        return false;
                }

                if (firstTimestamp is null
                    && root.TryGetProperty("timestamp", out var tsEl)
                    && tsEl.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(
                        tsEl.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsed))
                {
                    firstTimestamp = parsed;
                    // C3: first timestamped record predating the session is the operator-collision.
                    if (parsed.UtcDateTime < startedAt.ToUniversalTime() - EpochSkewSlack)
                        return false;
                }
            }

            // C2 must have matched (or we have not seen a cwd yet — keep scanning the lead).
            if (cwd is not null && isUserRecord && needles.Any(n => n.IsMatch(line)))
                return true;
        }

        return false;
    }

    internal static IReadOnlyList<Needle> DistinctiveNeedles(AgentTask task)
    {
        var list = new List<Needle>(4);
        var shortId = DelegationReportFormatter.Short(task.Id);
        list.Add(Needle.Bounded(shortId));
        list.Add(Needle.Literal(DelegationReportFormatter.TaskMarker(task.Id)));

        foreach (var source in new[] { task.Title, task.Goal })
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            foreach (Match m in CardId.Matches(source))
                list.Add(Needle.Card(m.Value));
        }

        return list;
    }

    private static IReadOnlyList<Needle> JsonlNeedles(AgentTask task) =>
    [
        Needle.Bounded(DelegationReportFormatter.Short(task.Id)),
        Needle.Literal(DelegationReportFormatter.TaskMarker(task.Id)),
    ];

    private static bool IsAnotherSessionTranscript(
        string path, Guid? ownSessionId, IReadOnlySet<Guid> knownSessionIds) =>
        Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var candidateSessionId)
        && candidateSessionId != ownSessionId
        && knownSessionIds.Contains(candidateSessionId);

    /// <summary>Claude's per-cwd project folder: every non-alphanumeric character becomes '-'.</summary>
    internal static string EncodeClaudeProjectDir(string cwd)
    {
        var chars = cwd.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]))
                chars[i] = '-';
        }
        return new string(chars);
    }

    internal static bool CwdMatches(string? candidateCwd, string sessionCwd)
    {
        if (string.IsNullOrWhiteSpace(candidateCwd) || string.IsNullOrWhiteSpace(sessionCwd))
            return false;
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(candidateCwd), Path.GetFullPath(sessionCwd), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string ParseSha(string oneline)
    {
        var span = oneline.AsSpan().Trim();
        var space = span.IndexOf(' ');
        return space < 0 ? span.ToString() : span[..space].ToString();
    }

    private static string ResolveProjectsRoot(string? injected)
    {
        if (!string.IsNullOrWhiteSpace(injected))
            return injected;
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var root = !string.IsNullOrWhiteSpace(configDir)
            ? configDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(root, "projects");
    }

    /// <summary>A distinctive token the recovery scan will accept as "this is our task".</summary>
    internal readonly struct Needle
    {
        private readonly Regex _regex;

        private Needle(Regex regex) => _regex = regex;

        public bool IsMatch(string text) => _regex.IsMatch(text);

        /// <summary>The <c>[antiphon-task:…]</c> marker, taken literally.</summary>
        public static Needle Literal(string value) =>
            new(new Regex(Regex.Escape(value), RegexOptions.CultureInvariant | RegexOptions.Compiled));

        /// <summary>
        /// Same anchored rule <see cref="GitWorkspaceService.ListCommitsByGrepAsync"/> uses so
        /// <c>CARD-0083</c> does not hit <c>CARD-00830</c>.
        /// </summary>
        public static Needle Card(string cardId) =>
            new(new Regex(
                $@"(^|[^A-Za-z0-9]){Regex.Escape(cardId)}([^0-9]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

        /// <summary>8-hex short id, bounded so it cannot match a longer guid fragment.</summary>
        public static Needle Bounded(string shortId) =>
            new(new Regex(
                $@"(^|[^A-Za-z0-9]){Regex.Escape(shortId)}([^A-Za-z0-9]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
    }
}

/// <summary>Positive evidence that a bind-refused session still produced the task's work.</summary>
public sealed record DelegateBindRefusalEvidence(
    IReadOnlyList<string> Commits,
    string? JsonlPath)
{
    public string Describe()
    {
        var bits = new List<string>();
        if (Commits.Count > 0)
            bits.Add("commit " + string.Join(", ", Commits.Take(5)));
        if (JsonlPath is { } path)
            bits.Add("transcript file " + path);
        return bits.Count == 0 ? "unknown evidence" : string.Join("; ", bits);
    }
}

/// <summary>
/// Test seam for <see cref="DelegateBindRefusalRecovery"/>. Production leaves
/// <see cref="ClaudeProjectsRoot"/> null so the helper uses <c>CLAUDE_CONFIG_DIR</c> /
/// <c>~/.claude/projects</c> — the same default the tailer uses.
/// </summary>
public sealed class DelegateBindRefusalRecoverySettings
{
    /// <summary>Override Claude's projects root so tests do not mutate <c>CLAUDE_CONFIG_DIR</c>.</summary>
    public string? ClaudeProjectsRoot { get; set; }
}
