namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0589 S4: <c>Landing</c> — how the land verifier waits for a host build slot.</summary>
public sealed class LandingSettings
{
    /// <summary>
    /// The longest the verifier waits for a slot before refusing to build (step <c>build-slot</c>),
    /// inside the land's own time budget. The delegate wrappers wait 45.
    /// </summary>
    public int BuildSlotWaitMinutes { get; set; } = 30;

    /// <summary>
    /// How long an unanswering runner (or one without <c>/build-slots</c>) is retried before the
    /// verifier builds unleased at <c>-maxcpucount:4</c>. Never a land failure.
    /// </summary>
    public int BuildSlotUnreachableGraceSeconds { get; set; } = 60;
}
