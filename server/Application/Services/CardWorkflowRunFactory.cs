using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>Copies an immutable revision into mutable per-card stage rows.</summary>
public sealed class CardWorkflowRunFactory
{
    private readonly TimeProvider _timeProvider;

    public CardWorkflowRunFactory(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    // The database parameter keeps existing hand-built AgentService test graphs compatible.
    public CardWorkflowRunFactory(AppDbContext db, TimeProvider timeProvider) : this(timeProvider) { }

    public CardWorkflowRun CreateFromRevision(
        Card card, PipelineDefinition definition, PipelineDefinitionRevision revision)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var run = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            PipelineDefinitionId = definition.Id,
            PipelineDefinitionRevisionId = revision.Id,
            WorkflowName = definition.Name,
            Status = CardWorkflowRunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var (stage, order) in PipelineStagesJson.Parse(revision.StagesJson)
                     .Select((value, index) => (value, index)))
        {
            run.Stages.Add(new CardWorkflowStage
            {
                Id = Guid.NewGuid(),
                CardWorkflowRunId = run.Id,
                StageOrder = order,
                Name = stage.Role.ToString(),
                Role = stage.Role,
                BundleKey = stage.BundleKey,
                AllowedNextJson = JsonSerializer.Serialize(stage.AllowedNext),
                Status = CardWorkflowStageStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        run.CurrentStageId = run.Stages.OrderBy(s => s.StageOrder).First().Id;
        return run;
    }
}
