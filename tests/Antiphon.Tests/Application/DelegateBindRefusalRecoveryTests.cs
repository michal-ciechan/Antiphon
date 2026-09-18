using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0551: the JSONL arm recovers only when a later assistant record ends with this task's
/// <c>[antiphon-report:&lt;id&gt; done]</c> line. Harness-free: <c>TryMatchJsonl</c> is internal.
/// </summary>
[Category("Unit")]
public sealed class DelegateBindRefusalRecoveryTests
{
    [Test]
    public async Task J1_user_marker_then_assistant_done_matches()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(taskId, "done")));

        Match(fixture).ShouldBeTrue();
    }

    [Test]
    public async Task J2_user_marker_then_assistant_done_dot_does_not_match()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            fixture.Assistant("done."));

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    public async Task J3_done_token_only_on_a_user_record_does_not_match()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.ReportToken(taskId, "done")));

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    [Arguments("thinking")]
    [Arguments("tool_use")]
    public async Task J4_done_token_only_in_thinking_or_tool_use_does_not_match(string block)
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        var token = DelegationReportFormatter.ReportToken(taskId, "done");
        var assistant = block == "thinking"
            ? fixture.AssistantThinking(token)
            : fixture.AssistantToolUse(token);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            assistant);

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    public async Task J5_done_token_for_another_task_does_not_match()
    {
        var taskId = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(other, "done")));

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    [Arguments("blocked")]
    [Arguments("failed")]
    public async Task J6_blocked_or_failed_verdict_does_not_match(string verdict)
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(taskId, verdict)));

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    public async Task J7_assistant_done_before_the_brief_does_not_match()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(taskId, "done")),
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief"));

        Match(fixture).ShouldBeFalse();
    }

    [Test]
    public async Task J8_queued_command_brief_then_assistant_done_matches()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        await fixture.WriteAsync(
            fixture.QueuedCommand(DelegationReportFormatter.TaskMarker(taskId) + " the brief"),
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(taskId, "done")));

        Match(fixture).ShouldBeTrue();
    }

    [Test]
    public async Task J9_c3_refused_first_timestamp_does_not_match_even_with_brief_and_done()
    {
        var taskId = Guid.NewGuid();
        using var fixture = new JsonlFixture(taskId);
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.WriteAsync(
            fixture.User(DelegationReportFormatter.TaskMarker(taskId) + " the brief", hourAgo),
            fixture.Assistant("Report.\n" + DelegationReportFormatter.ReportToken(taskId, "done"), hourAgo.AddMinutes(1)));

        Match(fixture).ShouldBeFalse();
    }

    private static bool Match(JsonlFixture fixture) =>
        DelegateBindRefusalRecovery.TryMatchJsonl(
            fixture.Path,
            fixture.Cwd,
            fixture.StartedAt,
            DelegateBindRefusalRecovery.JsonlNeedles(new AgentTask { Id = fixture.TaskId }),
            fixture.TaskId);

    private sealed class JsonlFixture : IDisposable
    {
        public JsonlFixture(Guid taskId)
        {
            TaskId = taskId;
            Cwd = Directory.CreateTempSubdirectory("c551-jsonl-cwd").FullName;
            Path = System.IO.Path.Combine(Cwd, taskId.ToString("D") + ".jsonl");
            StartedAt = DateTime.UtcNow.AddMinutes(-5);
        }

        public Guid TaskId { get; }
        public string Cwd { get; }
        public string Path { get; }
        public DateTime StartedAt { get; }

        public string User(string text, DateTimeOffset? timestamp = null) =>
            JsonSerializer.Serialize(new
            {
                type = "user",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = Cwd,
                timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o"),
                message = new { role = "user", content = text },
            });

        public string QueuedCommand(string prompt, DateTimeOffset? timestamp = null) =>
            JsonSerializer.Serialize(new
            {
                type = "attachment",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = Cwd,
                timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o"),
                attachment = new { type = "queued_command", prompt },
            });

        public string Assistant(string text, DateTimeOffset? timestamp = null) =>
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = Cwd,
                timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o"),
                message = new { role = "assistant", content = new[] { new { type = "text", text } } },
            });

        public string AssistantThinking(string thinking, DateTimeOffset? timestamp = null) =>
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = Cwd,
                timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o"),
                message = new
                {
                    role = "assistant",
                    content = new[] { new { type = "thinking", thinking } },
                },
            });

        public string AssistantToolUse(string token, DateTimeOffset? timestamp = null) =>
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = Cwd,
                timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("o"),
                message = new
                {
                    role = "assistant",
                    content = new[]
                    {
                        new
                        {
                            type = "tool_use",
                            name = "Write",
                            input = new { path = ".antiphon/task.md", content = token },
                        },
                    },
                },
            });

        public Task WriteAsync(params string[] lines) =>
            File.WriteAllTextAsync(Path, string.Join("\n", lines) + "\n");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Cwd))
                    Directory.Delete(Cwd, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
