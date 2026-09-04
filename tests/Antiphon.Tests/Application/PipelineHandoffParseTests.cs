using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0146 S2 — the <c>--- next stage ---</c> block parser.</summary>
[Category("Unit")]
public class PipelineHandoffParseTests
{
    [Test]
    [Arguments("investigate", PipelineHandoffKind.Investigate)]
    [Arguments("plan", PipelineHandoffKind.Plan)]
    [Arguments("design", PipelineHandoffKind.Plan)]
    [Arguments("test-design", PipelineHandoffKind.TestDesign)]
    [Arguments("testdesign", PipelineHandoffKind.TestDesign)]
    [Arguments("test design", PipelineHandoffKind.TestDesign)]
    [Arguments("code", PipelineHandoffKind.Code)]
    [Arguments("build", PipelineHandoffKind.Code)]
    [Arguments("execute", PipelineHandoffKind.Code)]
    [Arguments("review", PipelineHandoffKind.Review)]
    [Arguments("verify", PipelineHandoffKind.Review)]
    [Arguments("land", PipelineHandoffKind.Land)]
    [Arguments("merge", PipelineHandoffKind.Land)]
    [Arguments("cleanup", PipelineHandoffKind.Land)]
    [Arguments("decide", PipelineHandoffKind.Decide)]
    [Arguments("none", PipelineHandoffKind.None)]
    [Arguments("PLAN", PipelineHandoffKind.Plan)]
    [Arguments("Build", PipelineHandoffKind.Code)]
    public void every_token_and_alias_normalises_to_the_canonical_kind(
        string token, PipelineHandoffKind expected)
    {
        var parsed = PipelineHandoff.TryParse(Block(token, "handoff line"));
        parsed.Found.ShouldBeTrue();
        parsed.Kind.ShouldBe(expected);
        parsed.RawToken.ShouldBe(token);
        parsed.Handoff.ShouldBe("handoff line");
        PipelineHandoff.HeaderBit(AgentTaskRole.Investigate, parsed)
            .ShouldBe(PipelineHandoff.Token(expected));
    }

    [Test]
    public void a_missing_block_is_not_found()
    {
        var parsed = PipelineHandoff.TryParse(
            "Root cause confirmed.\n[antiphon-report:6e2ec08d done]");
        parsed.Found.ShouldBeFalse();
        parsed.Kind.ShouldBeNull();
        parsed.Handoff.ShouldBeNull();
        parsed.ArtifactPath.ShouldBeNull();
        PipelineHandoff.HeaderBit(AgentTaskRole.Investigate, parsed).ShouldBe("unmarked");
        PipelineHandoff.HeaderBit(AgentTaskRole.Docs, parsed).ShouldBeNull();
    }

    [Test]
    public void two_blocks_picks_the_last_before_the_token()
    {
        var report = """
            --- next stage ---
            next: code
            handoff: first block
            --- next stage ---
            next: plan
            handoff: last block
            [antiphon-report:6e2ec08d done]
            """;
        var parsed = PipelineHandoff.TryParse(report);
        parsed.Found.ShouldBeTrue();
        parsed.Kind.ShouldBe(PipelineHandoffKind.Plan);
        parsed.Handoff.ShouldBe("last block");
    }

    [Test]
    public void a_block_after_the_report_token_is_ignored()
    {
        var report = """
            Findings only.
            [antiphon-report:6e2ec08d done]
            --- next stage ---
            next: plan
            handoff: too late
            """;
        var parsed = PipelineHandoff.TryParse(report);
        parsed.Found.ShouldBeFalse();
        parsed.Kind.ShouldBeNull();
    }

    [Test]
    public void handoff_is_clipped_at_400_characters()
    {
        var longLine = new string('x', 401);
        var parsed = PipelineHandoff.TryParse(Block("plan", longLine));
        parsed.Handoff.ShouldNotBeNull();
        parsed.Handoff!.Length.ShouldBe(400);
        parsed.Handoff.ShouldBe(new string('x', 400));
    }

    [Test]
    public void artifact_is_extracted_even_when_another_docs_path_appears_first()
    {
        var report = """
            See also `docs/superpowers/plans/cited.md` for background.

            --- next stage ---
            next: plan
            handoff: root cause confirmed
            artifact: docs/investigations/real.md
            [antiphon-report:6e2ec08d done]
            """;
        var parsed = PipelineHandoff.TryParse(report);
        parsed.ArtifactPath.ShouldBe("docs/investigations/real.md");
        parsed.Kind.ShouldBe(PipelineHandoffKind.Plan);
    }

    [Test]
    public void artifact_that_does_not_match_the_deliverable_pattern_is_dropped()
    {
        var parsed = PipelineHandoff.TryParse(
            Block("plan", "ok", artifact: "C:/src/not-a-repo-relative.md"));
        parsed.ArtifactPath.ShouldBeNull();
        parsed.Kind.ShouldBe(PipelineHandoffKind.Plan);
    }

    [Test]
    public void an_unrecognised_token_keeps_the_raw_value_and_no_kind()
    {
        var parsed = PipelineHandoff.TryParse(Block("deploy-prod", "ship it"));
        parsed.Found.ShouldBeTrue();
        parsed.Kind.ShouldBeNull();
        parsed.RawToken.ShouldBe("deploy-prod");
        parsed.Handoff.ShouldBe("ship it");
        PipelineHandoff.HeaderBit(AgentTaskRole.Code, parsed)
            .ShouldBe("unrecognised:deploy-prod");
    }

    [Test]
    public void unrecognised_token_is_clipped_to_24_characters_in_the_header_bit()
    {
        const string raw = "this-token-is-way-too-long-to-fit";
        var parsed = PipelineHandoff.TryParse(Block(raw, "x"));
        PipelineHandoff.HeaderBit(AgentTaskRole.Plan, parsed)
            .ShouldBe("unrecognised:" + raw[..24]);
        raw[..24].Length.ShouldBe(24);
    }

    [Test]
    public void backticked_artifact_strips_the_ticks()
    {
        var parsed = PipelineHandoff.TryParse(
            Block("plan", "ok", artifact: "`docs/investigations/real.md`"));
        parsed.ArtifactPath.ShouldBe("docs/investigations/real.md");
    }

    [Test]
    public void null_or_empty_report_is_not_found()
    {
        PipelineHandoff.TryParse(null).Found.ShouldBeFalse();
        PipelineHandoff.TryParse("").Found.ShouldBeFalse();
        PipelineHandoff.TryParse("   ").Found.ShouldBeFalse();
    }

    private static string Block(string next, string handoff, string? artifact = null)
    {
        var artifactLine = artifact is null ? "" : $"\nartifact: {artifact}";
        return $"""
            Findings.

            --- next stage ---
            next: {next}
            handoff: {handoff}{artifactLine}
            [antiphon-report:6e2ec08d done]
            """;
    }
}
