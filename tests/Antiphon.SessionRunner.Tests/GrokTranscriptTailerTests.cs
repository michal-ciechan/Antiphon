using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// File-driven coverage of <see cref="GrokTranscriptTailer"/> + <see cref="GrokTranscriptNormalizer"/>
/// (CARD-0080 S2). The row constants below are REAL rows captured verbatim from a real grok 1.0.5
/// session's <c>updates.jsonl</c> (<c>01a01178-bfe3-7493-b326-1785d2ebf7db</c>, the session that
/// built <c>5754e02</c>) — not invented shapes; the cancelled turn_completed is reconstructed from
/// the S1 canary's measured Esc shape (<c>GrokCanaryTests.Esc_interrupt_shape_in_updates_jsonl</c>:
/// stop_reason "cancelled", NO usage block, cancelTrigger esc), whose live capture was a pruned
/// temp session.
/// </summary>
public class GrokTranscriptTailerTests
{
    private const string Sid = "01a01178-bfe3-7493-b326-1785d2ebf7db";
    private const string PromptId = "fb469fb0-6940-476a-bc78-fdf757090144";

    private const string UserChunkRow =
        """{"timestamp":1786999676,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"check antiphon if it supports grok, if not create a plan and make it support grok as a tui. and verify al ltests (same as against claude) including having a fake grok in tests which mimics real grok executable. and test"},"_meta":{"modelId":"grok-4.6","promptIndex":0}},"_meta":{"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-2","agentTimestampMs":1786999655763}}}""";

    private const string ThoughtChunkRow =
        """{"timestamp":1786999679,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"The user wants me to check if Antiphon supports Grok as a TUI (terminal UI agent), and if not, create a plan and implement support for Grok as a TUI. They also want tests similar to those against Clau..."}},"_meta":{"totalTokens":29277,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-50","agentTimestampMs":1786999677112,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999674711,"turnStartMs":1786999655767,"updateType":"AgentThoughtChunk","chunkId":48}}}""";

    private const string AgentChunkRow =
        """{"timestamp":1786999681,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"I'll start by checking how Antiphon currently treats agent TUIs and whether Grok is already in the picture, then plan and implement support to match Claude's test coverage."}},"_meta":{"totalTokens":29277,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-84","agentTimestampMs":1786999680267,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999674711,"turnStartMs":1786999655767,"updateType":"AgentMessageChunk","chunkId":82}}}""";

    // The second message chunk of the same turn: the real row with only text/eventId/timestamp
    // varied, proving per-promptId coalescing over more than one chunk.
    private const string AgentChunkRow2 =
        """{"timestamp":1786999682,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":" SECOND-CHUNK tail."}},"_meta":{"totalTokens":29280,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-85","agentTimestampMs":1786999681900,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999674711,"turnStartMs":1786999655767,"updateType":"AgentMessageChunk","chunkId":83}}}""";

    private const string ToolCallRow =
        """{"timestamp":1786999681,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"tool_call","toolCallId":"call-bb331df7-4505-47df-822e-d2e29fd0a19c-0","title":"read_file","rawInput":{"target_file":"C:\\src\\Antiphon\\docs\\project-context.md","limit":150},"_meta":{"x.ai/tool":{"version":1,"name":"read_file","kind":"read","namespace":"grok_build","label":"Read","read_only":true}}},"_meta":{"totalTokens":40754,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-86","agentTimestampMs":1786999681651,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999674711,"turnStartMs":1786999655767,"updateType":"ToolCall","updateParams":{"toolCallId":"call-bb331df7-4505-47df-822e-d2e29fd0a19c-0","title":"read_file","kind":"Other","status":"Pending"}}}}""";

    private const string ToolCallUpdateRow =
        """{"timestamp":1786999681,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"tool_call_update","toolCallId":"call-bb331df7-4505-47df-822e-d2e29fd0a19c-0","kind":"read","title":"Read `C:\\src\\Antiphon\\docs\\project-context.md`","locations":[{"path":"C:\\src\\Antiphon\\docs\\project-context.md"}],"rawInput":{"variant":"ReadFile","target_file":"C:\\src\\Antiphon\\docs\\project-context.md","limit":150},"_meta":{"x.ai/tool":{"version":1,"name":"read_file","kind":"read","namespace":"grok_build","label":"Read","read_only":true,"input":{"path":"C:\\src\\Antiphon\\docs\\project-context.md","limit":150}}}},"_meta":{"totalTokens":40754,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-87","agentTimestampMs":1786999681651,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999674711,"turnStartMs":1786999655767,"updateType":"ToolCallUpdate","updateParams":{"toolCallId":"call-bb331df7-4505-47df-822e-d2e29fd0a19c-0","status":null}}}}""";

