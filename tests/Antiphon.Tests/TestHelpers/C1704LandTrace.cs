using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1704 S-5 (T-D-12): the RunAsync-level git trace of one harness land, normalised so it compares across
/// runs. Each line is <c>channel|directory-class|argv</c>; 40/64-hex ids become <c>&lt;oid&gt;</c>, GUIDs
/// <c>&lt;guid&gt;</c>, the fixture root <c>&lt;root&gt;</c>. Reads compare as a multiset; mutating commands
/// (<see cref="LandingGit.IsMutating"/>) compare in order.
/// </summary>
internal static partial class C1704LandTrace
{
    public static string GoldenPath => Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot,
        "tests", "Antiphon.Tests", "Infrastructure", "C1704", "land-trace-base.txt");

    public static List<string> Normalise(LandingGitFixture fixture)
    {
        var lines = new List<string>();
        for (var i = 0; i < fixture.Git.Commands.Count; i++)
        {
            var (directory, arguments) = fixture.Git.Commands[i];
            var channel = i < fixture.Git.Channels.Count ? fixture.Git.Channels[i] : "?";
            lines.Add(channel + "|" + Classify(fixture, directory) + "|"
                + string.Join(' ', arguments.Select(a => Scrub(fixture, a))));
        }
        return lines;
    }

    public static bool IsMutatingLine(string line)
    {
        var argv = line.Split('|', 3)[2].Split(' ');
        return LandingGit.IsMutating(argv);
    }

    private static string Classify(LandingGitFixture fixture, string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Same(full, fixture.Repository)) return "canonical";
        if (Same(full, fixture.Source)) return "source";
        if (Same(full, fixture.Remote)) return "remote";
        if (full.StartsWith(Path.Combine(fixture.Root, "trees") + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return "land";
        return "other";
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);

    private static string Scrub(LandingGitFixture fixture, string argument)
    {
        var text = argument.Replace(fixture.Root, "<root>", StringComparison.Ordinal).Replace('\\', '/');
        text = Guid().Replace(text, "<guid>");
        text = Oid().Replace(text, "<oid>");
        text = LandId().Replace(text, "${prefix}<id>");
        return text.Replace("\n", "\\n").Replace("|", "\\|");
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])")]
    private static partial Regex Guid();

    /// <summary>The land worktree directory name is 12 random hex digits.</summary>
    [GeneratedRegex("(?<prefix>/trees/land/)[0-9a-f]{12}(?![0-9a-f])")]
    private static partial Regex LandId();

    [GeneratedRegex("(?<![0-9a-f])(?:[0-9a-f]{64}|[0-9a-f]{40})(?![0-9a-f])")]
    private static partial Regex Oid();

    /// <summary>Null when equal; otherwise the first difference, decisive and readable.</summary>
    public static string? Compare(IReadOnlyList<string> golden, IReadOnlyList<string> actual)
    {
        var goldenMutating = golden.Where(IsMutatingLine).ToList();
        var actualMutating = actual.Where(IsMutatingLine).ToList();
        for (var i = 0; i < Math.Max(goldenMutating.Count, actualMutating.Count); i++)
        {
            var g = i < goldenMutating.Count ? goldenMutating[i] : "<none>";
            var a = i < actualMutating.Count ? actualMutating[i] : "<none>";
            if (g != a) return $"mutating entry {i}: golden '{g}' actual '{a}'";
        }
        var reads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in golden.Where(l => !IsMutatingLine(l))) reads[line] = reads.GetValueOrDefault(line) + 1;
        foreach (var line in actual.Where(l => !IsMutatingLine(l))) reads[line] = reads.GetValueOrDefault(line) - 1;
        var differing = reads.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (differing.Count == 0) return null;
        var report = new StringBuilder("read multiset differs (golden minus actual):");
        foreach (var (line, count) in differing.Take(20)) report.Append($"\n  {count:+0;-0} {line}");
        return report.ToString();
    }
}
