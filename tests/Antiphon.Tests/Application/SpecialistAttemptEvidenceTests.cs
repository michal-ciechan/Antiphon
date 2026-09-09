using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SpecialistAttemptEvidenceTests
{
    [Test]
    [Arguments("complete", SpecialistAttemptOutcome.ValidReading)]
    [Arguments("late-confirmed-within-budget", SpecialistAttemptOutcome.ValidReading)]
    [Arguments("damaged-middle", SpecialistAttemptOutcome.DeliveryUnconfirmed)]
    [Arguments("queued-prompt", SpecialistAttemptOutcome.DeliveryUnconfirmed)]
    [Arguments("unconfirmed", SpecialistAttemptOutcome.DeliveryUnconfirmed)]
    [Arguments("wrong-token", SpecialistAttemptOutcome.InvalidReading)]
    [Arguments("old-api-text", SpecialistAttemptOutcome.Empty)]
    [Arguments("too-long", SpecialistAttemptOutcome.InvalidReading)]
    [Arguments("extra-line", SpecialistAttemptOutcome.InvalidReading)]
    [Arguments("tool", SpecialistAttemptOutcome.ToolAttempt)]
    [Arguments("late-tool", SpecialistAttemptOutcome.TimedOutAfterDispatch)]
    [Arguments("next-prompt", SpecialistAttemptOutcome.InvalidReading)]
    public void Card0415_V07_native_turn_gates_precede_reading_presentation(string variation, SpecialistAttemptOutcome outcome)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var attempt = new SpecialistAttempt { TaskId = id, DeadlineAt = now.AddSeconds(5) };
        // This is a validator unit fixture, never a claim of a real provider execution.
        var task = new AgentTask { Id = id, Status = AgentTaskStatus.Succeeded, Result = "On track, planted row data must not be read." };
        var message = new SessionQueuedMessage { Body = $"{DelegationReportFormatter.TaskMarker(id)}\nFULL MIDDLE\nTAIL",
            LastDeliveryBaselineSequence = 10, DeliveryVerdict = DeliveryVerdict.Delivered, DeliveryAttempts = 1 };
        var reading = "Needs attention, the captured log reports a test failure.";
        var reportToken = DelegationReportFormatter.ReportToken(id, "done");
        var entries = new List<TranscriptEntry>
        {
            new() { Sequence = 11, Kind = TranscriptKinds.UserPrompt, Text = message.Body },
            new() { Sequence = 12, Kind = TranscriptKinds.AssistantText, ApiCallId = "current", Text = reading + "\n" + reportToken },
            new() { Sequence = 13, Kind = TranscriptKinds.TurnEnd, ApiCallId = "current", StopReason = "end_turn" },
        };
        switch (variation)
        {
            case "damaged-middle": entries[0].Text = message.Body.Replace("FULL MIDDLE", "lost"); break;
            case "queued-prompt": entries[0].Kind = TranscriptKinds.QueuedUserPrompt; break;
            case "unconfirmed": message.DeliveryVerdict = null; break;
            case "late-confirmed-within-budget": message.DeliveryVerdict = DeliveryVerdict.LateConfirmed; break;
            case "wrong-token": entries[1].Text = reading + "\n" + DelegationReportFormatter.ReportToken(Guid.NewGuid(), "done"); break;
            case "old-api-text": entries[1].ApiCallId = "previous-response"; break;
            case "too-long": entries[1].Text = "On track, " + new string('x', 241) + "\n" + reportToken; break;
            case "extra-line": entries[1].Text = reading + "\nA second reading.\n" + reportToken; break;
            case "tool": entries.Insert(1, new() { Sequence = 12, Kind = TranscriptKinds.ToolCall, ToolName = "Read" }); break;
            case "late-tool":
                entries.Insert(1, new() { Sequence = 12, Kind = TranscriptKinds.ToolCall, ToolName = "Read" });
                now = attempt.DeadlineAt;
                break;
            case "next-prompt": entries.Insert(1, new() { Sequence = 12, Kind = TranscriptKinds.UserPrompt, Text = "unrelated" }); break;
        }
        var verdict = SpecialistAttemptEvidence.Evaluate(attempt, task, message, entries, now);
        verdict.ShouldNotBeNull();
        verdict.Outcome.ShouldBe(outcome);
        if (outcome == SpecialistAttemptOutcome.ValidReading) verdict.Reading.ShouldBe(reading);
        else verdict.Reading.ShouldBeNull();
    }

    [Test]
    public void Card0415_V07_running_task_cannot_win_from_planted_result_or_old_prompt()
    {
        var id = Guid.NewGuid();
        var task = new AgentTask { Id = id, Status = AgentTaskStatus.Working, Result = "On track, planted text." };
        var now = DateTime.UtcNow;
        SpecialistAttemptEvidence.Evaluate(new() { TaskId = id, DeadlineAt = now.AddSeconds(1) }, task,
            new() { Body = DelegationReportFormatter.TaskMarker(id), LastDeliveryBaselineSequence = 10 },
            [new() { Kind = TranscriptKinds.UserPrompt, Sequence = 9, Text = DelegationReportFormatter.TaskMarker(id) }], now).ShouldBeNull();
    }
}
