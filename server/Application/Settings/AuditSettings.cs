namespace Antiphon.Server.Application.Settings;

public class AuditSettings
{
    public int RetentionDays { get; set; } = 90;
    public bool EnableFullContent { get; set; } = true;
    public bool EnableIpLogging { get; set; } = true;
}
