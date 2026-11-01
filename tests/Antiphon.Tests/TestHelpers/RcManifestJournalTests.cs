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
        var applied = 0;
        var skipped = 0;
        var appliedWriters = new HashSet<string>(StringComparer.Ordinal);
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
            if (IsZoneNotApplicable(write))
            {
                NoteZoneSkip(zone.Name + " write", write.Reason);
                skipped++;
                continue;
            }

            write.Ok.ShouldBeTrue(zone.Name + " " + write.Reason);
            applied++;
            appliedWriters.Add(zone.Name);
        }

        foreach (var writer in zones)
        {
            var source = Path.Combine(root, writer.Name.Replace('/', '_'), "prod", "timing");
            var expected = Path.Combine(root, writer.Name.Replace('/', '_'), "oracle", "timing", "groups-executed.json");
            foreach (var reducer in zones)
            {
                var label = writer.Name + "->" + reducer.Name;
                if (!appliedWriters.Contains(writer.Name))
                {
                    NoteZoneSkip(label, "writer-not-applicable");
                    skipped++;
                    continue;
                }

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
                if (IsZoneNotApplicable(reduced))
                {
                    NoteZoneSkip(label, reduced.Reason);
                    skipped++;
                    continue;
                }

                reduced.Ok.ShouldBeTrue(label + " " + reduced.Reason);
                applied++;
            }
        }

        Console.WriteLine("zone-grid applied=" + applied + " skipped=" + skipped);
        if (OperatingSystem.IsWindows())
        {
            skipped.ShouldBeGreaterThan(0);
            applied.ShouldBeLessThan(12);
        }
        else
        {
            skipped.ShouldBe(0);
            applied.ShouldBe(12);
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

    [Test]
    [Timeout(60_000)]
    public async Task PinnedZoneMapsIanaWhenWindowsIdLookupRefusesIt(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "zone-map", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(90_000)]
    public async Task SuccessorWaitDrainsRedirectedOutput(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(cancellationToken, "successor-drain", root);
        result.Ok.ShouldBeTrue(result.Reason);
    }

    [Test]
    [Timeout(60_000)]
    public async Task EffectiveZoneCheckRefusesPinnedWindowsId(CancellationToken cancellationToken)
    {
        var root = C1514Support.FreshRoot();
        var result = await ManifestPublicationFixture.Run(
            cancellationToken,
            "zone-effective",
            root,
            new Dictionary<string, string>
            {
                ["TZ"] = "India Standard Time",
            });
        if (result.Reason.StartsWith("zone-effective-not-applicable", StringComparison.Ordinal))
        {
            Console.WriteLine("SKIP zone-effective-not-applicable " + result.Reason);
            return;
        }

        result.Ok.ShouldBeFalse(result.Reason);
        result.Reason.StartsWith("zone-not-applicable", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        result.Reason.Contains("expected=330", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        result.Reason.Contains("actual=330", StringComparison.Ordinal).ShouldBeFalse(result.Reason);
        result.Reason.Contains(" id=", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        Console.WriteLine("zone-effective " + result.Reason);
    }

    private static bool IsZoneNotApplicable(ManifestPublicationResult result)
    {
        return !result.Ok
            && result.Reason.StartsWith("zone-not-applicable", StringComparison.Ordinal);
    }

    private static void NoteZoneSkip(string label, string reason)
    {
        Console.WriteLine("SKIP zone-not-applicable " + label + " " + reason);
    }
}
