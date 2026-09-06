using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>
/// DI registration for the gateway side of the bus: ingress (adapter → inbound topic) and
/// outbound (outbound topic → adapter) hosted loops, plus the Kafka producer they share.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAntiphonGateway(this IServiceCollection services, Action<AntiphonGatewayOptions> configure)
    {
        services.Configure(configure);
        return services.AddAntiphonGatewayCore();
    }

    /// <summary>
    /// Bind from a config section. Pass <c>"Kafka"</c> to consume the same section name
    /// <c>Antiphon.Messaging.Service</c> already uses.
    /// </summary>
    public static IServiceCollection AddAntiphonGateway(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = AntiphonGatewayOptions.SectionName)
    {
        services.Configure<AntiphonGatewayOptions>(configuration.GetSection(sectionName));
        return services.AddAntiphonGatewayCore();
    }

    private static IServiceCollection AddAntiphonGatewayCore(this IServiceCollection services)
    {
        services.AddOptions<AntiphonGatewayOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AntiphonGatewayOptions>, AntiphonGatewayOptionsValidator>());
        services.AddSingleton<IProducer<string, string>>(sp =>
        {
            var kafka = sp.GetRequiredService<IOptions<AntiphonGatewayOptions>>().Value;
            return new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                MessageMaxBytes = kafka.MaxMessageBytes,
            }).Build();
        });
        services.AddHostedService<GatewayIngressService>();
        services.AddHostedService<GatewayOutboundService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConsumerGroupObservationReader, KafkaConsumerGroupObservationReader>();
        services.TryAddSingleton<IConsumerGroupOffsetReader>(sp => new KafkaConsumerGroupOffsetReader(sp.GetRequiredService<IConsumerGroupObservationReader>()));
        services.TryAddSingleton<InboundUnconsumedMonitorStatus>();
        services.TryAddSingleton<IInboundUnconsumedMonitorStatus>(sp => sp.GetRequiredService<InboundUnconsumedMonitorStatus>());
        services.TryAddSingleton<IAppHostHealthProbe, HttpAppHostHealthProbe>();
        services.TryAddSingleton<IInboundUnconsumedEventPublisher, KafkaInboundUnconsumedEventPublisher>();
        services.AddHostedService(sp => new InboundUnconsumedMonitorService(
            sp.GetServices<IInboxReceiptStore>(), sp.GetRequiredService<IConsumerGroupObservationReader>(),
            sp.GetServices<IChannelAdapter>(), sp.GetRequiredService<IInboundUnconsumedEventPublisher>(),
            sp.GetRequiredService<IAppHostHealthProbe>(), sp.GetRequiredService<IOptions<AntiphonGatewayOptions>>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InboundUnconsumedMonitorService>>(),
            sp.GetRequiredService<InboundUnconsumedMonitorStatus>()));
        return services;
    }
}
