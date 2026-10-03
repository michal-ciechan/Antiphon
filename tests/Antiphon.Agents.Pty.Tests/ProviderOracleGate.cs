using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

internal static class ProviderOracleGate
{
    public const string EnvFlag = "ANTIPHON_PTY_PROVIDER_ORACLES";
    public const string DisabledReason =
        "Set ANTIPHON_PTY_PROVIDER_ORACLES=1 to opt in to offline Codex/Grok provider oracles";

    public static void SkipIfNotEnabled(Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        if (readEnvironment(EnvFlag) != "1")
            throw new SkipTestException(DisabledReason);
    }
}