    private const string SessionRecapRow =
        """{"timestamp":1787002204,"method":"_x.ai/session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"session_recap","summary":"We added Grok as a first-class Antiphon TUI (`AgentKind.Grok`, `RunnerGrokAdapter`, FakeGrok tests) and pushed `5754e02` to master.","auto":true},"_meta":{"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-5518","agentTimestampMs":1787002204891}}}""";

    private const string TaskBackgroundedRow =
        """{"timestamp":1787000865,"method":"_x.ai/session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"task_backgrounded","tool_call_id":"call-ef3457af-19ef-4cf2-84bb-20262c0a0d6f-208","task_id":"call-ef3457af-19ef-4cf2-84bb-20262c0a0d6f-208","command":"dotnet build","cwd":"C:\\src\\Antiphon","output_file":"C:\\Users\\lndco\\.grok\\sessions\\x\\terminal\\t.log","description":"Build test projects isolated"},"_meta":{"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-2753","agentTimestampMs":1787000865617}}}""";

    private const string TurnCompletedRow =
        """{"timestamp":1787001911,"method":"_x.ai/session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"turn_completed","prompt_id":"fb469fb0-6940-476a-bc78-fdf757090144","stop_reason":"end_turn","usage":{"inputTokens":18747424,"outputTokens":70713,"totalTokens":18818137,"cachedReadTokens":18482432,"cacheCreationTokens":0,"reasoningTokens":62843,"modelCalls":103,"apiDurationMs":1273834,"costUsdTicks":26753525600,"modelUsage":{"grok-4.6-build":{"inputTokens":18747424,"outputTokens":70713,"totalTokens":18818137,"cachedReadTokens":18482432,"cacheCreationTokens":0,"reasoningTokens":62843,"modelCalls":103,"apiDurationMs":1273834,"costUsdTicks":26753525600}},"numTurns":103}},"_meta":{"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-5081","agentTimestampMs":1787001911643}}}""";

    // The measured Esc shape (S1): stop_reason "cancelled", NO usage, cancelTrigger esc — and the
    // measured file-order hazard: a trailing agent chunk (eventId N-46) was written AFTER this row
    // (N-47) with an EARLIER agentTimestampMs.
    private const string CancelledTurnCompletedRow =
        """{"timestamp":1786999900,"method":"_x.ai/session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"turn_completed","prompt_id":"fb469fb0-6940-476a-bc78-fdf757090144","stop_reason":"cancelled","_meta":{"cancelTrigger":"esc","cancellationCategory":"MidTurnAbort"}},"_meta":{"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-47","agentTimestampMs":1786999900500}}}""";

    private const string TrailingChunkAfterCancelRow =
        """{"timestamp":1786999900,"method":"session/update","params":{"sessionId":"01a01178-bfe3-7493-b326-1785d2ebf7db","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"a fragment that streamed past the cancel"}},"_meta":{"totalTokens":123,"eventId":"01a01178-bfe3-7493-b326-1785d2ebf7db-46","agentTimestampMs":1786999900100,"promptId":"fb469fb0-6940-476a-bc78-fdf757090144","streamStartMs":1786999890000,"turnStartMs":1786999880000,"updateType":"AgentMessageChunk","chunkId":46}}}""";

