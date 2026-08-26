using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Pure projection helpers keep report text and window rules deterministic.</summary>
public class AwayDigestProjectionTests
{
    [Test]
    public void First_sentence_uses_the_first_sentence_or_single_line()
    {
        AwayDigestProjection.FirstSentence("First sentence. Second sentence.").ShouldBe("First sentence.");
        AwayDigestProjection.FirstSentence("Single line").ShouldBe("Single line");
    }

    [Test]
    public void Cost_walk_rolls_children_into_their_root_once()
    {
        var rootId = Guid.NewGuid();
        var root = new Antiphon.Server.Domain.Entities.AgentTask { Id = rootId, RootTaskId = rootId, CostUsd = 2m };
        var child = new Antiphon.Server.Domain.Entities.AgentTask { Id = Guid.NewGuid(), RootTaskId = rootId, ParentTaskId = rootId, CostUsd = 3m };

        AgentTaskCostWalk.Calculate([root], [root, child])[rootId].ShouldBe(5m);
    }
}
