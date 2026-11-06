using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;

namespace Antiphon.Server.Application.Services;

/// <summary>The code-owned default; editing its content appends a revision on startup.</summary>
public static class PipelineDefinitions
{
    public static readonly Guid StandardPipelineId = new("f0000000-0000-0000-0000-000000000001");
    public const string StandardPipelineName = "Standard pipeline";

    public static IReadOnlyList<PipelineStageSpec> StandardPipeline =>
    [
        new(AgentTaskRole.Investigate, "stage-investigate", ["plan", "investigate", "decide", "none"]),
        new(AgentTaskRole.Plan, "stage-plan", ["test-design", "code", "decide", "investigate"]),
        new(AgentTaskRole.TestDesign, "stage-test-design", ["code", "plan", "decide"]),
        new(AgentTaskRole.Code, "stage-code", ["review", "code", "decide"]),
        new(AgentTaskRole.Review, "stage-review", ["land", "review", "code", "decide"]),
        new(AgentTaskRole.Mutation, "stage-mutation", ["none", "decide"])
    ];

    public static string Json => PipelineStagesJson.Serialise(StandardPipeline);
    public static string Hash => PipelineStagesJson.ContentHash(Json);
}
