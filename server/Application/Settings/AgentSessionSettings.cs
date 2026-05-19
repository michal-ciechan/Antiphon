namespace Antiphon.Server.Application.Settings;

public sealed class AgentSessionSettings
{
    public int SignalRMaxChunkChars { get; set; } = 16 * 1024;
    public int ReplayBufferMaxChars { get; set; } = 512 * 1024;
    public string SessionLogPath { get; set; } = @"C:\MavLog\Antiphon\sessions";
    public int FirstDeltaTimeoutMs { get; set; } = 5_000;
    public int KillGraceMs { get; set; } = 5_000;
    public int StallTimeoutMs { get; set; } = 300_000;
    public int StallScanIntervalMs { get; set; } = 10_000;
    public int ManualTurnQuietPeriodMs { get; set; } = 3_000;
    public int MemoryLimitMb { get; set; } = 0;
}
