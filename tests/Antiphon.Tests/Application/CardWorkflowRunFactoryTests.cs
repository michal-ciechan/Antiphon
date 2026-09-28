using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class CardWorkflowRunFactoryTests
{
    [Test]
    public void CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var definition = new PipelineDefinition { Id = Guid.NewGuid(), Name = "Six" };
        var revision = new PipelineDefinitionRevision { Id = Guid.NewGuid(), DefinitionId = definition.Id,
            RevisionNumber = 1, StagesJson = PipelineDefinitions.Json, ContentHash = PipelineDefinitions.Hash };
        var card = new Card { Id = Guid.NewGuid() };
        var run = new CardWorkflowRunFactory(new FixedClock(now))
            .CreateFromRevision(card, definition, revision);

        run.Status.ShouldBe(CardWorkflowRunStatus.Queued);
        run.CardId.ShouldBe(card.Id);
        run.PipelineDefinitionId.ShouldBe(definition.Id);
        run.PipelineDefinitionRevisionId.ShouldBe(revision.Id);
        run.WorkflowName.ShouldBe("Six");
        run.CreatedAt.ShouldBe(now);
        run.Stages.Select(s => s.StageOrder).ShouldBe([0, 1, 2, 3, 4, 5]);
        run.Stages.Select(s => s.Role).ShouldBe(PipelineDefinitions.StandardPipeline.Select(s => s.Role));
        run.Stages.Select(s => s.Name).ShouldBe(run.Stages.Select(s => s.Role.ToString()));
        run.Stages.Select(s => s.BundleKey).ShouldBe(PipelineDefinitions.StandardPipeline.Select(s => s.BundleKey));
        run.Stages.ShouldAllBe(s => s.Status == CardWorkflowStageStatus.Pending);
        run.CurrentStageId.ShouldBe(run.Stages.First().Id);
        run.Stages.First().AllowedNextJson.ShouldContain("plan");
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
