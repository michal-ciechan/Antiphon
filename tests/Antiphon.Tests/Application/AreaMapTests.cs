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
