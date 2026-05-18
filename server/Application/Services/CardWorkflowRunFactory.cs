using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class CardWorkflowRunFactory
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CardWorkflowRunFactory(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<CardWorkflowRun> CreateFromAgentDefaultAsync(
        Card card,
        Agent agent,
        CancellationToken ct)
    {
        var template = agent.DefaultWorkflowTemplateId is Guid templateId
            ? await _db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
                ?? throw new NotFoundException(nameof(WorkflowTemplate), templateId)
            : await _db.WorkflowTemplates
                .OrderBy(t => t.Name)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException(
                    nameof(agent.DefaultWorkflowTemplateId),
                    "At least one workflow template is required.");

        var definition = WorkflowDefinitionParser.ParseYamlDefinition(template.YamlDefinition);
        return CreateRun(card, agent, template, definition);
    }

    private CardWorkflowRun CreateRun(
        Card card,
        Agent agent,
        WorkflowTemplate template,
        WorkflowDefinition definition)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var run = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AgentId = agent.Id,
            WorkflowTemplateId = template.Id,
            WorkflowName = definition.Name,
            WorkflowDefinitionSnapshot = template.YamlDefinition,
            Status = CardWorkflowRunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var stage in definition.Stages.Select((stage, index) => new { stage, index }))
        {
            run.Stages.Add(new CardWorkflowStage
            {
                Id = Guid.NewGuid(),
                CardWorkflowRunId = run.Id,
                StageOrder = stage.index,
                Name = stage.stage.Name,
                ExecutorType = stage.stage.ExecutorType,
                ModelName = stage.stage.ModelName,
                GateRequired = stage.stage.GateRequired,
                SystemPrompt = stage.stage.SystemPrompt,
                Status = CardWorkflowStageStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        run.CurrentStageId = run.Stages.OrderBy(s => s.StageOrder).FirstOrDefault()?.Id;
        return run;
    }
}