    [Test]
    public async Task Real_turn_rows_normalize_to_UserPrompt_ToolCall_coalesced_text_and_TurnEnd()
    {
        var (dir, path) = TempUpdatesPath();
        var hub = new SessionRunnerEventHub();
        var sessionId = Guid.NewGuid();
        var tailer = new GrokTranscriptTailer(
            sessionId, path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            // The file is created lazily at the first submit (measured ~1.1s after Enter) — the
            // tailer must start against a missing file and pick it up when it appears.
            await Task.Delay(150);
            tailer.Snapshot().Entries.ShouldBeEmpty("no file yet — nothing to ingest");

            await AppendRowsAsync(path,
                UserChunkRow, ThoughtChunkRow, AgentChunkRow, ToolCallRow, ToolCallUpdateRow,
                TaskBackgroundedRow, AgentChunkRow2, TurnCompletedRow, SessionRecapRow);

            var entries = await PollForEntriesAsync(tailer, want: 5, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Kind).ShouldBe(
            [
                TranscriptKinds.UserPrompt,   // immediate
                TranscriptKinds.ToolCall,     // immediate (the mid-turn activity signal)
                TranscriptKinds.Thinking,     // coalesced, flushed by turn_completed
                TranscriptKinds.AssistantText,
                TranscriptKinds.TurnEnd,
            ]);

            var user = entries[0];
            user.Text.ShouldStartWith("check antiphon if it supports grok");
            user.Uuid.ShouldBe($"{Sid}-2");
            user.Role.ShouldBe("user");
            user.Timestamp.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1786999655763));

            var tool = entries[1];
            tool.ToolName.ShouldBe("read_file");
            tool.ToolUseId.ShouldBe("call-bb331df7-4505-47df-822e-d2e29fd0a19c-0");
            tool.ToolInput.ShouldNotBeNull();
            tool.ToolInput.ShouldContain("project-context.md");

            var text = entries[3];
            text.Text.ShouldBe(
                "I'll start by checking how Antiphon currently treats agent TUIs and whether Grok is "
                + "already in the picture, then plan and implement support to match Claude's test "
                + "coverage. SECOND-CHUNK tail.");
            text.Uuid.ShouldBe($"{Sid}-84", "the coalesced part keeps the FIRST chunk's eventId");
            text.ApiCallId.ShouldBe(PromptId);

            var end = entries[4];
            end.StopReason.ShouldBe("end_turn");
            end.Uuid.ShouldBe($"{Sid}-5081");
            end.ApiCallId.ShouldBe(PromptId);
            text.ApiCallId.ShouldBe(end.ApiCallId,
                "the turn-ending response text and turn_completed boundary share Grok's promptId");
            end.InputTokens.ShouldBe(18747424);
            end.OutputTokens.ShouldBe(70713);
            end.CacheReadTokens.ShouldBe(18482432);
            end.CacheCreationTokens.ShouldBe(0);
            end.ModelCalls.ShouldBe(103);
            end.Model.ShouldBe("grok-4.6-build");

