using Antiphon.TestSupport;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(420_000)]
public sealed class VolumeRetirementPublicationInvariantTests
{
    [Test]
    public async Task Shipped_binding_clock_producer_has_a_complete_function()
    {
        var content = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-content-audit.sh"));
        const string header = "c1609_emit_binding_clock() {\n";
        var start = content.IndexOf(header, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = content.IndexOf("\n}\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
    }
}
