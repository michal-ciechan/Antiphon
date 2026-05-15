namespace Antiphon.Server.Domain.ValueObjects;

/// <summary>
/// Parsed representation of a YAML workflow template definition.
/// Pure value object — no infrastructure dependencies.
/// </summary>
public sealed record WorkflowDefinition(
    string Name,
    string Description,
    IReadOnlyList<StageDefinition> Stages,
    bool SelectableStages,
    WorkflowHooks Hooks)
{
    public WorkflowDefinition(
        string name,
        string description,
        IReadOnlyList<StageDefinition> stages,
        bool selectableStages)
        : this(name, description, stages, selectableStages, WorkflowHooks.Empty)
    {
    }
}

/// <summary>
/// A single stage within a workflow definition, parsed from YAML.
/// </summary>
public sealed record StageDefinition(
    string Name,
    string ExecutorType,
    string? ModelName,
    bool GateRequired,
    string? SystemPrompt);

/// <summary>
/// Optional shell hooks configured on a workflow definition.
/// </summary>
public sealed record WorkflowHooks(
    WorkspaceHookDefinition? AfterCreate,
    WorkspaceHookDefinition? BeforeRun,
    WorkspaceHookDefinition? AfterRun,
    WorkspaceHookDefinition? BeforeRemove)
{
    public static WorkflowHooks Empty => new(null, null, null, null);

    public WorkspaceHookDefinition? GetByName(string hookName) =>
        hookName switch
        {
            "after_create" => AfterCreate,
            "before_run" => BeforeRun,
            "after_run" => AfterRun,
            "before_remove" => BeforeRemove,
            _ => null
        };
}

/// <summary>
/// Shell command plus timeout for a workspace lifecycle hook.
/// </summary>
public sealed record WorkspaceHookDefinition(
    string Command,
    TimeSpan Timeout);
