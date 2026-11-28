using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// OTLP export for <see cref="TestTrace"/>. A trace directory, <c>ANTIPHON_TEST_TRACE_OTLP=1</c>
/// and an explicit endpoint enable export of fixture and TUnit spans. Endpoint, protocol and headers come from
/// the standard OTEL_* variables. The queue is large so a busy run is not thinned by dropped spans.
/// Only assemblies that reference OpenTelemetry.Exporter.OpenTelemetryProtocol compile this file.
/// </summary>
public static partial class TestTrace
{
    public const string OtlpVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string OtlpOptInVariable = "ANTIPHON_TEST_TRACE_OTLP";

    internal static bool HasOtlpDestination(string? directory, string? optIn, string? endpoint) =>
        !string.IsNullOrWhiteSpace(directory) && optIn == "1" && !string.IsNullOrWhiteSpace(endpoint);

    private static TracerProvider? _otlp;

    static partial void InstallOtlp()
    {
        if (!HasOtlpDestination(Environment.GetEnvironmentVariable(DirectoryVariable),
                Environment.GetEnvironmentVariable(OtlpOptInVariable),
                Environment.GetEnvironmentVariable(OtlpVariable)))
            return;
        lock (Gate)
        {
            if (_otlp is not null)
                return;
            var service = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
            var provider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(string.IsNullOrWhiteSpace(service) ? SourceName : service))
                .AddSource(SourceName, "TUnit", "TUnit.Lifecycle")
                .SetSampler(new AlwaysOnSampler())
                .AddOtlpExporter(o =>
                {
                    o.BatchExportProcessorOptions.MaxQueueSize = 262_144;
                    o.BatchExportProcessorOptions.MaxExportBatchSize = 4_096;
                    o.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 1_000;
                })
                .Build();
            _otlp = provider;
        }
    }

    static partial void DisposeOtlp() => _otlp?.Dispose();

    static partial void FlushOtlp() => _otlp?.ForceFlush(10_000);
}
