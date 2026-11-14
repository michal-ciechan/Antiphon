using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[Timeout(420_000)]
public sealed class VolumeRetirementPublicationTests
{
    internal const string NativeLinuxReason = "CARD-1901: full retirement publication proofs require native Linux (Bash and POSIX mounts); portable host writer witnesses remain required on Windows.";

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Host_origin_salvage_proves_exact_required_objects()
    {
        var f = await VolumeRetirementPublicationFixture.Create();
        var refused = await f.WriteProof([(f.U, "commit")]);
        refused.Exit.ShouldBe(2, "H1 before exact publication; " + refused.Output);
        await f.Publish(f.U);
        var tree = await f.Git(f.Repo, "rev-parse", f.U + "^{tree}");
        var accepted = await f.WriteProof([(f.U, "commit"), (f.A, "commit"), (tree, "tree")]);
        accepted.Exit.ShouldBe(0, "H1 real writer must prove canonical origin salvage and its tree; " + accepted.Output);
        var body = await File.ReadAllTextAsync(accepted.File);
        body.ShouldStartWith("c1008-host-proof 2\n");
        body.ShouldContain(f.U + "\t" + VolumeRetirementPublicationFixture.Canonical(f.U));
        var heads = await f.Git(f.Root, "bundle", "list-heads", accepted.File + ".bundle");
        heads.ShouldContain(f.U + " " + VolumeRetirementPublicationFixture.Canonical(f.U));
        foreach (var oid in new[] { f.X, await f.Commit("child", f.U, "unpublished child") })
            (await f.WriteProof([(oid, "commit")])).Exit.ShouldBe(2, "H1 a sibling/child is still unique");
        var v1 = await f.WriteProof(version: 1);
        v1.Exit.ShouldBe(0, v1.Output);
        (await File.ReadAllTextAsync(v1.File)).ShouldNotContain("refs/antiphon/salvage/");
        (await f.Git(f.Root, "bundle", "list-heads", v1.File + ".bundle")).ShouldNotContain("refs/antiphon/salvage/");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task V2_invalid_bundle_cannot_use_present_head_fallback()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        var valid = await f.Preview(proof);
        valid.Exit.ShouldBe(0, "V7 head-only control; " + valid.Output);
        valid.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
        // All advertised heads are already local. A hash-bound invalid bundle
        // must fail at graph validation, rather than rely on missing local heads.
        (await f.Git(f.Repo, "rev-parse", "HEAD")).ShouldBe(f.A);
        var original = await File.ReadAllBytesAsync(proof.File + ".bundle");
        await File.WriteAllTextAsync(proof.File + ".bundle", "not a Git bundle\n");
        await f.RehashProof(proof, await File.ReadAllLinesAsync(proof.File));
        var invalid = await f.Preview(proof);
        invalid.Exit.ShouldBe(2, "V7 an invalid v2 graph must not fall back to locally present main; " + invalid.Output);
        invalid.Output.ShouldContain("RecycleGitAuditUnknown");
        invalid.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        await File.WriteAllBytesAsync(proof.File + ".bundle", original);
        await f.RehashProof(proof, await File.ReadAllLinesAsync(proof.File));
        (await f.Preview(proof)).Exit.ShouldBe(0, "V7 restoration of the same valid graph");
    }
}
