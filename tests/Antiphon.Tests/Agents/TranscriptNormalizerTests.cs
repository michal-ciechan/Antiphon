using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Verifies the Claude Code JSONL transcript normalizer against the real record shapes observed in a
/// live session (assistant text/thinking/tool_use, stop_reason, user string prompts, tool_result blocks,
/// ai-title, isMeta).
/// </summary>
public class TranscriptNormalizerTests
{
    // The compact-boundary fixture is the single source of truth for the record shape (captured
    // from real claude by ClaudeCompactionCanaryTests; fakeclaude mirrors it).
    private static string CompactBoundaryFixtureLine() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "compact-boundary.jsonl"))
            .First(l => !string.IsNullOrWhiteSpace(l));

    [Test]
    public void Compact_boundary_line_normalizes_to_CompactBoundary_kind()
    {
        var parts = TranscriptNormalizer.Normalize(CompactBoundaryFixtureLine());

        var part = parts.ShouldHaveSingleItem();
        part.Kind.ShouldBe(TranscriptKinds.CompactBoundary);
        part.Text.ShouldBe("Context compacted (manual)");
        part.Uuid.ShouldNotBeNull();
        part.Timestamp.ShouldNotBeNull();
    }

    [Test]
    public void Compact_boundary_is_not_a_turn_end()
    {
        var parts = TranscriptNormalizer.Normalize(CompactBoundaryFixtureLine());

        parts.ShouldNotContain(p => p.Kind == TranscriptKinds.TurnEnd);
        parts.Single().StopReason.ShouldBeNull();
    }

    [Test]
    public void Other_system_records_are_still_skipped()
    {
        const string line = """{"type":"system","subtype":"info","content":"something else","uuid":"u9"}""";
        TranscriptNormalizer.Normalize(line).ShouldBeEmpty();
    }

    [Test]
    public void Assistant_text_plus_tool_use_with_tool_use_stop_reason_yields_no_turn_end()
    {
        const string line = """{"type":"assistant","uuid":"u1","parentUuid":"p0","timestamp":"2026-06-11T21:23:32.934Z","message":{"role":"assistant","model":"claude-opus-4-8","stop_reason":"tool_use","content":[{"type":"text","text":"Let me check."},{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"npm run dev"}}]}}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(2);
        parts[0].Kind.ShouldBe(TranscriptKinds.AssistantText);
        parts[0].Text.ShouldBe("Let me check.");
        parts[0].Uuid.ShouldBe("u1");
        parts[1].Kind.ShouldBe(TranscriptKinds.ToolCall);
        parts[1].ToolName.ShouldBe("Bash");
        parts[1].ToolUseId.ShouldBe("toolu_1");
        parts[1].ToolInput.ShouldNotBeNull();
        parts[1].ToolInput!.ShouldContain("npm run dev");
        parts.ShouldNotContain(p => p.Kind == TranscriptKinds.TurnEnd);
    }

    [Test]
    public void Assistant_end_turn_emits_turn_end_marker()
    {
        const string line = """{"type":"assistant","uuid":"u2","message":{"role":"assistant","stop_reason":"end_turn","content":[{"type":"text","text":"Committed and pushed."}]}}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(2);
        parts[0].Kind.ShouldBe(TranscriptKinds.AssistantText);
        parts[1].Kind.ShouldBe(TranscriptKinds.TurnEnd);
        parts[1].StopReason.ShouldBe("end_turn");
    }

    [Test]
    public void Assistant_thinking_block_is_captured()
    {
        const string line = """{"type":"assistant","message":{"role":"assistant","stop_reason":"tool_use","content":[{"type":"thinking","thinking":"weighing options"}]}}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(1);
        parts[0].Kind.ShouldBe(TranscriptKinds.Thinking);
        parts[0].Text.ShouldBe("weighing options");
    }

    [Test]
    public void User_string_content_is_a_prompt()
    {
        const string line = """{"type":"user","message":{"role":"user","content":"run the app"}}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(1);
        parts[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
        parts[0].Text.ShouldBe("run the app");
    }

    [Test]
    public void User_tool_result_is_extracted_with_error_flag()
    {
        const string line = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","is_error":true,"content":"boom"}]}}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(1);
        parts[0].Kind.ShouldBe(TranscriptKinds.ToolResult);
        parts[0].ToolUseId.ShouldBe("toolu_1");
        parts[0].ToolIsError.ShouldBe(true);
        parts[0].Text.ShouldBe("boom");
    }

    [Test]
    public void Ai_title_becomes_turn_title()
    {
        const string line = """{"type":"ai-title","aiTitle":"Run the app"}""";

        var parts = TranscriptNormalizer.Normalize(line);

        parts.Count.ShouldBe(1);
        parts[0].Kind.ShouldBe(TranscriptKinds.TurnTitle);
        parts[0].Text.ShouldBe("Run the app");
    }

    [Test]
    public void Meta_user_records_are_skipped()
    {
        const string line = """{"type":"user","isMeta":true,"message":{"role":"user","content":[{"type":"text","text":"<system>"}]}}""";

        TranscriptNormalizer.Normalize(line).ShouldBeEmpty();
    }

    [Test]
    public void Invalid_blank_and_metadata_only_lines_yield_nothing()
    {
        TranscriptNormalizer.Normalize("not json").ShouldBeEmpty();
        TranscriptNormalizer.Normalize("").ShouldBeEmpty();
        TranscriptNormalizer.Normalize("""{"type":"file-history-snapshot"}""").ShouldBeEmpty();
        TranscriptNormalizer.Normalize("""{"type":"last-prompt","lastPrompt":"x"}""").ShouldBeEmpty();
    }
}
