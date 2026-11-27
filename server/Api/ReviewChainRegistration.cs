using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api;

public static class ReviewChainRegistration
{
    public static void AddReviewChainOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReviewDecisionSettings>()
            .Bind(configuration.GetSection(ReviewDecisionSettings.SectionName))
            .PostConfigure(s =>
            {
                s.ApplyDefaults();
                // A provider names its key by configuration key only; the value never reaches a log.
                foreach (var provider in s.Providers.Values)
                {
                    if (string.IsNullOrEmpty(provider.ApiKey) && !string.IsNullOrWhiteSpace(provider.ApiKeyConfigKey))
                        provider.ApiKey = configuration[provider.ApiKeyConfigKey];
                }
            });

        services.AddOptions<ReviewCompletionSettings>()
            .Bind(configuration.GetSection(ReviewCompletionSettings.SectionName))
            .PostConfigure(s =>
            {
                s.ApplyDefaultProviders();
                // A provider names its key by configuration key only; the value never reaches a log.
                foreach (var provider in s.Providers.Values)
                {
                    if (string.IsNullOrEmpty(provider.ApiKey) && !string.IsNullOrWhiteSpace(provider.ApiKeyConfigKey))
                        provider.ApiKey = configuration[provider.ApiKeyConfigKey];
                }

                s.ApplyDefaultChain();
            });

        services.AddHostedService<ReviewChainStartup>();
    }

    public static void WriteEffectiveReviewChains(IServiceProvider services, TextWriter output)
    {
        var decisions = services.GetRequiredService<IOptions<ReviewDecisionSettings>>().Value;
        var completions = services.GetRequiredService<IOptions<ReviewCompletionSettings>>().Value;
        foreach (var entry in Entries(decisions, completions))
            output.WriteLine($"{entry.Section} chain '{entry.Chain}' entry {entry.Index} provider '{entry.Entry.Provider}' "
                + $"transport '{entry.Entry.Transport}' dialect '{entry.Dialect ?? "(missing)"}'");
    }

    private static IEnumerable<ChainEntry> Entries(ReviewDecisionSettings decisions, ReviewCompletionSettings completions) =>
        EntriesIn(ReviewDecisionSettings.SectionName, decisions.Chains, id => decisions.Providers.GetValueOrDefault(id)?.Dialect)
            .Concat(EntriesIn(ReviewCompletionSettings.SectionName, completions.Chains, id => completions.Providers.GetValueOrDefault(id)?.Dialect));

    private static IEnumerable<ChainEntry> EntriesIn(string section, Dictionary<string, List<ReviewDecisionChainEntry>> chains,
        Func<string, string?> dialect) =>
        chains.SelectMany(chain => chain.Value.Select((entry, index) => new ChainEntry(section, chain.Key, index, entry, dialect(entry.Provider))));

    private sealed record ChainEntry(string Section, string Chain, int Index, ReviewDecisionChainEntry Entry, string? Dialect);

    private sealed class ReviewChainStartup(
        IOptions<ReviewDecisionSettings> decisions,
        IOptions<ReviewCompletionSettings> completions,
        ILogger<ReviewChainStartup> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var entry in Entries(decisions.Value, completions.Value).Where(e => !e.Entry.HasValidOneShotDialect(e.Dialect)))
                logger.LogWarning("{Section} chain '{Chain}' entry {Entry} provider '{Provider}' uses Transport=OneShot with dialect '{Dialect}'. "
                    + "Provider dialect still selects the adapter. Set Transport to match the provider.",
                    entry.Section, entry.Chain, entry.Index, entry.Entry.Provider, entry.Dialect ?? "(missing)");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
