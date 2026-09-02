namespace Antiphon.Server.Domain.Enums;

/// <summary>How a card-kind schedule spends a session at fire time (CARD-0057 D6). Phase 2.</summary>
public enum ScheduleStart
{
    /// <summary>Bookkeeping move only. Structurally cannot spawn.</summary>
    None = 0,

    /// <summary>Move, then lift the auto-dispatch hold so the orchestrator may start under caps.</summary>
    Release = 1,

    /// <summary>Spawn a session at fire time, bypassing caps.</summary>
    Spawn = 2,
}
