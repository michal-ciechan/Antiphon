namespace Antiphon.Server.Application.Settings;

public sealed class SessionStateSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxSessions { get; set; } = 4096;
    public int WarmupBatchSize { get; set; } = 512;
    public int TerminalIdleMinutes { get; set; } = 15;
    public int FailureRetrySeconds { get; set; } = 5;
}
