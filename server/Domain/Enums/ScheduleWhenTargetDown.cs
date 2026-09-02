namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// What a prompt schedule does when the target agent has no live session (CARD-0057 D3).
/// Defaults: <see cref="Queue"/> for always-on agents, <see cref="Skip"/> otherwise.
/// </summary>
public enum ScheduleWhenTargetDown
{
    /// <summary>
    /// Enqueue onto the persistent session. <c>AgentControlService.StartAsync</c> carries
    /// pending rows across a relaunch.
    /// </summary>
    Queue = 0,

    /// <summary>Do not enqueue. Record <c>SkippedNoSession</c>.</summary>
    Skip = 1,
}
