using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One run of an <see cref="OrchestrationStage"/> (CARD-0272). Append-only: an orchestrator override
/// writes a later row pointing at the one it replaces via <see cref="SupersedesId"/>; the report
/// takes the latest per (task, stage). Not an incident and not a task event — clean runs have to
/// survive for months, and the hit rate must not parse prose.
/// </summary>
public class StageOutcome
{
    public const int DetailMaxLength = 1000;

    public Guid Id { get; set; }
    public OrchestrationStage Stage { get; set; }
    public StageOutcomeKind Outcome { get; set; }
    public StageOutcomeSource Source { get; set; }

    /// <summary>The task whose work was being checked — the landed task; the reviewed task when known.</summary>
    public Guid? SubjectTaskId { get; set; }

    /// <summary>The task that ran the stage. Null for a server-run step.</summary>
    public Guid? StageTaskId { get; set; }

    /// <summary>Denormalised from the task at write time: the card is the unit the question is asked in.</summary>
    public Guid? CardId { get; set; }

    /// <summary>Copied from the stage task at settlement; null for a server-run step.</summary>
    public decimal? CostUsd { get; set; }
    public long? TokensIn { get; set; }
    public long? TokensOut { get; set; }

    /// <summary>Stopwatch inside the step for the server; CompletedAt − DispatchedAt for a delegate.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>The Merge task that resolved a Rebase finding, and what it cost. Set when that task settles.</summary>
    public Guid? ResolutionTaskId { get; set; }
    public decimal? ResolutionCostUsd { get; set; }

    /// <summary>≤ 1 000 chars: conflict files, the refusal tail head, the delegate's finding line, the orchestrator's note.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>A SHA, a task id, a verdict line — whatever lets a reader chase it.</summary>
    public string? Ref { get; set; }

    /// <summary>An orchestrator override points at the row it replaces; the report takes the latest per (task, stage).</summary>
    public Guid? SupersedesId { get; set; }

    public DateTime RecordedAt { get; set; }
}
