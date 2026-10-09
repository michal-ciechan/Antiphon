using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0491 V-1. One flip per row. Expected decisions are written out, not derived.
/// </summary>
[Category("Unit")]
public sealed class MidTurnInterruptPolicyTests
{
    private static readonly Guid SessionA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SessionB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Started = new(2026, 10, 9, 18, 15, 29, DateTimeKind.Utc);

    [Test]
    [Arguments("positive")]
    [Arguments("positive-dispatched")]
    [Arguments("not-requested")]
    [Arguments("setting-disabled")]
    [Arguments("status-queued")]
    [Arguments("status-blocked")]
    [Arguments("status-succeeded")]
    [Arguments("status-failed")]
    [Arguments("status-canceled")]
    [Arguments("session-null")]
    [Arguments("row-session-differs")]
    [Arguments("kind-claude")]
    [Arguments("kind-codex")]
    [Arguments("kind-opencode")]
    [Arguments("kind-raw")]
    [Arguments("session-created")]
    [Arguments("session-starting")]
    [Arguments("session-stopping")]
    [Arguments("session-stopped")]
    [Arguments("session-failed")]
    [Arguments("runner-missing")]
    [Arguments("runner-exited")]
    [Arguments("generation-unknown")]
    [Arguments("generation-mismatch")]
    [Arguments("not-working")]
    [Arguments("row-sent")]
    [Arguments("row-canceled")]
    [Arguments("attempts-one")]
    [Arguments("open-question")]
    [Arguments("modal-blocked")]
    [Arguments("composer-draft")]
    [Arguments("composer-unreadable")]
    [Arguments("conditional-unsupported")]
    public void C0491_AdmitsTheKeyOnlyWhenEveryPositiveHolds(string flip)
    {
        var facts = Flip(flip);
        var expected = Expected(flip);
        MidTurnInterruptPolicy.Decide(facts).ShouldBe(expected, flip);
    }

    private static MidTurnInterruptFacts Positive() => new()
    {
        Enabled = true,
        Requested = true,
        Status = AgentTaskStatus.Working,
        TaskSessionId = SessionA,
        RowSessionId = SessionA,
        Kind = AgentKind.Grok,
        SessionStatus = SessionStatus.Running,
        RunnerFound = true,
        RunnerExited = false,
        SessionStartedAt = Started,
        RunnerAcceptedStartedAt = Started,
        Working = true,
        RowStatus = QueuedMessageStatus.Pending,
        DeliveryAttempts = 0,
        QuestionOpen = false,
        ModalBlocked = false,
        Composer = GrokComposerState.Empty,
        ConditionalCapability = true,
    };

    private static MidTurnInterruptFacts Flip(string flip)
    {
        var facts = Positive();
        return flip switch
        {
            "positive" => facts,
            "positive-dispatched" => facts with { Status = AgentTaskStatus.Dispatched },
            "not-requested" => facts with { Requested = false },
            "setting-disabled" => facts with { Enabled = false },
            "status-queued" => facts with { Status = AgentTaskStatus.Queued },
            "status-blocked" => facts with { Status = AgentTaskStatus.Blocked },
            "status-succeeded" => facts with { Status = AgentTaskStatus.Succeeded },
            "status-failed" => facts with { Status = AgentTaskStatus.Failed },
            "status-canceled" => facts with { Status = AgentTaskStatus.Canceled },
            "session-null" => facts with { TaskSessionId = null },
            "row-session-differs" => facts with { RowSessionId = SessionB },
            "kind-claude" => facts with { Kind = AgentKind.ClaudeCode },
            "kind-codex" => facts with { Kind = AgentKind.Codex },
            "kind-opencode" => facts with { Kind = AgentKind.OpenCode },
            "kind-raw" => facts with { Kind = AgentKind.Raw },
            "session-created" => facts with { SessionStatus = SessionStatus.Created },
            "session-starting" => facts with { SessionStatus = SessionStatus.Starting },
            "session-stopping" => facts with { SessionStatus = SessionStatus.Stopping },
            "session-stopped" => facts with { SessionStatus = SessionStatus.Stopped },
            "session-failed" => facts with { SessionStatus = SessionStatus.Failed },
            "runner-missing" => facts with { RunnerFound = false },
            "runner-exited" => facts with { RunnerExited = true },
            "generation-unknown" => facts with { RunnerAcceptedStartedAt = null },
            "generation-mismatch" => facts with { RunnerAcceptedStartedAt = Started.AddHours(-1) },
            "not-working" => facts with { Working = false },
            "row-sent" => facts with { RowStatus = QueuedMessageStatus.Sent },
            "row-canceled" => facts with { RowStatus = QueuedMessageStatus.Canceled },
            "attempts-one" => facts with { DeliveryAttempts = 1 },
            "open-question" => facts with { QuestionOpen = true },
            "modal-blocked" => facts with { ModalBlocked = true },
            "composer-draft" => facts with { Composer = GrokComposerState.Draft },
            "composer-unreadable" => facts with { Composer = GrokComposerState.Unreadable },
            "conditional-unsupported" => facts with { ConditionalCapability = false },
            _ => throw new ArgumentOutOfRangeException(nameof(flip), flip, "unmapped policy row"),
        };
    }

    private static MidTurnInterruptDecision Expected(string flip) => flip switch
    {
        "positive" or "positive-dispatched" => MidTurnInterruptDecision.Admit,
        "not-requested" => MidTurnInterruptDecision.Refuse("not-requested"),
        "setting-disabled" => MidTurnInterruptDecision.Refuse("disabled"),
        "status-queued" or "status-blocked" or "status-succeeded" or "status-failed" or "status-canceled"
            => MidTurnInterruptDecision.Refuse("task-status"),
        "session-null" => MidTurnInterruptDecision.Refuse("no-session"),
        "row-session-differs" => MidTurnInterruptDecision.Refuse("row-session-mismatch"),
        "kind-claude" or "kind-codex" or "kind-opencode" or "kind-raw"
            => MidTurnInterruptDecision.Refuse("kind-not-grok"),
        "session-created" or "session-starting" or "session-stopping" or "session-stopped" or "session-failed"
            => MidTurnInterruptDecision.Refuse("session-not-running"),
        "runner-missing" => MidTurnInterruptDecision.Refuse("runner-missing"),
        "runner-exited" => MidTurnInterruptDecision.Refuse("runner-exited"),
        "generation-unknown" => MidTurnInterruptDecision.Refuse("generation-unproven"),
        "generation-mismatch" => MidTurnInterruptDecision.Refuse("generation-mismatch"),
        "not-working" => MidTurnInterruptDecision.Refuse("not-working"),
        "row-sent" => MidTurnInterruptDecision.Refuse("already-sent"),
        "row-canceled" => MidTurnInterruptDecision.Refuse("row-not-pending"),
        "attempts-one" => MidTurnInterruptDecision.Refuse("row-attempted"),
        "open-question" => MidTurnInterruptDecision.Refuse("question-open"),
        "modal-blocked" => MidTurnInterruptDecision.Refuse("modal-blocked"),
        "composer-draft" => MidTurnInterruptDecision.Refuse("composer-not-empty"),
        "composer-unreadable" => MidTurnInterruptDecision.Refuse("composer-unreadable"),
        "conditional-unsupported" => MidTurnInterruptDecision.Refuse("conditional-input-unsupported"),
        _ => throw new ArgumentOutOfRangeException(nameof(flip), flip, "unmapped policy decision"),
    };
}
