using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// The build publishes its artifact after model parity verification. A missing or mismatched
// stage fails this guard even when the runtime generator could recover.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundProbeModelTests
{
    [Test]
    public async Task Probe_publishes_a_compiled_model_matching_its_runtime_model()
    {
        var staged = await ProbeGeneratedModel.RequireSelectedStagedArtifactAsync();
        var probe = await RunModelProbeAsync(staged);
        probe.ExitCode.ShouldBe(0, probe.Stderr);
        probe.Stderr.ShouldContain(ProbeModel.LoadedModelLabel + ProbeModel.ReadModuleVersionId(staged));
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
            var probe = await RunModelProbeAsync(notAModel);
            probe.ExitCode.ShouldBe(1);
            probe.Stderr.ShouldContain("model-not-loaded not-a-model.dll BadImageFormatException");
            probe.Stderr.ShouldNotContain("not an assembly");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<(int ExitCode, string Stderr)> RunModelProbeAsync(string modelPath)
    {
        var config = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
            {
                ConnectionString = "Host=unused", StoreRoot = Path.GetDirectoryName(config), DeliveryId = Guid.NewGuid(),
                EvidencePath = config + ".evidence", MarkerPath = config + ".marker",
                Barrier = (string?)null, ClockOffsetSeconds = 0, Mode = "model-identity", CompiledModelPath = modelPath,
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
                throw new TimeoutException("the model identity probe did not exit within 60 s");
            }
            return (probe.ExitCode, await stderr);
        }
        finally
        {
            File.Delete(config);
        }
    }
}
