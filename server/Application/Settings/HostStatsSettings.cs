using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class HostStatsSettings
{
    public const string SectionName = "HostStats";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 5000;
    public int StaleAfterMs { get; set; } = 15000;
    public int RequestTimeoutMs { get; set; } = 3000;
    public int SeriesTimeoutMs { get; set; } = 5000;

    public void Validate()
    {
        if (PollIntervalMs <= 0 || StaleAfterMs <= 0 || RequestTimeoutMs <= 0 || SeriesTimeoutMs <= 0)
            throw new OptionsValidationException(SectionName, typeof(HostStatsSettings),
                ["Host stats intervals and timeouts must be positive."]);
    }
}
