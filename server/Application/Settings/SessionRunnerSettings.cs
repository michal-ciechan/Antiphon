namespace Antiphon.Server.Application.Settings;

public sealed class SessionRunnerSettings
{
    public string BaseUrl { get; set; } = "http://localhost:17283";
    public bool Enabled { get; set; } = true;
    public int EventReconnectDelayMs { get; set; } = 1000;
}
