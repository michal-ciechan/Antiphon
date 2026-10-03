using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidenceDeletionGuardTests
{
    private const string Marker = "remove evidence\n\nAntiphon-Evidence-Deletion: CARD-1015";
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
    private static async Task<string> BoundaryBaseAsync(EvidenceGitFixture f)
    {
        await f.GitAsync("read-tree", f.Base);
        foreach (var (path, bytes, mode) in new[] {
            (".antiphon/newer-pre-base.log", Text("unique-" + f.Root), "100644"),
            (".antiphon/new-report.md", Text("allowed"), "100644"),
            (".antiphon/c998-fixture/report.md", Text("allowed"), "100644"),
            (".ANTIPHON/UPPER.MD", Text("allowed"), "100644"),
            (".antiphon/checkpoints.md", Text("allowed"), "100644"),
            (".antiphon/nested/Mixed-CheckPoints/a.md", Text("reject"), "100644"),
            (".antiphon/executable.md", Text("allowed"), "100755"),
            (".antiphon/link.md", Text("outside.txt"), "120000"),
            (".antiphon/under.md", new byte[1048575], "100644"),
            (".antiphon/equal.md", new byte[1048576], "100644"),
            (".antiphon/over.md", new byte[1048577], "100644") })
            await f.PutAsync(path, bytes, mode);
        await f.SetAsync(".antiphon/module.md", f.Base, "160000");
        return await f.CommitIndexAsync([f.Base]);
    }
    private static JsonDocument InventoryJson(EvidenceGitFixture.Result r) =>
        JsonDocument.Parse(r.Output.Split('\n').Single(x => x.StartsWith('{')));

    [Test]
    public async Task Verifies_exact_legacy_deletion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var anchor = (await f.TreeAsync(EvidenceGitFixture.Anchor)).Where(EvidenceGitFixture.Scoped).ToArray();
        anchor.Length.ShouldBe(108);
        anchor.Sum(x => x.Bytes).ShouldBe(17775541);
        EvidenceGitFixture.PathHash(anchor).ShouldBe("356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c");
        var rejected = anchor.Where(EvidenceGitFixture.Reject).ToArray();
        var permitted = anchor.Where(x => !EvidenceGitFixture.Reject(x)).ToArray();
        rejected.Length.ShouldBe(88); permitted.Length.ShouldBe(20);
        rejected.Sum(x => x.Bytes).ShouldBe(17414103); permitted.Sum(x => x.Bytes).ShouldBe(361438);
        EvidenceGitFixture.PathHash(rejected).ShouldBe("26abc1b90cf1dc40dec8dfb38bd8f5c75f8c555f22f8042c49268c78f259e95f");
        EvidenceGitFixture.PathHash(permitted).ShouldBe("320fcb2d7ffe6607de6c46d3bd477522ffda0ab7355bb10be252e7eac8be05f6");
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(anchor.Select(x => x.Path + "\0" + (EvidenceGitFixture.Reject(x) ? "delete" : "keep") + "\0"))))).ToLowerInvariant();
        signature.ShouldBe("f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b");
        foreach (var b in new[] { EvidenceGitFixture.Anchor, await BoundaryBaseAsync(f) })
        {
            var inv = await f.InventoryAsync(b);
            var before = await f.SnapshotAsync();
            var inventory = await f.InventoryOnlyAsync(b);
            inventory.Exit.ShouldBe(0);
            (await f.SnapshotAsync()).ShouldBe(before, "c1015-deletion-read-only");
            using var json = InventoryJson(inventory);
            json.RootElement.GetProperty("Delete").EnumerateArray().Select(x => x.GetProperty("Path").GetString()).ToArray()
                .ShouldBe(inv.Delete.Select(x => x.Path).ToArray(), "c1015-inventory-rule-set");
            json.RootElement.GetProperty("Keep").EnumerateArray().Select(x => x.GetProperty("Path").GetString()).ToArray()
                .ShouldBe(inv.Keep.Select(x => x.Path).ToArray());
            json.RootElement.GetProperty("AnchorSignature").GetString().ShouldBe(signature);
            json.RootElement.GetProperty("Bytes").GetInt64().ShouldBe(inv.Bytes);
            json.RootElement.GetProperty("PathSha256").GetString().ShouldBe(inv.Digest);
            if (b != EvidenceGitFixture.Anchor)
            {
                var paths = inv.Delete.Select(x => x.Path).ToArray();
                paths.ShouldContain(".antiphon/newer-pre-base.log");
                paths.ShouldContain(".antiphon/module.md"); paths.ShouldContain(".antiphon/link.md"); paths.ShouldContain(".antiphon/over.md");
                foreach (var p in new[] { ".ANTIPHON/UPPER.MD", ".antiphon/equal.md", ".antiphon/under.md", ".antiphon/checkpoints.md", ".antiphon/executable.md" }) paths.ShouldNotContain(p);
            }
            var deletion = await f.ExactDeletionAsync(b, inv);
            var after = await f.TreeAsync(deletion);
            after.ShouldBe((await f.TreeAsync(b)).Where(x => !inv.Delete.Any(d => d.Path == x.Path)).ToArray());
            before = await f.SnapshotAsync();
            var validated = await f.DeletionAsync(b, deletion, inv);
            validated.Exit.ShouldBe(0);
            validated.Output.ShouldContain("deletion=" + deletion);
            validated.Output.ShouldContain("finalTreeViolations=0");
            (await f.SnapshotAsync()).ShouldBe(before, "c1015-deletion-read-only");
            foreach (var e in inv.Delete.Where(x => x.Type == "blob"))
                (await f.RunAsync("git", ["cat-file", "-e", e.Oid])).Exit.ShouldBe(0);
        }
    }

    [Test]
    public async Task Rejects_wrong_inventory()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var b = await BoundaryBaseAsync(f); var inv = await f.InventoryAsync(b); var deletion = await f.ExactDeletionAsync(b, inv);
        (await f.DeletionAsync(b, deletion, inv, count: (inv.Delete.Length + 1).ToString())).Exit.ShouldBe(1, "c1015-inventory-count");
        (await f.DeletionAsync(b, deletion, inv, digest: new string('0', 64))).Exit.ShouldBe(1, "c1015-inventory-digest");
        (await f.DeletionAsync(b, deletion, inv, bytes: (inv.Bytes + 1).ToString())).Exit.ShouldBe(1, "c1015-inventory-bytes");
        var anchorKeep = (await f.TreeAsync(EvidenceGitFixture.Anchor)).First(x => EvidenceGitFixture.Scoped(x) && !EvidenceGitFixture.Reject(x));
        await f.GitAsync("read-tree", b); await f.RemoveAsync(anchorKeep.Path);
        var absent = await f.CommitIndexAsync([b]);
        (await f.InventoryOnlyAsync(absent)).Exit.ShouldBe(1, "c1015-anchor-present");
        await f.GitAsync("read-tree", b);
        await f.PutAsync(anchorKeep.Path, Enumerable.Repeat((byte)'x', (int)anchorKeep.Bytes).ToArray());
        var changed = await f.CommitIndexAsync([b]);
        (await f.InventoryOnlyAsync(changed)).Exit.ShouldBe(1, "c1015-anchor-oid");
        await f.GitAsync("read-tree", b); await f.SetAsync(anchorKeep.Path, anchorKeep.Oid, "100755");
        var mode = await f.CommitIndexAsync([b]);
        (await f.InventoryOnlyAsync(mode)).Exit.ShouldBe(1, "c1015-anchor-mode");
        var hook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments[0] -eq 'ls-tree' -and $Arguments[-1] -eq '" + EvidenceGitFixture.Anchor + "') { $s=[Text.Encoding]::UTF8.GetString($r.Bytes); " +
            "$old=" + EvidenceGitFixture.Quote(anchorKeep.Mode + " blob " + anchorKeep.Oid) + "; " +
            "$s=[regex]::Replace($s,[regex]::Escape($old)+' +[0-9]+'+[char]9,$old+' 1048577'+[char]9); $r.Bytes=[Text.Encoding]::UTF8.GetBytes($s); Write-Host 'FAULT-HIT' }; return $r }";
        var classifier = await f.InventoryOnlyAsync(b, hook);
        classifier.Output.ShouldContain("FAULT-HIT");
        classifier.Exit.ShouldBe(1, "c1015-anchor-classification");
        foreach (var (count, digest, bytes) in new[] { ("", inv.Digest, inv.Bytes.ToString()), ("-1", inv.Digest, inv.Bytes.ToString()), ("0", "invalid", "0"), ("0", inv.Digest, "NaN") })
            (await f.DeletionAsync(b, deletion, inv, count, digest, bytes)).Exit.ShouldBe(2);
        (await f.GuardAsync("check-evidence-deletion.ps1", [])).Exit.ShouldBe(2);
    }

    [Test]
    public async Task Rejects_ambiguous_or_merge_deletion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var inv = await f.InventoryAsync(f.Base);
        var zero = await f.ExactDeletionAsync(f.Base, inv, message: "unmarked deletion");
        (await f.DeletionAsync(f.Base, zero, inv)).Exit.ShouldBe(1, "c1015-deletion-unique");
        var one = await f.ExactDeletionAsync(f.Base, inv);
        await f.GitAsync("read-tree", one);
        var two = await f.CommitIndexAsync([one], Marker);
        (await f.DeletionAsync(f.Base, two, inv)).Exit.ShouldBe(1, "c1015-deletion-unique");
        var near = await f.ExactDeletionAsync(f.Base, inv, message: "remove\n\nAntiphon-Evidence-Deletion: CARD-10150");
        (await f.DeletionAsync(f.Base, near, inv)).Exit.ShouldBe(1, "c1015-deletion-trailer");
        var embedded = await f.ExactDeletionAsync(f.Base, inv, message: "Antiphon-Evidence-Deletion: CARD-1015\n\nordinary body follows");
        (await f.DeletionAsync(f.Base, embedded, inv)).Exit.ShouldBe(1, "c1015-deletion-trailer");
        var side = await f.AddCommitAsync(f.Base, "side.txt", Text("side"));
        await f.GitAsync("read-tree", one);
        var merge = await f.CommitIndexAsync([f.Base, side], Marker);
        (await f.DeletionAsync(f.Base, merge, inv)).Exit.ShouldBe(1, "c1015-deletion-parent");
    }

    [Test]
    public async Task Rejects_missing_extra_or_non_deletions()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var inv = await f.InventoryAsync(f.Base);
        var omitted = inv.Delete[0];
        await f.GitAsync("read-tree", f.Base);
        foreach (var e in inv.Delete.Skip(1)) await f.RemoveAsync(e.Path);
        var partial = await f.CommitIndexAsync([f.Base], Marker);
        var final = await f.DeleteCommitAsync(partial, omitted.Path);
        (await f.DeletionAsync(f.Base, final, inv)).Exit.ShouldBe(1, "c1015-deletion-missing");
        foreach (var extra in new[] { inv.Keep[0], (await f.TreeAsync(f.Base)).Single(x => x.Path == "outside.txt") })
        {
            await f.GitAsync("read-tree", f.Base);
            foreach (var e in inv.Delete) await f.RemoveAsync(e.Path);
            await f.RemoveAsync(extra.Path);
            var excess = await f.CommitIndexAsync([f.Base], Marker);
            await f.GitAsync("read-tree", excess); await f.SetAsync(extra.Path, extra.Oid, extra.Mode);
            var restored = await f.CommitIndexAsync([excess]);
            (await f.DeletionAsync(f.Base, restored, inv)).Exit.ShouldBe(1, "c1015-deletion-extra");
        }
        foreach (var path in new[] { "added-source.txt", "outside.txt" })
        {
            await f.GitAsync("read-tree", f.Base);
            foreach (var e in inv.Delete) await f.RemoveAsync(e.Path);
            await f.PutAsync(path, Text("changed"));
            var modified = await f.CommitIndexAsync([f.Base], Marker);
            (await f.DeletionAsync(f.Base, modified, inv)).Exit.ShouldBe(1, "c1015-deletion-only");
        }
    }

    [Test]
    public async Task Rejects_changed_legacy_or_resurrected_paths()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var b = await BoundaryBaseAsync(f); var inv = await f.InventoryAsync(b);
        var newer = inv.Delete.Single(x => x.Path == ".antiphon/newer-pre-base.log");
        var edited = await f.AddCommitAsync(b, newer.Path, Enumerable.Repeat((byte)'z', (int)newer.Bytes).ToArray());
        var wrong = await f.ExactDeletionAsync(b, inv, parent: edited);
        var oldOid = await f.DeletionAsync(b, wrong, inv);
        oldOid.Exit.ShouldBe(1, "c1015-deletion-old-oid");
        oldOid.Exit.ShouldBe(1, "c1015-inventory-pinned-base");
        await f.GitAsync("read-tree", b); await f.SetAsync(newer.Path, newer.Oid, "100755");
        var retyped = await f.CommitIndexAsync([b]);
        var wrongMode = await f.ExactDeletionAsync(b, inv, parent: retyped);
        (await f.DeletionAsync(b, wrongMode, inv)).Exit.ShouldBe(1, "c1015-deletion-old-mode");
        var deletion = await f.ExactDeletionAsync(b, inv);
        var resurrection = await f.AddCommitAsync(deletion, ".antiphon/over.md", Text("small allowed replacement"));
        (await f.DeletionAsync(b, resurrection, inv)).Exit.ShouldBe(1, "c1015-deletion-resurrection");
        var forbidden = await f.AddCommitAsync(deletion, ".antiphon/new-final.trx", Text("bad"));
        (await f.DeletionAsync(b, forbidden, inv)).Exit.ShouldBe(1, "c1015-final-tree-policy");
        var kept = inv.Keep.Single(x => x.Path == ".antiphon/new-report.md");
        var missing = await f.DeleteCommitAsync(deletion, kept.Path);
        (await f.DeletionAsync(b, missing, inv)).Exit.ShouldBe(1, "c1015-kept-present");
        var keptChanged = await f.AddCommitAsync(deletion, kept.Path, Text("new allowed bytes"));
        (await f.DeletionAsync(b, keptChanged, inv)).Exit.ShouldBe(1, "c1015-kept-oid");
        await f.GitAsync("read-tree", deletion); await f.SetAsync(kept.Path, kept.Oid, "100755");
        var keptMode = await f.CommitIndexAsync([deletion]);
        (await f.DeletionAsync(b, keptMode, inv)).Exit.ShouldBe(1, "c1015-kept-mode");
    }

    [Test]
    public async Task Refuses_unverifiable_deletion_history()
    {
        await using var f = await EvidenceGitFixture.CreateAsync(anchored: true);
        var b = await BoundaryBaseAsync(f); var inv = await f.InventoryAsync(b);
        var deletion = await f.ExactDeletionAsync(b, inv);
        (await f.DeletionAsync("missing", deletion, inv)).Exit.ShouldBe(2);
        (await f.DeletionAsync(b, "missing", inv)).Exit.ShouldBe(2);
        // Merge B with an independently rooted exact-deletion lineage. B..head is valid;
        // B..deletion-parent is deliberately not, with identical original anchor entries.
        await f.GitAsync("read-tree", b); var unrelatedBase = await f.CommitIndexAsync([]);
        var unrelatedDeletion = await f.ExactDeletionAsync(unrelatedBase, inv);
        await f.GitAsync("read-tree", unrelatedDeletion);
        var merged = await f.CommitIndexAsync([b, unrelatedDeletion]);
        (await f.DeletionAsync(b, merged, inv)).Exit.ShouldBe(2, "c1015-inventory-ancestry");
        var siblingHook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments[0] -eq 'rev-list' -and $Arguments -contains '--reverse') { $r.Bytes=[Text.Encoding]::UTF8.GetBytes('" + deletion + "'+[char]10); Write-Host 'FAULT-HIT' }; return $r }";
        var sibling = await f.AddCommitAsync(b, "normal.txt", Text("sibling"));
        var outOfHead = await f.DeletionAsync(b, sibling, inv, hook: siblingHook);
        outOfHead.Output.ShouldContain("FAULT-HIT");
        outOfHead.Exit.ShouldBe(2, "c1015-deletion-ancestry");
        var fieldsHook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments[0] -eq 'ls-tree' -and $Arguments[-1] -eq '" + b + "') { $s=[Text.Encoding]::UTF8.GetString($r.Bytes); $at=$s.IndexOf([char]9); $s=$s.Insert($at,' extra'); $r.Bytes=[Text.Encoding]::UTF8.GetBytes($s); Write-Host 'FAULT-HIT' }; return $r }";
        var malformed = await f.InventoryOnlyAsync(b, fieldsHook);
        malformed.Output.ShouldContain("FAULT-HIT");
        malformed.Exit.ShouldBe(2, "c1015-tree-fields");
        var failureHook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; if ($Arguments[0] -eq 'ls-tree') { $r.ExitCode=1; Write-Host 'FAULT-HIT' }; return $r }";
        var failed = await f.InventoryOnlyAsync(b, failureHook); failed.Output.ShouldContain("FAULT-HIT"); failed.Exit.ShouldBe(2);
        var missingBlob = inv.Delete.Single(x => x.Path == ".antiphon/newer-pre-base.log");
        var ownedBlob = Path.Combine(f.Repo, ".git", "objects", missingBlob.Oid[..2], missingBlob.Oid[2..]);
        File.Exists(ownedBlob).ShouldBeTrue("fixture-owned loose blob exists, never delete borrowed objects");
        var recoveryHook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments[0] -eq 'ls-tree' -and $Arguments[-1] -eq '" + deletion + "') { [IO.File]::Delete(" + EvidenceGitFixture.Quote(ownedBlob) + "); Write-Host 'FAULT-HIT' }; return $r }";
        var recovery = await f.DeletionAsync(b, deletion, inv, hook: recoveryHook);
        recovery.Output.ShouldContain("FAULT-HIT");
        recovery.Exit.ShouldBe(2, "c1015-deletion-recoverable");
        File.Exists(ownedBlob).ShouldBeFalse();
    }
}
