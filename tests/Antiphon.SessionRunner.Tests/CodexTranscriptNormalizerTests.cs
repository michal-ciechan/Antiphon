using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0099 S1: <see cref="CodexTranscriptNormalizer"/> driven over REAL captured Codex rollouts.
///
/// <para><b>The fixtures are real files, not shapes written from memory of the schema.</b> Each was
/// captured on 2026-08-20 from <c>~/.codex/sessions/...</c> with codex-cli 0.147.0 and is verbatim
/// apart from one documented reduction: <c>world_state</c> rows are removed (52 KB per row of this
/// machine's skills/environment state, which the normalizer ignores anyway), and the multi-turn
/// capture also drops <c>response_item</c> rows (encrypted reasoning blobs). Nothing that reaches
/// the normalizer's switch was edited, reordered or invented.</para>
///
/// <list type="bullet">
/// <item><c>codex-tui-turn.jsonl</c> — session <c>01a01fbe-bb3c…</c>, driven through a real modern
/// ConPTY by the S1 probe: the launch shape Antiphon actually uses (<c>codex --no-alt-screen
/// --dangerously-bypass-approvals-and-sandbox</c>). This is the <b>thread-item</b> dialect.</item>
/// <item><c>codex-exec-turn.jsonl</c> — session <c>01a01d76-d22f…</c>, a <c>codex exec</c> run.
/// The <b>flat</b> dialect.</item>
/// <item><c>codex-tui-multi-turn.jsonl</c> — session <c>01a01189-2109…</c>, an operator's real
/// interactive session: three turns, one of which FAILED with an HTTP 400 (an unsupported model on
/// a ChatGPT account) and one of which spent seven API calls. The cumulative-usage arithmetic and
/// the errored-turn shape both come from here.</item>
/// </list>
/// </summary>
public class CodexTranscriptNormalizerTests
{
    /// <summary>
    /// The shape an Antiphon-launched Codex session actually produces. The plan's section 2 said
    /// <c>event_msg/user_message</c> and <c>event_msg/agent_message</c>; the TUI writes NEITHER —
    /// it writes <c>item_completed</c> thread items — so a normalizer built to the plan's letter
    /// would ingest zero content from every real delegate session.
    /// </summary>
    [Test]
    public async Task Real_TUI_rollout_yields_UserPrompt_AssistantText_and_a_TurnEnd()
    {
        var parts = NormalizeFixture("codex-tui-turn.jsonl");

        parts.Select(p => p.Kind).ShouldBe(
        [
            TranscriptKinds.UserPrompt,
            TranscriptKinds.AssistantText,
            TranscriptKinds.TurnEnd,
        ]);

        parts[0].Text.ShouldBe("b2526831 and nothing else.");
        parts[0].Role.ShouldBe("user");
        parts[1].Text.ShouldBe("b2526831");
        parts[1].Role.ShouldBe("assistant");

        // TUI AgentMessage.final_answer and task_complete carry the same turn_id. This is the
        // identity the generic delegate-report gate requires to select the true final answer.
        parts[1].ApiCallId.ShouldBe(parts[2].ApiCallId);
        parts[1].Text.ShouldBe(LastAgentMessage("codex-tui-turn.jsonl"));

        // The item's own id is preferred over a positional key — it is what a response_item
        // cross-reference uses and it survives any change to how rows are numbered.
        parts[0].Uuid.ShouldBe("01a01fbe-c3da-7a12-9dc1-7bfd06fa928f");
        parts[1].Uuid.ShouldBe("msg_0c20db1efa54bf9e016a871a51e6d087d0845621d814a9ca6f");

        // Every timestamp is the row's own ISO-8601 stamp.
        parts[0].Timestamp.ShouldNotBeNull();
        parts[2].Timestamp!.Value.ShouldBeGreaterThan(parts[0].Timestamp!.Value);

        await Assert.That(parts[2].StopReason).IsEqualTo("end_turn");
    }

