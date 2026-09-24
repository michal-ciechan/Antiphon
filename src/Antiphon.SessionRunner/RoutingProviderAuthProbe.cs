using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0647. One <see cref="IProviderAuthProbe"/> for the phone-home operation: Claude keeps
/// CARD-0628's probe, Grok is presence of <c>GROK_HOME/auth.json</c>.
/// </summary>
public sealed class RoutingProviderAuthProbe(ClaudeAuthProbe claude, GrokAuthProbe grok) : IProviderAuthProbe
{
    public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
    {
        if (string.Equals(provider, GrokAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return grok.ProbeAsync(provider, ct);
        if (string.Equals(provider, ClaudeAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return claude.ProbeAsync(provider, ct);
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.UnsupportedTarget,
            $"Provider '{provider}' has no auth probe on this runner.",
            400);
    }
}
