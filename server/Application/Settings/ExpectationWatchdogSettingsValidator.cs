using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// S1 scaffold. Syntax checks land with the ledger write; this build accepts every document
/// so the red persist test can execute.
/// </summary>
public sealed class ExpectationWatchdogSettingsValidator : IValidateOptions<ExpectationWatchdogSettings>
{
    public ValidateOptionsResult Validate(string? name, ExpectationWatchdogSettings options) =>
        ValidateOptionsResult.Success;
}
