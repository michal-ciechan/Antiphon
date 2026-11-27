using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            })
            .Validate(s => s.Chains.Values.SelectMany(entries => entries)
                    .All(entry => entry.HasValidOneShotDialect(s.Providers.GetValueOrDefault(entry.Provider)?.Dialect)),
                "Review:Decisions:Chains entries with Transport=OneShot require a configured provider with Dialect=claude-cli.")
            .ValidateOnStart();

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
            })
            .Validate(s => s.Chains.Values.SelectMany(entries => entries)
                    .All(entry => entry.HasValidOneShotDialect(s.Providers.GetValueOrDefault(entry.Provider)?.Dialect)),
                "Review:Completions:Chains entries with Transport=OneShot require a configured provider with Dialect=claude-cli.")
            .ValidateOnStart();
    }
}
