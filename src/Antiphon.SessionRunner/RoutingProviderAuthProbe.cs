using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0647. One <see cref="IProviderAuthProbe"/> for the phone-home operation: Claude keeps
/// CARD-0628's probe, Grok is presence of <c>GROK_HOME/auth.json</c>, and Codex (CARD-0660) is
/// metadata-only presence of <c>CODEX_HOME/auth.json</c>.
/// </summary>
public sealed class RoutingProviderAuthProbe(ClaudeAuthProbe claude, GrokAuthProbe grok, CodexAuthProbe codex) : IProviderAuthProbe
{
    public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
    {
        if (string.Equals(provider, GrokAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return grok.ProbeAsync(provider, ct);
        if (string.Equals(provider, ClaudeAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return claude.ProbeAsync(provider, ct);
        if (string.Equals(provider, CodexAuthProbe.ProviderName, StringComparison.OrdinalIgnoreCase))
            return codex.ProbeAsync(provider, ct);
        throw new PhoneHomeAdmissionException(
            PhoneHomeProblemTypes.UnsupportedTarget,
            $"Provider '{provider}' has no auth probe on this runner.",
            400);
    }
}

/// <summary>
/// The runner's provider-auth composition, shared by <c>Program.cs</c> and the routing test so the
/// test exercises the same wiring the daemon runs.
/// </summary>
public static class ProviderAuthProbeRegistration
{
    public static IServiceCollection AddProviderAuthProbes(this IServiceCollection services)
    {
        // CARD-0628 D-7: measures Claude's sign-in state for the ProviderAuth operation and the launch backstop.
        services.AddSingleton(sp => new ClaudeAuthProbe(
            sp.GetRequiredService<IOptions<PhoneHomeSettings>>().Value,
            sp.GetRequiredService<ILogger<ClaudeAuthProbe>>()));
        // CARD-0647: Grok is presence of GROK_HOME/auth.json. The file is never opened.
        services.AddSingleton(sp => new GrokAuthProbe(
            sp.GetRequiredService<IOptions<PhoneHomeSettings>>().Value,
            sp.GetRequiredService<ILogger<GrokAuthProbe>>()));
        // CARD-0660 D-6: Codex is metadata-only presence of CODEX_HOME/auth.json.
        services.AddSingleton(sp => new CodexAuthProbe(
            sp.GetRequiredService<IOptions<PhoneHomeSettings>>().Value,
            sp.GetRequiredService<ILogger<CodexAuthProbe>>()));
        services.AddSingleton<IProviderAuthProbe>(sp => new RoutingProviderAuthProbe(
            sp.GetRequiredService<ClaudeAuthProbe>(),
            sp.GetRequiredService<GrokAuthProbe>(),
            sp.GetRequiredService<CodexAuthProbe>()));
        return services;
    }
}
