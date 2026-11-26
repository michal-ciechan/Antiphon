using Antiphon.Tests.Application;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

/// <summary>
/// The repo's OWN <c>antiphon.areas.json</c>, checked against the tree it describes.
///
/// <para>This is what keeps the map honest as the repo changes: a glob whose literal prefix names
/// nothing matches nothing, and an area that matches nothing is a silent no-op — the caller
/// declares it, nobody is ever held, and the drift never surfaces. A rename in the repo goes red
/// HERE rather than quietly disarming an area.</para>
/// </summary>
[Category("RepositoryInvariant")]
[Category("Unit")]
public class AreaMapContractTests
{
    [Test]
    public void the_repos_own_map_loads()
    {
        Map.Count.ShouldBeGreaterThan(0, "antiphon.areas.json must parse — a broken map is silent");
        Map.SourcePath.ShouldNotBeNull();
    }

    [Test]
    public void every_glob_names_something_that_exists_in_the_tree()
    {
        var paths = TreePaths();
        var missing = new List<string>();

        foreach (var area in Map.Areas)
        foreach (var glob in area.Paths)
        {
            var prefix = ScopeResolver.LiteralPrefix(glob);
            if (prefix.Length == 0 || !paths.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                missing.Add($"{area.Name}: {glob} (literal prefix '{prefix}')");
        }

        missing.ShouldBeEmpty(
            "every area glob must reach real files — an area that matches nothing holds nobody:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Test]
    public void no_glob_has_an_empty_literal_prefix()
    {
        // A leading wildcard ("*.ps1", "**/Foo.cs") collapses to nothing, and a glob that claims
        // nothing is indistinguishable from an area nobody declared.
        foreach (var area in Map.Areas)
        foreach (var glob in area.Paths)
        {
            ScopeResolver.LiteralPrefix(glob).Length
                .ShouldBeGreaterThan(0, $"{area.Name}: '{glob}' starts with a wildcard");
        }
    }

    [Test]
    public void no_glob_collapses_to_a_directory_another_area_owns_wholesale()
    {
        // A wildcard in the FILE name ("Services/*Profile.cs") makes the literal prefix the whole
        // directory, so the area quietly swallows everything in it. Caught by asserting that no
        // glob's literal prefix is a bare directory that also contains another area's files —
        // approximated as "a prefix ending in '/' must be a real directory the area means to own".
        foreach (var area in Map.Areas)
        foreach (var glob in area.Paths)
        {
            var prefix = ScopeResolver.LiteralPrefix(glob);
            if (!prefix.EndsWith('/'))
                continue;

            // A directory-wide claim is legitimate only when the glob asks for the whole subtree.
            glob.Replace('\\', '/').EndsWith("**", StringComparison.Ordinal).ShouldBeTrue(
                $"{area.Name}: '{glob}' collapses to the directory '{prefix}' — put the literal "
                + "part of the file name before the wildcard, or claim the subtree explicitly");
        }
    }

    [Test]
    public void docs_is_the_only_area_weighted_allow()
    {
        Map.Areas
            .Where(a => a.Weight == AreaWeight.Allow)
            .Select(a => a.Name)
            .ShouldBe(["docs"]);
    }

    [Test]
    public void every_area_name_is_a_name_and_not_a_path()
    {
        // A name containing a separator, a dot or a wildcard would be parsed as a PATH by every
        // caller that wrote it, and the area would never resolve.
        foreach (var area in Map.Areas)
        {
            ScopeResolver.Parse(area.Name)[0].IsPath
                .ShouldBeFalse($"area '{area.Name}' would be read as a path glob, never as a name");
        }
    }

    private static AreaMap Map { get; } = new AreaMapLoader(
        Options.Create(new DelegationSettings()), NullLogger<AreaMapLoader>.Instance)
        .Load(FindRepoRoot());

    /// <summary>
    /// Repo-relative paths, forward-slashed, excluding build output and package directories — the
    /// same shape <see cref="ScopeResolver.LiteralPrefix"/> produces.
    /// </summary>
    private static IReadOnlyList<string> TreePaths()
    {
        var root = FindRepoRoot();
        var results = new List<string>();
        Walk(new DirectoryInfo(root), string.Empty, results, depth: 0);
        return results;

        static void Walk(DirectoryInfo directory, string prefix, List<string> into, int depth)
        {
            // 6 levels is deeper than any literal prefix in the map and keeps the walk cheap.
            if (depth > 6)
                return;

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                var relative = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
                if (entry is DirectoryInfo child)
                {
                    if (IsNoise(child.Name))
                        continue;
                    into.Add(relative + "/");
                    Walk(child, relative, into, depth + 1);
                }
                else
                {
                    into.Add(relative);
                }
            }
        }

        static bool IsNoise(string name) =>
            name is "bin" or "obj" or "node_modules" or ".git" or "TestResults" or "dist"
            || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }
}
