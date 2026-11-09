using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1785. Pins the operator additive-migration rule and the downgrade-selector obligation.
/// Each test deletes its pinned sentence in memory and requires that assertion to fail.
/// CARD-1789 adds the sort-after-master clause, one owner paragraph, and selector tokens that
/// name real call sites.
/// </summary>
[Category("Unit")]
public sealed class AdditiveMigrationRuleWitnessTests
{
    private const string Rule = "Migrations should be additive; Down must fail safely.";

    // CARD-1789 A-1: operator rule 2026-11-07, reaffirmed 2026-11-08.
    private const string SortClause = "sorted after master's latest migration";

    // CARD-1789 A-2: the real call sites are GetService<IMigrator>().MigrateAsync(.
    private const string MigratorToken = "GetService<IMigrator>().MigrateAsync(";

    private const string OwnerHeading = "### Additive migrations (operator rule, 2026-11-07)";

    private const string OwnerLink = "testing-and-build.md#additive-migrations-operator-rule-2026-11-07";

    private const string SelectorSentence = "The downgrade selector greps `tests/` for ";

    private const string NotReviewed =
        "A reviewer who only reruns the new migration tests has not reviewed the migration.";

    private const string Affected =
        "migrations, public DTOs/routes, operation labels, remote script functions and statement budgets";

    private const string CodeLine =
        "MIGRATION: additive, sorted after master's latest; Down fails safely. Run the migrations row (downgrade selector, model classes); report red/green counts";

    private const string ReviewLine = "MIGRATION: run downgrade selector; report its count.";

    [Test]
    public void project_context_links_the_single_migration_owner()
    {
        var text = Read("docs/project-context.md");
        text.ShouldContain("### EF Core migrations", Case.Sensitive);
        AssertLink(text);
        RejectWithout(text, SortClause, AssertLink);
        RejectWithout(text, OwnerLink, AssertLink);
        Should.Throw<ShouldAssertException>(() => AssertLink(text + "\n" + SelectorSentence));
    }

    [Test]
    public void testing_and_build_records_the_additive_migration_rule()
    {
        var text = Read("docs/testing-and-build.md");
        text.ShouldContain(OwnerHeading, Case.Sensitive);
        AssertOwner(text);
        RejectWithout(text, Rule, AssertOwner);
        RejectWithout(text, SortClause, AssertOwner);
        RejectWithout(text, MigratorToken, AssertOwner);
    }

    [Test]
    public void owner_selector_tokens_each_name_a_real_test_call_site()
    {
        var text = Read("docs/testing-and-build.md");
        var sources = TestSources();
        AssertTokensReal(text, sources);
        // The CARD-1785 token matched only this witness file.
        var stale = text.Replace("`" + MigratorToken + "`", "`IMigrator.Migrate`", StringComparison.Ordinal);
        stale.ShouldNotBe(text);
        Should.Throw<ShouldAssertException>(() => AssertTokensReal(stale, sources));
    }

