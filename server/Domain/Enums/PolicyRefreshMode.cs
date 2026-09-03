namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// How a standing agent picks up drifted bundles and instruction files (CARD-0334).
/// Null on <c>Agent.PolicyRefreshMode</c> means <see cref="Auto"/>.
/// </summary>
public enum PolicyRefreshMode
{
    /// <summary>AlwaysOn ClaudeCode relaunches at idle; everything else is notified.</summary>
    Auto = 0,

    /// <summary>Kill and resume with rebuilt launch args at the next idle boundary.</summary>
    Relaunch = 1,

    /// <summary>WhenIdle system note naming what changed; the process keeps its current prompt.</summary>
    Notify = 2,

    /// <summary>Neither lane. Drift is still visible on the badge.</summary>
    Off = 3,
}
