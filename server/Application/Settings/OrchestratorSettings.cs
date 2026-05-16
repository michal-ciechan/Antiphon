namespace Antiphon.Server.Application.Settings;

public sealed class OrchestratorSettings
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxDispatchesPerTick { get; set; } = 25;
    public int DefaultCols { get; set; } = 120;
    public int DefaultRows { get; set; } = 30;
    public int ContinuationDelayMs { get; set; } = 1_000;
    public int FailureBackoffBaseMs { get; set; } = 10_000;
    public int FailureBackoffMaxMs { get; set; } = 300_000;
    public int StartingSessionGraceSeconds { get; set; } = 300;
    public string? InternalTrackerRepositoryPathPrefix { get; set; }
}
