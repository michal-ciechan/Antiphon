namespace Antiphon.SessionRunner;

public sealed class SessionRunnerSettings
{
    public string SessionLogPath { get; set; } = Path.Combine("workspace", "session-runner-logs");
    public int ReplayBufferMaxChars { get; set; } = 256 * 1024;
}
