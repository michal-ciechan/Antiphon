using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0716 D-2. Operator-route settings. Token path stays on the phone-home settings.</summary>
public sealed class OperatorSettings
{
    public const int DefaultShutdownDrainSeconds = 10;

    /// <summary>How long a shutdown waits for in-flight launches. 0 skips the wait. 0..120.</summary>
    public int ShutdownDrainSeconds { get; set; } = DefaultShutdownDrainSeconds;
}

public sealed class OperatorSettingsValidator : IValidateOptions<OperatorSettings>
{
    public ValidateOptionsResult Validate(string? name, OperatorSettings options)
    {
        if (options.ShutdownDrainSeconds is < 0 or > 120)
            return ValidateOptionsResult.Fail("Operator:ShutdownDrainSeconds must be between 0 and 120.");
        return ValidateOptionsResult.Success;
    }
}