            // The lockstep working rule reads this turn as proven idle.
            TranscriptWorkingState.IsProvenIdle(entries).ShouldBeTrue();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Cancelled_turn_is_a_TurnEnd_and_a_trailing_out_of_order_chunk_still_lands()
    {
        var (dir, path) = TempUpdatesPath();
        var hub = new SessionRunnerEventHub();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            // Measured file order of an interrupted turn: the cancelled turn_completed lands
            // BEFORE a final agent chunk of the same turn (eventIds N-47 then N-46).
            await AppendRowsAsync(path,
                UserChunkRow, CancelledTurnCompletedRow, TrailingChunkAfterCancelRow);

            var entries = await PollForEntriesAsync(tailer, want: 3, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Kind).ShouldBe(
                [TranscriptKinds.UserPrompt, TranscriptKinds.TurnEnd, TranscriptKinds.AssistantText]);

            var end = entries[1];
            end.StopReason.ShouldBe("cancelled", "an Esc interrupt is an EXPLICIT turn end for Grok");
            end.InputTokens.ShouldBeNull("a cancelled stop carries no usage block");
            end.ModelCalls.ShouldBeNull();
            end.Model.ShouldBeNull();

            // The trailing chunk is emitted rather than held for a flush that never comes, and its
            // own timestamp PRECEDES the TurnEnd's — which is what the server/client working rules'
            // timestamp override ranks, so the session still reads idle despite the sequence order.
            var trailing = entries[2];
            trailing.Text.ShouldBe("a fragment that streamed past the cancel");
            trailing.ApiCallId.ShouldBe(PromptId);
            trailing.Timestamp!.Value.ShouldBeLessThan(end.Timestamp!.Value);

            // Documented, deliberate divergence: the runner-side mirror is sequence-only (no
            // timestamp override), so it reads NOT-proven-idle here. That is the conservative
            // direction — its only consumer, the CPU watchdog, WITHHOLDS a kill it cannot prove.
            TranscriptWorkingState.IsProvenIdle(entries).ShouldBeFalse();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task A_half_written_trailing_line_is_held_until_its_newline_arrives()
    {
        var (dir, path) = TempUpdatesPath();
        var hub = new SessionRunnerEventHub();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            Directory.CreateDirectory(dir);
            var split = UserChunkRow.Length / 2;
            await File.AppendAllTextAsync(path, UserChunkRow[..split]);
            await Task.Delay(300);
            tailer.Snapshot().Entries.ShouldBeEmpty("a row grok is still appending is not ours to read yet");

            await File.AppendAllTextAsync(path, UserChunkRow[split..] + "\n");
            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
            entries[0].Text.ShouldStartWith("check antiphon");
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task A_retail_from_offset_zero_reproduces_identical_sequences_and_uuids()
    {
        var (dir, path) = TempUpdatesPath();
        var hub = new SessionRunnerEventHub();
        var sessionId = Guid.NewGuid();
        await AppendRowsAsync(path,
            UserChunkRow, ThoughtChunkRow, AgentChunkRow, ToolCallRow, AgentChunkRow2, TurnCompletedRow);

        try
        {
            IReadOnlyList<RunnerTranscriptEvent> first, second;
            await using (var tailer = new GrokTranscriptTailer(
                sessionId, path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50)))
            {
                tailer.Start();
                first = await PollForEntriesAsync(tailer, want: 5, TimeSpan.FromSeconds(10));
            }

            // A runner restart re-tails from offset 0; the server dedupes on (SessionId, Uuid),
            // which only works if the replay is byte-for-byte deterministic.
            await using (var retailer = new GrokTranscriptTailer(
                sessionId, path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50)))
            {
                retailer.Start();
                second = await PollForEntriesAsync(retailer, want: 5, TimeSpan.FromSeconds(10));
            }

            second.Select(e => (e.Sequence, e.Kind, e.Uuid, e.Text))
                .ShouldBe(first.Select(e => (e.Sequence, e.Kind, e.Uuid, e.Text)));
        }
        finally
        {
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Child_exit_flushes_streamed_text_without_synthesizing_a_TurnEnd()
    {
        var (dir, path) = TempUpdatesPath();
        var hub = new SessionRunnerEventHub();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, hub, NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            // A turn that died mid-stream: chunks, no turn_completed, then the child exits.
            await AppendRowsAsync(path, UserChunkRow, AgentChunkRow);
            await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));

            tailer.NotifyChildExited();

            // ChildExitSettle is 3s; the flush emits the accumulated text but NO TurnEnd — ending
            // an abandoned turn is the relaunch path's SessionRestartBoundary's job.
            var entries = await PollForEntriesAsync(tailer, want: 2, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Kind).ShouldBe(
                [TranscriptKinds.UserPrompt, TranscriptKinds.AssistantText]);
            entries.ShouldNotContain(e => e.Kind == TranscriptKinds.TurnEnd);
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    // Verbatim rows from session 1636e434-b4bc-4743-ae39-9381bd83a2cc (grok 1.0.5, CARD-0157).
    private const string CompactSid = "1636e434-b4bc-4743-ae39-9381bd83a2cc";
    private const string CompactionCheckpointRow =
        """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-b4bc-4743-ae39-9381bd83a2cc","update":{"sessionUpdate":"compaction_checkpoint","checkpoint_id":"8016d019-65b3-4023-9882-52aa2b88e92f","prompt_index_at_compaction":1,"checkpoint_file":"compaction_checkpoints/8016d019-65b3-4023-9882-52aa2b88e92f.json","schema_version":1,"created_at":"2026-08-19T19:24:20.583418700+00:00"},"_meta":{"eventId":"1636e434-b4bc-4743-ae39-9381bd83a2cc-1549","agentTimestampMs":1787167460583}}}""";
    private const string AutoCompactCompletedRow =
        """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-b4bc-4743-ae39-9381bd83a2cc","update":{"sessionUpdate":"auto_compact_completed","tokens_before":106112,"tokens_after":34833,"summary_preview":null},"_meta":{"eventId":"1636e434-b4bc-4743-ae39-9381bd83a2cc-1550","agentTimestampMs":1787167460583}}}""";
    private const string AutoCompactCompletedNoTokensRow =
        """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-b4bc-4743-ae39-9381bd83a2cc","update":{"sessionUpdate":"auto_compact_completed","summary_preview":null},"_meta":{"eventId":"1636e434-b4bc-4743-ae39-9381bd83a2cc-1550","agentTimestampMs":1787167460583}}}""";
    // Same pair fakegrok emits for a typed /compact (CARD-0157 S2).
    private const string FakeGrokCheckpointRow =
        """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"fakegrok-compact","update":{"sessionUpdate":"compaction_checkpoint","checkpoint_id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","prompt_index_at_compaction":1,"checkpoint_file":"compaction_checkpoints/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.json","schema_version":1,"created_at":"2026-08-19T19:24:20.0000000+00:00"},"_meta":{"eventId":"fakegrok-compact-1","agentTimestampMs":1787167460583}}}""";
    private const string FakeGrokAutoCompactRow =
        """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"fakegrok-compact","update":{"sessionUpdate":"auto_compact_completed","tokens_before":106112,"tokens_after":34833,"summary_preview":null},"_meta":{"eventId":"fakegrok-compact-2","agentTimestampMs":1787167460583}}}""";

    [Test]
    public async Task Real_auto_compact_completed_is_a_usage_bearing_auto_CompactBoundary()
    {
        var (dir, path) = TempUpdatesPath();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            await AppendRowsAsync(path, AutoCompactCompletedRow);
            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries.ShouldHaveSingleItem();
            var boundary = entries[0];
            boundary.Kind.ShouldBe(TranscriptKinds.CompactBoundary);
            var text = boundary.Text.ShouldNotBeNull();
            text.ShouldContain("(auto)");
            text.ShouldContain("106112 -> 34833");
            text.ShouldNotContain("(manual)");
            boundary.InputTokens.ShouldBe(34833);
            boundary.OutputTokens.ShouldBeNull();
            boundary.CacheReadTokens.ShouldBeNull();
            boundary.CacheCreationTokens.ShouldBeNull();
            boundary.Uuid.ShouldBe($"{CompactSid}-1550");
            boundary.Timestamp.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1787167460583));
            entries.ShouldNotContain(e => e.Kind == TranscriptKinds.TurnEnd);
            TranscriptKinds.IsManualCompactBoundary(boundary.Kind, boundary.Text).ShouldBeFalse();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Real_compaction_checkpoint_emits_zero_parts()
    {
        var (dir, path) = TempUpdatesPath();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            await AppendRowsAsync(path, CompactionCheckpointRow);
            await Task.Delay(400);
            tailer.Snapshot().Entries.ShouldBeEmpty();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Tokens_less_auto_compact_completed_is_a_plain_boundary()
    {
        var (dir, path) = TempUpdatesPath();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            await AppendRowsAsync(path, AutoCompactCompletedNoTokensRow);
            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries.ShouldHaveSingleItem();
            entries[0].Kind.ShouldBe(TranscriptKinds.CompactBoundary);
            entries[0].Text.ShouldBe("Context compacted (auto)");
            entries[0].InputTokens.ShouldBeNull();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Auto_compact_boundary_after_a_completed_turn_is_housekeeping_not_activity()
    {
        var (dir, path) = TempUpdatesPath();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            await AppendRowsAsync(path, UserChunkRow, TurnCompletedRow, AutoCompactCompletedRow);
            var entries = await PollForEntriesAsync(tailer, want: 3, TimeSpan.FromSeconds(10));
            entries.Select(e => e.Kind).ShouldBe(
            [
                TranscriptKinds.UserPrompt,
                TranscriptKinds.TurnEnd,
                TranscriptKinds.CompactBoundary,
            ]);
            TranscriptWorkingState.IsProvenIdle(entries).ShouldBeTrue();
            TranscriptKinds.IsManualCompactBoundary(entries[2].Kind, entries[2].Text).ShouldBeFalse();
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public async Task Fakegrok_compact_pair_normalizes_the_same_as_the_measured_shape()
    {
        var (dir, path) = TempUpdatesPath();
        var tailer = new GrokTranscriptTailer(
            Guid.NewGuid(), path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(50));
        tailer.Start();
        try
        {
            await AppendRowsAsync(path, FakeGrokCheckpointRow, FakeGrokAutoCompactRow);
            var entries = await PollForEntriesAsync(tailer, want: 1, TimeSpan.FromSeconds(10));
            entries.ShouldHaveSingleItem();
            entries[0].Kind.ShouldBe(TranscriptKinds.CompactBoundary);
            var text = entries[0].Text.ShouldNotBeNull();
            text.ShouldContain("(auto)");
            text.ShouldContain("106112 -> 34833");
            entries[0].InputTokens.ShouldBe(34833);
            entries[0].Uuid.ShouldBe("fakegrok-compact-2");
            entries.ShouldNotContain(e => e.Kind == TranscriptKinds.TurnEnd);
        }
        finally
        {
            await tailer.DisposeAsync();
            BestEffortDelete(dir);
        }
    }

    [Test]
    public void ResolveUpdatesPath_matches_groks_session_store_layout()
    {
        var cwd = OperatingSystem.IsWindows() ? @"C:\src\Antiphon" : "/src/Antiphon";
        var sessionId = Guid.Parse(Sid);

        // The launch env's GROK_HOME wins — that is the environment the child actually sees.
        var fromEnv = GrokTranscriptTailer.ResolveUpdatesPath(
            new Dictionary<string, string> { ["GROK_HOME"] = Path.Combine(Path.GetTempPath(), "gh") },
            cwd, sessionId);
        fromEnv.ShouldBe(Path.Combine(
            Path.GetTempPath(), "gh", "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)),
            Sid, "updates.jsonl"));

        // Verified real layout (grok 1.0.5): the cwd segment is Uri.EscapeDataString of the FULL
        // path — "C:\src\Antiphon" becomes "C%3A%5Csrc%5CAntiphon".
        if (OperatingSystem.IsWindows())
            fromEnv.ShouldContain("C%3A%5Csrc%5CAntiphon");

        // No env anywhere: ~/.grok, the real default.
        var fallback = GrokTranscriptTailer.ResolveUpdatesPath(null, cwd, sessionId);
        if (Environment.GetEnvironmentVariable("GROK_HOME") is null)
            fallback.ShouldStartWith(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok"));
    }

    [Test]
    public void TryLocateSessionDirectory_finds_the_guid_under_a_foreign_cwd_encoding()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-grok-locate-{Guid.NewGuid():N}");
        var nativeId = Guid.NewGuid();
        var encoded = Uri.EscapeDataString(@"D:\src\OTHER-machine\repo");
        var sessionDir = Path.Combine(root, "sessions", encoded, nativeId.ToString("D"));
        Directory.CreateDirectory(sessionDir);
        try
        {
            var found = GrokTranscriptTailer.TryLocateSessionDirectory(root, nativeId);
            found.ShouldBe(sessionDir);
            GrokTranscriptTailer.EncodedCwdOf(found!).ShouldBe(encoded);
            GrokTranscriptTailer.TryLocateSessionDirectory(root, Guid.NewGuid()).ShouldBeNull();
        }
        finally
        {
            BestEffortDelete(root);
        }
    }

    private static (string Dir, string Path) TempUpdatesPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"antiphon-grok-tailer-{Guid.NewGuid():N}");
        return (dir, Path.Combine(dir, "updates.jsonl"));
    }

    private static async Task AppendRowsAsync(string path, params string[] rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path, string.Join("\n", rows) + "\n");
    }

    private static async Task<IReadOnlyList<RunnerTranscriptEvent>> PollForEntriesAsync(
        GrokTranscriptTailer tailer, int want, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = tailer.Snapshot();
            if (snapshot.Entries.Count >= want)
                return snapshot.Entries;
            await Task.Delay(100);
        }
        return tailer.Snapshot().Entries;
    }

    private static void BestEffortDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
