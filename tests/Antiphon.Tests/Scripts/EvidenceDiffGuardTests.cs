using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidenceDiffGuardTests
{
    private static byte[] Text(string value = "fixture") => Encoding.UTF8.GetBytes(value);

    [Test]
    public async Task Rejects_checkpoint_paths()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var (path, label) in new[] {
            (".antiphon/checkpoints/a.md", "checkpoint"), (".antiphon/a/b/checkpoints/a.md", "nested-checkpoint"),
            (".antiphon/c1015-checkpoints/a.md", "prefixed-checkpoint"), (".antiphon/a/CheckPoints/a.md", "checkpoint-case") })
        {
            var head = await f.AddCommitAsync(f.Base, path, Text());
            var r = await f.HistoryAsync(f.Base, head);
            switch (label)
            {
                case "checkpoint": r.Exit.ShouldBe(1, "c1015-checkpoint"); break;
                case "nested-checkpoint": r.Exit.ShouldBe(1, "c1015-nested-checkpoint"); break;
                case "prefixed-checkpoint": r.Exit.ShouldBe(1, "c1015-prefixed-checkpoint"); break;
                case "checkpoint-case": r.Exit.ShouldBe(1, "c1015-checkpoint-case"); break;
                default: throw new InvalidOperationException("unknown fixture label");
            }
            r.Output.ShouldContain(head);
            r.Output.ShouldContain(path);
        }
        var leaf = await f.AddCommitAsync(f.Base, ".antiphon/checkpoints.md", Text());
        (await f.HistoryAsync(f.Base, leaf)).Exit.ShouldBe(0, "c1015-checkpoint-leaf");
    }

    [Test]
    public async Task Rejects_non_markdown_outputs()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var ext in new[] { ".trx.gz", ".trx", ".json", ".log", ".zip", ".tar.gz", "", ".md.exe" })
        {
            var head = await f.AddCommitAsync(f.Base, ".antiphon/output" + ext, Text());
            (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(1, "c1015-format");
        }
        var allowed = await f.AddCommitAsync(f.Base, ".antiphon/report.MD", Text());
        (await f.HistoryAsync(f.Base, allowed)).Exit.ShouldBe(0);
    }

    [Test]
    public async Task Rejects_non_regular_modes()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var link = await f.AddCommitAsync(f.Base, ".antiphon/link.md", Text("outside.txt"), "120000");
        (await f.HistoryAsync(f.Base, link)).Exit.ShouldBe(1, "c1015-mode");
        await f.GitAsync("read-tree", f.Base);
        await f.SetAsync(".antiphon/module.md", f.Base, "160000");
        var gitlink = await f.CommitIndexAsync([f.Base]);
        (await f.HistoryAsync(f.Base, gitlink)).Exit.ShouldBe(1, "c1015-mode-gitlink");
        foreach (var mode in new[] { "100644", "100755" })
        {
            var head = await f.AddCommitAsync(f.Base, ".antiphon/regular.md", Text(), mode);
            (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(0);
        }
    }

    [Test]
    public async Task Enforces_one_mib_blob_limit()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var size in new[] { 1048575, 1048576 })
        {
            var head = await f.AddCommitAsync(f.Base, ".antiphon/report.md", new byte[size]);
            (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(0);
        }
        var over = await f.AddCommitAsync(f.Base, ".antiphon/report.md", new byte[1048577]);
        (await f.HistoryAsync(f.Base, over)).Exit.ShouldBe(1, "c1015-oversize");
        var utf8 = Text(new string('\u00e9', 524289));
        utf8.Length.ShouldBe(1048578);
        var multi = await f.AddCommitAsync(f.Base, ".antiphon/report.md", utf8);
        (await f.HistoryAsync(f.Base, multi)).Exit.ShouldBe(1, "c1015-utf8-bytes");
        var raw = await f.AddCommitAsync(f.Base, ".antiphon/output.trx", new byte[1048577]);
        (await f.HistoryAsync(f.Base, raw)).Exit.ShouldBe(1);
    }

    [Test]
    public async Task Allows_small_markdown_and_other_source()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        foreach (var path in new[] { ".antiphon/report.md", ".antiphon/provenance.md", "source.bin", ".antiphon-other/output.json", "x/.antiphon/output.json" })
        {
            var head = await f.AddCommitAsync(f.Base, path, path.EndsWith(".md") ? [] : new byte[1048577]);
            (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(0, "c1015-root-scope");
        }
    }

    [Test]
    public async Task Grandfathers_unchanged_legacy_and_allows_deletion()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var legacy = await f.AddCommitAsync(f.Base, ".antiphon/legacy.trx", Text());
        var unrelated = await f.AddCommitAsync(legacy, "source.txt", Text());
        var unchanged = await f.HistoryAsync(legacy, unrelated);
        unchanged.Exit.ShouldBe(0, "c1015-legacy-unchanged");
        unchanged.Output.ShouldContain("commits=1 entries=0 violations=0");
        var deleted = await f.DeleteCommitAsync(legacy, ".antiphon/legacy.trx");
        (await f.HistoryAsync(legacy, deleted)).Exit.ShouldBe(0, "c1015-legacy-delete");
        var empty = await f.HistoryAsync(legacy, legacy);
        empty.Exit.ShouldBe(0);
        empty.Output.ShouldContain("commits=0 entries=0 violations=0");
    }

    [Test]
    public async Task Rechecks_modified_and_renamed_legacy_paths()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var legacy = await f.AddCommitAsync(f.Base, ".antiphon/legacy.trx", Text("old"));
        var modified = await f.AddCommitAsync(legacy, ".antiphon/legacy.trx", Text("new"));
        (await f.HistoryAsync(legacy, modified)).Exit.ShouldBe(1, "c1015-legacy-modified");
        var entry = (await f.TreeAsync(legacy)).Single(x => x.Path == ".antiphon/legacy.trx");
        await f.GitAsync("read-tree", legacy);
        await f.RemoveAsync(entry.Path);
        await f.SetAsync(".antiphon/renamed.trx", entry.Oid);
        var renamed = await f.CommitIndexAsync([legacy]);
        (await f.HistoryAsync(legacy, renamed)).Exit.ShouldBe(1, "c1015-rename-destination");
        await f.GitAsync("read-tree", f.Base);
        var source = (await f.TreeAsync(f.Base)).Single(x => x.Path == "outside.txt");
        await f.SetAsync(".antiphon/x.json", source.Oid);
        var copied = await f.CommitIndexAsync([f.Base]);
        (await f.HistoryAsync(f.Base, copied)).Exit.ShouldBe(1, "c1015-rename-destination");
        var md = await f.AddCommitAsync(f.Base, ".antiphon/report.md", Text());
        var retyped = await f.AddCommitAsync(md, ".antiphon/report.md", Text(), "120000");
        (await f.HistoryAsync(md, retyped)).Exit.ShouldBe(1);
        await f.GitAsync("read-tree", legacy);
        await f.RemoveAsync(entry.Path);
        await f.SetAsync(".antiphon/renamed.md", entry.Oid);
        var allowed = await f.CommitIndexAsync([legacy]);
        (await f.HistoryAsync(legacy, allowed)).Exit.ShouldBe(0);
    }

    [Test]
    public async Task Checks_intermediate_commits_even_when_tip_is_clean()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var bad = await f.AddCommitAsync(f.Base, ".antiphon/temporary.trx", Text());
        var clean = await f.DeleteCommitAsync(bad, ".antiphon/temporary.trx");
        var r = await f.HistoryAsync(f.Base, clean);
        r.Exit.ShouldBe(1, "c1015-intermediate");
        r.Output.ShouldContain(bad);
        (await f.GitAsync("diff", "--name-only", f.Base, clean)).ShouldBeEmpty();
    }

    [Test]
    public async Task Checks_merge_side_history()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var bad = await f.AddCommitAsync(f.Base, ".antiphon/side.trx", Text());
        var clean = await f.DeleteCommitAsync(bad, ".antiphon/side.trx");
        await f.GitAsync("read-tree", f.Base);
        var merge = await f.CommitIndexAsync([f.Base, clean]);
        var side = await f.HistoryAsync(f.Base, merge);
        side.Exit.ShouldBe(1, "c1015-side-history"); side.Output.ShouldContain(bad);
        var normal = await f.AddCommitAsync(f.Base, "side.txt", Text());
        await f.GitAsync("read-tree", f.Base);
        await f.PutAsync(".antiphon/resolution.trx", Text());
        var resolution = await f.CommitIndexAsync([f.Base, normal]);
        var resolved = await f.HistoryAsync(f.Base, resolution);
        resolved.Exit.ShouldBe(1, "c1015-merge-resolution"); resolved.Output.ShouldContain(resolution);
        await f.GitAsync("read-tree", "--empty");
        await f.PutAsync(".antiphon/root.trx", Text());
        var root = await f.CommitIndexAsync([]);
        var rootClean = await f.DeleteCommitAsync(root, ".antiphon/root.trx");
        await f.GitAsync("read-tree", f.Base);
        var unrelatedMerge = await f.CommitIndexAsync([f.Base, rootClean]);
        var rooted = await f.HistoryAsync(f.Base, unrelatedMerge);
        rooted.Exit.ShouldBe(1, "c1015-merged-root"); rooted.Output.ShouldContain(root);
    }

    [Test]
    public async Task Reads_pinned_git_objects_not_index_or_worktree()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var large = await f.AddCommitAsync(f.Base, ".antiphon/report.md", new byte[1048577]);
        Directory.CreateDirectory(Path.Combine(f.Repo, ".antiphon"));
        File.WriteAllText(Path.Combine(f.Repo, ".antiphon/report.md"), "small");
        await f.PutAsync(".antiphon/report.md", Text("small"));
        var entry = (await f.TreeAsync(large)).Single(x => x.Path == ".antiphon/report.md");
        entry.Bytes.ShouldBe(1048577);
        (await f.GitAsync("cat-file", "-s", entry.Oid)).Trim().ShouldBe("1048577");
        (await f.HistoryAsync(f.Base, large)).Exit.ShouldBe(1, "c1015-committed-object");
        var small = await f.AddCommitAsync(f.Base, ".antiphon/report.md", Text());
        File.WriteAllBytes(Path.Combine(f.Repo, ".antiphon/report.md"), new byte[1048577]);
        await f.PutAsync(".antiphon/report.md", new byte[1048577]);
        (await f.HistoryAsync(f.Base, small)).Exit.ShouldBe(0);
    }

    [Test]
    public async Task Handles_literal_paths_and_case_variants()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var canary = Path.Combine(f.Root, "path-executed");
        var shell = ".antiphon/$(New-Item -ItemType File -Path '" + canary.Replace("\\", "/") + "').json";
        foreach (var (path, label) in new[] { (".ANTIPHON/case.json", "root-case"),
            (".antiphon/a b\t\n\u00e9.json", "literal-newline"), (shell, "no-path-execution") })
        {
            var head = await f.AddCommitAsync(f.Base, path, Text("PAYLOAD-C1015-PRIVATE-CANARY"));
            var r = await f.HistoryAsync(f.Base, head);
            File.Exists(canary).ShouldBeFalse("c1015-no-path-execution");
            r.Output.ShouldNotContain("PAYLOAD-C1015-PRIVATE-CANARY", customMessage: "c1015-no-payload");
            switch (label)
            {
                case "root-case": r.Exit.ShouldBe(1, "c1015-root-case"); break;
                case "literal-newline": r.Exit.ShouldBe(1, "c1015-literal-newline"); break;
                case "no-path-execution": r.Exit.ShouldBe(1, "c1015-no-path-execution"); break;
                default: throw new InvalidOperationException("unknown fixture label");
            }
            var line = r.Output.Split('\n').Single(x => x.StartsWith("EVIDENCE violation ", StringComparison.Ordinal));
            var encoded = line[(line.IndexOf("path=", StringComparison.Ordinal) + 5)..line.IndexOf(" bytes=", StringComparison.Ordinal)];
            JsonSerializer.Deserialize<string>(encoded).ShouldBe(path, "c1015-escaped-path");
            if (path.Contains('\n')) encoded.ShouldContain("\\n", customMessage: "c1015-escaped-path");
        }
    }

    [Test]
    public async Task Refuses_unverifiable_ranges_and_objects()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        (await f.HistoryAsync("missing-base", f.Base)).Exit.ShouldBe(2, "c1015-base-unresolved");
        (await f.HistoryAsync(f.Base, "missing-head")).Exit.ShouldBe(2, "c1015-head-unresolved");
        var blob = await f.PutAsync("fixture.md", Text());
        (await f.HistoryAsync(blob, f.Base)).Exit.ShouldBe(2);
        await f.GitAsync("tag", "-a", "blob-tag", "-m", "blob tag", blob);
        (await f.HistoryAsync("blob-tag", f.Base)).Exit.ShouldBe(2);
        await f.GitAsync("read-tree", "--empty");
        var unrelated = await f.CommitIndexAsync([]);
        var refused = await f.HistoryAsync(f.Base, unrelated);
        refused.Exit.ShouldBe(2, "c1015-unrelated"); refused.Output.ShouldNotContain("EVIDENCE result");
        await f.GitAsync("tag", "-a", "fixture-tag", "-m", "tag", f.Base);
        (await f.HistoryAsync("fixture-tag", f.Base)).Exit.ShouldBe(0);
        var head = await f.AddCommitAsync(f.Base, ".antiphon/missing.md", Text(Guid.NewGuid().ToString()));
        var e = (await f.TreeAsync(head)).Single(x => x.Path == ".antiphon/missing.md");
        File.Delete(Path.Combine(f.Repo, ".git", "objects", e.Oid[..2], e.Oid[2..]));
        (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(2);
    }

    [Test]
    public async Task Refuses_failed_or_malformed_git_results()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var head = await f.AddCommitAsync(f.Base, ".antiphon/report.md", Text());
        (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(0);
        foreach (var (condition, alteration, label) in new[] {
            ("$Arguments[0] -eq 'rev-list' -and $Arguments -contains '--reverse'", "$r.ExitCode=1; $r.Bytes=[byte[]]@()", "git-exit"),
            ("$Arguments[0] -eq 'rev-parse' -and $Arguments -contains '--verify'", "$r.ExitCode=1", "git-exit-ref"),
            ("$Arguments[0] -eq 'merge-base'", "$r.ExitCode=1", "git-exit-ancestry"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.ExitCode=1", "git-exit-diff"),
            ("$Arguments[0] -eq 'cat-file'", "$r.ExitCode=1", "git-exit-size"),
            ("$Arguments[0] -eq 'diff-tree'", "$s=$s.Replace(' A'+[char]0,' A extra'+[char]0); $r.Bytes=[Text.Encoding]::UTF8.GetBytes($s)", "raw-fields"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.Bytes=$r.Bytes[0..($r.Bytes.Length-2)]", "raw-terminator"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.Bytes=[Text.Encoding]::UTF8.GetBytes($s.Replace(' A'+[char]0,' Q'+[char]0))", "raw-status"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.Bytes=[Text.Encoding]::UTF8.GetBytes($s.Replace('100644','10064x'))", "raw-mode"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.Bytes=[Text.Encoding]::UTF8.GetBytes($s.Replace('0000000000000000000000000000000000000000','bad'))", "raw-object-id"),
            ("$Arguments[0] -eq 'cat-file'", "$r.Bytes=[Text.Encoding]::UTF8.GetBytes('invalid')", "invalid-size"),
            ("$Arguments[0] -eq 'cat-file'", "$r.Bytes=[byte[]]@()", "invalid-size-empty"),
            ("$Arguments[0] -eq 'diff-tree'", "$r.Bytes[[Array]::IndexOf($r.Bytes,[byte]0)+1]=255", "raw-encoding") })
        {
            var hook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
                "if (" + condition + ") { Write-Host 'FAULT-HIT'; $s=[Text.Encoding]::UTF8.GetString($r.Bytes); " + alteration + " }; return $r }";
            var r = await f.HistoryAsync(f.Base, head, hook);
            r.Output.Split("FAULT-HIT", StringSplitOptions.None).Length.ShouldBe(2, "fault cut hit exactly once");
            switch (label)
            {
                case "git-exit": r.Exit.ShouldBe(2, "c1015-git-exit"); break;
                case "git-exit-ref": r.Exit.ShouldBe(2, "c1015-git-exit-ref"); break;
                case "git-exit-ancestry": r.Exit.ShouldBe(2, "c1015-git-exit-ancestry"); break;
                case "git-exit-diff": r.Exit.ShouldBe(2, "c1015-git-exit-diff"); break;
                case "git-exit-size": r.Exit.ShouldBe(2, "c1015-git-exit-size"); break;
                case "raw-fields": r.Exit.ShouldBe(2, "c1015-raw-fields"); break;
                case "raw-terminator": r.Exit.ShouldBe(2, "c1015-raw-terminator"); break;
                case "raw-status": r.Exit.ShouldBe(2, "c1015-raw-status"); break;
                case "raw-mode": r.Exit.ShouldBe(2, "c1015-raw-mode"); break;
                case "raw-object-id": r.Exit.ShouldBe(2, "c1015-raw-object-id"); break;
                case "invalid-size": r.Exit.ShouldBe(2, "c1015-invalid-size"); break;
                case "invalid-size-empty": r.Exit.ShouldBe(2, "c1015-invalid-size-empty"); break;
                case "raw-encoding": r.Exit.ShouldBe(2, "c1015-raw-encoding"); break;
                default: throw new InvalidOperationException("unknown fixture label");
            }
            r.Output.ShouldNotContain("EVIDENCE result");
        }
    }

    [Test]
    public async Task Resolves_refs_once_and_stays_read_only()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var head = await f.AddCommitAsync(f.Base, ".antiphon/report.md", Text());
        var bad = await f.AddCommitAsync(head, ".antiphon/later.trx", Text());
        await f.GitAsync("update-ref", "refs/heads/moving-head", head);
        await f.GitAsync("update-ref", "refs/heads/moving-base", f.Base);
        File.WriteAllText(Path.Combine(f.Repo, "outside.txt"), "worktree dirty sentinel");
        var before = await f.SnapshotAsync();
        (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(0);
        (await f.SnapshotAsync()).ShouldBe(before, "c1015-read-only");
        (await f.HistoryAsync(f.Base, bad)).Exit.ShouldBe(1);
        (await f.SnapshotAsync()).ShouldBe(before, "c1015-read-only");
        var hook = "function Get-EvidenceGitResult { param($Repository,$Arguments) " +
            "if ($Arguments[0] -notin @('rev-parse','merge-base','rev-list','diff-tree','cat-file')) { throw 'forbidden write' }; " +
            "$r=Invoke-EvidenceNativeGit $Repository $Arguments; if ($Arguments -contains 'moving-head^{commit}') { " +
            "[void](Invoke-EvidenceNativeGit $Repository @('update-ref','refs/heads/moving-head'," + EvidenceGitFixture.Quote(bad) + ")); Write-Host 'FAULT-HIT' }; return $r }";
        var pinned = await f.HistoryAsync("moving-base", "moving-head", hook);
        pinned.Output.Split("FAULT-HIT", StringSplitOptions.None).Length.ShouldBe(2, "head movement cut hit exactly once");
        pinned.Exit.ShouldBe(0, "c1015-pinned-head");
        pinned.Output.ShouldContain("head=" + head, customMessage: "c1015-pinned-head");
        var baseHook = "function Get-EvidenceGitResult { param($Repository,$Arguments) $r=Invoke-EvidenceNativeGit $Repository $Arguments; " +
            "if ($Arguments -contains 'moving-base^{commit}') { [void](Invoke-EvidenceNativeGit $Repository @('update-ref','refs/heads/moving-base'," +
            EvidenceGitFixture.Quote(bad) + ")); Write-Host 'FAULT-HIT' }; return $r }";
        var pinnedBase = await f.HistoryAsync("moving-base", head, baseHook);
        pinnedBase.Output.Split("FAULT-HIT", StringSplitOptions.None).Length.ShouldBe(2);
        pinnedBase.Exit.ShouldBe(0, "c1015-pinned-base");
        pinnedBase.Output.ShouldContain("base=" + f.Base + " head=" + head);
    }

    [Test]
    public async Task Ci_selects_cumulative_feature_push_range()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        await f.GitAsync("update-ref", "refs/remotes/origin/master", f.Base);
        var bad = await f.AddCommitAsync(f.Base, ".antiphon/earlier.trx", Text());
        var head = await f.DeleteCommitAsync(bad, ".antiphon/earlier.trx");
        foreach (var before in new[] { new string('0', 40), bad })
        {
            var r = await f.EventAsync("push", EvidenceGitFixture.Push(head, before));
            r.Exit.ShouldBe(1, "c1015-feature-cumulative");
            r.Output.ShouldContain("base=" + f.Base + " head=" + head);
        }
        var advanced = await f.AddCommitAsync(f.Base, "default.txt", Text());
        await f.GitAsync("update-ref", "refs/remotes/origin/master", advanced);
        var advance = await f.EventAsync("push", EvidenceGitFixture.Push(head, bad));
        advance.Exit.ShouldBe(1); advance.Output.ShouldContain("base=" + f.Base + " head=" + head);
    }

    [Test]
    public async Task Ci_selects_complete_default_branch_push_range()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var bad = await f.AddCommitAsync(f.Base, ".antiphon/earlier.trx", Text());
        var head = await f.DeleteCommitAsync(bad, ".antiphon/earlier.trx");
        var r = await f.EventAsync("push", EvidenceGitFixture.Push(head, f.Base, "master"));
        r.Exit.ShouldBe(1, "c1015-default-whole-push");
        r.Output.ShouldContain("head=" + head, customMessage: "c1015-event-head");
        (await f.GitAsync("rev-parse", "HEAD")).Trim().ShouldBe(f.Base);
        r.Output.ShouldContain("base=" + f.Base + " head=" + head);
    }

    [Test]
    public async Task Ci_requires_valid_manual_range()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var head = await f.AddCommitAsync(f.Base, ".antiphon/report.md", Text());
        var valid = await f.EventAsync("workflow_dispatch", new { }, f.Base, head);
        valid.Exit.ShouldBe(0); valid.Output.ShouldContain("base=" + f.Base + " head=" + head);
        foreach (var value in new[] { "", " ", "missing" })
            (await f.EventAsync("workflow_dispatch", new { }, value, head)).Exit.ShouldBe(2, "c1015-manual-base-required");
        await f.GitAsync("tag", "-a", "manual-head", "-m", "tag", head);
        (await f.EventAsync("workflow_dispatch", new { }, f.Base, "manual-head")).Exit.ShouldBe(0);
    }

    [Test]
    public async Task Ci_refuses_invalid_events_and_missing_history()
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        (await f.EventAsync("unknown", new { })).Exit.ShouldBe(2, "c1015-unknown-event");
        (await f.EventAsync("push", new { after = new[] { f.Base } })).Exit.ShouldBe(2, "c1015-event-shape");
        (await f.EventAsync("push", EvidenceGitFixture.Push(f.Base, f.Base))).Exit.ShouldBe(2, "c1015-remote-base-missing");
        (await f.EventAsync("push", EvidenceGitFixture.Push(f.Base, new string('0', 40), "master"))).Exit.ShouldBe(2, "c1015-default-zero");
        (await f.EventAsync("push", EvidenceGitFixture.Push(f.Base, f.Base, deleted: "false"))).Exit.ShouldBe(2, "c1015-deleted-type");
        (await f.EventAsync("push", EvidenceGitFixture.Push(new string('0', 40), f.Base, deleted: true))).Exit.ShouldBe(0);
        var malformed = Path.Combine(f.Root, "malformed.json"); File.WriteAllText(malformed, "{");
        (await f.GuardAsync("check-evidence-diff.ps1", ["-EventPath", malformed, "-EventName", "push"])).Exit.ShouldBe(2);
        var head = await f.AddCommitAsync(f.Base, "normal.txt", Text());
        File.WriteAllText(Path.Combine(f.Repo, ".git", "shallow"), f.Base + "\n");
        (await f.HistoryAsync(f.Base, head)).Exit.ShouldBe(2, "c1015-shallow-history");
    }
}
