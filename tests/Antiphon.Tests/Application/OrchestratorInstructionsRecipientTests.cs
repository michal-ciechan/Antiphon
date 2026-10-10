using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class OrchestratorInstructionsRecipientTests
{
    [Test]
    public void Standing_bundle_agent_with_a_running_pty_session_is_selected()
    {
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var chosen = OrchestratorInstructionsRecipients.Select(
            [Agent(agentId, sessionId)],
            [Session(sessionId, agentId)],
            [],
            "abcdef12",
            OrchestratorInstructionsNotify.All);
        chosen.ShouldBe([sessionId]);
    }

    [Test]
    public void Orchestrator_task_sessions_are_in_and_worker_task_sessions_are_out()
    {
        var orchestratorSession = Guid.NewGuid();
        var workerSession = Guid.NewGuid();
        var chosen = OrchestratorInstructionsRecipients.Select(
            [],
            [Session(orchestratorSession, null), Session(workerSession, null)],
            [
                new OrchestratorInstructionsTask(AgentTaskKind.Orchestrator, AgentTaskStatus.Working, orchestratorSession),
                new OrchestratorInstructionsTask(AgentTaskKind.Worker, AgentTaskStatus.Working, workerSession),
            ],
            "abcdef12",
            OrchestratorInstructionsNotify.All);
        chosen.ShouldBe([orchestratorSession]);
    }

    [Test]
    public void Herdr_specialist_card_and_off_sessions_are_out()
    {
        var herdr = Guid.NewGuid();
        var specialistSession = Guid.NewGuid();
        var specialist = Guid.NewGuid();
        var card = Guid.NewGuid();
        var offSession = Guid.NewGuid();
        var offAgent = Guid.NewGuid();
        var chosen = OrchestratorInstructionsRecipients.Select(
            [
                Agent(specialist, specialistSession) with { IsSpecialist = true },
                Agent(offAgent, offSession) with { PolicyRefreshMode = PolicyRefreshMode.Off },
            ],
            [
                Session(herdr, null) with { Backend = SessionBackend.Herdr },
                Session(specialistSession, specialist),
                Session(card, null) with { CardId = Guid.NewGuid() },
                Session(offSession, offAgent),
            ],
            [
                new OrchestratorInstructionsTask(AgentTaskKind.Orchestrator, AgentTaskStatus.Dispatched, herdr),
                new OrchestratorInstructionsTask(AgentTaskKind.Orchestrator, AgentTaskStatus.Working, card),
                new OrchestratorInstructionsTask(AgentTaskKind.Orchestrator, AgentTaskStatus.Working, offSession),
            ],
            "abcdef12",
            OrchestratorInstructionsNotify.All);
        chosen.ShouldBeEmpty();
    }

    [Test]
    public void A_session_already_at_the_current_version_is_skipped()
    {
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var chosen = OrchestratorInstructionsRecipients.Select(
            [Agent(agentId, sessionId)],
            [Session(sessionId, agentId) with { InstructionsVersion = "abcdef12" }],
            [],
            "abcdef12",
            OrchestratorInstructionsNotify.All);
        chosen.ShouldBeEmpty();
    }

    private static OrchestratorInstructionsAgent Agent(Guid id, Guid sessionId) => new(
        id,
        CarriesOrchestratorBundle: true,
        IsPoolDelegate: false,
        IsSpecialist: false,
        PersistentSessionId: sessionId,
        PolicyRefreshMode: null);

    private static OrchestratorInstructionsSession Session(Guid id, Guid? agentId) => new(
        id,
        agentId,
        SessionStatus.Running,
        SessionBackend.PtyHost,
        CardId: null,
        InstructionsVersion: null);
}
