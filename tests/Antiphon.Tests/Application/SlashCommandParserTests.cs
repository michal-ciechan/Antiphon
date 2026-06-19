using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SlashCommandParserTests
{
    [Test]
    public void Uses_frontmatter_description_when_present()
    {
        const string content = """
            ---
            name: docker-desktop
            description: Start, restart, or fix Docker Desktop.
            ---
            # Docker Desktop
            Body text here.
            """;

        SlashCommandParser.Describe(content, "fallback").ShouldBe("Start, restart, or fix Docker Desktop.");
    }

    [Test]
    public void Falls_back_to_first_body_line_when_no_frontmatter()
    {
        const string content = """
            # antiphon-run — Antiphon Dev Stack Manager

            Use this skill when the user wants to start the dev stack.
            """;

        // Leading '#' is stripped; first non-empty line wins.
        SlashCommandParser.Describe(content, "antiphon-run")
            .ShouldBe("antiphon-run — Antiphon Dev Stack Manager");
    }

    [Test]
    public void Falls_back_to_supplied_name_when_empty()
    {
        SlashCommandParser.Describe("", "myskill").ShouldBe("myskill");
        SlashCommandParser.Describe("   \n  \n", "myskill").ShouldBe("myskill");
    }

    [Test]
    public void Malformed_yaml_degrades_to_body_line()
    {
        const string content = """
            ---
            name: broken
            description: "unterminated
              : : not valid yaml : :
            ---
            Sensible body description.
            """;

        SlashCommandParser.Describe(content, "fallback").ShouldBe("Sensible body description.");
    }

    [Test]
    public void Frontmatter_without_description_uses_body()
    {
        const string content = """
            ---
            name: only-name
            ---
            The body explains it.
            """;

        SlashCommandParser.Describe(content, "fallback").ShouldBe("The body explains it.");
    }

    [Test]
    public void Long_description_is_truncated()
    {
        var longText = new string('x', 500);
        var content = $"---\ndescription: {longText}\n---\nbody";

        var result = SlashCommandParser.Describe(content, "fallback");

        result.Length.ShouldBeLessThanOrEqualTo(201); // 200 + ellipsis
        result.ShouldEndWith("…");
    }

    [Test]
    public void Split_frontmatter_returns_null_when_absent()
    {
        var (frontmatter, body) = SlashCommandParser.SplitFrontmatter("# Just a heading\nbody");
        frontmatter.ShouldBeNull();
        body.ShouldBe("# Just a heading\nbody");
    }
}