    [Test]
    public void stage_code_runs_the_downgrade_selector()
    {
        var text = Read("server/Bundles/stage-code.md");
        text.ShouldContain(CodeLine, Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        RejectWithout(text, CodeLine, static mutant => mutant.ShouldContain(CodeLine, Case.Sensitive));
        RejectWithout(text, "sorted after master's latest", static mutant => mutant.ShouldContain(CodeLine, Case.Sensitive));
    }

    [Test]
    public void stage_review_runs_the_downgrade_selector()
    {
        var text = Read("server/Bundles/stage-review.md");
        text.ShouldContain(ReviewLine, Case.Sensitive);
        RejectWithout(text, ReviewLine, static mutant => mutant.ShouldContain(ReviewLine, Case.Sensitive));
    }

    [Test]
    public void stage_plan_requires_existing_tests_affected()
    {
        var text = Read("server/Bundles/stage-plan.md");
        AssertAffected(text);
        RejectWithout(text, "Existing tests affected", AssertAffected);
    }

    [Test]
    public void stage_test_design_requires_existing_tests_affected()
    {
        var text = Read("server/Bundles/stage-test-design.md");
        AssertAffected(text);
        RejectWithout(text, "Existing tests affected", AssertAffected);
    }

    [Test]
    public void orchestrator_skill_requires_the_migration_blast_radius()
    {
        var text = Read(".claude/skills/antiphon-orchestrator/SKILL.md");
        AssertSkill(text);
        RejectWithout(text, NotReviewed, AssertSkill);
        RejectWithout(text, SortClause, AssertSkill);
        RejectWithout(text, MigratorToken, AssertSkill);
        RejectWithout(text, "MigrationTestDatabase.CreateAsync", AssertSkill);
    }

    private static void AssertOwner(string text)
    {
        text.ShouldContain(Rule, Case.Sensitive);
        text.ShouldContain(SortClause, Case.Sensitive);
        text.ShouldContain("MigrationTestDatabase.CreateAsync(<predecessor>)", Case.Sensitive);
        text.ShouldContain("instead of downgrading a latest clone", Case.Sensitive);
        text.ShouldContain("MigrateAsync(<target>)", Case.Sensitive);
        text.ShouldContain(MigratorToken, Case.Sensitive);
        text.ShouldNotContain("`IMigrator.Migrate`", Case.Sensitive);
        text.ShouldContain("MigrationTestDatabase.CreateAsync", Case.Sensitive);
        text.ShouldContain(".Down(", Case.Sensitive);
        text.ShouldContain("CreateIsolatedSchemaAsync", Case.Sensitive);
        text.ShouldContain("backwards", Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        text.ShouldContain("owed", Case.Sensitive);
        text.ShouldContain(NotReviewed, Case.Sensitive);
    }

    // CARD-1789 A-3: project-context keeps the rule and links the owner; the selector paragraph
    // lives only in testing-and-build.md.
    private static void AssertLink(string text)
    {
        text.ShouldContain(Rule, Case.Sensitive);
        text.ShouldContain(SortClause, Case.Sensitive);
        text.ShouldContain(OwnerLink, Case.Sensitive);
        text.ShouldNotContain(SelectorSentence, Case.Sensitive);
    }

    private static void AssertAffected(string text)
    {
        text.ShouldContain("Existing tests affected", Case.Sensitive);
        text.ShouldContain(Affected, Case.Sensitive);
        text.ShouldContain("CARD-1782", Case.Sensitive);
    }

    private static void AssertSkill(string text)
    {
        text.ShouldContain(Rule, Case.Sensitive);
        text.ShouldContain(SortClause, Case.Sensitive);
        text.ShouldContain("server/Migrations/**", Case.Sensitive);
        text.ShouldContain("MigrateAsync(<target>)", Case.Sensitive);
        text.ShouldContain(MigratorToken, Case.Sensitive);
        text.ShouldContain("MigrationTestDatabase.CreateAsync", Case.Sensitive);
        text.ShouldContain(".Down(", Case.Sensitive);
        text.ShouldContain("CreateIsolatedSchemaAsync", Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        text.ShouldContain("blast radius", Case.Sensitive);
        text.ShouldContain("Existing tests affected", Case.Sensitive);
        text.ShouldContain(Affected, Case.Sensitive);
        text.ShouldContain("CARD-1782", Case.Sensitive);
        text.ShouldContain(NotReviewed, Case.Sensitive);
    }

    // Every backticked token of the owner's selector sentence must occur in a tests/ source other
    // than this witness. A lowercase <placeholder> stands for one non-empty argument.
    private static void AssertTokensReal(string text, IReadOnlyList<string> sources)
    {
        var section = text.IndexOf(OwnerHeading, StringComparison.Ordinal);
        section.ShouldBeGreaterThanOrEqualTo(0, "owner section");
        var start = text.IndexOf(SelectorSentence, section, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(section, "selector sentence in the owner section");
        start += SelectorSentence.Length;
        var end = text.IndexOf(". ", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, "selector sentence end");
        var tokens = Regex.Matches(text[start..end], "`([^`]+)`").Select(match => match.Groups[1].Value).ToArray();
        tokens.Length.ShouldBeGreaterThanOrEqualTo(6, string.Join(", ", tokens));
        var unmatched = tokens
            .Where(token =>
            {
                var pattern = new Regex(Regex.Replace(Regex.Escape(token), "<[a-z]+>", @"[^)\s][^)]*"), RegexOptions.CultureInvariant);
                return !sources.Any(pattern.IsMatch);
            })
            .ToArray();
        unmatched.ShouldBeEmpty("selector tokens with no tests/ call site outside the witness");
    }

    private static IReadOnlyList<string> TestSources()
    {
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "tests");
        var self = Path.Combine(root, "Antiphon.Tests", "Application", nameof(AdditiveMigrationRuleWitnessTests) + ".cs");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(self), StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        files.Length.ShouldBeGreaterThan(100, "tests/ sources");
        return files;
    }

    private static void RejectWithout(string text, string sentence, Action<string> assert)
    {
        var mutant = text.Replace(sentence, "", StringComparison.Ordinal);
        mutant.ShouldNotBe(text);
        Should.Throw<ShouldAssertException>(() => assert(mutant));
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));
}
