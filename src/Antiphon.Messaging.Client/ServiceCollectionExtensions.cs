using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Messaging.Client;

/// <summary>DI registration for the Antiphon messaging client (Kafka-backed producer + consumer).</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAntiphonMessaging(this IServiceCollection services, Action<AntiphonMessagingOptions> configure)
    {
        services.Configure(configure);
        return services.AddAntiphonMessagingCore();
    }

    /// <summary>Bind from a config section (defaults to <see cref="AntiphonMessagingOptions.SectionName"/>).</summary>
    public static IServiceCollection AddAntiphonMessaging(this IServiceCollection services, IConfiguration configuration, string sectionName = AntiphonMessagingOptions.SectionName)
    {
        services.Configure<AntiphonMessagingOptions>(configuration.GetSection(sectionName));
        return services.AddAntiphonMessagingCore();
    }

    private static IServiceCollection AddAntiphonMessagingCore(this IServiceCollection services)
    {
        services.AddSingleton<IAntiphonMessagingProducer, KafkaAntiphonMessagingProducer>();
        services.AddSingleton<IAntiphonMessagingConsumer, KafkaAntiphonMessagingConsumer>();
        return services;
    }
}