    /// <summary>
    /// Codex has no <c>stop_reason</c>. <c>AgentSessionRuntime.IsTurnBoundary</c> keys on the
    /// literal string <c>end_turn</c>, so the normalizer must synthesize it — this is the single
    /// assertion the whole card rests on. Without it there is no turn-end queue flush, no report
    /// extraction and no task settlement, and a Codex delegate hangs at InProgress forever.
    /// </summary>
    [Test]
    public async Task TurnEnd_carries_the_stop_reason_the_turn_boundary_rule_keys_on()
    {
        foreach (var fixture in new[] { "codex-tui-turn.jsonl", "codex-exec-turn.jsonl" })
        {
            var end = NormalizeFixture(fixture).Single(p => p.Kind == TranscriptKinds.TurnEnd);
            end.StopReason.ShouldBe("end_turn", $"{fixture}: the turn-boundary rule matches this exact string");
            end.ApiCallId.ShouldNotBeNullOrWhiteSpace($"{fixture}: turn_id is the usage-rollup grouping key");
        }

        await Task.CompletedTask;
    }

    /// <summary>The other dialect: <c>codex exec</c> (and the Desktop app) write flat rows.</summary>
    [Test]
    public async Task Real_exec_rollout_yields_the_same_normalized_rows_from_the_flat_dialect()
    {
        var parts = NormalizeFixture("codex-exec-turn.jsonl");

        parts.Select(p => p.Kind).ShouldBe(
        [
            TranscriptKinds.UserPrompt,
            TranscriptKinds.AssistantText,
            TranscriptKinds.TurnEnd,
        ]);
        parts[0].Text.ShouldBe("Reply with exactly the word OK and nothing else.");
        parts[1].Text.ShouldBe("OK");
        parts[1].ApiCallId.ShouldBeNull("the flat dialect supplies no turn_id on agent_message");
        parts[2].ApiCallId.ShouldNotBeNullOrWhiteSpace();

        // No `ordinal` on an exec rollout (measured: only codex-tui stamps one), so the row's
        // position stands in — still stable under a re-tail from offset 0.
        parts[0].Uuid.ShouldBe("01a01d76-d22f-78a1-a94b-8f9431fae11a#6");

        await Assert.That(parts[0].Uuid!.Length).IsLessThanOrEqualTo(64);
    }

    /// <summary>
    /// The TUI uses the same AgentMessage item type for narration and final answers. Only the
    /// final_answer phase may share task_complete's turn identity; commentary must remain outside
    /// the final-report join.
    /// </summary>
    [Test]
    public async Task TUI_final_answers_share_their_turn_identity_while_commentary_stays_unattributed()
    {
        var parts = NormalizeFixture("codex-tui-multi-turn.jsonl");
        var texts = parts.Where(p => p.Kind == TranscriptKinds.AssistantText).ToArray();
        var ends = parts.Where(p => p.Kind == TranscriptKinds.TurnEnd).ToArray();

        texts.Length.ShouldBe(3);
        ends.Length.ShouldBe(3);
        texts[0].ApiCallId.ShouldBe(ends[1].ApiCallId);
        texts[1].Text.ShouldStartWith("I’ll check the current Codex guidance");
        texts[1].ApiCallId.ShouldBeNull("commentary is narration, never the delegate's final report");
        texts[2].ApiCallId.ShouldBe(ends[2].ApiCallId);
        texts[2].ApiCallId.ShouldNotBe(texts[0].ApiCallId, "each final_answer belongs to its own turn");
        texts[2].Text.ShouldBe(LastAgentMessage("codex-tui-multi-turn.jsonl"));

        await Task.CompletedTask;
    }

