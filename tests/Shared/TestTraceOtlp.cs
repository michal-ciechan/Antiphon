using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// OTLP export for <see cref="TestTrace"/>: with <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set (for example
/// <c>http://localhost:4317</c>), every span from every source is exported, so the run can be read
/// as traces in a collector, Jaeger or the Aspire dashboard. Endpoint, protocol and headers come from
/// the standard OTEL_* variables. The queue is large so a busy run is not thinned by dropped spans.
/// Only assemblies that reference OpenTelemetry.Exporter.OpenTelemetryProtocol compile this file.
/// </summary>
public static partial class TestTrace
{
    public const string OtlpVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private static TracerProvider? _otlp;

    static partial void InstallOtlp()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpVariable)))
            return;
        lock (Gate)
        {
            if (_otlp is not null)
                return;
            var service = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
            var provider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(string.IsNullOrWhiteSpace(service) ? SourceName : service))
                .AddSource("*")
                .SetSampler(new AlwaysOnSampler())
                .AddOtlpExporter(o =>
                {
                    o.BatchExportProcessorOptions.MaxQueueSize = 262_144;
                    o.BatchExportProcessorOptions.MaxExportBatchSize = 4_096;
                    o.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 1_000;
                })
                .Build();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => provider.Dispose();
            _otlp = provider;
        }
    }

    static partial void FlushOtlp() => _otlp?.ForceFlush(10_000);
}
