using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1219: RunnerContractMapper parsing of transcript-fault and transcript-bound frames.
/// </summary>
[Category("Unit")]
public class RunnerContractMapperTranscriptEventTests
{
    private static readonly Guid Sid = Guid.Parse("11111111-2222-4333-8444-555555551219");
    private const string FaultDetail = "no prompt in it matches input delivered to this session";
    private const string FaultPath = "/home/a/.claude/projects/x/37512455.jsonl";
    private const string BoundPath = "/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl";
    private const string OldDetail = "No cwd-matching transcript candidates after 60s";

    private const string HubFaultJson =
        "{\"SessionId\":\"11111111-2222-4333-8444-555555551219\",\"Kind\":\"AdoptionRefused\"," +
        "\"Detail\":\"no prompt in it matches input delivered to this session\"," +
        "\"CandidatePath\":\"/home/a/.claude/projects/x/37512455.jsonl\",\"UnboundSeconds\":184.5,\"Repeat\":3}";

    private const string HubBoundJson =
        "{\"SessionId\":\"11111111-2222-4333-8444-555555551219\"," +
        "\"TranscriptPath\":\"/home/a/.codex/sessions/2026/10/13/rollout-abc.jsonl\",\"How\":\"discovery\"}";

    private static readonly SessionRunnerTranscriptFaultEvent ExpectedFault =
        new(Sid, "AdoptionRefused", FaultDetail, FaultPath, 184.5, 3);

    private static readonly SessionRunnerTranscriptBoundEvent ExpectedBound =
        new(Sid, BoundPath, "discovery");

    [Test]
    public void Codex_token_count_maps_without_becoming_a_transcript_entry()
    {
        var at = DateTimeOffset.Parse("2026-10-28T16:03:58.832Z");
        var mapped = new RunnerContractMapper().MapTranscript(
            new RunnerTranscriptDto(Sid, [], 0, CodexTokenCountAt: at));
        mapped.Entries.ShouldBeEmpty();
        mapped.CodexTokenCountAt.ShouldBe(SessionGeneration.Normalize(at.UtcDateTime));

        var legacy = JsonSerializer.Deserialize<RunnerTranscriptDto>(
            "{\"sessionId\":\"11111111-2222-4333-8444-555555551219\",\"entries\":[],\"lastSequence\":0}",
            RunnerContractMapper.Json)!;
        new RunnerContractMapper().MapTranscript(legacy).CodexTokenCountAt.ShouldBeNull(
            "a runner that predates the field reports no heartbeat, which is not a cleared one");
    }

