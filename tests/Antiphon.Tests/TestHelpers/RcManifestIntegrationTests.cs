using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M2. Real qualification publication and recovery. RED while the
// loop still projects each chunk and the summarizer has no recovery switch.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcManifestIntegrationTests
{
    [Test]
    [Timeout(180_000)]
    public async Task QualificationFinalizesOnEveryTerminalPath(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v9", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task ExplicitSummaryAndLegacyCallerStayCompatible(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v10", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task PublicationFailurePreservesVerdict(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v11", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task LedgerAndReceiptBytesAreUnchanged(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v12", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task ProductionLoopPublishesObservableProgress(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v13", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task InstrumentationAndIdentitySurvivePublication(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v14", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }
}
