using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class MutationRoleContractTests
{
    [Test]
    public void C470_enum_and_stage_contract()
    {
        string[] roles = ["Custom", "Plan", "Code", "Review", "Debug", "Coverage", "Docs", "Commit",
            "Test", "Deploy", "Merge", "Check", "Distill", "Diagnose", "Investigate", "TestDesign", "Mutation"];
        for (var i = 0; i < roles.Length; i++)
            ((int)Enum.Parse<AgentTaskRole>(roles[i])).ShouldBe(i);
        string[] destinations = ["Investigate", "Plan", "TestDesign", "Code", "Review", "Land", "Decide", "None", "Mutation"];
        for (var i = 0; i < destinations.Length; i++)
            ((int)Enum.Parse<PipelineHandoffKind>(destinations[i])).ShouldBe(i);
        AgentTaskRoles.IsStage(AgentTaskRole.Mutation).ShouldBeTrue();
        AgentTaskRoles.IsSpecialist(AgentTaskRole.Mutation).ShouldBeFalse();
        AgentTaskRoles.IsOptionalWork(new AgentTask { Role = AgentTaskRole.Mutation }).ShouldBeFalse();
    }

    [Test]
    public void C470_mutation_policy_is_independent()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Code"].Level = AgentModelLevel.Low;
        settings.RolePolicy["Code"].RecommendedInFlight = 9;
        var mutation = settings.RolePolicy["Mutation"];
        mutation.Level.ShouldBe(AgentModelLevel.Frontier);
        mutation.RecommendedInFlight.ShouldBe(1);
        ComplexityRoutingService.RoutableRoles.ShouldContain(AgentTaskRole.Mutation);
    }
}
