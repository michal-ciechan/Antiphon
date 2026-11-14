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
        foreach (var (index, value) in new[]
        {
            (1, "operation c1008" + new string('f', 32)), (2, "origin " + new string('0', 64)),
            (3, "repo repos/not-outer"), (5, "nonce " + new string('z', 32)),
            (6, "inventory " + new string('z', 64)), (7, "bundle " + new string('0', 64)),
            (8, "advertisement " + new string('0', 64)), (9, "count 0")
        })
        {
            var header = originalHeader.ToArray(); header[index] = value;
            await File.WriteAllTextAsync(first.File, string.Join('\n', header) + "\n");
            (await f.WriteProof([(f.A, "commit")], previous: first.File)).Exit.ShouldBe(2,
                "H3 malformed or mismatched prior-proof field " + index);
        }
        await f.RehashProof(first, originalHeader.Append(originalHeader[11]).ToArray());
        (await f.WriteProof(previous: first.File)).Exit.ShouldBe(2, "H3 duplicate prior advertisement refuses");
        // Reproof creates a new phase nonce and inventory (c590-real.ps1). Old
        // valid bindings are not the new authority; fresh origin/graph checks are.
        var renewalHeader = originalHeader.ToArray();
        renewalHeader[5] = "nonce " + new string('a', 32);
        renewalHeader[6] = "inventory " + new string('b', 64);
        await File.WriteAllTextAsync(first.File, string.Join('\n', renewalHeader) + "\n");
        (await f.WriteProof([(f.U, "commit")], previous: first.File)).Exit.ShouldBe(0,
            "H3 a fresh phase replaces valid old nonce/inventory after graph revalidation");
        var damagedBundle = originalBundle.ToArray(); damagedBundle[0] ^= 0xff;
        await File.WriteAllBytesAsync(first.File + ".bundle", damagedBundle);
        await f.RehashProof(first, originalHeader.ToArray());
        (await f.WriteProof(previous: first.File)).Exit.ShouldBe(2,
            "H3 a hash-bound unusable prior bundle refuses");
        await File.WriteAllBytesAsync(first.File + ".bundle", originalBundle);
        await File.WriteAllTextAsync(first.File, string.Join('\n', originalHeader) + "\n");
        // Same live advertisement, different bundle ref set. Recomputed hashes
        // and an independently readable U graph isolate ref equality from object
        // availability: HEAD imports U without advertising its canonical name.
        var missingRepo = Path.Combine(f.Root, "missing-bundle.git");
        await f.Git(f.Root, "init", "-q", "--bare", "--template=", missingRepo);
        await f.Git(missingRepo, "bundle", "unbundle", first.File + ".bundle");
        await f.Git(missingRepo, "update-ref", "refs/heads/main", f.A);
        await f.Git(missingRepo, "update-ref", "--no-deref", "HEAD", f.U);
        var missingBundle = Path.Combine(f.Root, "missing-ref.bundle");
        await f.Git(missingRepo, "bundle", "create", missingBundle, "refs/heads/main", "HEAD");
        var missingHeads = await f.Git(missingRepo, "bundle", "list-heads", missingBundle);
        missingHeads.ShouldContain(f.A + " refs/heads/main");
        missingHeads.ShouldContain(f.U + " HEAD");
        missingHeads.ShouldNotContain(VolumeRetirementPublicationFixture.Canonical(f.U));
        var verifyRepo = Path.Combine(f.Root, "missing-verify.git");
        await f.Git(f.Root, "init", "-q", "--bare", "--template=", verifyRepo);
        (await f.Run("git", ["-C", verifyRepo, "cat-file", "-e", f.U])).Exit.ShouldNotBe(0);
        await f.Git(verifyRepo, "bundle", "unbundle", missingBundle);
        (await f.Git(verifyRepo, "cat-file", "-t", f.U)).ShouldBe("commit");
        await File.WriteAllBytesAsync(first.File + ".bundle", await File.ReadAllBytesAsync(missingBundle));
        await f.RehashProof(first, originalHeader.ToArray());
        (await f.WriteProof([(f.A, "commit")], previous: first.File)).Exit.ShouldBe(2, "H3 missing bundle salvage ref must refuse reuse");
        await f.Protect(f.U, VolumeRetirementPublicationFixture.Canonical(f.U));
        await f.Protect(f.X, VolumeRetirementPublicationFixture.Canonical(f.X));
        await f.Git(f.Repo, "bundle", "create", first.File + ".extra", "refs/heads/main", VolumeRetirementPublicationFixture.Canonical(f.U), VolumeRetirementPublicationFixture.Canonical(f.X));
        // The canonical source ref is local preservation, independently of origin.
        await File.WriteAllBytesAsync(first.File + ".bundle", await File.ReadAllBytesAsync(first.File + ".extra"));
        await f.RehashProof(first, originalHeader.ToArray());
        (await f.WriteProof(previous: first.File)).Exit.ShouldBe(2, "H3 extra canonical bundle ref must refuse reuse");
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
    public async Task Origin_salvage_graph_passes_the_full_offline_audit()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var tip = await f.Commit("retained child", f.U, "retained child bytes");
        await f.Protect(tip, VolumeRetirementPublicationFixture.Canonical(tip));
        await File.WriteAllTextAsync(Path.Combine(f.Root, "helper-auth-128"), "1\n");
        var inventory = await f.Host!.Run("retire-temp-inventory");
        inventory.Exit.ShouldBe(0, inventory.Output);
        var requests = inventory.Output.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.StartsWith("C1008_PRIVATE_ORIGIN ", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        requests.Length.ShouldBe(1, inventory.Output);
        VolumeRetirementPublicationFixture.Sha(string.Join('\n', requests) + "\n").ShouldBe(f.Inventory);
        var before = await f.WriteProof(baselineWriter: true);
        before.Exit.ShouldBe(0, before.Output);
        await f.Oracle("V1 before publication", [f.A, f.U, tip], [f.A], false, before);
        Refused(await f.Preview(before), "RecycleUnpublishedWork");

        await f.Publish(tip);
        var tree = await f.Git(f.Repo, "rev-parse", f.U + "^{tree}");
        var proof = await f.WriteProof([(tip, "commit"), (f.U, "commit"), (tree, "tree")]);
        proof.Exit.ShouldBe(0, proof.Output);
        (await File.ReadAllTextAsync(proof.File)).ShouldContain(tip + "\t" + VolumeRetirementPublicationFixture.Canonical(tip));
        (await f.Git(f.Root, "bundle", "list-heads", proof.File + ".bundle")).ShouldContain(tip + " " + VolumeRetirementPublicationFixture.Canonical(tip));
        (await f.WriteProof([(f.U, "commit")], baselineWriter: true)).Exit.ShouldBe(2, "V1 intentional producer delta from the task base");
        var snapshot = f.Snapshot();
        await f.Oracle("V1 exact remote chain", [f.A, f.U, tip], [f.A, tip], true, proof, compareBase: false);
        Ready(await f.Preview(proof));
        f.Snapshot().ShouldBe(snapshot, "V1 audit must not alter target refs, reflogs, indexes or object bytes");
        File.Exists(Path.Combine(f.Root, "helper-git-trace")).ShouldBeFalse("V1 the offline helper never looks up a remote");
        await f.Protect(f.X, "refs/salvage/unique-sibling");
        await f.Oracle("V1 additional unique sibling", [f.U, tip, f.X], [f.A, tip], false, proof, compareBase: false);
        Refused(await f.Preview(proof), "RecycleUnpublishedWork");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Local_salvage_refs_are_protected_roots()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var y = await f.Commit("canonical loose", f.A, "canonical loose bytes");
        var z = await f.Commit("canonical packed", f.A, "canonical packed bytes");
        var proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        var protectedRoots = new List<string> { f.A };
        foreach (var (oid, reference, packed) in new[]
        {
            (f.U, "refs/salvage/loose", false), (f.X, "refs/salvage/packed", true),
            (y, VolumeRetirementPublicationFixture.Canonical(y), false), (z, VolumeRetirementPublicationFixture.Canonical(z), true)
        })
        {
            await f.Protect(oid, reference, packed);
            File.Exists(Path.Combine(f.Repo, ".git", reference)).ShouldBe(!packed, "V2 actual loose/packed placement");
            if (packed) (await File.ReadAllTextAsync(Path.Combine(f.Repo, ".git/packed-refs"))).ShouldContain(oid + " " + reference);
            protectedRoots.Add(oid);
            await f.Oracle("V2 " + reference, protectedRoots.ToArray(), [f.A], false, proof);
        }
        Refused(await f.Preview(proof), "RecycleUnpublishedWork");
        var unrelated = await f.Commit("unrelated remote salvage", f.A, "unrelated origin bytes");
        await f.Publish(unrelated);
        var withUnrelated = await f.WriteProof();
        withUnrelated.Exit.ShouldBe(0, withUnrelated.Output);
        await f.Oracle("V2 unrelated canonical origin root", protectedRoots.ToArray(), [f.A, unrelated], false, withUnrelated, compareBase: false);
        foreach (var oid in protectedRoots.Skip(1)) await f.Publish(oid, "refs/heads/archive/" + oid);
        var published = await f.WriteProof();
        published.Exit.ShouldBe(0, published.Output);
        await f.Oracle("V2 exact archive branches", protectedRoots.ToArray(), protectedRoots.Append(unrelated).ToArray(), true, published, compareBase: false);
        Ready(await f.Preview(published));
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Reflog_patch_equivalence_never_proves_publication()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var originals = new List<string>(); var twins = new List<string>();
        var original = f.A; var twin = f.A;
        for (var i = 0; i < 16; i++)
        {
            original = await f.Commit("original " + i, original, "index-like contents " + i);
            originals.Add(original);
            if (i >= 14) continue;
            twin = await f.Commit("different commit metadata " + i, twin, "index-like contents " + i);
            twins.Add(twin);
            original.ShouldNotBe(twin, "V3 commit identity differs");
            var left = await PatchId(f, original); var right = await PatchId(f, twin);
            left.ShouldBe(right, "V3 equal stable patch IDs are an independently established input fact");
        }
        originals.Count.ShouldBe(16); twins.Count.ShouldBe(14);
        await f.Publish(twin, "refs/heads/patch-twins");
        await f.SetReflog("logs/HEAD", original, f.A); // oldest old-side only
        var before = await f.WriteProof(baselineWriter: true);
        before.Exit.ShouldBe(0, before.Output);
        await f.Oracle("V3 sixteen original objects, fourteen patch twins", originals.Append(f.A).ToArray(), [f.A, twin], false, before);
        Refused(await f.Preview(before), "RecycleUnpublishedWork");
        var logBytes = await File.ReadAllBytesAsync(Path.Combine(f.Repo, ".git/logs/HEAD"));
        await f.Publish(original, "refs/heads/original-chain");
        var exact = await f.WriteProof(baselineWriter: true);
        exact.Exit.ShouldBe(0, exact.Output);
        await f.Oracle("V3 all sixteen exact originals published", originals.ToArray(), [f.A, twin, original], true, exact);
        (await File.ReadAllBytesAsync(Path.Combine(f.Repo, ".git/logs/HEAD"))).ShouldBe(logBytes, "V3 no reflog expiry");
        Ready(await f.Preview(exact));

        var empty = await f.Git(f.Repo, "commit-tree", f.Tree, "-p", f.A, "-m", "empty recovery commit");
        var merge = await f.Commit("merge recovery", original, "merge bytes", f.X);
        await f.SetReflog("logs/HEAD", f.A, empty); // new-side only, empty patch
        await f.Oracle("V3 new-side empty commit", [empty], [f.A, twin, original], false, exact);
        await f.SetReflog("logs/HEAD", empty, merge);
        await f.Oracle("V3 both sides and merge parents", [empty, merge, f.X], [f.A, twin, original], false, exact);
        await f.Publish(empty, "refs/heads/empty-original");
        await f.Publish(merge, "refs/heads/merge-original");
        var final = await f.WriteProof(baselineWriter: true);
        final.Exit.ShouldBe(0, final.Output);
        await f.Oracle("V3 exact empty and merge histories", [empty, merge, f.X], [f.A, twin, original, empty, merge], true, final);
        await GoldenCorpus(f, [f.A, twin, original, empty, merge]);
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Other_remote_tracking_refs_do_not_prove_publication()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Git(f.Repo, "remote", "add", "backup", f.Backup);
        await f.Git(f.Repo, "push", "-q", f.Backup, f.U + ":refs/heads/copy");
        await f.Protect(f.U, "refs/remotes/backup/loose");
        await f.Protect(f.X, "refs/remotes/backup/packed", packed: true);
        await f.Publish(f.X, "refs/heads/deleted");
        await f.Protect(f.X, "refs/remotes/origin/deleted");
        await f.Git(f.Bare, "update-ref", "-d", "refs/heads/deleted");
        var proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        await f.Oracle("V4 backup copy and stale origin tracking", [f.A, f.U, f.X], [f.A], false, proof);
        Refused(await f.Preview(proof), "RecycleUnpublishedWork");
        await f.Publish(f.U, "refs/heads/salvage/original");
        var partial = await f.WriteProof(baselineWriter: true);
        partial.Exit.ShouldBe(0, partial.Output);
        await f.Oracle("V4 salvage-named ordinary head does not cover sibling", [f.U, f.X], [f.A, f.U], false, partial);
        await f.Publish(f.X, "refs/heads/original-sibling");
        var exact = await f.WriteProof(baselineWriter: true);
        exact.Exit.ShouldBe(0, exact.Output);
        await f.Oracle("V4 exact current heads", [f.U, f.X], [f.A, f.U, f.X], true, exact);
        Ready(await f.Preview(exact));
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Detached_linked_heads_and_logs_require_exact_publication()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var linked = Path.Combine(f.Work, "worktrees/linked");
        await f.Git(f.Repo, "worktree", "add", "-q", "--detach", linked, f.U);
        (await f.Git(linked, "rev-parse", "HEAD")).ShouldBe(f.U);
        var admin = await f.Git(linked, "rev-parse", "--absolute-git-dir");
        await File.WriteAllTextAsync(Path.Combine(admin, "logs/HEAD"), f.X + " " + f.U +
            " Fixture <fixture@example.invalid> 1767225600 +0000\tlinked recovery\n");
        var proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        await f.Oracle("V5 detached linked HEAD plus old-only log", [f.A, f.U, f.X], [f.A], false, proof);
        Refused(await f.LinkedPreview(proof), "RecycleUnpublishedWork");
        Directory.Move(linked, Path.Combine(f.Root, "missing-linked-checkout"));
        Directory.Exists(linked).ShouldBeFalse("V5 prunable checkout, retained admin state");
        File.Exists(Path.Combine(admin, "HEAD")).ShouldBeTrue();
        await f.Oracle("V5 missing checkout retains admin roots", [f.U, f.X], [f.A], false, proof);
        await f.Publish(f.U, "refs/heads/linked-original");
        var partial = await f.WriteProof(baselineWriter: true);
        partial.Exit.ShouldBe(0, partial.Output);
        await f.Oracle("V5 linked log is independently required", [f.U, f.X], [f.A, f.U], false, partial);
        await f.Publish(f.X, "refs/heads/linked-log-original");
        var exact = await f.WriteProof(baselineWriter: true);
        exact.Exit.ShouldBe(0, exact.Output);
        await f.Oracle("V5 both exact histories published", [f.U, f.X], [f.A, f.U, f.X], true, exact);
        Ready(await f.LinkedPreview(exact));
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Tags_preserve_object_and_ancestry_requirements()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Protect(f.U, "refs/tags/lightweight");
        await f.Publish(f.U, "refs/tags/lightweight");
        var light = await f.WriteProof(baselineWriter: true);
        light.Exit.ShouldBe(0, light.Output);
        await f.Oracle("V6 lightweight tag is not a commit root", [f.U], [f.A], false, light);
        await f.Git(f.Repo, "tag", "-a", "annotated", "-m", "original annotation", f.U);
        var tag = await f.Git(f.Repo, "rev-parse", "refs/tags/annotated");
        await f.Publish(tag, "refs/tags/annotated");
        var annotated = await f.WriteProof(baselineWriter: true);
        annotated.Exit.ShouldBe(0, annotated.Output);
        await f.Oracle("V6 advertised annotation does not prove ancestry", [f.U], [f.A], false, annotated);
        Refused(await f.Preview(annotated), "RecycleUnpublishedWork");
        await f.Publish(f.U, "refs/heads/tag-ancestry");
        var ancestry = await f.WriteProof(baselineWriter: true);
        ancestry.Exit.ShouldBe(0, ancestry.Output);
        await f.Oracle("V6 exact tag object and accepted commit ancestry", [f.U], [f.A, f.U], true, ancestry);
        await f.Git(f.Repo, "tag", "-a", "private-annotation", "-m", "private metadata", f.A);
        Refused(await f.Audit(ancestry), "RecycleUnpublishedWork");
        var privateTag = await f.Git(f.Repo, "rev-parse", "refs/tags/private-annotation");
        await f.Publish(privateTag, "refs/tags/private-annotation");
        var publicTag = await f.WriteProof(baselineWriter: true);
        publicTag.Exit.ShouldBe(0, publicTag.Output);
        (await f.Audit(publicTag)).Exit.ShouldBe(0, "V6 exact annotation control");
        var tree = await f.Git(f.Repo, "rev-parse", f.U + "^{tree}");
        var blob = await f.Git(f.Repo, "rev-parse", f.U + ":vector.txt");
        await f.Protect(tree, "refs/recovery/tree");
        (await f.Audit(publicTag)).Exit.ShouldBe(0, "V6 accepted commit graph proves its tree");
        await f.Protect(blob, "refs/recovery/blob");
        Refused(await f.Audit(publicTag), "RecycleUnpublishedWork");
        await f.Publish(blob, "refs/tags/exact-blob");
        await f.Git(f.Repo, "tag", "-a", "tag-of-tag", "-m", "outer annotation", "annotated");
        var blobProof = await f.WriteProof(baselineWriter: true);
        blobProof.Exit.ShouldBe(0, blobProof.Output);
        Refused(await f.Audit(blobProof), "RecycleUnpublishedWork");
        await f.Publish(await f.Git(f.Repo, "rev-parse", "refs/tags/tag-of-tag"), "refs/tags/tag-of-tag");
        var exact = await f.WriteProof(baselineWriter: true);
        exact.Exit.ShouldBe(0, exact.Output);
        (await f.Audit(exact)).Exit.ShouldBe(0, "V6 exact blob and tag-of-tag objects");
        Ready(await f.Preview(exact));
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
        var originalHeader = await File.ReadAllLinesAsync(proof.File);
        var readyFile = Path.Combine(f.Root, "server/retirement", VolumeRetirementFixture.Operation, "receipt.json");
        var readyBytes = await File.ReadAllBytesAsync(readyFile);
        await File.WriteAllTextAsync(proof.File + ".bundle", "not a Git bundle\n");
        await f.RehashProof(proof, await File.ReadAllLinesAsync(proof.File));
        var invalid = await f.Preview(proof);
        invalid.Exit.ShouldBe(2, "V7 an invalid v2 graph must not fall back to locally present main; " + invalid.Output);
        invalid.Output.ShouldContain("RecycleGitAuditUnknown");
        invalid.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
        (await File.ReadAllBytesAsync(readyFile)).ShouldBe(readyBytes, "V7 refusal preserves the earlier Ready receipt");
        await File.WriteAllBytesAsync(proof.File + ".bundle", original);
        await f.RehashProof(proof, originalHeader.ToArray());
        foreach (var (index, value) in new[]
        {
            (1, "operation c1008" + new string('0', 32)), (2, "origin " + new string('0', 64)),
            (5, "nonce " + new string('0', 32)), (6, "inventory " + new string('0', 64))
        })
        {
            var header = originalHeader.ToArray(); header[index] = value;
            await f.RehashProof(proof, header);
            Refused(await f.Audit(proof), "RecycleGitAuditUnknown");
        }
        await f.RehashProof(proof, originalHeader.Append(originalHeader[11]).ToArray());
        Refused(await f.Audit(proof), "RecycleGitAuditUnknown");
        await f.RehashProof(proof, originalHeader.ToArray());
        await f.Protect(f.X, VolumeRetirementPublicationFixture.Canonical(f.X));
        var extraBundle = Path.Combine(f.Root, "extra-bundle");
        await f.Git(f.Repo, "bundle", "create", extraBundle, "refs/heads/main", VolumeRetirementPublicationFixture.Canonical(f.X));
        await File.WriteAllBytesAsync(proof.File + ".bundle", await File.ReadAllBytesAsync(extraBundle));
        await f.RehashProof(proof, originalHeader.ToArray());
        Refused(await f.Audit(proof), "RecycleGitAuditUnknown");
        await f.Git(f.Repo, "update-ref", "-d", VolumeRetirementPublicationFixture.Canonical(f.X));
        await File.WriteAllBytesAsync(proof.File + ".bundle", original);
        await f.RehashProof(proof, originalHeader.ToArray());
        (await f.Preview(proof)).Exit.ShouldBe(0, "V7 restoration of the same valid graph");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Selector_fixture_own_gitdir_file_stays_in_full_audit()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await SelectorVector(symlink: false);
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Selector_fixture_own_gitdir_symlink_stays_in_full_audit()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        await SelectorVector(symlink: true);
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Salvage_deleted_after_sealing_refuses_ready()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        await f.Git(f.Bare, "update-ref", "-d", VolumeRetirementPublicationFixture.Canonical(f.U));
        var audit = await f.Preview(proof);
        Refused(audit, "RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Head_rolled_back_after_sealing_refuses_ready()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, "refs/heads/published-original");
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        await f.Git(f.Bare, "update-ref", "refs/heads/published-original", f.A);
        var audit = await f.Preview(proof);
        Refused(audit, "RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Salvage_replaced_after_sealing_refuses_ready()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        const string beforeRepair = "137fde4adc58f98b4d59b5d945b3380e9b1a5749";
        var proof = await f.WriteProof(writerRevision: beforeRepair);
        proof.Exit.ShouldBe(0, proof.Output);
        // Transfer the replacement object without advertising it as publication.
        await f.Git(f.Bare, "fetch", "-q", f.Repo, f.X);
        await f.Git(f.Bare, "update-ref", VolumeRetirementPublicationFixture.Canonical(f.U), f.X);
        (await f.Git(f.Repo, "rev-list", "--count", f.U, "^" + f.A)).ShouldBe("1", "the protected original is unique against valid live origin roots");
        var old = await f.Audit(proof, complete: false, sourceRevision: beforeRepair);
        old.Exit.ShouldBe(0, "the committed pre-repair reader reproduces the fail-open; " + old.Output);
        old.Output.ShouldContain("C1609_PROVED_OBJECT repo=repos/outer object=" + f.U);
        var audit = await f.Preview(proof);
        Refused(audit, "RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Origin_unreachable_at_final_check_refuses_ready()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        Directory.Move(f.Bare, f.Bare + "-unreachable");
        var audit = await f.Preview(proof);
        Refused(audit, "RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Origin_rebound_at_final_check_refuses_ready()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        // Change the URL after the helper has consumed its bound proof, at the
        // last status check before the Ready gate. Changing it before the scan
        // exercises the existing manifest binding rather than this defect.
        var change = "eval \"$(declare -f c1008_status_proof | sed '1s/c1008_status_proof/c1901_status_proof/')\"\n" +
            "c1008_status_proof() { c1901_status_proof; if [ -f \"$CASE_DIR/preview-audit.out\" ] && " +
            "grep -q '^repositories=' \"$CASE_DIR/preview-audit.out\"; then git -C " +
            VolumeRetirementPublicationFixture.Q(f.Repo) + " remote set-url origin https://fixture.invalid/unbound.git; fi; }\n";
        var audit = await f.Preview(proof, change);
        Refused(audit, "RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Unchanged_objects_renew_at_each_ready_gate()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        Ready(await f.Preview(proof, extraGates: 2));
        f.RenewalRequests.ShouldBeGreaterThanOrEqualTo(3, "each real gate repeats the host writer for the same object set");
        f.ControlRequests.ShouldBe(3, "each gate completes a distinct live control exchange");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Offline_scan_requires_live_completion()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create();
        await f.Publish(f.U);
        await f.Protect(f.U, "refs/recovery/original");
        var proof = await f.WriteProof();
        proof.Exit.ShouldBe(0, proof.Output);
        var scan = await f.Audit(proof, complete: false);
        scan.Exit.ShouldBe(6, scan.Output);
        scan.Output.ShouldContain("RecycleGitAuditUnknown");
        (await f.Audit(proof)).Exit.ShouldBe(0, "a current origin completes the same scan through the real writer");
        await f.Git(f.Bare, "update-ref", "-d", VolumeRetirementPublicationFixture.Canonical(f.U));
        var refused = await f.Audit(proof);
        refused.Exit.ShouldBe(2, refused.Output);
        refused.Output.ShouldContain("RecycleGitAuditUnknown");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1901() => RenewalCorpus(1901);
    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1902() => RenewalCorpus(1902);
    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1903() => RenewalCorpus(1903);
    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1904() => RenewalCorpus(1904);

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1901_second_half() => RenewalCorpus(1901, 35);

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1902_second_half() => RenewalCorpus(1902, 35);

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1903_second_half() => RenewalCorpus(1903, 35);

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task Origin_graph_corpus_seed_1904_second_half() => RenewalCorpus(1904, 35);

    private static async Task RenewalCorpus(int seed, int firstShape = 0)
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create();
        var random = new Random(seed);
        var objects = new List<string>(); var parents = new List<int[]>();
        for (var i = 0; i < 40; i++)
        {
            var first = i == 0 ? -1 : random.Next(-1, i);
            var second = i > 2 && i % 5 == 0 ? random.Next(i) : -1;
            if (first == second) second = -1;
            parents.Add(second < 0 ? [first] : [first, second]);
            objects.Add(await f.Commit("seed " + seed + " object " + i, first < 0 ? f.A : objects[first],
                "object " + i, second < 0 ? null : objects[second]));
        }
        var roots = new[] { 4, 17, 31 };
        var published = new HashSet<int>();
        void Visit(int i) { if (i < 0 || !published.Add(i)) return; foreach (var parent in parents[i]) Visit(parent); }
        foreach (var i in roots) { Visit(i); await f.Publish(objects[i], "refs/heads/corpus/" + i); }
        published.Count.ShouldBeGreaterThan(0); published.Count.ShouldBeLessThan(objects.Count);
        var proof = await f.WriteProof(); proof.Exit.ShouldBe(0, proof.Output);
        var accepted = new[] { f.A }.Concat(roots.Select(i => objects[i])).ToArray();
        for (var shape = firstShape; shape < firstShape + 35; shape++)
        {
            var index = shape % objects.Count; var tip = objects[index];
            var family = shape % 6;
            var reference = family switch
            {
                0 => VolumeRetirementPublicationFixture.Canonical(tip),
                1 => "refs/salvage/corpus", 2 => "refs/remotes/backup/corpus", _ => "refs/recovery/corpus"
            };
            if (family == 3) await File.WriteAllTextAsync(Path.Combine(f.Repo, ".git/ORIG_HEAD"), tip + "\n");
            else if (family == 4) await f.SetReflog("logs/HEAD", tip, f.A);
            else await f.Protect(tip, reference, packed: shape % 2 == 0);
            await f.Oracle("seed=" + seed + " shape=" + shape + " family=" + family,
                [tip], accepted, published.Contains(index), proof, compareBase: false);
            if (family == 3) await File.WriteAllTextAsync(Path.Combine(f.Repo, ".git/ORIG_HEAD"), f.A + "\n");
            else if (family == 4) await f.SetReflog("logs/HEAD", f.A, f.A);
            else await f.Git(f.Repo, "update-ref", "-d", reference);
        }
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Salvage_withdrawn_after_staging_refuses_control()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof(); proof.Exit.ShouldBe(0, proof.Output);
        var refused = await f.Preview(proof, beforeControl: async () => { await f.Git(f.Bare, "update-ref", "-d", VolumeRetirementPublicationFixture.Canonical(f.U)); });
        Refused(refused, "RecycleGitAuditUnknown");
        f.RenewalRequests.ShouldBeGreaterThanOrEqualTo(1, "the full writer already succeeded before publication was withdrawn");
        f.ControlRequests.ShouldBe(1, "the final live check, rather than the initial scan or graph fetch, refuses");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Head_rolled_back_after_staging_refuses_control()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, "refs/heads/published-original");
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof(); proof.Exit.ShouldBe(0, proof.Output);
        var refused = await f.Preview(proof, beforeControl: async () => { await f.Git(f.Bare, "update-ref", "refs/heads/published-original", f.A); });
        Refused(refused, "RecycleGitAuditUnknown");
        f.RenewalRequests.ShouldBeGreaterThanOrEqualTo(1, "the full writer already succeeded before publication was withdrawn");
        f.ControlRequests.ShouldBe(1, "the final live check, rather than the initial scan or graph fetch, refuses");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Origin_unreachable_after_staging_refuses_control()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U, null);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof(); proof.Exit.ShouldBe(0, proof.Output);
        var refused = await f.Preview(proof, beforeControl: async () => { Directory.Move(f.Bare, f.Bare + "-unreachable"); await Task.CompletedTask; });
        Refused(refused, "RecycleGitAuditUnknown");
        f.RenewalRequests.ShouldBeGreaterThanOrEqualTo(1, "the full writer already succeeded before publication was withdrawn");
        f.ControlRequests.ShouldBe(1, "the final live check, rather than the initial scan or graph fetch, refuses");
    }

    [Test]
    [NativeLinuxOnly(NativeLinuxReason)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Final_origin_binding_refresh_is_inside_effect_window()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        await f.Publish(f.U);
        await f.Protect(f.U, "refs/heads/recovery");
        var proof = await f.WriteProof(); proof.Exit.ShouldBe(0, proof.Output);
        // Delay the real final binding observation after it has read the URL.
        // The production gate and monotonic clock remain unchanged.
        var delayedBindings = """
            eval "$(declare -f c1609_recheck_origin_bindings | sed '1s/c1609_recheck_origin_bindings/c1901_original_origin_binding_check/')"
            C1901_BINDING_CHECKS=0
            c1609_recheck_origin_bindings() {
                C1901_BINDING_CHECKS=$((C1901_BINDING_CHECKS+1))
                c1901_original_origin_binding_check || return 1
                if [ "$C1901_BINDING_CHECKS" = 2 ]; then
                    sleep 6
                    printf 'C1901_FINAL_BINDING_DELAYED\n'
                fi
            }
            """;
        var delayedModule = Path.Combine(f.Root, "delayed-binding-module.sh");
        await File.WriteAllTextAsync(delayedModule,
            await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c1008-retirement.sh")) +
            "\n" + delayedBindings + "\n");
        var refused = await f.Preview(proof, extra: "export C1609_RETIREMENT_FILE=" +
            VolumeRetirementPublicationFixture.Q(delayedModule) + "\n");
        refused.Output.ShouldContain("C1901_FINAL_BINDING_DELAYED");
        f.RenewalRequests.ShouldBeGreaterThanOrEqualTo(1);
        f.ControlRequests.ShouldBe(1, "the fresh advertisement succeeds; the effect-window bound must refuse the stale binding observation");
        (await File.ReadAllTextAsync(Path.Combine(f.Root, "coordinator.log"))).ShouldContain("C1609_CONTROL_REQUEST ",
            "the final real advertisement and bound response succeeded before the window verdict");
        Refused(refused, "RecycleGitAuditUnknown");
    }

    private static void Refused((int Exit, string Output) run, string diagnosis)
    {
        run.Exit.ShouldBe(2, run.Output);
        run.Output.ShouldContain(diagnosis);
        run.Output.ShouldNotContain(VolumeRetirementFixture.ReadyMarker);
    }

    private static void Ready((int Exit, string Output) run)
    {
        run.Exit.ShouldBe(0, run.Output);
        run.Output.ShouldContain(VolumeRetirementFixture.ReadyMarker);
    }

    private static async Task<string> PatchId(VolumeRetirementPublicationFixture f, string oid)
    {
        var patch = await f.Run("git", ["-C", f.Repo, "show", "--pretty=format:", "--no-ext-diff", oid]);
        patch.Exit.ShouldBe(0, patch.Output);
        var id = await f.Run("git", ["patch-id", "--stable"], input: patch.Output);
        id.Exit.ShouldBe(0, id.Output);
        id.Output.Length.ShouldBeGreaterThanOrEqualTo(40);
        return id.Output[..40];
    }

    private static async Task GoldenCorpus(VolumeRetirementPublicationFixture f, string[] previousRoots)
    {
        // Fixed seed, explicit parent-index model, and a separate real-Git oracle.
        // Neither accepted membership nor the recovery placement comes from the audit.
        var random = new Random(1901);
        var objects = new List<string>(); var parents = new List<int[]>();
        for (var i = 0; i < 24; i++)
        {
            var first = i == 0 ? -1 : random.Next(-1, i);
            var second = i > 2 && i % 4 == 0 ? random.Next(i) : -1;
            if (second == first) second = -1;
            parents.Add(second >= 0 ? [first, second] : [first]);
            objects.Add(await f.Commit("corpus " + i, first < 0 ? f.A : objects[first], "corpus bytes " + i,
                second < 0 ? null : objects[second]));
        }
        var publicIndices = new[] { 5, 13, 19 };
        var golden = new HashSet<int>();
        void Visit(int i) { if (i < 0 || !golden.Add(i)) return; foreach (var parent in parents[i]) Visit(parent); }
        foreach (var i in publicIndices) { Visit(i); await f.Publish(objects[i], "refs/heads/corpus/" + i); }
        var accepted = previousRoots.Concat(publicIndices.Select(i => objects[i])).ToArray();
        var proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        // Both verdicts are mandatory in the corpus, independently of random edges.
        golden.Count.ShouldBeGreaterThan(0); golden.Count.ShouldBeLessThan(objects.Count);
        var families = new[] { "refs/salvage/corpus", "refs/remotes/backup/corpus", "refs/remotes/origin/deleted-corpus" };
        for (var i = 0; i < objects.Count; i++)
        {
            var reference = i % 4 == 3 ? VolumeRetirementPublicationFixture.Canonical(objects[i]) : families[i % 3];
            await f.Protect(objects[i], reference, packed: i % 2 == 0);
            await f.Oracle("V3 corpus seed=1901 index=" + i + " ref=" + reference,
                [objects[i]], accepted, golden.Contains(i), proof);
            await f.Git(f.Repo, "update-ref", "-d", reference);
        }
        await File.WriteAllTextAsync(Path.Combine(f.Root, "corpus-golden.json"), System.Text.Json.JsonSerializer.Serialize(new
            { seed = 1901, objects, parents, publicIndices, acceptedIndices = golden.Order().ToArray() }));
    }

    private static string ProductionFunction(string source, string name)
    {
        var start = source.IndexOf(name + "() {", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("\n}\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return source[start..(end + 2)];
    }

    private static async Task SelectorVector(bool symlink)
    {
        var f = await VolumeRetirementPublicationFixture.Create(preview: true);
        var nested = Path.Combine(f.Repo, ".antiphon/selector-evidence/x/fixtures/y");
        await f.Git(f.Root, "init", "-q", "-b", "main", nested);
        await f.Git(nested, "commit", "-qm", "independent fixture", "--allow-empty");
        var proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        var directoryControl = await f.Preview(proof);
        Ready(directoryControl);
        directoryControl.Output.ShouldContain("disposable-fixture classifier=selector-evidence-v1");
        await File.AppendAllTextAsync(Path.Combine(f.Repo, "readme.txt"), "dirty parent\n");
        Refused(await f.Preview(proof), "RecycleWorktreeDirty");
        await File.WriteAllTextAsync(Path.Combine(f.Repo, "readme.txt"), "published\n");
        await f.Git(f.Repo, "commit", "-qm", "unpublished parent", "--allow-empty");
        Refused(await f.Preview(proof), "RecycleUnpublishedWork");
        await f.Publish(await f.Git(f.Repo, "rev-parse", "HEAD"), "refs/heads/main");
        proof = await f.WriteProof(baselineWriter: true);
        proof.Exit.ShouldBe(0, proof.Output);
        Ready(await f.Preview(proof));
        // Keep administrative bytes outside the checkout so the primary witness
        // reaches gitdir classification rather than untracked-content refusal.
        var admin = Path.Combine(f.Repo, ".antiphon/selector-evidence/x/owned-admin");
        Directory.Move(Path.Combine(nested, ".git"), admin);
        var relativeAdmin = Path.GetRelativePath(nested, admin);
        if (symlink) Directory.CreateSymbolicLink(Path.Combine(nested, ".git"), relativeAdmin);
        else await File.WriteAllTextAsync(Path.Combine(nested, ".git"), "gitdir: " + relativeAdmin + "\n");
        (await f.Git(nested, "rev-parse", "--absolute-git-dir")).ShouldBe(admin, "V8/V9 valid own-gitdir pointer, no confinement error");
        (await f.Git(nested, "rev-parse", "--show-toplevel")).ShouldBe(nested);
        (await f.Git(nested, "rev-parse", "HEAD")).Length.ShouldBe(40);
        (await f.Git(nested, "status", "--porcelain", "--untracked-files=all")).ShouldBeEmpty("V8/V9 independent fixture is clean");
        (await f.Git(f.Repo, "status", "--porcelain", "--untracked-files=all")).ShouldBeEmpty("V8/V9 enclosing parent is clean and published");
        var source = await File.ReadAllTextAsync(Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var script = "set -euo pipefail\nC1609_CONTENT_V2=1\nroot=" + VolumeRetirementPublicationFixture.Q(f.Work) +
            "\ntop=" + VolumeRetirementPublicationFixture.Q(admin) + "\ngitdir=" + VolumeRetirementPublicationFixture.Q(admin) + "\n" +
            ProductionFunction(source, "path_of") + "\n" + ProductionFunction(source, "selector_fixture") +
            "\nselector_fixture " + VolumeRetirementPublicationFixture.Q(nested);
        var classifier = await f.Run("bash", ["-c", script]);
        classifier.Exit.ShouldBe(1, "V8/V9 extracted production classifier must reject the actual derived own-gitdir shape; " + classifier.Output);
        var retained = await f.Preview(proof);
        Refused(retained, "RecycleGitAuditUnknown");
        retained.Output.ShouldNotContain("disposable-fixture classifier=selector-evidence-v1", Case.Sensitive,
            "V8/V9 the only nested selector fixture stays in the full audit");
        if (symlink)
            retained.Output.ShouldContain(".antiphon/selector-evidence/x/fixtures/y", Case.Sensitive,
                "V9 the valid fixture reaches the production census boundary");
        else
        {
            var mountedRepo = "/work/" + Path.GetRelativePath(f.Work, nested).Replace('\\', '/');
            var mountedAdmin = "/work/" + Path.GetRelativePath(f.Work, admin).Replace('\\', '/');
            retained.Output.ShouldContain("entry repo=" + VolumeRetirementPublicationFixture.Sha(mountedRepo) +
                " common=" + VolumeRetirementPublicationFixture.Sha(mountedAdmin), Case.Sensitive,
                "V8 the exact valid fixture and its owned admin reach the production census boundary");
        }
    }
}
