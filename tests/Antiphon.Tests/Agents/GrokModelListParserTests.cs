using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class GrokModelListParserTests
{
    [Test]
    public void Parses_the_measured_grok_models_prose()
    {
        const string output =
            """
            You are logged in with grok.com.

            Default model: grok-4.6

            Available models:
              * grok-4.6 (default)
              - grok-4.5
            """;

        var models = GrokModelListParser.Parse(output);
        models.ShouldBe(["grok-4.6", "grok-4.5"]);
    }

    [Test]
    public void Empty_or_prose_without_identifiers_returns_nothing()
    {
        GrokModelListParser.Parse(null).ShouldBeEmpty();
        GrokModelListParser.Parse("").ShouldBeEmpty();
        GrokModelListParser.Parse("You are logged in with grok.com.").ShouldBeEmpty();
    }
}
