namespace Antiphon.Server.Application.Settings;

public class SignalRSettings
{
    public string HubPath { get; set; } = "/hubs/workflow";
    public int[] ReconnectIntervalsMs { get; set; } = [0, 2000, 5000, 10000, 30000];
    public int KeepAliveIntervalSeconds { get; set; } = 15;
    public int ClientTimeoutSeconds { get; set; } = 30;
}
