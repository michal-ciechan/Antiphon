namespace Antiphon.Server.Domain.ValueObjects;

/// <summary>
/// Parsed representation of a YAML workflow template definition.
/// Pure value object — no infrastructure dependencies.
/// </summary>
public sealed record WorkflowDefinition(
    string Name,
    string Description,
    IReadOnlyList<StageDefinition> Stages,
    bool SelectableStages);

/// <summary>
/// A single stage within a workflow definition, parsed from YAML.
/// </summary>
public sealed record StageDefinition(
    string Name,
    string ExecutorType,
    string? ModelName,
    bool GateRequired,
    string? SystemPrompt);
