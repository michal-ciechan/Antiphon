using Antiphon.TestSupport;
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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Host_salvage_namespace_and_types_are_closed()
    {
        var f = await VolumeRetirementPublicationFixture.Create();
        await f.Protect(f.U, VolumeRetirementPublicationFixture.Canonical(f.U));
        (await f.WriteProof([(f.U, "commit")])).Exit.ShouldBe(2, "H2 local canonical preservation grants no authority");
        foreach (var reference in new[] { "refs/salvage/legacy", "refs/antiphon/other", "refs/antiphon/salvage-lookalike/unique" })
        {
            await f.Publish(f.U, reference);
            (await f.WriteProof()).Exit.ShouldBe(0, "H2 unrelated namespaces do not invalidate ordinary head proofs");
            (await f.WriteProof([(f.U, "commit")])).Exit.ShouldBe(2, "H2 excluded namespace cannot prove original history: " + reference);
            await f.Git(f.Bare, "update-ref", "-d", reference);
        }
        var tree = await f.Git(f.Repo, "rev-parse", f.U + "^{tree}");
        var blob = await f.Git(f.Repo, "rev-parse", f.U + ":vector.txt");
        await f.Git(f.Repo, "tag", "-a", "annotation", "-m", "annotation", f.U);
        var tag = await f.Git(f.Repo, "rev-parse", "refs/tags/annotation");
        var vectors = new[]
        {
            ("invalid-guid", f.U, "refs/antiphon/salvage/not-a-guid/" + f.U),
            ("uppercase-guid", f.U, "refs/antiphon/salvage/AAAAAAAA-0000-0000-0000-000000000001/" + f.U),
            ("mismatched-oid", f.U, VolumeRetirementPublicationFixture.Canonical(f.X)),
            ("extra-component", f.U, VolumeRetirementPublicationFixture.Canonical(f.U) + "/extra"),
            ("tree", tree, VolumeRetirementPublicationFixture.Canonical(tree)),
            ("blob", blob, VolumeRetirementPublicationFixture.Canonical(blob)),
            ("tag", tag, VolumeRetirementPublicationFixture.Canonical(tag))
        };
        foreach (var (label, oid, reference) in vectors)
        {
            await f.Publish(oid, reference);
            var refused = await f.WriteProof();
            refused.Exit.ShouldBe(2, "H2 malformed/non-commit canonical evidence refuses even without required objects: " + label + "; " + refused.Output);
            await f.Git(f.Bare, "update-ref", "-d", reference);
        }
        await f.Publish(f.U);
        (await f.WriteProof([(f.U, "commit")])).Exit.ShouldBe(0, "H2 exact lowercase GUID/OID commit control");
        await f.Git(f.Bare, "update-ref", "-d", "refs/heads/main");
        (await f.WriteProof([(f.U, "commit")])).Exit.ShouldBe(2, "H2 a branch head is still required");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Host_salvage_snapshot_prunes_and_renews_exact_objects()
    {
        var f = await VolumeRetirementPublicationFixture.Create();
        await f.Publish(f.U);
        var first = await f.WriteProof([(f.U, "commit")]);
        first.Exit.ShouldBe(0, first.Output);
        var reused = await f.WriteProof([(f.U, "commit")], previous: first.File);
        reused.Exit.ShouldBe(0, reused.Output);
        (await f.WriteProof([(f.X, "commit")], previous: first.File)).Exit.ShouldBe(2, "H3 reuse rechecks required objects");

        var originalBundle = await File.ReadAllBytesAsync(first.File + ".bundle");
        var originalHeader = await File.ReadAllLinesAsync(first.File);
        // Same live advertisement, different bundle ref set. Recomputed hashes
        // prove equality is checked independently of file integrity.
        await f.Git(f.Repo, "bundle", "create", first.File + ".head-only", "refs/heads/main");
        await File.WriteAllBytesAsync(first.File + ".bundle", await File.ReadAllBytesAsync(first.File + ".head-only"));
        await f.RehashProof(first, originalHeader.ToArray());
        (await f.WriteProof([(f.A, "commit")], previous: first.File)).Exit.ShouldBe(2, "H3 missing bundle salvage ref must refuse reuse");
        await f.Protect(f.U, VolumeRetirementPublicationFixture.Canonical(f.U));
        await f.Protect(f.X, "refs/heads/extra");
        await f.Git(f.Repo, "bundle", "create", first.File + ".extra", "refs/heads/main", VolumeRetirementPublicationFixture.Canonical(f.U), "refs/heads/extra");
        // The canonical source ref is local preservation, independently of origin.
        await File.WriteAllBytesAsync(first.File + ".bundle", await File.ReadAllBytesAsync(first.File + ".extra"));
        await f.RehashProof(first, originalHeader.ToArray());
        (await f.WriteProof(previous: first.File)).Exit.ShouldBe(2, "H3 extra bundle ref must refuse reuse");
        await File.WriteAllBytesAsync(first.File + ".bundle", originalBundle);
        await File.WriteAllTextAsync(first.File, string.Join('\n', originalHeader) + "\n");
        await f.Git(f.Bare, "update-ref", "-d", VolumeRetirementPublicationFixture.Canonical(f.U));
        (await f.WriteProof([(f.U, "commit")], previous: first.File)).Exit.ShouldBe(2, "H3 removed origin root cannot reuse old authority");

        // Real origin changes between fetch and the second advertisement. The
        // only retryable failure is snapshot; the next fetch must prune U.
        await f.Publish(f.U);
        var shim = "if($GitArgs -contains 'ls-remote'){ $script:reads=1+[int]$script:reads; if($script:reads -eq 2){ " +
            "& git -C " + VolumeRetirementPublicationFixture.Ps(f.Bare) + " update-ref -d " +
            VolumeRetirementPublicationFixture.Ps(VolumeRetirementPublicationFixture.Canonical(f.U)) + " } }; ";
        var raced = await f.WriteProof([(f.U, "commit")], extraShim: shim);
        raced.Exit.ShouldBe(2, "H3 snapshot retry must not retain disappeared salvage refs");
        var log = await File.ReadAllTextAsync(Path.Combine(f.Root, "host-proof.log"));
        log.ShouldContain("attempt=1 reason=snapshot");
        log.ShouldContain("attempt=2 reason=");
        log.ShouldNotContain("attempt=3", Case.Sensitive, "H3 the pruned snapshot reaches required-object validation on the second attempt");
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
