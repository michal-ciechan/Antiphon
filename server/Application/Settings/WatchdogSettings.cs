namespace Antiphon.Server.Application.Settings;

public sealed class WatchdogSettings
{
    public bool Enabled { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 10_000;
    public int CooldownMs { get; set; } = 60_000;
    public List<WatchdogRuleSettings> Rules { get; set; } =
    [
        new() { Name = "press-enter", Pattern = "Press Enter", Response = "\r" },
        new() { Name = "confirm-yes", Pattern = "(Y/n)", Response = "y\r" },
        new() { Name = "confirm-no", Pattern = "[y/N]", Response = "n\r" }
    ];
}

public sealed class WatchdogRuleSettings
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string Response { get; set; } = "\r";
    public bool IsRegex { get; set; }
}
