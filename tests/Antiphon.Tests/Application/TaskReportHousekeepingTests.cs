using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0714 provider allowlist. Eleven executions: the measured envelope is housekeeping only
/// for Grok, and only when every line and both ids agree.
/// </summary>
[Category("Unit")]
public class TaskReportHousekeepingTests
{
    [Test]
    public async Task C714_lf_envelope_is_grok_housekeeping()
    {
        TaskReportHousekeeping.Classify(AgentKind.Grok, Card0714Transcript.Reminder)
            .ShouldBe(TaskReportHousekeeping.Shape.GrokBackgroundCompletion);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_crlf_envelope_is_grok_housekeeping()
    {
        var crlf = Card0714Transcript.Reminder.Replace("\n", "\r\n", StringComparison.Ordinal);
        TaskReportHousekeeping.Classify(AgentKind.Grok, "\r\n" + crlf + "\r\n")
            .ShouldBe(TaskReportHousekeeping.Shape.GrokBackgroundCompletion);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_unknown_system_reminder_is_real_prompt()
    {
        const string generic = "<system-reminder>\nRemember to use the tools.\n</system-reminder>";
        TaskReportHousekeeping.Classify(AgentKind.Grok, generic)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        TaskReportHousekeeping.IsMeasuredCompletionEnvelope(generic).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_quoted_reminder_is_real_prompt()
    {
        var quoted = "The tool said:\n" + Card0714Transcript.Reminder;
        TaskReportHousekeeping.Classify(AgentKind.Grok, quoted)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_suffixed_reminder_is_real_prompt()
    {
        var suffixed = Card0714Transcript.Reminder + "\nThanks, that is the whole story.";
        TaskReportHousekeeping.Classify(AgentKind.Grok, suffixed)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_incomplete_envelope_is_real_prompt()
    {
        const string incomplete =
            "<system-reminder>\n"
            + "Background task \"01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7\" completed (exit code: 1).\n"
            + "Use get_command_or_subagent_output(\"01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7\") to see the full output.\n"
            + "</system-reminder>";
        TaskReportHousekeeping.Classify(AgentKind.Grok, incomplete)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_mismatched_completion_ids_are_real_prompt()
    {
        var mismatched = Card0714Transcript.Reminder.Replace(
            "Use get_command_or_subagent_output(\"" + Card0714Transcript.BackgroundTaskId + "\")",
            "Use get_command_or_subagent_output(\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\")",
            StringComparison.Ordinal);
        TaskReportHousekeeping.Classify(AgentKind.Grok, mismatched)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments(AgentKind.ClaudeCode)]
    [Arguments(AgentKind.Codex)]
    public async Task C714_grok_shape_on_other_provider_is_real_prompt(AgentKind provider)
    {
        TaskReportHousekeeping.Classify(provider, Card0714Transcript.Reminder)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_claude_notification_keeps_its_identity()
    {
        const string notification =
            "<task-notification>\n<tool-use-id>toolu_abc</tool-use-id>\n<status>completed</status>\n</task-notification>";
        TaskReportHousekeeping.Classify(AgentKind.ClaudeCode, notification)
            .ShouldBe(TaskReportHousekeeping.Shape.ClaudeTaskNotification);
        TranscriptKinds.TryReadNotifiedToolUseId(notification).ShouldBe("toolu_abc");
        TaskReportHousekeeping.Classify(AgentKind.Grok, notification)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C714_codex_text_is_not_a_housekeeping_prompt()
    {
        TaskReportHousekeeping.Classify(AgentKind.Codex, "task_complete").ShouldBe(TaskReportHousekeeping.Shape.None);
        TaskReportHousekeeping.Classify(AgentKind.Codex, "Background task \"abc\" completed (exit code: 0).")
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        TaskReportHousekeeping.Classify(AgentKind.Codex, Card0714Transcript.Reminder)
            .ShouldBe(TaskReportHousekeeping.Shape.None);
        await Task.CompletedTask;
    }
}
