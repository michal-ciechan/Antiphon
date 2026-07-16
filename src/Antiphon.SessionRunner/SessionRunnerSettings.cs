namespace Antiphon.SessionRunner;

public sealed class SessionRunnerSettings
{
    public string SessionLogPath { get; set; } = Path.Combine("workspace", "session-runner-logs");
    public int ReplayBufferMaxChars { get; set; } = 256 * 1024;

    /// <summary>How often the liveness sweep verifies that "Running" sessions still have a live OS process.</summary>
    public int LivenessSweepIntervalMs { get; set; } = 5_000;
}
