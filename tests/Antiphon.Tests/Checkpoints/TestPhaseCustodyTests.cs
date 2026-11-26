using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class TestPhaseCustodyTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C1514T_GateAndCustody(CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1514-custody-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var summaryPath = Path.Combine(root, "summary.json");
        var summaryText = "{\"outcome\":\"green\",\"coverageComplete\":false}";
        await File.WriteAllTextAsync(summaryPath, summaryText, cancellationToken);
        var before = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summaryText)));

        var cleanup = new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context);
        var missingInventory = cleanup.CleanOwnedOutputs(root, ["bin-owned"], 0, false, false, null, null);
        missingInventory.Refusals.ShouldContain("inventory-missing");

        var outside = Path.Combine(Path.GetTempPath(), "c1514-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "timing-sidecar.txt");
        await File.WriteAllTextAsync(outsideFile, "keep-outside", cancellationToken);
        var stray = Path.Combine(root, "stray.txt");
        await File.WriteAllTextAsync(stray, "keep-stray", cancellationToken);
        var unowned = Path.Combine(root, "bin-unowned");
        Directory.CreateDirectory(unowned);
        await File.WriteAllTextAsync(Path.Combine(unowned, "marker.txt"), "keep-bin", cancellationToken);

        var cleaned = cleanup.CleanOwnedOutputs(root, ["bin-owned"], 0, true, false, null, [outsideFile, stray, unowned]);
        cleaned.Refusals.ShouldContain("outside-isolated-root");
        cleaned.Refusals.ShouldContain("not-isolated-output");
        File.Exists(outsideFile).ShouldBeTrue("timing-outside-cleanup-root");
        File.Exists(stray).ShouldBeTrue("timing-outside-cleanup-root");
        Directory.Exists(unowned).ShouldBeTrue("timing-outside-cleanup-root");
        cleaned.Deleted.ShouldBeEmpty();

        // The gate's manifest digest does not move when timing files (one malformed) are written beside the run's summary, and
        // a manifest with an unknown field throws. In process since unit 62 of the 2026-11-22 remaining-ps1 plan (a pwsh child
        // dot-sourced scripts/lib/nightly-common.ps1 to reach ReleaseGatePublication).
        var manifest = ReleaseGatePublication.NewManifest(new Hashtable { ["repository"] = "michal-ciechan/Antiphon", ["sha"] = "abc", ["profile"] = "rc" });
        var beforeDigest = ReleaseGatePublication.ManifestDigest(manifest);
        var timing = Path.Combine(root, "timing");
        Directory.CreateDirectory(timing);
        await File.WriteAllTextAsync(Path.Combine(timing, "summary.json"), "{\"complete\":true}", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(timing, "malformed"), "{", cancellationToken);
        ReleaseGatePublication.ManifestDigest(manifest).ShouldBe(beforeDigest);
        Should.Throw<Exception>(() => ReleaseGatePublication.NewManifest(new Hashtable { ["notAField"] = 1 }));
        var after = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(summaryPath, cancellationToken)));
        after.ShouldBe(before);
        File.ReadAllText(summaryPath).ShouldBe(summaryText);
        File.Exists(Path.Combine(root, "timing", "malformed")).ShouldBeTrue();
    }
}