    /// <summary>
    /// <c>response_item{message, role:user}</c> repeats the prompt verbatim one line before the
    /// <c>item_completed</c>/<c>user_message</c> row. Mapping response_items too would emit every
    /// prompt twice — and a duplicate UserPrompt is not cosmetic, it moves the CARD-0055 confirm
    /// baseline and the reply-dispatch turn window.
    /// </summary>
    [Test]
    public async Task Response_items_that_repeat_the_prompt_are_not_emitted_twice()
    {
        foreach (var fixture in new[] { "codex-tui-turn.jsonl", "codex-exec-turn.jsonl" })
        {
            var prompts = NormalizeFixture(fixture).Where(p => p.Kind == TranscriptKinds.UserPrompt).ToArray();
            prompts.Length.ShouldBe(1, $"{fixture}: the rollout carries the prompt on a response_item too");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Per-turn usage is the DELTA of the session-cumulative <c>total_token_usage</c>, and the
    /// cached portion is subtracted out of <c>InputTokens</c> because
    /// <c>DelegationCost.TotalInputTokens</c> sums the three fields and Codex's
    /// <c>cached_input_tokens</c> is a SUBSET of <c>input_tokens</c>.
    ///
    /// Turn 3 of the real session spent seven API calls; its last <c>token_count</c> reports
    /// 25,080 input tokens for that ONE call. Stamping that would under-report the turn by 113,369.
    /// </summary>
    [Test]
    public async Task Per_turn_usage_is_the_cumulative_delta_with_cached_input_de_overlapped()
    {
        var ends = NormalizeFixture("codex-tui-multi-turn.jsonl")
            .Where(p => p.Kind == TranscriptKinds.TurnEnd)
            .ToArray();
        ends.Length.ShouldBe(3);

        // Turn 1 failed before any token_count was written: no usage to claim, so none is claimed.
        ends[0].InputTokens.ShouldBeNull();
        ends[0].OutputTokens.ShouldBeNull();

        // Turn 2: total (18,609 in / 11,008 cached / 13 out) and it is the file's first usage row,
        // so the baseline starts at that call's own numbers subtracted out — i.e. zero.
        ends[1].InputTokens.ShouldBe(18_609 - 11_008);
        ends[1].CacheReadTokens.ShouldBe(11_008);
        ends[1].CacheCreationTokens.ShouldBe(0);
        ends[1].OutputTokens.ShouldBe(13);

        // Turn 3: cumulative moved 18,609 -> 157,058 input and 11,008 -> 138,240 cached.
        ends[2].InputTokens.ShouldBe((157_058 - 18_609) - (138_240 - 11_008));
        ends[2].CacheReadTokens.ShouldBe(138_240 - 11_008);
        ends[2].OutputTokens.ShouldBe(529 - 13);

        // The three stay disjoint, so the rollup's sum is the turn's real prompt spend and not
        // the cached tokens counted twice.
        (ends[2].InputTokens + ends[2].CacheReadTokens + ends[2].CacheCreationTokens)
            .ShouldBe(157_058 - 18_609);

        // Not the last call's 25,080 — the whole turn's 138,449.
        await Assert.That(ends[2].InputTokens + ends[2].CacheReadTokens).IsEqualTo(138_449);
    }

    /// <summary>
    /// A turn that ends in an API error still ENDS. Codex reports it on <c>task_complete</c> — the
    /// same record a successful turn uses — so the TurnEnd keeps <c>end_turn</c> (the queue must
    /// flush; a delegate whose model was rejected must not hang) and the failure rides the
    /// API-error fields instead. The measured shape nests a JSON document inside the message
    /// string, so the status and class are re-parsed out of it.
    /// </summary>
    [Test]
    public async Task An_errored_task_complete_is_still_a_turn_end_and_carries_the_error()
    {
        var ends = NormalizeFixture("codex-tui-multi-turn.jsonl")
            .Where(p => p.Kind == TranscriptKinds.TurnEnd)
            .ToArray();

        ends[0].StopReason.ShouldBe("end_turn");
        ends[0].IsApiError.ShouldBe(true);
        ends[0].ApiErrorStatus.ShouldBe(400);
        ends[0].ApiErrorClass.ShouldBe("invalid_request_error");

        // A healthy turn claims no error at all rather than claiming "no error".
        ends[1].IsApiError.ShouldBeNull();
        await Assert.That(ends[1].ApiErrorStatus).IsNull();
    }

    /// <summary>
    /// Everything v1 deliberately drops, asserted against the real file rather than by inspection:
    /// <c>task_started</c>, <c>turn_context</c>, <c>thread_settings_applied</c>, an
    /// <c>Extension</c> (web search) item, and <c>Reasoning</c> items whose <c>summary_text</c> is
    /// empty — the TUI writes those with the content only in the encrypted response_item.
    /// </summary>
    [Test]
    public async Task Rows_v1_does_not_map_produce_nothing()
    {
        var parts = NormalizeFixture("codex-tui-multi-turn.jsonl");

        parts.Count(p => p.Kind == TranscriptKinds.UserPrompt).ShouldBe(3);
        parts.Count(p => p.Kind == TranscriptKinds.AssistantText).ShouldBe(3);
        parts.Count(p => p.Kind == TranscriptKinds.TurnEnd).ShouldBe(3);
        parts.ShouldAllBe(p =>
            p.Kind == TranscriptKinds.UserPrompt
            || p.Kind == TranscriptKinds.AssistantText
            || p.Kind == TranscriptKinds.TurnEnd);

        await Assert.That(parts.Count).IsEqualTo(9);
    }

    /// <summary>
    /// The dialect latch. A file whose TUI thread items are followed by flat rows for the same
    /// content must not emit both — a duplicated UserPrompt moves the CARD-0055 baseline and a
    /// duplicated AssistantText duplicates a delegate's report.
    /// </summary>
    [Test]
    public async Task A_second_dialect_in_the_same_file_cannot_double_emit()
    {
        var normalizer = new CodexTranscriptNormalizer();
        var parts = new List<TranscriptPart>();

        // Real thread-item rows first (the TUI dialect), then flat rows carrying the same text.
        foreach (var line in ReadFixtureLines("codex-tui-turn.jsonl"))
            parts.AddRange(normalizer.Normalize(line));

        parts.Count(p => p.Kind == TranscriptKinds.UserPrompt).ShouldBe(1);

        const string flatUser =
            """{"timestamp":"2026-08-20T15:16:35.000Z","type":"event_msg","payload":{"type":"user_message","message":"b2526831 and nothing else.","images":[]}}""";
        const string flatAgent =
            """{"timestamp":"2026-08-20T15:16:35.100Z","type":"event_msg","payload":{"type":"agent_message","message":"b2526831","phase":"final_answer"}}""";

        parts.AddRange(normalizer.Normalize(flatUser));
        parts.AddRange(normalizer.Normalize(flatAgent));

        parts.Count(p => p.Kind == TranscriptKinds.UserPrompt)
            .ShouldBe(1, "the user latch took the thread-item dialect and the flat row is ignored");
        await Assert.That(parts.Count(p => p.Kind == TranscriptKinds.AssistantText)).IsEqualTo(1);
    }

    /// <summary>
    /// Re-tailing the same bytes must reproduce the same parts with the same uuids — that is what
    /// the server's (Uuid, Kind) dedup relies on when a restarted tailer re-reads from offset 0.
    /// </summary>
    [Test]
    public async Task A_re_tail_from_offset_zero_reproduces_identical_parts()
    {
        var first = NormalizeFixture("codex-tui-multi-turn.jsonl");
        var second = NormalizeFixture("codex-tui-multi-turn.jsonl");

        second.Select(p => (p.Kind, p.Uuid, p.Text, p.ApiCallId, p.InputTokens))
            .ShouldBe(first.Select(p => (p.Kind, p.Uuid, p.Text, p.ApiCallId, p.InputTokens)));

        await Assert.That(first.Select(p => p.Uuid).Distinct().Count()).IsEqualTo(first.Count);
    }

    /// <summary>A half-written line while Codex appends is normal, not an error.</summary>
    [Test]
    public async Task Malformed_and_empty_lines_are_skipped_without_throwing()
    {
        var normalizer = new CodexTranscriptNormalizer();
        normalizer.Normalize("").ShouldBeEmpty();
        normalizer.Normalize("   ").ShouldBeEmpty();
        normalizer.Normalize("{\"timestamp\":\"2026-08-20T15:16:2").ShouldBeEmpty();
        normalizer.Normalize("[1,2,3]").ShouldBeEmpty();
        await Assert.That(normalizer.Normalize("""{"type":"event_msg"}""")).IsEmpty();
    }

    // ---------------------------------------------------------------------------- test plumbing

    private static IReadOnlyList<TranscriptPart> NormalizeFixture(string name)
    {
        var normalizer = new CodexTranscriptNormalizer();
        var parts = new List<TranscriptPart>();
        foreach (var line in ReadFixtureLines(name))
            parts.AddRange(normalizer.Normalize(line));
        return parts;
    }

    internal static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    internal static string[] ReadFixtureLines(string name)
    {
        var path = FixturePath(name);
        File.Exists(path).ShouldBeTrue($"captured Codex rollout fixture missing: {path}");
        return File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
    }

    private static string? LastAgentMessage(string fixture)
    {
        string? lastAgentMessage = null;
        foreach (var line in ReadFixtureLines(fixture))
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("type", out var type)
                || type.GetString() != "task_complete")
            {
                continue;
            }

            lastAgentMessage = payload.GetProperty("last_agent_message").GetString() ?? lastAgentMessage;
        }

        return lastAgentMessage;
    }
}
