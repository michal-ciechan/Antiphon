using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S2 — the placeholder itself, and THE TRIPWIRE.
///
/// <para>The tripwire is the rule that outlives this card: every launch in the server passes through
/// <c>AgentSessionService.BuildRuntimeLaunchSpec</c> on its way to one of the three
/// <c>adapter.StartAsync</c> sites, so a future path that builds an <c>Env</c> and forgets to
/// resolve it fails its first launch by name instead of exporting a literal token into a real
/// process. These tests drive the tripwire directly, which is exactly the "future forgotten path"
/// case: a spec constructed without going near either resolver.</para>
/// </summary>
[Category("Unit")]
public class ApiKeyPlaceholderTests
{
    [Test]
    public void a_well_formed_token_is_found_and_named()
    {
        ApiKeyPlaceholder.ContainsMarker("{{key:anthropic-maven}}").ShouldBeTrue();
        ApiKeyPlaceholder.Names("Bearer {{key:anthropic-maven}}").ShouldBe(["anthropic-maven"]);
        ApiKeyPlaceholder.Names("{{key:a}} and {{key:b}} and {{key:a}}").ShouldBe(["a", "b"]);
    }

    [Test]
    [Arguments("sk-live-abcdef")]
    [Arguments("{ \"json\": \"in an env var\" }")]
    [Arguments("${SHELL_STYLE}")]
    [Arguments("{singleBrace}")]
    [Arguments("{{notkey:x}}")]
    public void an_ordinary_value_is_left_entirely_alone(string value)
    {
        // The double brace plus the "key:" discriminator is what makes accidental collision
        // effectively impossible — JSON, shell syntax and the single-brace ChannelPreamble style
        // all pass through untouched.
        ApiKeyPlaceholder.ContainsMarker(value).ShouldBeFalse();
        ApiKeyPlaceholder.HasMalformedToken(value).ShouldBeFalse();
    }

    [Test]
    [Arguments("{{key:has space}}")]
    [Arguments("{{key:}}")]
    [Arguments("{{key:unterminated")]
    [Arguments("{{key:has/slash}}")]
    public void a_marker_that_is_not_a_well_formed_token_is_a_malformed_one_not_inert_text(string value)
    {
        // Detection is deliberately looser than resolution. A strict-only check would let
        // {{key:has space}} through as literal text and export it to a child process; loud beats
        // literal, and the operator gets told which variable to fix.
        ApiKeyPlaceholder.ContainsMarker(value).ShouldBeTrue();
        ApiKeyPlaceholder.HasMalformedToken(value).ShouldBeTrue();
    }

    [Test]
    public void a_well_formed_token_beside_a_malformed_one_is_still_reported_malformed()
    {
        ApiKeyPlaceholder.HasMalformedToken("{{key:good}} {{key:not good}}").ShouldBeTrue();
    }

    // ---- the tripwire ---------------------------------------------------------------------------

    [Test]
    public void the_tripwire_refuses_an_unresolved_placeholder_in_an_env_value()
    {
        // The "future forgotten path": a spec that never went through either resolver. It must fail
        // HERE rather than launching a process whose ANTHROPIC_API_KEY is the literal ten-character
        // token, which authenticates as nobody and produces no error anywhere.
        var spec = NewSpec(env: new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = "{{key:anthropic-maven}}",
        });

        var ex = Should.Throw<InvalidOperationException>(
            () => ApiKeyPlaceholder.EnsureResolved(spec, Guid.Empty));

        ex.Message.ShouldContain("ANTHROPIC_API_KEY");
        ex.Message.ShouldContain("{{key:anthropic-maven}}");
        ex.Message.ShouldContain("ApiKeyEnvResolver");
    }

    [Test]
    public void the_tripwire_names_the_variable_and_the_token_and_never_the_value()
    {
        // By the time a spec reaches the tripwire a value may be PARTIALLY resolved — one token
        // substituted, another not — so quoting the value would put a real secret in an exception
        // message that becomes a task failure reason.
        var spec = NewSpec(env: new Dictionary<string, string>
        {
            ["MIXED"] = "sk-real-secret-value and {{key:missing}}",
        });

        var ex = Should.Throw<InvalidOperationException>(
            () => ApiKeyPlaceholder.EnsureResolved(spec, Guid.Empty));

        ex.Message.ShouldContain("MIXED");
        ex.Message.ShouldContain("{{key:missing}}");
        ex.Message.ShouldNotContain("sk-real-secret-value");
    }

    [Test]
    public void the_tripwire_refuses_a_malformed_token_too()
    {
        var spec = NewSpec(env: new Dictionary<string, string>
        {
            ["BROKEN"] = "{{key:has space}}",
        });

        Should.Throw<InvalidOperationException>(
                () => ApiKeyPlaceholder.EnsureResolved(spec, Guid.Empty))
            .Message.ShouldContain("BROKEN");
    }

    [Test]
    public void the_tripwire_refuses_a_placeholder_in_a_launch_argument()
    {
        // Enforced, not documented (plan section 3): an argument is visible to any process lister
        // and is quoted into failure reasons and argv-integrity tests.
        var spec = NewSpec(args: ["--model", "opus", "--header", "Bearer {{key:anthropic-maven}}"]);

        var ex = Should.Throw<InvalidOperationException>(
            () => ApiKeyPlaceholder.EnsureResolved(spec, Guid.Empty));

        ex.Message.ShouldContain("argument 3");
        ex.Message.ShouldContain("environment VALUES only");
    }

    [Test]
    public void the_tripwire_refuses_a_placeholder_in_appended_system_prompt_text()
    {
        // --append-system-prompt is an argument AND its text lands in the transcript, so this is the
        // worst of the two leak surfaces.
        var spec = NewSpec(args: ["--append-system-prompt", "Your key is {{key:anthropic-maven}}."]);

        Should.Throw<InvalidOperationException>(
                () => ApiKeyPlaceholder.EnsureResolved(spec, Guid.Empty))
            .Message.ShouldContain("system-prompt text");
    }

    [Test]
    public void the_tripwire_names_the_session_so_a_failure_reason_says_which_launch_died()
    {
        var sessionId = Guid.NewGuid();
        var spec = NewSpec(env: new Dictionary<string, string> { ["K"] = "{{key:x}}" });

        Should.Throw<InvalidOperationException>(
                () => ApiKeyPlaceholder.EnsureResolved(spec, sessionId))
            .Message.ShouldContain(sessionId.ToString("D"));
    }

    [Test]
    public void a_fully_resolved_spec_passes_the_tripwire_untouched()
    {
        var spec = NewSpec(
            env: new Dictionary<string, string>
            {
                ["ANTHROPIC_API_KEY"] = "sk-resolved",
                ["JSON_CONFIG"] = "{\"a\":1}",
                ["EMPTY"] = string.Empty,
            },
            args: ["--append-system-prompt", "You are an agent. Braces {like this} are fine."]);

        Should.NotThrow(() => ApiKeyPlaceholder.EnsureResolved(spec, Guid.NewGuid()));
    }

    private static AgentLaunchSpec NewSpec(
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyList<string>? args = null) =>
        new(
            DefinitionName: "test",
            Kind: AgentKind.ClaudeCode,
            Exe: "claude.exe",
            Args: args ?? [],
            Env: env ?? new Dictionary<string, string>(),
            Cwd: "C:\\tmp",
            Cols: 120,
            Rows: 30);
}
