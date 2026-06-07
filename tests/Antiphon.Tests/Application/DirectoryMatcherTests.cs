using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DirectoryMatcher"/> — the fuzzy/partial/BM25 ranking that powers
/// the working-directory autocomplete. Inputs are the typed leaf segment (e.g. "lea") and the
/// candidate child paths; output is the matching subset ranked best-first.
/// </summary>
[Category("Unit")]
public class DirectoryMatcherTests
{
    [Test]
    public void partial_match_surfaces_substring_within_name()
    {
        // The headline case: "/lea" must match "torquay-leander" even though the name does not
        // *start* with "lea" — "lea" is a substring (and a token prefix) inside it.
        var children = new[] { "C:/src/torquay-leander", "C:/src/other", "C:/src/projects" };

        var ranked = DirectoryMatcher.Rank("lea", children);

        ranked.ShouldContain("C:/src/torquay-leander");
        ranked.ShouldNotContain("C:/src/other");
        ranked.ShouldNotContain("C:/src/projects");
    }

    [Test]
    public void empty_query_returns_all_children_unchanged()
    {
        var children = new[] { "C:/src/alpha", "C:/src/beta" };

        DirectoryMatcher.Rank("", children).ShouldBe(children);
    }

    [Test]
    public void prefix_matches_rank_before_substring_matches()
    {
        // "src" as a prefix beats "src" buried in the middle of a name.
        var children = new[] { "C:/x/my-src-dir", "C:/x/src" };

        var ranked = DirectoryMatcher.Rank("src", children);

        ranked[0].ShouldBe("C:/x/src");
    }

    [Test]
    public void substring_matches_rank_before_pure_subsequence_matches()
    {
        // "abc" contiguous beats "a..b..c" scattered as a subsequence.
        var children = new[] { "C:/x/axbxcx", "C:/x/abcdef" };

        var ranked = DirectoryMatcher.Rank("abc", children);

        ranked[0].ShouldBe("C:/x/abcdef");
        ranked.ShouldContain("C:/x/axbxcx"); // still included as a fuzzy match
    }

    [Test]
    public void word_boundary_match_ranks_before_mid_word_match()
    {
        var children = new[] { "C:/x/smart-things", "C:/x/art-gallery" };

        var ranked = DirectoryMatcher.Rank("art", children);

        ranked[0].ShouldBe("C:/x/art-gallery"); // "art" starts a word here
    }

    [Test]
    public void bm25_ranks_rarer_token_match_above_common_token_match()
    {
        // "se" is a prefix of both "sentinel" and "service". "service" is a common token across the
        // sibling set, so its IDF is low; "sentinel" is unique, so BM25 lifts it above the equally
        // prefix-matching "service-x".
        var children = new[]
        {
            "C:/x/alpha-service",
            "C:/x/beta-service",
            "C:/x/service-x",
            "C:/x/sentinel",
        };

        var ranked = DirectoryMatcher.Rank("se", children).ToList();

        ranked.IndexOf("C:/x/sentinel").ShouldBeLessThan(ranked.IndexOf("C:/x/service-x"));
    }

    [Test]
    public void non_matching_query_returns_empty()
    {
        var children = new[] { "C:/src/alpha", "C:/src/beta" };

        DirectoryMatcher.Rank("zzz", children).ShouldBeEmpty();
    }
}
