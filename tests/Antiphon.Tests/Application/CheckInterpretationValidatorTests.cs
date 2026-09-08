using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CheckInterpretationValidatorTests
{
    [Test]
    [Arguments("On track: parser verification is in progress.")]
    [Arguments("Needs attention: no model response to the delivered brief.")]
    [Arguments("Unclear: shared-checkout changes cannot be attributed.")]
    [Arguments("Settled at capture: superseded by another task.")]
    [Arguments("On track: the complete fixture was written; tests are pending.")]
    public void Card0415_V06_current_prefixes_and_legitimate_evidence_pass(string reading)
    {
        var result = Validate(reading);
        result.IsValid.ShouldBeTrue(result.Reason);
        result.Reading.ShouldBe(reading);
    }

    [Test]
    [Arguments(239, true)]
    [Arguments(240, true)]
    [Arguments(241, false)]
    public void Card0415_V06_length_is_checked_before_presentation(int length, bool valid)
    {
        var reading = "On track: " + new string('a', length - "On track: ".Length);
        Validate(reading).IsValid.ShouldBe(valid);
    }

    [Test]
    public void Card0415_V06_UTF16_length_counts_surrogate_pairs_and_combining_marks()
    {
        var reading = "On track: " + new string('a', 226) + "\U0001F600e\u0301";
        reading.Length.ShouldBe(240);
        Validate(reading).IsValid.ShouldBeTrue();
        Validate(reading + "x").IsValid.ShouldBeFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("ready")]
    [Arguments("DOING: tests")]
    [Arguments("- On track: tests")]
    [Arguments("On tracker: tests")]
    [Arguments("On track")]
    [Arguments("On track: ")]
    public void Card0415_V06_empty_ready_and_noncontract_readings_fail(string reading) =>
        Validate(reading).IsValid.ShouldBeFalse();

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    [Arguments("\r")]
    [Arguments("\u2028")]
    public void Card0415_V06_multiple_physical_lines_fail(string separator) =>
        Validate("On track: tests pending." + separator + "Second reading.").IsValid.ShouldBeFalse();

    [Test]
    [Arguments("[antiphon-task:deadbeef]")]
    [Arguments("[antiphon-report:deadbeef done]")]
    [Arguments("[ANTIPHON-TASK:deadbeef]")]
    public void Card0415_V06_leftover_markers_fail(string marker) =>
        Validate("On track: " + marker).IsValid.ShouldBeFalse();

    [Test]
    [Arguments("missing")]
    [Arguments("wrong-task")]
    [Arguments("blocked")]
    [Arguments("failed")]
    [Arguments("trailing-text")]
    public void Card0415_V06_current_done_token_is_required(string defect)
    {
        var taskId = Guid.NewGuid();
        var token = DelegationReportFormatter.ReportToken(defect == "wrong-task" ? Guid.NewGuid() : taskId,
            defect is "blocked" or "failed" ? defect : "done");
        var report = "On track: tests pending." + (defect == "missing" ? "" : "\n" + token)
            + (defect == "trailing-text" ? "\nmore" : "");
        CheckInterpretationValidator.Validate(taskId, 10, taskId, 10, AgentTaskStatus.Succeeded, report)
            .IsValid.ShouldBeFalse();
    }

    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Blocked)]
    [Arguments(AgentTaskStatus.Working)]
    public void Card0415_V06_terminal_success_is_required(AgentTaskStatus status)
    {
        var taskId = Guid.NewGuid();
        var report = "On track: tests pending.\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        CheckInterpretationValidator.Validate(taskId, 10, taskId, 10, status, report).IsValid.ShouldBeFalse();
    }

    [Test]
    [Arguments("task")]
    [Arguments("turn")]
    [Arguments("missing-sequence")]
    public void Card0415_V06_same_Check_and_current_turn_are_required(string defect)
    {
        var taskId = Guid.NewGuid();
        var report = "On track: tests pending.\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        CheckInterpretationValidator.Validate(taskId, defect == "missing-sequence" ? 0 : 10,
            defect == "task" ? Guid.NewGuid() : taskId, defect == "turn" ? 11 : 10,
            AgentTaskStatus.Succeeded, report).IsValid.ShouldBeFalse();
    }

    private static CheckReadingValidation Validate(string reading)
    {
        var taskId = Guid.NewGuid();
        return CheckInterpretationValidator.Validate(taskId, 10, taskId, 10, AgentTaskStatus.Succeeded,
            reading + "\n" + DelegationReportFormatter.ReportToken(taskId, "done") + "\n");
    }
}
