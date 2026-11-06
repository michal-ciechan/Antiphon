using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional immutable source packs; template and migrations are assembly-shared.
public sealed class C0824RefOnlyDeliverableTests
{
    [Test]
    public async Task Bundle_reads_exact_settled_bytes_despite_stale_disk()
    {
        var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("# Exact 雪\r\nbyte-preserving source\r\n")).ToArray();
        foreach (var role in new[] { AgentTaskRole.Plan, AgentTaskRole.Docs, AgentTaskRole.Custom })
        foreach (var refOnly in new[] { true, false })
        {
            await using var f = await RefOnlyDeliverableFixture.CreateAsync(role, refOnly);
            var w = f.World;
            var source = await f.CommitAsync(new Dictionary<string, byte[]> { ["docs/evidence.md"] = bytes });
            Directory.CreateDirectory(Path.Combine(w.Git.Desktop, "docs"));
            await File.WriteAllTextAsync(Path.Combine(w.Git.Desktop, "docs/evidence.md"), "stale main bytes");
            if (refOnly)
            {
                Directory.CreateDirectory(Path.Combine(w.Task.WorktreePath!, "docs"));
                await File.WriteAllTextAsync(Path.Combine(w.Task.WorktreePath!, "docs/evidence.md"), "foreign identity decoy");
            }
            await w.SettleAsync(RunnerSettlementWorld.Report("Wrote `docs/evidence.md`. " + w.Claim(source)));
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, w.Task.CompletionProgressEvidenceJson);
            var manifest = await f.ManifestAsync();
            var member = manifest.Sources.ShouldHaveSingleItem();
            member.Length.ShouldBe(bytes.LongLength);
            member.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
            (await File.ReadAllBytesAsync(Path.Combine(w.Task.DeliverableBundleDir!, member.StoredFile))).ShouldBe(bytes, "bundle-bytes=S:" + role);
            if (refOnly)
            {
                w.Task.DeliverableBundleDir!.ShouldStartWith(f.Root.Path, "explicit-durable-artifact-store");
                f.Git.ByteReads.ShouldContain(r => r.Sha == source && r.Path == "docs/evidence.md", "read-exact-commit");
                (await File.ReadAllTextAsync(Path.Combine(w.Task.WorktreePath!, "docs/evidence.md"))).ShouldBe("foreign identity decoy");
            }
            (await File.ReadAllTextAsync(Path.Combine(w.Git.Desktop, "docs/evidence.md"))).ShouldBe("stale main bytes");
        }
    }

    [Test]
    public async Task Bundle_pins_one_sha_while_branch_moves()
    {
        await using var f = await RefOnlyDeliverableFixture.CreateAsync();
        var w = f.World;
        var first = Encoding.UTF8.GetBytes("first at S\r\n");
        var second = Encoding.UTF8.GetBytes("second at S\r\n");
        var source = await f.CommitAsync(new Dictionary<string, byte[]> { ["docs/a.md"] = first, ["docs/b.md"] = second });
        var moved = false;
        string? newer = null;
        f.Bundles.BeforeCommittedMemberAsync = async (relative, _) =>
        {
            if (relative != "docs/b.md") return;
            moved = true;
            newer = await w.Git.RunnerPushAsync("docs/b.md", "different and longer second member at T");
        };
        await w.SettleAsync(RunnerSettlementWorld.Report("Wrote `docs/a.md` and `docs/b.md`. " + w.Claim(source)));
        moved.ShouldBeTrue("reached:B2:second-member:branch-moved");
        newer.ShouldNotBe(source);
        var manifest = await f.ManifestAsync();
        manifest.Sources.Count.ShouldBe(2);
        foreach (var member in manifest.Sources)
        {
            var expected = member.OriginalRelativePath == "docs/a.md" ? first : second;
            (await File.ReadAllBytesAsync(Path.Combine(w.Task.DeliverableBundleDir!, member.StoredFile))).ShouldBe(expected, "all-members-from=S");
            member.Length.ShouldBe(expected.LongLength);
            member.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(expected)));
        }
        f.Git.LengthReads.ShouldAllBe(r => r.Sha == source);
        f.Git.ByteReads.ShouldAllBe(r => r.Sha == source);
        // A fresh caller must continue using the accepted S rather than the moved branch T.
        f.Bundles.BeforeCommittedMemberAsync = null;
        await w.RestartServicesAsync();
        await using (var db = w.CreateContext()) await f.Bundles.TryBuildAsync(w.Task, "Wrote `docs/b.md`.", db, default);
        (await f.ManifestAsync()).Sources.ShouldHaveSingleItem().Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(second)));
    }

    [Test]
    public async Task Commit_diff_handles_docs_only_rename_delete_and_named_paths()
    {
        var initial = new Dictionary<string, string> { ["docs/old.md"] = "unchanged rename content " + new string('x', 256), ["docs/delete.md"] = "deleted" };
        foreach (var variant in new[] { "docs", "mixed-unnamed", "mixed-named" })
        foreach (var refOnly in new[] { true, false })
        {
            await using var f = await RefOnlyDeliverableFixture.CreateAsync(AgentTaskRole.Code, refOnly, initial);
            var w = f.World;
            await w.Git.EnsureRunnerAsync();
            await w.Git.RunAsync(w.Git.Runner, "mv", "docs/old.md", "docs/renamed 雪.md");
            await w.Git.RunAsync(w.Git.Runner, "rm", "docs/delete.md");
            var files = new Dictionary<string, byte[]> { ["docs/new.md"] = Encoding.UTF8.GetBytes("new doc at S") };
            if (variant != "docs") files["worker.cs"] = Encoding.UTF8.GetBytes("// code at S");
            var source = await f.CommitAsync(files);
            Directory.CreateDirectory(Path.Combine(w.Git.Desktop, "docs"));
            await File.WriteAllTextAsync(Path.Combine(w.Git.Desktop, "docs/untracked.md"), "untracked canonical decoy");
            await File.WriteAllTextAsync(Path.Combine(w.Git.Desktop, "docs/delete.md"), "stale deleted decoy");
            var report = variant == "mixed-named" ? "Wrote `docs/new.md`." : "Implemented committed changes.";
            await w.SettleAsync(RunnerSettlementWorld.Report(report + " " + w.Claim(source)));
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, w.Task.CompletionProgressEvidenceJson);
            if (variant == "mixed-unnamed") w.Task.DeliverableBundleDir.ShouldBeNull("mixed-unnamed-stays-unattached");
            else
            {
                var manifest = await f.ManifestAsync();
                var expected = variant == "docs" ? new[] { "docs/new.md", "docs/renamed 雪.md" } : new[] { "docs/new.md" };
                manifest.Sources.Select(m => m.OriginalRelativePath).Order().ShouldBe(expected.Order(), "paths=expected-at-S");
                manifest.Sources.ShouldNotContain(m => m.OriginalRelativePath == "docs/delete.md" || m.OriginalRelativePath == "docs/untracked.md");
            }
        }
    }

    [Test]
    public async Task Missing_oversize_excluded_and_link_blobs_are_not_attached()
    {
        await using var f = await RefOnlyDeliverableFixture.CreateAsync();
        var w = f.World;
        await w.Git.EnsureRunnerAsync();
        await File.WriteAllTextAsync(Path.Combine(w.Git.Runner, "link-object"), "safe.md");
        var target = await w.Git.RunAsync(w.Git.Runner, "hash-object", "-w", "link-object");
        await w.Git.RunAsync(w.Git.Runner, "update-index", "--add", "--cacheinfo", "120000," + target + ",docs/link.md");
        var source = await f.CommitAsync(new Dictionary<string, byte[]>
        {
            ["docs/safe.md"] = Encoding.UTF8.GetBytes("small"), ["docs/exact.md"] = Encoding.UTF8.GetBytes("12345678"),
            ["docs/over.md"] = Encoding.UTF8.GetBytes("123456789"), ["docs/cards/secret.md"] = Encoding.UTF8.GetBytes("excluded"),
            [".antiphon/secret.md"] = Encoding.UTF8.GetBytes("excluded"),
        });
        f.Settings.MaxSourceBytes = 8; f.Settings.MaxTotalSourceBytes = 13;
        var report = "Wrote `docs/safe.md`, `docs/exact.md`, `docs/over.md`, `docs/link.md`, `docs/missing.md`, `docs/cards/secret.md`, `../docs/safe.md`, `/docs/safe.md`, `.antiphon/secret.md`.";
        await w.SettleAsync(RunnerSettlementWorld.Report(report + " " + w.Claim(source)));
        var manifest = await f.ManifestAsync();
        manifest.Complete.ShouldBeFalse("incomplete-is-explicit");
        manifest.Sources.Select(s => s.OriginalRelativePath).Order().ShouldBe(new[] { "docs/exact.md", "docs/safe.md" });
        foreach (var path in new[] { "docs/over.md", "docs/link.md", "docs/missing.md" })
            manifest.Omitted.ShouldContain(m => m.OriginalRelativePath == path, "omitted:" + path);
        f.Git.ByteReads.ShouldNotContain(r => r.Path == "docs/link.md" || r.Path == "docs/cards/secret.md" || r.Path.StartsWith(".antiphon/"), "unsafe-source-never-opened");
        (await File.ReadAllTextAsync(Path.Combine(w.Task.DeliverableBundleDir!, "safe.md"))).ShouldBe("small");
        f.Settings.MaxSourceBytes = 100; f.Settings.MaxTotalSourceBytes = 100;
        f.Git.ByteReads.Clear();
        await using (var db = w.CreateContext()) await f.Bundles.TryBuildAsync(w.Task,
            "Wrote `docs/safe.md`, `docs/link.md`, `docs/cards/secret.md`, `../docs/over.md`, `/docs/over.md`, `.antiphon/secret.md`.", db, default);
        (await f.ManifestAsync()).Sources.ShouldHaveSingleItem().OriginalRelativePath.ShouldBe("docs/safe.md", "type-and-path-fences-independent-of-size");
        f.Git.ByteReads.ShouldAllBe(r => r.Path == "docs/safe.md", "unsafe-source-never-opened-with-large-budget");
        f.Settings.MaxSourceBytes = 8;
        foreach (var fault in new[] { "lying-length", "failed-length", "per-source", "total-budget" })
        {
            f.Git.ByteReads.Clear(); f.Git.LengthReads.Clear();
            var path = fault == "total-budget" ? "docs/safe.md" : "docs/over.md";
            f.Settings.MaxTotalSourceBytes = fault == "total-budget" ? 4 : fault == "per-source" ? 100 : 8;
            f.Git.LengthOverride = fault switch { "lying-length" => _ => 1, "failed-length" => _ => null, _ => null };
            await using var db = w.CreateContext();
            await f.Bundles.TryBuildAsync(w.Task, "Wrote `" + path + "`.", db, default);
            var omitted = await f.ManifestAsync();
            omitted.Complete.ShouldBeFalse("omitted:" + fault);
            omitted.Sources.ShouldBeEmpty("overflow-is-not-an-empty-success");
            omitted.Omitted.ShouldContain(m => m.OriginalRelativePath == path);
            if (fault == "lying-length") f.Git.ByteReads.ShouldHaveSingleItem().Budget.ShouldBe(8, "streaming-budget-is-enforced");
            else f.Git.ByteReads.ShouldBeEmpty("budget-length-refused-before-allocation");
        }
        f.Settings.StorageRoot = w.Task.WorktreePath;
        await using (var db = w.CreateContext()) await f.Bundles.TryBuildAsync(w.Task, "Wrote `docs/safe.md`.", db, default);
        Directory.Exists(w.Task.WorktreePath!).ShouldBeFalse("identity-is-never-an-artifact-destination");
    }
}
