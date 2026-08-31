using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Reading a repo's <c>antiphon.areas.json</c> (CARD-0063 S2).
///
/// <para>The load-bearing property is that it CANNOT fail a dispatch: a missing file, a malformed
/// file, an area with no paths — each degrades to "this name is an opaque label", which is exactly
/// the behaviour that predates the map. A bookkeeping field must never refuse a launch.</para>
/// </summary>
[Category("Unit")]
public class AreaMapLoaderTests
{
    [Test]
    public void a_repo_with_no_map_has_no_areas()
    {
        using var repo = new TempRepo();

        var map = NewLoader().Load(repo.Path);

        map.Count.ShouldBe(0);
        map.SourcePath.ShouldBeNull();
    }

    [Test]
    public void a_null_repo_path_has_no_areas()
    {
        NewLoader().Load(null).Count.ShouldBe(0);
        NewLoader().Load("   ").Count.ShouldBe(0);
    }

    [Test]
    public void a_malformed_map_is_empty_rather_than_fatal()
    {
        using var repo = new TempRepo();
        repo.WriteMap("{ \"areas\": { \"delivery\": ");

        var map = NewLoader().Load(repo.Path);

        map.Count.ShouldBe(0, "a broken JSON file must not stop every dispatch in the repo");
    }

    [Test]
    public void an_area_with_no_paths_is_skipped_and_the_rest_of_the_map_survives()
    {
        using var repo = new TempRepo();
        repo.WriteMap("""
            { "areas": {
                "empty":    { "paths": [] },
                "delivery": { "paths": ["server/Application/Services/SessionMessageQueueService*.cs"] }
            } }
            """);

        var map = NewLoader().Load(repo.Path);

        map.Count.ShouldBe(1);
        map.TryGet("delivery", out _).ShouldBeTrue();
        map.TryGet("empty", out _).ShouldBeFalse();
    }

    [Test]
    public void weight_defaults_to_serialise_and_allow_is_parsed()
    {
        using var repo = new TempRepo();
        repo.WriteMap("""
            { "areas": {
                "delivery": { "paths": ["server/**"] },
                "docs":     { "paths": ["docs/**"], "weight": "allow" },
                "odd":      { "paths": ["odd/**"], "weight": "whatever" }
            } }
            """);

        var map = NewLoader().Load(repo.Path);

        map.TryGet("delivery", out var delivery).ShouldBeTrue();
        delivery.Weight.ShouldBe(AreaWeight.Serialise);
        map.TryGet("docs", out var docs).ShouldBeTrue();
        docs.Weight.ShouldBe(AreaWeight.Allow);
        map.TryGet("odd", out var odd).ShouldBeTrue();
        odd.Weight.ShouldBe(AreaWeight.Serialise, "an unknown weight is not a licence to run free");
    }

    [Test]
    public void comments_and_trailing_commas_are_tolerated()
    {
        // The shipped map carries a header comment stating the extension rule; if the reader
        // could not skip comments, the repo's own map would load as empty.
        using var repo = new TempRepo();
        repo.WriteMap("""
            // the repo's areas
            { "areas": {
                "delivery": { "paths": ["server/**"], },
            } }
            """);

        NewLoader().Load(repo.Path).Count.ShouldBe(1);
    }

    [Test]
    public void names_are_matched_case_insensitively()
    {
        using var repo = new TempRepo();
        repo.WriteMap("""{ "areas": { "delivery": { "paths": ["server/**"] } } }""");

        NewLoader().Load(repo.Path).TryGet("DELIVERY", out _).ShouldBeTrue();
    }

    [Test]
    public async Task an_edited_map_is_reloaded_rather_than_served_from_the_cache()
    {
        using var repo = new TempRepo();
        var loader = NewLoader();
        repo.WriteMap("""{ "areas": { "delivery": { "paths": ["server/**"] } } }""");
        loader.Load(repo.Path).Count.ShouldBe(1);

        // The cache is keyed on write time AND length, so a same-size rewrite inside one clock
        // tick would still be caught — but make both move, as a real edit does.
        await Task.Delay(20);
        repo.WriteMap("""
            { "areas": {
                "delivery": { "paths": ["server/**"] },
                "schema":   { "paths": ["server/Migrations/**"] }
            } }
            """);

        var reloaded = loader.Load(repo.Path);

        reloaded.Count.ShouldBe(2, "an edit to the map is live on the next tick, without a restart");
    }

    [Test]
    public void a_map_that_disappears_falls_back_to_no_areas()
    {
        using var repo = new TempRepo();
        var loader = NewLoader();
        repo.WriteMap("""{ "areas": { "delivery": { "paths": ["server/**"] } } }""");
        loader.Load(repo.Path).Count.ShouldBe(1);

        File.Delete(Path.Combine(repo.Path, "antiphon.areas.json"));

        loader.Load(repo.Path).Count.ShouldBe(0);
    }

    private static AreaMapLoader NewLoader() => new(
        Options.Create(new DelegationSettings()), NullLogger<AreaMapLoader>.Instance);

    /// <summary>A throwaway repo root to write an <c>antiphon.areas.json</c> into.</summary>
    private sealed class TempRepo : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-areas-test").FullName;

        public void WriteMap(string json) =>
            File.WriteAllText(System.IO.Path.Combine(Path, "antiphon.areas.json"), json);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}

/// <summary>
/// Resolving a declared scope through a repo's area map: a name becomes the globs its area owns,
/// a path stays itself, and a name nobody declared stays an opaque label (CARD-0063 D1).
/// </summary>
[Category("Unit")]
public class ScopeResolverAreaTests
{
    private static readonly AreaMap Map = BuildMap();

