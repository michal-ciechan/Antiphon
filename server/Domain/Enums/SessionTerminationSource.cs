namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Who or what ended an <see cref="Antiphon.Server.Domain.Entities.AgentSession"/>. <see cref="SessionStatus.Stopped"/>
/// is not evidence of an operator action — a clean process exit lands the same status — so the
/// dead-session classifier may only name an operator when this is
/// <see cref="OperatorRequest"/> (CARD-0256).
/// </summary>
public enum SessionTerminationSource
{
    /// <summary>Legacy rows, and any path that closed the session without recording a source.</summary>
    Unknown = 0,

    /// <summary>An explicit Stop from the control API or the session kill endpoint.</summary>
    OperatorRequest = 1,

    /// <summary>Dispatcher cleanup, pool retirement, cancel/retry, or another internal stop.</summary>
    SystemRequest = 2,

    /// <summary>
    /// The process exited (or reconciliation observed that exit) and no prior request source
    /// had been persisted.
    /// </summary>
    ProcessExit = 3,

    /// <summary>
    /// CARD-0334: the policy-refresh sweep killed this session at an idle boundary so the
    /// next start can resume the same conversation with rebuilt standing instructions.
    /// Distinct from <see cref="SystemRequest"/> so an operator stop and a policy relaunch
    /// stay distinguishable, and from <see cref="OperatorRequest"/> so supervision is not
    /// treated as a human stop.
    /// </summary>
    PolicyRefresh = 4,
}
