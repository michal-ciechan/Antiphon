namespace Antiphon.Server.Application.Settings;

public sealed class HerdrLabelFollowSettings
{
    public bool Enabled { get; set; } = true;
    public int SweepPeriodSeconds { get; set; } = 60;

    public void Validate()
    {
        if (SweepPeriodSeconds <= 0)
            throw new Microsoft.Extensions.Options.OptionsValidationException(nameof(HerdrLabelFollowSettings),
                typeof(HerdrLabelFollowSettings), ["SweepPeriodSeconds must be positive."]);
    }
}