    [Test]
    public void two_areas_that_share_a_path_intersect()
    {
        // `delivery` reaches into Antiphon.Agents.Pty for the composer evidence; `pty` owns the
        // whole project. Areas overlap on PURPOSE — the path sets resolve it.
        Intersects("delivery", "pty").ShouldBeTrue();
    }

    [Test]
    public void two_areas_that_share_nothing_do_not_intersect()
    {
        Intersects("delivery", "board").ShouldBeFalse();
    }

    [Test]
    public void an_area_intersects_a_path_inside_it()
    {
        Intersects("schema", "server/Migrations/20260827_Thing.cs").ShouldBeTrue();
    }

    [Test]
    public void an_area_intersects_itself_and_says_so_by_name()
    {
        var result = Intersect("delivery,docs", "delivery");

        result.Any.ShouldBeTrue();
        result.Areas.ShouldContain("delivery");
        result.Areas.ShouldNotContain("docs", "docs did not take part in the intersection");
    }

    [Test]
    public void an_intersection_only_in_an_allow_area_is_marked_all_allow()
    {
        var result = Intersect("docs", "docs");

        result.Any.ShouldBeTrue();
        result.AllAllow.ShouldBeTrue("two tasks both editing docs cost a bullet-order rebase");
    }

    [Test]
    public void an_intersection_that_also_touches_a_serialising_area_is_not_all_allow()
    {
        var result = Intersect("docs,delivery", "docs,delivery");

        result.Any.ShouldBeTrue();
        result.AllAllow.ShouldBeFalse("the weight is a downgrade, never a blanket exemption");
    }

    [Test]
    public void an_unknown_name_is_a_label_that_matches_only_itself()
    {
        Intersects("card-reopen-cli", "card-reopen-cli").ShouldBeTrue();
        Intersects("card-reopen-cli", "card-reopen-client").ShouldBeFalse();
        Intersects("card-reopen-cli", "delivery").ShouldBeFalse();
        Intersects("card-reopen-cli", "server/**").ShouldBeFalse(
            "an unknown name owns no paths, so it cannot claim a subtree");
    }

    [Test]
    public void unknown_names_are_reported_for_the_create_time_warning()
    {
        var resolved = ScopeResolver.Resolve("delivery,made-up,server/Program.cs", Map);

        resolved.UnknownAreaNames.ShouldBe(["made-up"]);
    }

    [Test]
    public void a_known_area_reports_no_unknown_names()
    {
        ScopeResolver.Resolve("delivery,docs", Map).UnknownAreaNames.ShouldBeEmpty();
    }

    [Test]
    public void a_glob_with_no_literal_prefix_claims_nothing_rather_than_everything()
    {
        // `*.ps1` in an area would otherwise make it intersect the entire tree. A map defect must
        // degrade to "matches nothing", not to "matches everyone".
        Intersects("*.ps1", "server/Program.cs").ShouldBeFalse();
    }

    private static bool Intersects(string a, string b) => Intersect(a, b).Any;

    private static ScopeIntersection Intersect(string a, string b) =>
        ScopeResolver.Intersect(ScopeResolver.Resolve(a, Map), ScopeResolver.Resolve(b, Map));

    private static AreaMap BuildMap() => new(
        new Dictionary<string, AreaDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["delivery"] = new("delivery", [
                "server/Application/Services/SessionMessageQueueService*.cs",
                "src/Antiphon.Agents.Pty/ComposerDeliveryEvidence*.cs",
            ], AreaWeight.Serialise),
            ["pty"] = new("pty", ["src/Antiphon.Agents.Pty/**"], AreaWeight.Serialise),
            ["schema"] = new("schema", ["server/Migrations/**"], AreaWeight.Serialise),
            ["board"] = new("board", ["server/Application/Services/Card*.cs"], AreaWeight.Serialise),
            ["docs"] = new("docs", ["docs/**", "AGENTS.md"], AreaWeight.Allow),
        },
        sourcePath: "in-memory");
}

/// <summary>
/// The repo's OWN <c>antiphon.areas.json</c>, checked against the tree it describes.
///
/// <para>This is what keeps the map honest as the repo changes: a glob whose literal prefix names
/// nothing matches nothing, and an area that matches nothing is a silent no-op — the caller
/// declares it, nobody is ever held, and the drift never surfaces. A rename in the repo goes red
/// HERE rather than quietly disarming an area.</para>
/// </summary>
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

/// <summary>
/// CARD-0254 keeps the universal instruction file inside Codex's default project-document budget.
/// The file is a routing index, so every local owner it names must continue to exist.
/// </summary>
[Category("Unit")]
public class AgentContextContractTests
{
    private const int AgentContextByteLimit = 24_576;

    [Test]
    public void the_universal_agent_context_stays_within_the_raw_utf8_byte_budget()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FindRepoRoot(), "AGENTS.md"));

        bytes.Length.ShouldBeLessThanOrEqualTo(
            AgentContextByteLimit,
            "AGENTS.md is loaded by every project-facing agent; measure raw UTF-8 bytes, not characters");
    }

    [Test]
    public void every_local_owner_document_named_by_the_routing_index_exists()
    {
        var root = FindRepoRoot();
        var core = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var owners = System.Text.RegularExpressions.Regex.Matches(core, @"\]\((docs/[^)]+\.md)\)")
            .Select(match => match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        owners.ShouldNotBeEmpty("the universal core must route a reader to living owner documents");
        var missing = owners
            .Where(owner => !File.Exists(Path.Combine(root, owner)))
            .ToArray();

        missing.ShouldBeEmpty("every document named by AGENTS.md's routing index must exist");
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
