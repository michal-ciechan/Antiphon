using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S4: the derived <c>AttentionKind.Overdue</c> boot row. Design V-11. Fixture: the
/// id-scoped <c>AttentionServiceTests.Scenario</c> and <c>BuildService</c> over the shared test
/// Postgres with an injected <c>FakeTimeProvider</c>; one Working task, one real UserPrompt, no
/// model row. The row is derived from current boot facts, never from a persisted Warning.
/// </summary>
[Category("Integration")]
public class BootStallAttentionTests
{
    private const string Canary = "boot-attention-canary-7f3a";

    // The visible text must promise no outcome. "retry" alone is allowed: the operator sentence
    // names an explicit retry as one of the human's choices.
    private static readonly string[] ForbiddenOutcomeWords =
        ["kill", "fail", "retried", "retries", "requeue", "release", "reclaim", "escalat"];

    /// <summary>
    /// V-11. preview-6m24s: Warning row, detection wording, actions exactly OpenDrawer/Reply/Cancel,
    /// no Escalate. detected-8m: same row, "detected" wording, no "kills"/"fails"/"retried" text
    /// anywhere in headline or evidence, no persisted Warning required. operator-20m: Error, the
    /// operator sentence (inspect; wait, reply, or explicitly cancel/retry), original prompt age and
    /// boot due time in evidence. past-ceiling: still the boot row, Error, the ceiling breach named
    /// as evidence, no "will fail it". model-reply-resolves: one AssistantText after the prompt
    /// removes the row even though historical Warning events remain.
    /// </summary>
    [Test]
    [Arguments("preview-6m24s")]
    [Arguments("detected-8m")]
    [Arguments("operator-20m")]
    [Arguments("past-ceiling")]
    [Arguments("model-reply-resolves")]
    public async Task C1151_Attention_describes_detection_and_resolution(string moment)
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Working, dispatchedMinutesAgo: 1);
        DateTime dispatchedAt;
        await using (var db = NewContext())
            dispatchedAt = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task)).DispatchedAt!.Value;

        // Whole seconds, so the stored (microsecond) timestamp is exactly the one the clock is set from.
        var promptAt = new DateTime(dispatchedAt.Ticks - dispatchedAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc)
            .AddSeconds(30);
        await scenario.AddTranscriptAtAsync(session, promptAt, (TranscriptKinds.UserPrompt, $"the brief {Canary}", null));

        var offset = moment switch
        {
            "preview-6m24s" => TimeSpan.FromSeconds(6 * 60 + 24),
            "detected-8m" => TimeSpan.FromMinutes(8),
            "operator-20m" => TimeSpan.FromMinutes(20),
            "past-ceiling" => TimeSpan.FromMinutes(245),
            "model-reply-resolves" => TimeSpan.FromMinutes(9),
            _ => throw new ArgumentOutOfRangeException(nameof(moment), moment, null),
        };
        var now = promptAt + offset;
        var clock = new FakeTimeProvider(new DateTimeOffset(now));

        var row = await BootRowAsync(scenario, task, clock);

        // Every moment: the boot row, with the human's actions only, and no promised outcome.
        row.ShouldNotBeNull($"{moment}: an unresolved boot episode is listed");
        row.Kind.ShouldBe(AttentionKind.Overdue);
        row.SessionId.ShouldBe(session);
        row.SinceUtc.ShouldBe(promptAt, "the row's age is the original prompt's, whatever the clock");
        row.Actions.ShouldBe([AttentionAction.OpenDrawer, AttentionAction.Reply, AttentionAction.Cancel],
            "no Escalate: nothing escalates or reclaims the seat on its own; Retry is an ordinary task control");
        var visible = row.Headline + "\n" + row.Evidence;
        foreach (var word in ForbiddenOutcomeWords)
            visible.ShouldNotContain(word, Case.Insensitive, $"{moment}: no '{word}' promise in the boot row");
        visible.ShouldNotContain(Canary, Case.Sensitive, "the prompt text never reaches the row");
        row.Evidence.ShouldContain(
            "Inspect the session, then choose: keep waiting, reply, or explicitly cancel or retry the task.",
            Case.Sensitive);
        row.Evidence.ShouldContain("Detection only", Case.Sensitive);
        row.Evidence.ShouldContain($"Boot notice due {promptAt.AddMinutes(8):u}.", Case.Sensitive);
        row.Evidence.ShouldContain($"Operator decision due {promptAt.AddMinutes(20):u}.", Case.Sensitive);
        row.Evidence.ShouldContain($"Accepted prompt #1 at {promptAt:u}", Case.Sensitive);
        (await WarningCountAsync(task)).ShouldBe(0, "the row is derived from boot facts, never from a persisted Warning");

        switch (moment)
        {
            case "preview-6m24s":
                row.Severity.ShouldBe(AlertSeverity.Warning);
                row.Headline.ShouldBe("Boot prompt unanswered: no model reply 6m after the prompt yet.");
                row.Evidence.ShouldNotContain("deadline passed", Case.Sensitive);
                break;

            case "detected-8m":
                row.Severity.ShouldBe(AlertSeverity.Warning, "detected is still 'look at this'");
                row.Headline.ShouldBe("Boot stall detected: no model reply 8m after the prompt.");
                row.Evidence.ShouldNotContain("deadline passed", Case.Sensitive);
                break;

            case "operator-20m":
                row.Severity.ShouldBe(AlertSeverity.Error, "the operator threshold is inclusive");
                row.Headline.ShouldBe("Boot stall needs an operator decision: no model reply 20m after the prompt.");
                row.Evidence.ShouldContain(
                    $"The general 20-minute model-wait deadline passed {promptAt.AddMinutes(20):u}; it does not end a boot episode.",
                    Case.Sensitive);
                row.Evidence.ShouldNotContain("ceiling", Case.Sensitive);
                break;

            case "past-ceiling":
                row.Severity.ShouldBe(AlertSeverity.Error);
                row.Headline.ShouldBe("Boot stall needs an operator decision: no model reply 4h05m after the prompt.");
                row.Headline.ShouldNotContain("Past its deadline", Case.Sensitive,
                    "the generic Overdue row would promise the next sweep acts on it");
                row.Evidence.ShouldContain(
                    $"The 240-minute ceiling for role Code passed {dispatchedAt.AddMinutes(240):u}; it does not end a boot episode.",
                    Case.Sensitive);
                row.Evidence.ShouldContain("model-wait deadline passed", Case.Sensitive);
                break;

            case "model-reply-resolves":
                row.Headline.ShouldStartWith("Boot stall detected", Case.Sensitive);
                await scenario.AddTaskEventAsync(
                    task, AgentTaskEventType.Warning,
                    $"{BootStallPolicy.DetectedToken} episode=historical; detection only.", minutesAgo: 0,
                    at: promptAt.AddMinutes(8));
                await scenario.AddTranscriptAtAsync(
                    session, promptAt.AddSeconds(8 * 60 + 30), (TranscriptKinds.AssistantText, "here at last", null));

                (await BootRowAsync(scenario, task, clock)).ShouldBeNull(
                    "a model reply ends the boot episode and its row, whatever was written before");
                (await WarningCountAsync(task)).ShouldBe(1, "the historical Warning is still there");
                (await ItemsAsync(scenario, clock)).ShouldNotContain(
                    i => i.TaskId == task,
                    "nine minutes in, an answered task is nowhere near an ordinary deadline");
                break;
        }
    }

    private static async Task<AttentionItemDto?> BootRowAsync(
        AttentionServiceTests.Scenario scenario, Guid task, TimeProvider clock)
    {
        var rows = (await ItemsAsync(scenario, clock)).Where(i => i.TaskId == task).ToList();
        rows.Count.ShouldBeLessThanOrEqualTo(1, "one row per task: " + string.Join(", ", rows.Select(r => r.Kind)));
        var row = rows.SingleOrDefault();
        if (row is not null && row.Kind != AttentionKind.Overdue)
            throw new ShouldAssertException($"expected the Overdue boot row or nothing, got {row.Kind}: {row.Headline}");
        return row;
    }

    private static async Task<List<AttentionItemDto>> ItemsAsync(
        AttentionServiceTests.Scenario scenario, TimeProvider clock)
    {
        var result = await AttentionServiceTests
            .BuildService(new AttentionServiceTests.FakeRunnerClient(), timeProvider: clock)
            .GetAsync(CancellationToken.None);
        return result.Items.Where(scenario.Owns).ToList();
    }

    private static async Task<int> WarningCountAsync(Guid task)
    {
        await using var db = NewContext();
        return await db.AgentTaskEvents.AsNoTracking()
            .CountAsync(e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Warning);
    }

    private static AppDbContext NewContext() => new(TestDbFixture.CreateDbContextOptions());
}
