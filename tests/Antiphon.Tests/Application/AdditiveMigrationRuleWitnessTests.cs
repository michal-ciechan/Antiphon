using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1785. Pins the operator additive-migration rule and the downgrade-selector obligation.
/// Each test deletes its pinned sentence in memory and requires that assertion to fail.
/// </summary>
[Category("Unit")]
public sealed class AdditiveMigrationRuleWitnessTests
{
    private const string Rule = "Migrations should be additive; Down must fail safely.";

    private const string NotReviewed =
        "A reviewer who only reruns the new migration tests has not reviewed the migration.";

    private const string Affected =
        "migrations, public DTOs/routes, operation labels, remote script functions and statement budgets";

    private const string CodeLine =
        "MIGRATION: grep tests/ for MigrateAsync(<target>), .Down(, CreateIsolatedSchemaAsync then back; run model classes; report red/green counts";

    private const string ReviewLine = "MIGRATION: run downgrade selector; report its count.";

    [Test]
    public void project_context_records_the_additive_migration_rule()
    {
        var text = Read("docs/project-context.md");
        text.ShouldContain("### EF Core migrations", Case.Sensitive);
        AssertOwner(text);
        RejectWithout(text, Rule, AssertOwner);
    }

    [Test]
    public void testing_and_build_records_the_additive_migration_rule()
    {
        var text = Read("docs/testing-and-build.md");
        text.ShouldContain("### Additive migrations (operator rule, 2026-11-07)", Case.Sensitive);
        AssertOwner(text);
        RejectWithout(text, Rule, AssertOwner);
    }

    [Test]
    public void stage_code_runs_the_downgrade_selector()
    {
        var text = Read("server/Bundles/stage-code.md");
        text.ShouldContain(CodeLine, Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        RejectWithout(text, CodeLine, static mutant => mutant.ShouldContain(CodeLine, Case.Sensitive));
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
    }

    private static void AssertOwner(string text)
    {
        text.ShouldContain(Rule, Case.Sensitive);
        text.ShouldContain("MigrationTestDatabase.CreateAsync(<predecessor>)", Case.Sensitive);
        text.ShouldContain("instead of downgrading a latest clone", Case.Sensitive);
        text.ShouldContain("MigrateAsync(<target>)", Case.Sensitive);
        text.ShouldContain("IMigrator.Migrate", Case.Sensitive);
        text.ShouldContain("MigrationTestDatabase.CreateAsync", Case.Sensitive);
        text.ShouldContain(".Down(", Case.Sensitive);
        text.ShouldContain("CreateIsolatedSchemaAsync", Case.Sensitive);
        text.ShouldContain("backwards", Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        text.ShouldContain("owed", Case.Sensitive);
        text.ShouldContain(NotReviewed, Case.Sensitive);
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
        text.ShouldContain("server/Migrations/**", Case.Sensitive);
        text.ShouldContain("MigrateAsync(<target>)", Case.Sensitive);
        text.ShouldContain(".Down(", Case.Sensitive);
        text.ShouldContain("CreateIsolatedSchemaAsync", Case.Sensitive);
        text.ShouldContain("report red/green counts", Case.Sensitive);
        text.ShouldContain("blast radius", Case.Sensitive);
        text.ShouldContain("Existing tests affected", Case.Sensitive);
        text.ShouldContain(Affected, Case.Sensitive);
        text.ShouldContain("CARD-1782", Case.Sensitive);
        text.ShouldContain(NotReviewed, Case.Sensitive);
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
