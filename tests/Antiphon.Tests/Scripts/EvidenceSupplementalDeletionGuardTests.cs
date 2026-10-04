using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidenceSupplementalDeletionGuardTests
{
    private const string Card = "CARD-1024";
    private const string Marker = "remove later evidence\n\nAntiphon-Evidence-Deletion: " + Card;
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task<string> LaterBaseAsync(EvidenceGitFixture f, bool anchored = false)
    {
        var prior = anchored ? await f.ExactDeletionAsync(f.Base, await f.InventoryAsync(f.Base)) : f.Base;
        var first = await f.AddCommitAsync(prior, ".antiphon/later.json", Text("unique-" + f.Root));
        var second = await f.AddCommitAsync(first, ".antiphon/later.log", Text("log-" + f.Root));
        return await f.AddCommitAsync(second, ".antiphon/later.md", Text("kept report"));
    }

    private static Task<EvidenceGitFixture.Result> InventoryAsync(EvidenceGitFixture f, string b, string card = Card) =>
        f.GuardAsync("check-evidence-deletion.ps1", ["-InventoryRef", b, "-SupplementalCleanup", card, "-InventoryOnly"]);

    private static Task<EvidenceGitFixture.Result> ValidateAsync(EvidenceGitFixture f, string b, string head,
        EvidenceGitFixture.Inventory inv, string? count = null, string? digest = null, string? bytes = null,
        string card = Card, string? hook = null) =>
        f.GuardAsync("check-evidence-deletion.ps1", ["-InventoryRef", b, "-HeadRef", head,
            "-InventoryCount", count ?? inv.Delete.Length.ToString(), "-InventoryPathSha256", digest ?? inv.Digest,
            "-InventoryBytes", bytes ?? inv.Bytes.ToString(), "-SupplementalCleanup", card], hook, "Invoke-EvidenceDeletion");

    private static void SingleViolation(EvidenceGitFixture.Result result, string reason)
    {
        result.Exit.ShouldBe(1, "c1024-" + reason);
        result.Output.ShouldContain("reason=" + reason);
        result.Output.ShouldContain("violations=1");
    }

    [Test]
    public async Task Verifies_later_cleanup_without_original_anchor_entries()
    {
        foreach (var anchored in new[] { true, false })
        {
            await using var f = await EvidenceGitFixture.CreateAsync(anchored);
            var b = await LaterBaseAsync(f, anchored);
            var inv = await f.InventoryAsync(b);
            inv.Delete.Select(x => x.Path).ShouldBe([".antiphon/later.json", ".antiphon/later.log"]);
            if (anchored)
            {
                var legacy = await f.InventoryOnlyAsync(b);
                legacy.Exit.ShouldBe(1, "c1024-default-still-requires-anchor");
                legacy.Output.ShouldContain("reason=anchor_present");
            }
            var before = await f.SnapshotAsync();
            var inventory = await InventoryAsync(f, b);
            inventory.Exit.ShouldBe(0, "c1024-later-inventory");
            using var json = JsonDocument.Parse(inventory.Output.Split('\n').Single(x => x.StartsWith('{')));
            var root = json.RootElement;
            root.GetProperty("SupplementalCleanup").GetString().ShouldBe(Card);
            root.TryGetProperty("AnchorValid", out _).ShouldBeFalse();
            root.GetProperty("Count").GetInt32().ShouldBe(inv.Delete.Length);
            root.GetProperty("Bytes").GetInt64().ShouldBe(inv.Bytes);
            root.GetProperty("PathSha256").GetString().ShouldBe(inv.Digest);
            root.GetProperty("Keep").EnumerateArray().Select(x => x.GetProperty("Path").GetString()).ToArray()
                .ShouldBe(inv.Keep.Select(x => x.Path).ToArray());
            (await f.SnapshotAsync()).ShouldBe(before, "c1024-inventory-read-only");
            var deletion = await f.ExactDeletionAsync(b, inv, message: Marker);
            before = await f.SnapshotAsync();
            var result = await ValidateAsync(f, b, deletion, inv);
            result.Exit.ShouldBe(0, "c1024-exact-later-deletion");
            result.Output.ShouldContain("deletion=" + deletion);
            result.Output.ShouldContain("finalTreeViolations=0 recoverable=2 violations=0");
            (await f.SnapshotAsync()).ShouldBe(before, "c1024-validation-read-only");
            (await f.TreeAsync(deletion)).ShouldBe((await f.TreeAsync(b)).Where(x => !inv.Delete.Any(d => d.Path == x.Path)).ToArray());
            foreach (var entry in inv.Delete)
                (await f.RunAsync("git", ["cat-file", "-e", entry.Oid])).Exit.ShouldBe(0);
        }
    }

    [Test]
    public async Task Rejects_extra_deletion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        foreach (var extra in new[] { inv.Keep.Single(), (await f.TreeAsync(b)).Single(x => x.Path == "outside.txt") })
        {
            await f.GitAsync("read-tree", b);
            foreach (var entry in inv.Delete) await f.RemoveAsync(entry.Path);
            await f.RemoveAsync(extra.Path);
            var deletion = await f.CommitIndexAsync([b], Marker);
            // Restore afterward: final preservation cannot mask the exact deletion-set guard.
            await f.GitAsync("read-tree", deletion); await f.SetAsync(extra.Path, extra.Oid, extra.Mode);
            var head = await f.CommitIndexAsync([deletion]);
            SingleViolation(await ValidateAsync(f, b, head, inv), "deletion_extra");
        }
    }

    [Test]
    public async Task Rejects_missing_deletion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        await f.GitAsync("read-tree", b); await f.RemoveAsync(inv.Delete[0].Path);
        var partial = await f.CommitIndexAsync([b], Marker);
        var head = await f.DeleteCommitAsync(partial, inv.Delete[1].Path);
        SingleViolation(await ValidateAsync(f, b, head, inv), "deletion_missing");
    }

    [Test]
    public async Task Rejects_changed_kept_markdown()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        var deletion = await f.ExactDeletionAsync(b, inv, message: Marker); var kept = inv.Keep.Single();
        var changed = await f.AddCommitAsync(deletion, kept.Path, Text("different report"));
        SingleViolation(await ValidateAsync(f, b, changed, inv), "kept_oid");
        var missing = await f.DeleteCommitAsync(deletion, kept.Path);
        SingleViolation(await ValidateAsync(f, b, missing, inv), "kept_present");
        await f.GitAsync("read-tree", deletion); await f.SetAsync(kept.Path, kept.Oid, "100755");
        var retyped = await f.CommitIndexAsync([deletion]);
        SingleViolation(await ValidateAsync(f, b, retyped, inv), "kept_mode");
    }

    [Test]
    public async Task Refuses_nonrecoverable_deleted_blob()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        var deletion = await f.ExactDeletionAsync(b, inv, message: Marker);
        var blob = inv.Delete[0];
        var ownedBlob = Path.Combine(f.Repo, ".git", "objects", blob.Oid[..2], blob.Oid[2..]);
        File.Exists(ownedBlob).ShouldBeTrue("only remove this fixture's unique loose object");
        var hook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments[0] -eq 'ls-tree' -and $Arguments[-1] -eq '" + deletion + "') { [IO.File]::Delete(" + EvidenceGitFixture.Quote(ownedBlob) + "); Write-Host 'FAULT-HIT' }; return $r }";
        var result = await ValidateAsync(f, b, deletion, inv, hook: hook);
        result.Output.Split("FAULT-HIT", StringSplitOptions.None).Length.ShouldBe(2, "fault cut hit exactly once after sizing");
        result.Exit.ShouldBe(2, "c1024-deleted-blob-recoverable");
        result.Output.ShouldContain("EVIDENCE error reason=git_result_cat-file");
        File.Exists(ownedBlob).ShouldBeFalse();
    }

    [Test]
    public async Task Rejects_wrong_inventory_or_cleanup_label()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        var deletion = await f.ExactDeletionAsync(b, inv, message: Marker);
        SingleViolation(await ValidateAsync(f, b, deletion, inv, count: "3"), "inventory_count");
        SingleViolation(await ValidateAsync(f, b, deletion, inv, digest: new string('0', 64)), "inventory_digest");
        SingleViolation(await ValidateAsync(f, b, deletion, inv, bytes: (inv.Bytes + 1).ToString()), "inventory_bytes");
        foreach (var label in new[] { "", " ", "card-1024", "CARD-1015", "CARD-1024\nInjected: value" })
        {
            var inventory = await InventoryAsync(f, b, label);
            inventory.Exit.ShouldBe(2, "c1024-cleanup-label-inventory");
            inventory.Output.ShouldContain("reason=supplemental_cleanup_input");
            var validation = await ValidateAsync(f, b, deletion, inv, card: label);
            validation.Exit.ShouldBe(2, "c1024-cleanup-label-validation");
            validation.Output.ShouldContain("reason=supplemental_cleanup_input");
        }
    }

    [Test]
    public async Task Rejects_wrong_ambiguous_or_merge_marker()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        foreach (var message in new[] { "unmarked", Marker.Replace(Card, "CARD-9999"), Marker.Replace(Card, "CARD-1015"),
                     "Antiphon-Evidence-Deletion: " + Card + "\n\nordinary body follows" })
        {
            var wrong = await f.ExactDeletionAsync(b, inv, message: message);
            var result = await ValidateAsync(f, b, wrong, inv);
            result.Exit.ShouldBe(1, "c1024-exact-marker"); result.Output.ShouldContain("reason=deletion_unique");
        }
        var one = await f.ExactDeletionAsync(b, inv, message: Marker);
        await f.GitAsync("read-tree", one); var two = await f.CommitIndexAsync([one], Marker);
        var ambiguous = await ValidateAsync(f, b, two, inv);
        ambiguous.Exit.ShouldBe(1, "c1024-unique-marker"); ambiguous.Output.ShouldContain("reason=deletion_unique");
        var side = await f.AddCommitAsync(b, "side.txt", Text("side"));
        await f.GitAsync("read-tree", one); var merge = await f.CommitIndexAsync([b, side], Marker);
        var merged = await ValidateAsync(f, b, merge, inv);
        merged.Exit.ShouldBe(1, "c1024-single-parent"); merged.Output.ShouldContain("reason=deletion_parent");
    }

    [Test]
    public async Task Rejects_non_deletion_changes()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b);
        foreach (var path in new[] { "outside.txt", "added.txt" })
        {
            await f.GitAsync("read-tree", b);
            foreach (var entry in inv.Delete) await f.RemoveAsync(entry.Path);
            await f.PutAsync(path, Text("changed"));
            var deletion = await f.CommitIndexAsync([b], Marker);
            var result = await ValidateAsync(f, b, deletion, inv);
            result.Exit.ShouldBe(1, "c1024-deletion-only"); result.Output.ShouldContain("reason=deletion_only");
        }
    }

    [Test]
    public async Task Rejects_changed_inventory_entries()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await LaterBaseAsync(f); var inv = await f.InventoryAsync(b); var entry = inv.Delete[0];
        var changed = await f.AddCommitAsync(b, entry.Path, Enumerable.Repeat((byte)'z', (int)entry.Bytes).ToArray());
        var deletion = await f.ExactDeletionAsync(b, inv, parent: changed, message: Marker);
        SingleViolation(await ValidateAsync(f, b, deletion, inv), "deletion_old_oid");
        await f.GitAsync("read-tree", b); await f.SetAsync(entry.Path, entry.Oid, "100755");
        var retyped = await f.CommitIndexAsync([b]);
        var wrongMode = await f.ExactDeletionAsync(b, inv, parent: retyped, message: Marker);
        SingleViolation(await ValidateAsync(f, b, wrongMode, inv), "deletion_old_mode");
    }

    [Test]
    public async Task Rejects_resurrection_and_new_final_artifacts()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var b = await f.AddCommitAsync(await LaterBaseAsync(f), ".antiphon/oversize.md", new byte[1048577]);
        var inv = await f.InventoryAsync(b); var deletion = await f.ExactDeletionAsync(b, inv, message: Marker);
        var restored = await f.AddCommitAsync(deletion, ".antiphon/oversize.md", Text("small permitted replacement"));
        SingleViolation(await ValidateAsync(f, b, restored, inv), "deletion_resurrection");
        var newArtifact = await f.AddCommitAsync(deletion, ".antiphon/new-final.trx", Text("rejected"));
        SingleViolation(await ValidateAsync(f, b, newArtifact, inv), "final_tree_policy");
    }
}
