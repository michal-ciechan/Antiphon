using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0665 D-1: splits one reading's git-ignored paths into Protected names, then Evidence, then
/// Disposable, else Protected. A rooted path or a <c>..</c> segment is Protected before any pattern
/// is consulted, and a protected name wins over every directory rule, so a secret copied into a
/// build directory keeps that whole directory from being wholly disposable. A path with a
/// <c>.antiphon</c> segment is Protected unless a pattern that itself names <c>.antiphon</c> matches it.
/// Globs are compiled here rather than through FileSystemGlobbing because the report-spill
/// pattern needs <c>?</c>, which that matcher treats as a literal.
/// </summary>
public sealed class WorktreeIgnoredContentClassifier
{
    private readonly Regex? _protected;
    private readonly Regex? _evidence;
    private readonly Regex? _disposable;
    private readonly Regex? _antiphonEvidence;
    private readonly Regex? _antiphonDisposable;

    public WorktreeIgnoredContentClassifier(IOptions<WorktreeCleanupSettings> options)
    {
        _protected = Compile(options.Value.EffectiveProtectedIgnored);
        _evidence = Compile(options.Value.EffectiveRetainedIgnored);
        _disposable = Compile(options.Value.EffectiveDisposableIgnored);
        _antiphonEvidence = Compile(options.Value.EffectiveRetainedIgnored.Where(NamesAntiphon).ToList());
        _antiphonDisposable = Compile(options.Value.EffectiveDisposableIgnored.Where(NamesAntiphon).ToList());
    }

    public WorktreeIgnoredContent Classify(ImmutableArray<string> ignoredPaths)
    {
        var disposable = new List<string>();
        var evidence = new List<string>();
        var protectedPaths = new List<string>();
        foreach (var raw in ignoredPaths.IsDefault ? [] : ignoredPaths)
        {
            var path = Normalize(raw);
            // A `.antiphon` segment holds agent spills, reports and deliverables wherever it sits;
            // only a pattern that itself names `.antiphon` may release it, never an ancestor's rule.
            var spill = NamesAntiphon(path);
            if (IsRootedOrEscaping(path) || _protected?.IsMatch(path) == true) protectedPaths.Add(path);
            else if ((spill ? _antiphonEvidence : _evidence)?.IsMatch(path) == true) evidence.Add(path);
            else if ((spill ? _antiphonDisposable : _disposable)?.IsMatch(path) == true) disposable.Add(path);
            else protectedPaths.Add(path);
        }
        return new(Sorted(disposable), Sorted(evidence), Sorted(protectedPaths));
    }

    internal static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        return normalized;
    }

    /// <summary>Any <c>.antiphon</c> segment, in any case (a Windows tree does not distinguish them).</summary>
    internal static bool NamesAntiphon(string path) =>
        Normalize(path).Split('/').Any(segment => segment.Equals(".antiphon", StringComparison.OrdinalIgnoreCase));

    /// <summary>A leading <c>/</c>, a drive letter or any <c>..</c> segment.</summary>
    internal static bool IsRootedOrEscaping(string path) =>
        path.StartsWith('/') || path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
        || path.Split('/').Any(segment => segment == "..");

    private static ImmutableArray<string> Sorted(List<string> paths) =>
        [.. paths.Order(StringComparer.Ordinal)];

    private static Regex? Compile(IReadOnlyList<string> patterns)
    {
        var alternatives = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => ToRegex(Normalize(p.Trim()))).ToList();
        return alternatives.Count == 0 ? null
            : new Regex("^(?:" + string.Join("|", alternatives) + ")$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary><c>**/</c> any leading directories, <c>/**</c> anything below, <c>*</c> and <c>?</c> within one segment.</summary>
    private static string ToRegex(string glob)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var atStart = i == 0 || glob[i - 1] == '/';
                var slashAfter = i + 2 < glob.Length && glob[i + 2] == '/';
                if (atStart && slashAfter) { builder.Append("(?:[^/]*/)*"); i += 2; }
                else { builder.Append(".*"); i += 1; }
            }
            else if (c == '*') builder.Append("[^/]*");
            else if (c == '?') builder.Append("[^/]");
            else builder.Append(Regex.Escape(c.ToString()));
        }
        return builder.ToString();
    }
}
