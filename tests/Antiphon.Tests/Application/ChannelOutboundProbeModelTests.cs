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
            try
            {
                await probe.WaitForExitAsync(bound.Token);
            }
            catch (OperationCanceledException)
            {
                // A stalled probe is killed (and its exit waited for, bounded), never left running.
                try { probe.Kill(entireProcessTree: true); } catch (Exception) { }
                using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await probe.WaitForExitAsync(exited.Token); } catch (OperationCanceledException) { }
                _ = stderr.ContinueWith(t => t.Exception, TaskScheduler.Default);
                throw new TimeoutException("the probe did not exit within 60 s of a model that does not load");
            }
            probe.ExitCode.ShouldBe(1);
            var output = await stderr;
            output.ShouldContain("model-not-loaded not-a-model.dll BadImageFormatException");
            output.ShouldNotContain("not an assembly");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
