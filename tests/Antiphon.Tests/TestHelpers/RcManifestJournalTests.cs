using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M1. Real Publish-NightlyChunkObservation witnesses. RED on the
// cumulative writer is prior-row work and missing settled-key journals.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcManifestJournalTests
{
    [Test]
    [Timeout(180_000)]
    public async Task NextPublicationTouchesOnlyCurrentChunk(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v1", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(120_000)]
    public async Task WholeRunWorkIsLinear(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v2", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(180_000)]
    public async Task ProjectionPreservesLegacyBytes(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var zones = new (string Name, int OffsetMinutes)[]
        {
            ("UTC", 0),
            ("Asia/Kolkata", 330),
            ("America/New_York", -240),
        };
        foreach (var zone in zones)
        {
            var work = Path.Combine(root, zone.Name.Replace('/', '_'));
            var write = await ManifestPublicationFixture.Run(
                cancellationToken,
                "v3-write",
                work,
                new Dictionary<string, string>
                {
                    ["TZ"] = zone.Name,
                    ["MP_EXPECT_OFFSET"] = zone.OffsetMinutes.ToString(),
                });
            write.Ok.ShouldBeTrue(zone.Name + " " + write.Reason);
        }

        foreach (var writer in zones)
        {
            var source = Path.Combine(root, writer.Name.Replace('/', '_'), "prod", "timing");
            var expected = Path.Combine(root, writer.Name.Replace('/', '_'), "oracle", "timing", "groups-executed.json");
            foreach (var reducer in zones)
            {
                var work = Path.Combine(root, "reduce-" + writer.Name.Replace('/', '_') + "-" + reducer.Name.Replace('/', '_'));
                var reduced = await ManifestPublicationFixture.Run(
                    cancellationToken,
                    "v3-reduce",
                    work,
                    new Dictionary<string, string>
                    {
                        ["TZ"] = reducer.Name,
                        ["MP_EXPECT_OFFSET"] = reducer.OffsetMinutes.ToString(),
                        ["MP_SOURCE"] = source,
                        ["MP_EXPECTED"] = expected,
                    });
                reduced.Ok.ShouldBeTrue(writer.Name + "->" + reducer.Name + " " + reduced.Reason);
            }
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task LegacySeedAndReplacementOrder(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "v4", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }
}
