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
}
