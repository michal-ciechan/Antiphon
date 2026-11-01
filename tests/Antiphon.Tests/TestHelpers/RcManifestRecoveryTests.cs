using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M1. Crash, corruption, lock and live-reader witnesses.
// A missing barrier or successor is an assertion failure, not a missing command.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcManifestRecoveryTests
{
    [Test]
    [Timeout(120_000)]
    public async Task KilledWriterRecoversCommittedPrefix(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v5", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(120_000)]
    public async Task CommittedCorruptionCannotFallBack(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v6", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(90_000)]
    public async Task TwoWritersRefuseAndGenerationsAdvance(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v7", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(120_000)]
    public async Task LiveReaderSeesOnlyWholeRecords(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v8", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }
}
