using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidenceApprovedFixtureGuardTests
{
    private const string FixturePath = ".antiphon/fixtures/c1022-guard-probe.approved.json";
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    private static void Accepted(EvidenceGitFixture.Result result)
    {
        result.Exit.ShouldBe(0, "c1036-approved-accepted: " + result.Output);
        result.Output.ShouldContain("entries=1 violations=0");
    }

    private static void Rejected(EvidenceGitFixture.Result result, string path, string reason, string label)
    {
        result.Exit.ShouldBe(1, label + ": " + result.Output);
        // Parse the diagnostic path so newline paths are asserted without relying on JSON escaping style.
        var line = result.Output.Split('\n').Single(x => x.StartsWith("EVIDENCE violation "));
        var start = line.IndexOf(" path=", StringComparison.Ordinal) + 6;
        var end = line.LastIndexOf(" bytes=", StringComparison.Ordinal);
        JsonSerializer.Deserialize<string>(line[start..end]).ShouldBe(path, label);
        line.Contains("reason=" + reason, StringComparison.Ordinal).ShouldBeTrue(label);
        result.Output.Contains("entries=1 violations=1", StringComparison.Ordinal).ShouldBeTrue(label);
    }

    [Test]
    public async Task Allows_approved_json_fixtures()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var (path, mode) in new[] { (FixturePath, "100644"),
                     (".antiphon/fixtures/protocol_v1/Ready-2.approved.json", "100755") })
        {
            var head = await f.AddCommitAsync(f.Base, path, Text("{\"ready\":true}"), mode);
            Accepted(await f.HistoryAsync(f.Base, head));
            var modified = await f.AddCommitAsync(head, path, Text("{\"ready\":false}"), mode);
            Accepted(await f.HistoryAsync(head, modified));
        }
    }

    [Test]
    public async Task Rejects_fixture_path_near_misses()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var path in new[] {
                     ".antiphon/fixtures-other/x.approved.json", ".antiphon/x/fixtures/x.approved.json",
                     ".antiphon/reports/x.approved.json", ".antiphon/x.approved.json",
                     ".antiphon/Fixtures/x.approved.json", ".ANTIPHON/fixtures/x.approved.json",
                     FixturePath + "\n", ".antiphon/fixtures/with space/x.approved.json",
                     ".antiphon/fixtures/with.dot/x.approved.json", ".antiphon/fixtures/.approved.json" })
        {
            var head = await f.AddCommitAsync(f.Base, path, Text("{}"));
            Rejected(await f.HistoryAsync(f.Base, head), path, "non_markdown", "c1036-path-boundary");
        }
    }

    [Test]
    public async Task Rejects_run_output_in_fixture_directory()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var group in new[] { "", "protocol_v1/" })
        foreach (var name in new[] { "source.json", "receipt.json", "request.json", "report.json", "run.trx",
                     "compiler.log", "build.binlog", "console.txt", "receipt.jsonl", "receipts.tar.gz",
                     "x.received.json", "x.approved.json.log", "x.APPROVED.JSON" })
        {
            var path = ".antiphon/fixtures/" + group + name;
            var head = await f.AddCommitAsync(f.Base, path, Text("producer output"));
            Rejected(await f.HistoryAsync(f.Base, head), path, "non_markdown", "c1036-run-output-rejected");
        }
    }

    [Test]
    public async Task Approved_fixtures_obey_checkpoint_exclusion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var group in new[] { "checkpoints", "a/Mixed-CheckPoints", "c1036-checkpoints" })
        {
            var path = ".antiphon/fixtures/" + group + "/x.approved.json";
            var head = await f.AddCommitAsync(f.Base, path, Text("{}"));
            Rejected(await f.HistoryAsync(f.Base, head), path, "checkpoint_directory", "c1036-checkpoint-precedence");
        }
    }

    [Test]
    public async Task Approved_fixtures_obey_regular_mode_constraint()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var link = await f.AddCommitAsync(f.Base, FixturePath, Text("outside.txt"), "120000");
        Rejected(await f.HistoryAsync(f.Base, link), FixturePath, "non_regular_mode", "c1036-mode");
        await f.GitAsync("read-tree", f.Base);
        await f.SetAsync(FixturePath, f.Base, "160000");
        var module = await f.CommitIndexAsync([f.Base]);
        Rejected(await f.HistoryAsync(f.Base, module), FixturePath, "non_regular_mode", "c1036-mode");
    }

    [Test]
    public async Task Approved_fixtures_obey_one_mib_limit()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var size in new[] { 1_048_575, 1_048_576 })
        {
            var head = await f.AddCommitAsync(f.Base, FixturePath, new byte[size]);
            Accepted(await f.HistoryAsync(f.Base, head));
        }
        var oversized = await f.AddCommitAsync(f.Base, FixturePath, new byte[1_048_577]);
        Rejected(await f.HistoryAsync(f.Base, oversized), FixturePath, "oversize_blob", "c1036-size");
        var utf8 = Text("\"" + new string('\u00e9', 524_288) + "\"");
        utf8.Length.ShouldBe(1_048_578);
        var multibyte = await f.AddCommitAsync(f.Base, FixturePath, utf8);
        Rejected(await f.HistoryAsync(f.Base, multibyte), FixturePath, "oversize_blob", "c1036-size");
        await f.PutAsync(FixturePath, Text("{}"));
        var workingPath = Path.Combine(f.Repo, FixturePath);
        Directory.CreateDirectory(Path.GetDirectoryName(workingPath)!);
        File.WriteAllText(workingPath, "{}");
        var before = await f.SnapshotAsync();
        Rejected(await f.HistoryAsync(f.Base, oversized), FixturePath, "oversize_blob", "c1036-size");
        (await f.SnapshotAsync()).ShouldBe(before, "c1036-committed-size-read-only");
    }

    [Test]
    public async Task Deletion_inventory_preserves_approved_fixtures()
    {
        const string receipt = ".antiphon/fixtures/receipt.json";
        const string log = ".antiphon/fixtures/compiler.log";
        foreach (var anchored in new[] { true, false })
        {
            await using var f = await EvidenceGitFixture.CreateAsync(anchored);
            await f.GitAsync("read-tree", f.Base);
            var oid = await f.PutAsync(FixturePath, Text("{\"expected\":true}"), "100755");
            await f.PutAsync(receipt, Text("receipt"));
            await f.PutAsync(log, Text("compiler output"));
            var b = await f.CommitIndexAsync([f.Base]);
            var inv = await f.InventoryAsync(b);
            inv.Keep.Select(x => x.Path).ShouldContain(FixturePath, "c1036-fixture-kept");
            inv.Delete.Select(x => x.Path).ShouldNotContain(FixturePath, "c1036-fixture-kept");
            inv.Delete.Select(x => x.Path).ShouldContain(receipt);
            inv.Delete.Select(x => x.Path).ShouldContain(log);
            var supplemental = anchored ? Array.Empty<string>() : new[] { "-SupplementalCleanup", "CARD-1036" };
            var before = await f.SnapshotAsync();
            var inventory = await f.GuardAsync("check-evidence-deletion.ps1",
                new[] { "-InventoryRef", b, "-InventoryOnly" }.Concat(supplemental).ToArray());
            inventory.Exit.ShouldBe(0, "c1036-fixture-kept: " + inventory.Output);
            using var json = JsonDocument.Parse(inventory.Output.Split('\n').Single(x => x.StartsWith('{')));
            var root = json.RootElement;
            var keep = root.GetProperty("Keep").EnumerateArray().Select(x => x.GetProperty("Path").GetString()).ToArray();
            var delete = root.GetProperty("Delete").EnumerateArray().Select(x => x.GetProperty("Path").GetString()).ToArray();
            keep.ShouldContain(FixturePath, "c1036-fixture-kept");
            delete.ShouldNotContain(FixturePath, "c1036-fixture-kept");
            delete.ShouldContain(receipt); delete.ShouldContain(log);
            keep.ShouldBe(inv.Keep.Select(x => x.Path).ToArray());
            delete.ShouldBe(inv.Delete.Select(x => x.Path).ToArray());
            root.GetProperty("Count").GetInt32().ShouldBe(inv.Delete.Length);
            root.GetProperty("Bytes").GetInt64().ShouldBe(inv.Bytes);
            root.GetProperty("PathSha256").GetString().ShouldBe(inv.Digest);
            (await f.SnapshotAsync()).ShouldBe(before, "c1036-inventory-read-only");
            var marker = "remove rejected evidence\n\nAntiphon-Evidence-Deletion: " + (anchored ? "CARD-1015" : "CARD-1036");
            var deletion = await f.ExactDeletionAsync(b, inv, message: marker);
            foreach (var head in new[] { deletion, await f.AddCommitAsync(deletion,
                         ".antiphon/fixtures/new-group/later.approved.json", Text("{}")) })
            {
                before = await f.SnapshotAsync();
                var result = await f.GuardAsync("check-evidence-deletion.ps1", new[] {
                    "-InventoryRef", b, "-HeadRef", head, "-InventoryCount", inv.Delete.Length.ToString(),
                    "-InventoryPathSha256", inv.Digest, "-InventoryBytes", inv.Bytes.ToString() }.Concat(supplemental).ToArray());
                result.Exit.ShouldBe(0, "c1036-fixture-kept: " + result.Output);
                result.Output.ShouldContain("finalTreeViolations=0");
                result.Output.ShouldContain("recoverable=" + inv.Delete.Count(x => x.Type == "blob") + " violations=0");
                (await f.SnapshotAsync()).ShouldBe(before, "c1036-validation-read-only");
                var preserved = (await f.TreeAsync(head)).Single(x => x.Path == FixturePath);
                preserved.Oid.ShouldBe(oid, "c1036-fixture-kept");
                preserved.Mode.ShouldBe("100755", "c1036-fixture-kept");
                (await f.TreeAsync(head)).Any(EvidenceGitFixture.Reject).ShouldBeFalse();
                foreach (var entry in inv.Delete)
                    (await f.RunAsync("git", ["cat-file", "-e", entry.Oid])).Exit.ShouldBe(0);
            }
        }
    }
}
