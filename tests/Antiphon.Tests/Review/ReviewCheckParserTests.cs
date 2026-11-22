using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>The check file format (design 9): front matter, audience sections outside fences, and the prompt hash.</summary>
[Category("Unit")]
public class ReviewCheckParserTests
{
    private const string Check = """
        ---
        id: CHK-0142
        title: Lock released before async save completes
        severity: major
        flags: [Data-Loss]
        status: active
        requiresHuman: true
        triggers: { globs: ["**/*.cs"], languages: [csharp], tags: [concurrency, persistence], symbols: ["SaveChangesAsync"] }
        relatedFiles: ["**/DbContext*.cs"]
        revisions:
          - { v: 3, date: 2026-11-21, why: "narrowed to EF SaveChanges" }
        ---
        Ignored preamble.
        # @model: Selector
        Does the change release a lock around an async save?
        # @model: Prompt
        What to look for.
        ## A heading inside the prompt is content
        ```csharp
        # @model: Not a section, it is inside a fence
        _lock.Release();
        ```
        Still the prompt.
        # @humans: Why this check exists
        Short text for the review UI.
        # @maintainers: Rationale
        The incident it came from.
        """;

    [Test]
    public void Front_matter_and_sections_parse_and_markers_inside_fences_are_content()
    {
        var result = ReviewCheckParser.Parse(Check, "checks/concurrency/CHK-0142.md", "concurrency");

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Contains("before the first"));
        var check = result.Check.ShouldNotBeNull();
        check.Id.ShouldBe("CHK-0142");
        check.Category.ShouldBe("concurrency");
        check.Severity.ShouldBe(ReviewSeverity.Major);
        check.Flags.ShouldBe(["data-loss"]);
        check.RequiresHuman.ShouldBeTrue();
        check.IsActive.ShouldBeTrue();
        check.Triggers.Globs.ShouldBe(["**/*.cs"]);
        check.Triggers.Symbols.ShouldBe(["SaveChangesAsync"]);
        check.RelatedFiles.ShouldBe(["**/DbContext*.cs"]);
        check.Sections.Select(s => $"{s.Audience}:{s.Name}").ShouldBe(
            ["model:Selector", "model:Prompt", "humans:Why this check exists", "maintainers:Rationale"]);
        check.Selector.ShouldBe("Does the change release a lock around an async save?");
        var prompt = check.Section("model", "Prompt")!.Body;
        prompt.ShouldContain("## A heading inside the prompt is content");
        prompt.ShouldContain("# @model: Not a section, it is inside a fence");
        prompt.ShouldEndWith("Still the prompt.");
        check.HumansWhy.ShouldBe("Short text for the review UI.");
    }

    [Test]
    public void The_prompt_hash_covers_model_sections_only()
    {
        var original = ReviewCheckParser.Parse(Check, "a.md").Check!;
        var rationaleEdited = ReviewCheckParser.Parse(Check.Replace("The incident it came from.", "A different incident."), "a.md").Check!;
        var whyEdited = ReviewCheckParser.Parse(Check.Replace("Short text for the review UI.", "Other text."), "a.md").Check!;
        var promptEdited = ReviewCheckParser.Parse(Check.Replace("Still the prompt.", "Changed prompt."), "a.md").Check!;

        rationaleEdited.PromptHash.ShouldBe(original.PromptHash);
        whyEdited.PromptHash.ShouldBe(original.PromptHash);
        promptEdited.PromptHash.ShouldNotBe(original.PromptHash);
        original.PromptHash.Length.ShouldBe(64);
    }

    [Test]
    public void Missing_front_matter_and_bad_values_are_errors()
    {
        ReviewCheckParser.Parse("# @model: Prompt\ntext", "x.md").Errors.ShouldHaveSingleItem().ShouldContain("front matter");
        var bad = ReviewCheckParser.Parse("---\nid: X\ntitle: T\nseverity: huge\nstatus: draft\n---\n# @model: Prompt\np", "x.md");
        bad.Check.ShouldBeNull();
        bad.Errors.ShouldContain(e => e.Contains("'severity'"));
        bad.Errors.ShouldContain(e => e.Contains("'status'"));
    }

    [Test]
    public void The_built_in_seed_set_parses()
    {
        var problems = new List<string>();
        var seeds = new ReviewCheckCatalogue(Options.Create(new ReviewSettings()), NullLogger<ReviewCheckCatalogue>.Instance).LoadSeeds(problems);

        problems.ShouldBeEmpty();
        seeds.Count.ShouldBe(7);
        seeds.ShouldAllBe(c => c.IsActive && c.ModelSections.Any());
        seeds.Single(c => c.Id == "SEED-0002").Flags.ShouldBe(["security"]);
        seeds.Single(c => c.Id == "SEED-0003").Category.ShouldBe("data");
    }
}