    [Test]
    public void Fault_frame_round_trips_every_field()
    {
        var record = new RunnerTranscriptFaultEvent(
            Sid, TranscriptFaultKinds.AdoptionRefused, FaultDetail, FaultPath, 184.5, 3);
        var hub = JsonSerializer.Serialize(record);
        hub.ShouldBe(HubFaultJson);
        var web = JsonSerializer.Serialize(record, PhoneHomeFraming.Json);
        web.ShouldContain("\"unboundSeconds\":184.5");

        foreach (var (spelling, json) in new[] { ("hub", hub), ("web", web) })
        {
            var parsed = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionTranscriptFault, json);
            parsed.ShouldNotBeNull(spelling);
            parsed.EventName.ShouldBe("SessionTranscriptFault", spelling);
            parsed.SessionId.ShouldBe(Sid, spelling);
            parsed.TranscriptFault.ShouldBe(ExpectedFault, spelling);
            parsed.Output.ShouldBeNull(spelling);
            parsed.Exited.ShouldBeNull(spelling);
            parsed.Transcript.ShouldBeNull(spelling);
            parsed.Adopted.ShouldBeNull(spelling);
            parsed.TranscriptBound.ShouldBeNull(spelling);
            parsed.AgentStatus.ShouldBeNull(spelling);
            parsed.SubscriptionReady.ShouldBeFalse(spelling);
        }
    }

    [Test]
    public void Bound_frame_round_trips_every_field()
    {
        var record = new RunnerTranscriptBoundEvent(Sid, BoundPath, TranscriptBindMethods.Discovery);
        var hub = JsonSerializer.Serialize(record);
        hub.ShouldBe(HubBoundJson);
        var web = JsonSerializer.Serialize(record, PhoneHomeFraming.Json);
        web.ShouldContain("\"transcriptPath\":");

        foreach (var (spelling, json) in new[] { ("hub", hub), ("web", web) })
        {
            var parsed = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionTranscriptBound, json);
            parsed.ShouldNotBeNull(spelling);
            parsed.EventName.ShouldBe("SessionTranscriptBound", spelling);
            parsed.SessionId.ShouldBe(Sid, spelling);
            parsed.TranscriptBound.ShouldBe(ExpectedBound, spelling);
            parsed.Output.ShouldBeNull(spelling);
            parsed.Exited.ShouldBeNull(spelling);
            parsed.Transcript.ShouldBeNull(spelling);
            parsed.Adopted.ShouldBeNull(spelling);
            parsed.TranscriptFault.ShouldBeNull(spelling);
            parsed.AgentStatus.ShouldBeNull(spelling);
            parsed.SubscriptionReady.ShouldBeFalse(spelling);
        }
    }

    [Test]
    public void Old_runner_fault_without_repeat_fields_defaults_to_first_report()
    {
        // Pre-CARD-0101 shape: no UnboundSeconds, no Repeat.
        const string json =
            "{\"SessionId\":\"11111111-2222-4333-8444-555555551219\",\"Kind\":\"TranscriptMissing\"," +
            "\"Detail\":\"No cwd-matching transcript candidates after 60s\",\"CandidatePath\":null}";

        var parsed = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionTranscriptFault, json);

        parsed.ShouldNotBeNull();
        var fault = parsed.TranscriptFault.ShouldNotBeNull();
        fault.ShouldBe(new SessionRunnerTranscriptFaultEvent(Sid, "TranscriptMissing", OldDetail, null, 0, 1));
        fault.CandidatePath.ShouldBeNull();
        fault.UnboundSeconds.ShouldBe(0);
        fault.Repeat.ShouldBe(1);
    }

    [Test]
    public void Fault_frame_with_unknown_future_field_still_parses()
    {
        var fault = RunnerContractMapper.ParseEvent(
            SessionRunnerEventNames.SessionTranscriptFault, WithFutureField(HubFaultJson));
        fault.ShouldNotBeNull();
        fault.TranscriptFault.ShouldBe(ExpectedFault);

        var bound = RunnerContractMapper.ParseEvent(
            SessionRunnerEventNames.SessionTranscriptBound, WithFutureField(HubBoundJson));
        bound.ShouldNotBeNull();
        bound.TranscriptBound.ShouldBe(ExpectedBound);
    }

    [Test]
    public void Unknown_event_name_still_returns_null()
    {
        var rows = new[]
        {
            (SessionRunnerEventNames.SessionAgentStatus, "{}"),
            ("SessionSomethingNew", "{}"),
            (SessionRunnerEventNames.SessionTranscriptFault, "null"),
            (SessionRunnerEventNames.SessionTranscriptBound, "null"),
        };

        foreach (var (name, json) in rows)
            RunnerContractMapper.ParseEvent(name, json).ShouldBeNull($"{name} {json}");
    }

    [Test]
    public void Malformed_payload_throws_like_the_other_branches()
    {
        var rows = new[]
        {
            (SessionRunnerEventNames.SessionTranscriptFault, "{not json"),
            (SessionRunnerEventNames.SessionTranscriptFault, "[]"),
            (SessionRunnerEventNames.SessionTranscriptBound, "{not json"),
            (SessionRunnerEventNames.SessionTranscriptBound, "[]"),
            // Control: an existing branch throws the same type.
            (SessionRunnerEventNames.SessionOutput, "{not json"),
        };

        foreach (var (name, json) in rows)
            Should.Throw<JsonException>(() => RunnerContractMapper.ParseEvent(name, json), $"{name} {json}");
    }

    private static string WithFutureField(string json) =>
        json[..^1] + ",\"FutureField\":\"x\"}";
}
