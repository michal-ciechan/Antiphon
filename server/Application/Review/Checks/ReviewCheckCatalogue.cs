using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review.Checks;

/// <summary>The checks a run selects from, where they came from, and the files that did not parse.</summary>
public sealed record ReviewCheckSet(IReadOnlyList<ReviewCheck> Checks, string Sources, IReadOnlyList<string> Problems);

/// <summary>
/// Loads the check catalogue (design 9, 10): the instance folder (<c>Review:InstanceChecksPath</c>),
/// then the repo's <c>.antiphon/review/checks/</c> at the base SHA through the code host when it can
/// read files (D-8: never the head's). A repo check overrides an instance check with the same id.
/// With neither, the built-in seed set is used. A file that does not parse is skipped and reported.
/// </summary>
public sealed class ReviewCheckCatalogue(IOptions<ReviewSettings> settings, ILogger<ReviewCheckCatalogue> logger)
{
    public const string RepoChecksDirectory = ".antiphon/review/checks";
    public const string SeedResourcePrefix = "Antiphon.Server.Review.Checks.Seed/";

    public async Task<ReviewCheckSet> LoadAsync(CodeHostBinding binding, string baseSha, CancellationToken ct)
    {
        var problems = new List<string>();
        var byId = new Dictionary<string, ReviewCheck>(StringComparer.OrdinalIgnoreCase);
        var instance = LoadInstance(problems);
        foreach (var check in instance)
            byId[check.Id] = check;

        var repo = new List<ReviewCheck>();
        if (binding.Host.Capabilities.CanReadFiles)
        {
            var paths = await binding.Host.ListFilesAsync(binding.Target, baseSha, RepoChecksDirectory, ct);
            foreach (var path in paths.Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            {
                var text = await binding.Host.ReadFileAsync(binding.Target, baseSha, path, ct);
                if (text is null)
                    continue;
                var relative = path[(RepoChecksDirectory.Length + 1)..];
                if (Add(ReviewCheckParser.Parse(text, path, CategoryOf(relative)), problems) is { } check)
                    repo.Add(check);
            }
        }

        foreach (var check in repo)
            byId[check.Id] = check;

        if (byId.Count > 0)
        {
            var sources = string.Join(", ", new[] { (instance.Count, "instance"), (repo.Count, "repo") }
                .Where(s => s.Item1 > 0).Select(s => $"{s.Item1} {s.Item2}"));
            return new ReviewCheckSet(byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList(), sources, problems);
        }

        var seeds = LoadSeeds(problems);
        return new ReviewCheckSet(seeds, $"{seeds.Count} built-in seed", problems);
    }

    private List<ReviewCheck> LoadInstance(List<string> problems)
    {
        var root = settings.Value.InstanceChecksPath;
        if (string.IsNullOrWhiteSpace(root))
            return [];
        if (!Directory.Exists(root))
        {
            problems.Add($"Review:InstanceChecksPath '{root}' does not exist.");
            return [];
        }

        var checks = new List<ReviewCheck>();
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Add(ReviewCheckParser.Parse(File.ReadAllText(file), $"instance:{relative}", CategoryOf(relative)), problems) is { } check)
                checks.Add(check);
        }

        return checks;
    }

    /// <summary>The embedded seed set (<c>Application/Review/Checks/Seed/&lt;category&gt;/&lt;id&gt;.md</c>).</summary>
    public List<ReviewCheck> LoadSeeds(List<string> problems)
    {
        var assembly = typeof(ReviewCheckCatalogue).Assembly;
        var checks = new List<ReviewCheck>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(SeedResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var relative = name[SeedResourcePrefix.Length..].Replace('\\', '/');
            if (Add(ReviewCheckParser.Parse(reader.ReadToEnd(), $"seed:{relative}", CategoryOf(relative)), problems) is { } check)
                checks.Add(check);
        }

        return checks.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    private ReviewCheck? Add(ReviewCheckParseResult result, List<string> problems)
    {
        problems.AddRange(result.Errors);
        foreach (var warning in result.Warnings)
            logger.LogDebug("Review check: {Warning}", warning);
        return result.Check;
    }

    /// <summary>The first folder of <c>&lt;category&gt;/&lt;id&gt;.md</c>, or <c>general</c> for a file at the root.</summary>
    private static string CategoryOf(string relative) => relative.Contains('/') ? relative[..relative.IndexOf('/')] : "general";
}
