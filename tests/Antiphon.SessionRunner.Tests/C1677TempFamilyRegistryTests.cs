using System.Text.RegularExpressions;
using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S2 CP-7. The shipped temp family table is pinned to its producers: each producer file (read from
/// the repository root, not the test's cwd) still contains the literal that creates the family, the
/// <c>pty-test-root</c> alternation equals <see cref="TestSessionLogRoot.KnownPrefixes"/>, and every regex is
/// anchored, exact and matches its measured sample (V-48, V-48b, V-48c).
/// </summary>
[Category("Integration")]
[Timeout(10_000)]
public sealed class C1677TempFamilyRegistryTests
{
    private static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                    return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root (Antiphon.sln).");
    }

    [Test]
    public async Task Every_family_producer_still_contains_its_prefix()
    {
        // V-48 (G-108). Only the CLI-harness family (D-12) has no repository producer.
        var root = RepoRoot();
        var families = RunnerTempSweep.RegisteredFamilies;
        families.Count.ShouldBe(10);
        foreach (var family in families)
        {
            if (family.Id == RunnerTempSweep.ClaudeScratch)
            {
                family.Producer.ShouldBeNull();
                family.DefaultEnabled.ShouldBeFalse();
                continue;
            }
            var producer = family.Producer.ShouldNotBeNull(family.Id);
            var literal = family.Literal.ShouldNotBeNull(family.Id);
            var path = Path.Combine(root, producer);
            File.Exists(path).ShouldBeTrue(family.Id + " producer " + producer);
            (await File.ReadAllTextAsync(path)).ShouldContain(literal, Case.Sensitive, family.Id + " producer " + producer);
        }
    }

    [Test]
    public async Task Pty_test_root_alternation_equals_known_prefixes()
    {
        // V-48b (G-109).
        var family = RunnerTempSweep.RegisteredFamilies.Single(f => f.Id == "pty-test-root");
        var match = Regex.Match(family.Pattern, @"^\^antiphon-\((?<alt>[^()]+)\)-\[0-9a-f\]\{32\}\\z\z");
        match.Success.ShouldBeTrue(family.Pattern);
        var alternation = match.Groups["alt"].Value.Split('|').ToHashSet(StringComparer.Ordinal);
        alternation.SetEquals(TestSessionLogRoot.KnownPrefixes).ShouldBeTrue(
            "missing: " + string.Join(",", TestSessionLogRoot.KnownPrefixes.Except(alternation))
            + " extra: " + string.Join(",", alternation.Except(TestSessionLogRoot.KnownPrefixes)));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Family_regexes_are_anchored_and_exact()
    {
        // V-48c (G-110). Review 12ba9a25 D-1: `$` also matches before a final \n in .NET, so every pattern ends
        // in \z (end of string only) and no line break around a sample matches.
        foreach (var family in RunnerTempSweep.RegisteredFamilies)
        {
            family.Pattern.ShouldStartWith("^", Case.Sensitive, family.Id);
            family.Pattern.ShouldEndWith(@"\z", Case.Sensitive, family.Id);
            family.Pattern.ShouldNotContain("$", Case.Sensitive, family.Id);
            foreach (var loose in new[] { ".*", ".+", @"\S", @"\w", ".?" })
                family.Pattern.ShouldNotContain(loose, Case.Sensitive, family.Id);
            family.Regex.IsMatch(family.Sample).ShouldBeTrue(family.Id + " sample " + family.Sample);
            foreach (var variant in new[]
                     {
                         family.Sample + "x", "x" + family.Sample, family.Sample + "\n", family.Sample + "\r",
                         family.Sample + "\r\n", "\n" + family.Sample,
                     })
                family.Regex.IsMatch(variant).ShouldBeFalse(family.Id + " " + variant.Replace("\r", "\\r").Replace("\n", "\\n"));
        }
        await Task.CompletedTask;
    }
}
