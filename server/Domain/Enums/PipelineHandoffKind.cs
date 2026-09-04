namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0146. The <c>next:</c> token a stage-role report declares, stored on
/// <see cref="Entities.AgentTask.NextStage"/>. Distinct from <see cref="OrchestrationStage"/>
/// (the CARD-0272 landing-step axis). <see cref="Land"/> and <see cref="Decide"/> produce no
/// ready row — the first is the orchestrator's <c>-Land</c>, the second is a human's.
/// </summary>
public enum PipelineHandoffKind
{
    Investigate = 0,
    Plan = 1,
    TestDesign = 2,
    Code = 3,
    Review = 4,
    Land = 5,
    Decide = 6,
    None = 7,
}
