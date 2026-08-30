using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0099 S3 — the Codex-only pieces of a launch command line, and the measurements behind them.
///
/// <para>All measured against codex-cli 0.147.0 on 2026-08-20 through <c>codex debug prompt-input</c>,
/// which renders the model-visible prompt list as JSON and costs no model turns. Two of these are
/// facts a reader would otherwise have to take on trust, and one of them contradicts the plan.</para>
///
/// <para><b>The composed command line was also run against the real binary</b> (same date, same
/// version, no model turns spent): <c>codex.cmd --no-alt-screen
/// --dangerously-bypass-approvals-and-sandbox --model gpt-5.6-luna -c model_reasoning_effort=low -c
/// developer_instructions=&lt;multi-line body&gt;</c> exits <b>1</b> with "Error: stdin is not a
/// terminal" — i.e. it parsed every argument and got as far as TUI startup, which is exactly where a
/// non-pty parent stops. The negative control matters: a single unknown flag instead exits <b>2</b>
/// with clap's "unexpected argument … found". Parse success and pty absence are distinguishable, and
/// this argv is on the parse-success side.</para>
/// </summary>
[Category("Unit")]
public class CodexLaunchArgsTests
{
    [Test]
    [Arguments(AgentModelLevel.Frontier, "xhigh")]
    [Arguments(AgentModelLevel.High, "high")]
    [Arguments(AgentModelLevel.Medium, "medium")]
    [Arguments(AgentModelLevel.Low, "low")]
    public void every_tier_names_its_own_reasoning_effort(AgentModelLevel level, string expected)
    {
        CodexLaunchArgs.ReasoningEffort(level).ShouldBe(expected);
        CodexLaunchArgs.ReasoningEffortOverride(level).ShouldBe($"model_reasoning_effort={expected}");
    }

    [Test]
    public void the_effort_ladder_is_monotonic_with_the_tier_ladder()
    {
        // Frontier = 0 … Low = 3, so a HIGHER tier must never reason SHALLOWER. Stated as an ordering
        // rather than as a second copy of the table, because the failure that matters is a tier
        // silently buying less thinking than the one below it.
        var order = new[] { "low", "medium", "high", "xhigh", "max", "ultra" };
        var depths = Enum.GetValues<AgentModelLevel>()
            .OrderBy(level => (int)level)
            .Select(level => Array.IndexOf(order, CodexLaunchArgs.ReasoningEffort(level)))
            .ToList();

        depths.ShouldAllBe(d => d >= 0, "every effort must be one Codex actually accepts");
        depths.ShouldBe(depths.OrderByDescending(d => d).ToList());
    }

    [Test]
    public void the_standing_instruction_channel_is_developer_instructions()
    {
        // NOT `instructions`. Measured: `-c instructions=<codeword>` produced a model-visible prompt
        // list byte-identical to the baseline apart from generated message ids — the key is inert in
        // this CLI version, so a bundle sent that way is silently dropped. `developer_instructions`
        // adds an input_text block at the head of the first developer message and leaves Codex's own
        // base instructions and skills block untouched: it APPENDS, it does not replace.
        CodexLaunchArgs.DeveloperInstructions("rules").ShouldBe("developer_instructions=rules");
        CodexLaunchArgs.DeveloperInstructions("rules").ShouldNotStartWith("instructions=");
    }

    [Test]
    public void the_instruction_value_is_never_quoted_or_escaped_by_us()
    {
        // This is an argv element, not a shell word. Codex parses the value as TOML and falls back to
        // the raw literal when that fails, which is what a multi-line markdown bundle always does —
        // verified to survive newlines, tabs, double quotes, backticks and Windows path backslashes
        // unchanged. Quoting it here would put the quotes INSIDE the instructions.
        const string body = "# Rules\n\n- Use \"quotes\" and a path C:\\src\\Antiphon\n\t- indented\n";

        var arg = CodexLaunchArgs.DeveloperInstructions(body);

        arg.ShouldBe("developer_instructions=" + body);
        arg["developer_instructions=".Length..].ShouldBe(body);
    }

    [Test]
    public void disable_paste_burst_is_the_top_level_true_override()
    {
        // CARD-0133: a static launch flag, not a delay. Official Codex config key (boolean, default
        // false); -c overrides ~/.codex/config.toml, which does not set this key today.
        CodexLaunchArgs.DisablePasteBurst.ShouldBe("disable_paste_burst=true");
        CodexLaunchArgs.DisablePasteBurst.ShouldNotStartWith("-c");
    }
}
