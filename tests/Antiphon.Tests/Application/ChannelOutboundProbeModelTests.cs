using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// The channel-outbound probes use the compiled model the probe generates from its own build (ProbeGeneratedModel),
// and fall back to building the model themselves when there is none: slower, not different. This makes that
// fallback a red test instead of a silent slowdown. The generator publishes the model only when it matches the
// runtime model (ProbeModelComparison, pinned by ProbeModelComparisonTests).
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundProbeModelTests
{
    [Test]
    public async Task Probe_publishes_a_compiled_model_matching_its_runtime_model()
    {
        var result = await ProbeGeneratedModel.ResultAsync();
        result.Path.ShouldNotBeNull(result.Detail);
        File.Exists(result.Path).ShouldBeTrue(result.Path);
    }

    // A model the parent gives but the probe cannot load fails the probe at its start (only the exception type on
    // stderr), never a silent rebuild of the model that would only make every probe slower.
    [Test]
    public async Task A_given_model_that_does_not_load_fails_the_probe()
    {
        var root = Path.Combine(Path.GetTempPath(), "c519-probe-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var notAModel = Path.Combine(root, "not-a-model.dll");
            await File.WriteAllTextAsync(notAModel, "not an assembly");
            var config = Path.Combine(root, "probe.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
            {
                ConnectionString = "Host=unused", StoreRoot = root, DeliveryId = Guid.NewGuid(),
                EvidencePath = Path.Combine(root, "evidence"), MarkerPath = Path.Combine(root, "marker"),
                Barrier = (string?)null, ClockOffsetSeconds = 0, Mode = "unified", CompiledModelPath = notAModel,
            }));
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
                "Antiphon.ChannelOutbound.Probe.dll"));
            start.ArgumentList.Add(config);
            using var probe = Process.Start(start)!;
            var stderr = probe.StandardError.ReadToEndAsync();
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await probe.WaitForExitAsync(bound.Token);
            probe.ExitCode.ShouldBe(1);
            var output = await stderr;
            output.ShouldContain("model-not-loaded not-a-model.dll BadImageFormatException");
            output.ShouldNotContain("not an assembly");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
