using System.Diagnostics;
using System.IO.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1753. A landing batch whose own build-definition records or target pins drifted
/// refreshes the land-graph files, notifies, and never asks. Property and construct refusals,
/// a malformed manifest, a refresh outside the batch's files, and a check that still fails
/// after the refresh stay hard failures. A bare -Update outside that path stays forbidden.
/// The land-graph-land-refresh command (LandGraphLandRefresh; it replaced
/// scripts/land-graph-land-refresh.ps1, unit 11 of 2026-11-22-remaining-ps1-to-csharp.md) runs
/// in process with real git; the stub cases answer its audit and pin printer with
/// LandGraphRefreshSteps (the former -AuditScript and -PinScript stubs), the real cases run the
/// land-graph-audit command and the staged tool.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandGraphLandRefreshTests
{
    private const string Manifest = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";
    private const string Evaluation = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";
    private const string PinsFile = "server/Application/Services/LandVerificationBuildGraph.pins.txt";
    private const string CommitSubject = "chore: refresh land-graph records for the landing batch";

    [Test]
    [Timeout(60_000)]
    public void changed_csproj_refreshes_commits_only_land_graph_files_and_notifies()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        var seeded = fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var note = Path.Combine(fixture.Root, "note.txt");
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins, notePath: note);

        result.Code.ShouldBe(0);
        AssertRefreshNotice(result.Text, seeded, fixture, "App/App.csproj");
        result.Text.ShouldContain("card-note:");
        File.ReadAllText(note).ShouldContain("card-note:");
        File.ReadAllText(note).ShouldContain(seeded.Digest);
        AssertNoQuestion(result.Text);
        AssertLandGraphCommit(fixture, seeded.Digest);
        fixture.UpdateCount().ShouldBe(1);
        fixture.LogText().ShouldContain("update landrefresh=True");
        Notice(result.Text)["old-pins"].ShouldBe(Notice(result.Text)["new-pins"]);
    }

    [Test]
    [Timeout(60_000)]
    public void new_project_refreshes_and_reports_the_new_count()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        var seeded = fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("Probe/Probe.csproj", Project("net9.0"));
        fixture.Commit("batch");
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins);

        result.Code.ShouldBe(0);
        var notice = AssertRefreshNotice(result.Text, seeded, fixture, "Probe/Probe.csproj");
        notice["old-count"].ShouldBe(seeded.Count.ToString());
        notice["new-count"].ShouldBe((seeded.Count + 1).ToString());
        int.Parse(notice["new-count"]).ShouldBeGreaterThan(int.Parse(notice["old-count"]));
        AssertNoQuestion(result.Text);
        fixture.UpdateCount().ShouldBe(1);
    }

    [Test]
    [Timeout(60_000)]
    public void changed_pin_on_a_batch_file_is_rewritten_and_committed()
    {
        using var fixture = new Fixture();
        var oldHash = new string('A', 64);
        var otherHash = new string('C', 64);
        var newHash = new string('B', 64);
        var pins = PinLine("App/App.csproj", "Build", oldHash) + PinLine("Other/Other.csproj", "Build", otherHash);
        var seeded = fixture.Seed(pins,
            ("App/App.csproj", Project("net9.0")),
            ("Other/Other.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var printed = PinLine("App/App.csproj", "Build", newHash) + PinLine("Other/Other.csproj", "Build", otherHash);
        var result = fixture.Run(Check("refused: App/App.csproj Target Build (changed pin)"), printed);

        result.Code.ShouldBe(0);
        AssertRefreshNotice(result.Text, seeded, fixture, "App/App.csproj");
        var committed = fixture.Show("HEAD:" + PinsFile);
        committed.ShouldContain(newHash);
        committed.ShouldContain(otherHash);
        committed.ShouldNotContain(oldHash);
        Notice(result.Text)["old-pins"].ShouldNotBe(Notice(result.Text)["new-pins"]);
        fixture.UpdateCount().ShouldBe(1);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void property_or_construct_refusal_does_not_refresh()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var result = fixture.Run(Check(
            "refused: App/App.csproj property _PtyHostArtifactsArguments -Update cannot approve it: manifest does not match the selected graph"),
            pins);

        result.Code.ShouldBe(1);
        result.Text.ShouldContain("action=refuse reason=property-or-construct");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void malformed_manifest_does_not_refresh()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var result = fixture.Run(Check(
            "manifest digest is not 64 uppercase hex characters; manifest does not match the selected graph"),
            pins);

        result.Code.ShouldBe(1);
        result.Text.ShouldContain("action=refuse reason=malformed-manifest");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void inherited_mismatch_mixed_with_a_batch_change_is_outside_batch()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write(Manifest, new string('F', 64) + "\n1\n");
        fixture.Commit("wrong-manifest");
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins);

        result.Code.ShouldBe(1);
        result.Text.ShouldContain("action=refuse reason=outside-batch");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void pin_change_outside_the_batch_files_is_outside_batch()
    {
        using var fixture = new Fixture();
        var appHash = new string('A', 64);
        var otherHash = new string('C', 64);
        var pins = PinLine("App/App.csproj", "Build", appHash) + PinLine("Other/Other.csproj", "Build", otherHash);
        fixture.Seed(pins,
            ("App/App.csproj", Project("net9.0")),
            ("Other/Other.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var printed = PinLine("App/App.csproj", "Build", appHash) + PinLine("Other/Other.csproj", "Build", new string('D', 64));
        var result = fixture.Run(Check("refused: Other/Other.csproj Target Build (changed pin)"), printed);

        result.Code.ShouldBe(1);
        result.Text.ShouldContain("action=refuse reason=outside-batch");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
        fixture.Show("HEAD:" + PinsFile).ShouldContain(otherHash);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void check_still_failing_after_refresh_fails_the_land()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var result = fixture.Run(Check("manifest does not match the selected graph", secondCode: 1,
            secondText: "manifest does not match the selected graph"), pins);

        result.Code.ShouldBe(1);
        result.Text.ShouldContain("action=refuse reason=check-after-refresh");
        fixture.UpdateCount().ShouldBe(1);
        fixture.Head().ShouldNotBe(batch);
        AssertLandGraphCommit(fixture, beforeDigest: null);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void inherited_only_mismatch_warns_and_does_not_refresh()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write(Manifest, new string('F', 64) + "\n1\n");
        fixture.Commit("wrong-manifest");
        var batch = fixture.Head();
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins);

        result.Code.ShouldBe(0);
        result.Text.ShouldContain("action=warn reason=inherited-mismatch");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void current_graph_does_not_refresh()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        var batch = fixture.Head();
        var result = fixture.Run(Check("ok", firstCode: 0), pins);

        result.Code.ShouldBe(0);
        result.Text.ShouldContain("action=current");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
    }

    [Test]
    [Timeout(60_000)]
    public void failed_card_note_does_not_block_the_refresh()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins, card: "CARD-1753");

        result.Code.ShouldBe(0);
        result.Text.ShouldContain("card-note:");
        result.Text.ShouldContain("card-note: post-failed");
        fixture.UpdateCount().ShouldBe(1);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void card_note_posts_the_notice_with_the_task_token()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var posts = new List<(string Method, string Uri, IReadOnlyDictionary<string, string> Headers, string Body)>();
        var result = fixture.Run(Check("manifest does not match the selected graph"), pins, card: "CARD-1753", token: "task-token",
            board: (method, uri, headers, body) =>
            {
                posts.Add((method, uri, headers, body));
                return new NightlyReportHttpResponse(201, "{}");
            });

        result.Code.ShouldBe(0, result.Text);
        result.Text.ShouldNotContain("post-failed");
        posts.Count.ShouldBe(1);
        posts[0].Method.ShouldBe("POST");
        posts[0].Uri.ShouldBe("http://127.0.0.1:1/api/cards/CARD-1753/discussion");
        posts[0].Headers["X-Antiphon-Task-Token"].ShouldBe("task-token");
        using var body = JsonDocument.Parse(posts[0].Body);
        var note = body.RootElement.GetProperty("body").GetString()!;
        note.ShouldStartWith("LAND-GRAPH-NOTICE action=refresh\n");
        note.ShouldContain("changed-records:App/App.csproj\n");
        note.ShouldEndWith("card-note: recorded\n");
        LandGraphLandRefresh.JsonString("a\"b\\c\nd\re\tf\u0001g").ShouldBe("\"a\\\"b\\\\c\\nd\\re\\tf g\"");
    }

    [Test]
    [Timeout(60_000)]
    public void failed_update_restores_the_staged_pins_and_does_not_commit()
    {
        using var fixture = new Fixture();
        var oldHash = new string('A', 64);
        var pins = PinLine("App/App.csproj", "Build", oldHash);
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();
        var before = File.ReadAllBytes(Path.Combine(fixture.Repo, PinsFile.Replace('/', Path.DirectorySeparatorChar)));
        var result = fixture.Run(Check("refused: App/App.csproj Target Build (changed pin)"),
            PinLine("App/App.csproj", "Build", new string('B', 64)), updateCode: 1);

        result.Code.ShouldBe(1, result.Text);
        result.Text.ShouldContain("action=refuse reason=update-failed");
        fixture.UpdateCount().ShouldBe(1);
        fixture.Head().ShouldBe(batch);
        File.ReadAllBytes(Path.Combine(fixture.Repo, PinsFile.Replace('/', Path.DirectorySeparatorChar))).ShouldBe(before);
        AssertNoQuestion(result.Text);
    }

    [Test]
    [Timeout(60_000)]
    public void unknown_base_and_dirty_worktree_refuse_before_any_update()
    {
        using var fixture = new Fixture();
        var pins = PinLine("App/App.csproj", "Build", new string('A', 64));
        fixture.Seed(pins, ("App/App.csproj", Project("net9.0")));
        fixture.Write("App/App.csproj", Project("net10.0"));
        fixture.Commit("batch");
        var batch = fixture.Head();

        var unknown = fixture.Run(Check("manifest does not match the selected graph"), pins, baseSha: new string('0', 40));
        unknown.Code.ShouldBe(1, unknown.Text);
        unknown.Text.ShouldContain("action=refuse reason=bad-base");
        fixture.LogText().ShouldBeEmpty("a bad base refuses before the first check");

        File.WriteAllText(Path.Combine(fixture.Repo, "untracked.txt"), "x");
        var dirty = fixture.Run(Check("manifest does not match the selected graph"), pins);
        dirty.Code.ShouldBe(1, dirty.Text);
        dirty.Text.ShouldContain("action=refuse reason=dirty-worktree");
        fixture.UpdateCount().ShouldBe(0);
        fixture.Head().ShouldBe(batch);
    }

    [Test]
    public void command_line_needs_a_base_and_reads_the_card_note_settings_from_the_caller()
    {
        var options = LandGraphLandRefreshOptions.Parse(["--base", "abc", "--card", "CARD-1", "--note-path", "n.txt"], "repo", "http://board", "token", null);
        options.Base.ShouldBe("abc");
        options.RepoRoot.ShouldBe("repo");
        options.Card.ShouldBe("CARD-1");
        options.NotePath.ShouldBe("n.txt");
        options.Api.ShouldBe("http://board");
        options.TaskToken.ShouldBe("token");
        LandGraphLandRefreshOptions.Parse(["--base", "abc", "--repo", "elsewhere"], "repo", null!, null!, null).RepoRoot.ShouldBe("elsewhere");
        Should.Throw<NightlyUsageException>(() => LandGraphLandRefreshOptions.Parse([], "repo", "", "", null)).Message.ShouldBe("missing --base");
        Should.Throw<NightlyUsageException>(() => LandGraphLandRefreshOptions.Parse(["--base", "abc", "--audit-script", "x"], "repo", "", "", null));
        Should.Throw<NightlyUsageException>(() => LandGraphLandRefreshOptions.Parse(["-Base", "abc"], "repo", "", "", null));
    }

    [Test]
    public void instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden()
    {
        const string forbidden = "A bare `-Update` outside that land path stays forbidden.";
        var root = DelegateScriptRunner.RepoRoot;
        var loop = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var ruleAt = loop.IndexOf("11. **Land-graph", StringComparison.Ordinal);
        ruleAt.ShouldBeGreaterThanOrEqualTo(0);
        var ruleEnd = loop.IndexOf("\n#### ", ruleAt, StringComparison.Ordinal);
        var rule = loop[ruleAt..ruleEnd];
        rule.ShouldNotContain("nobody else runs it");
        rule.ShouldNotContain("operator/audit-only");
        rule.ShouldNotContain("stops on this digest");
        rule.ShouldContain("land-graph-land-refresh --base");
        rule.ShouldNotContain("land-graph-land-refresh.ps1");
        rule.ShouldContain("never asks");
        rule.ShouldContain(forbidden);
        rule.ShouldContain("policyHash");
        rule.ShouldContain("live-lane-input-digest");
        rule.ShouldContain("_PtyHostArtifactsArguments");
        rule.ShouldContain("malformed manifest");
        Count(loop, "<a id=\"land-graph-refresh\"></a>").ShouldBe(1);

        var author = loop.IndexOf("Author-time `-Update` requires", StringComparison.Ordinal);
        var runtime = loop.IndexOf("adds zero land-time runtime work", author, StringComparison.Ordinal);
        var authorWindow = loop[author..runtime];
        authorWindow.ShouldContain("land-graph-land-refresh");
        authorWindow.ShouldNotContain("land-graph-land-refresh.ps1");
        authorWindow.ShouldContain(forbidden);
        loop.ShouldContain("adds zero land-time runtime work: land still compares the embedded manifest only.");

        foreach (var relative in new[]
        {
            "AGENTS.md",
            ".claude/skills/antiphon-rc-qualification/SKILL.md",
            ".claude/skills/antiphon-orchestrator/SKILL.md",
        })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            text.ShouldContain("docs/orchestration-loop.md#land-graph-refresh", Case.Sensitive, relative);
            text.ShouldContain(forbidden, Case.Sensitive, relative);
        }

        var bundle = File.ReadAllText(Path.Combine(root, "server", "Bundles", "orchestrator.md"));
        bundle.ShouldContain("#land-graph-refresh");
        bundle.ShouldContain("never asks");
        bundle.ShouldContain("Own land-graph drift refreshes, never asks: #land-graph-refresh.");
        bundle.ShouldContain("one red witness plus one narrow green");
        bundle.ShouldContain("a master land is not activation");
        bundle.ShouldContain("reviewed rc picks");
        bundle.ShouldContain("docs/orchestration-loop.md#standing-operating-rules");
        bundle.ShouldContain("never Reply to a complete report Blocked only by runner sync");
        bundle.ShouldContain("full report");

        var skill = File.ReadAllText(Path.Combine(root, ".claude", "skills", "antiphon-rc-qualification", "SKILL.md"));
        skill.ShouldContain("Fixers never run native tests, tags, or land graph `-Update`.");
        skill.ShouldNotContain("may use the documented `-Update` once");
        skill.ShouldContain("land-graph-land-refresh --base");
        File.Exists(Path.Combine(root, "scripts", "land-graph-land-refresh.ps1")).ShouldBeFalse("the refresh is the land-graph-land-refresh command");

        // The command (scripts/lib/Antiphon.Nightly) and its CLI entry never ask, and name every notice.
        var script = File.ReadAllText(Path.Combine(root, "scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs"));
        var entry = File.ReadAllText(Path.Combine(root, "scripts/lib/Antiphon.Scripts/LandGraphLandRefreshCommand.cs"));
        script.All(static c => c <= 127).ShouldBeTrue("LandGraphLandRefresh.cs must stay ASCII");
        entry.All(static c => c <= 127).ShouldBeTrue("LandGraphLandRefreshCommand.cs must stay ASCII");
        foreach (var source in new[] { script, entry })
        {
            source.ShouldNotContain("Read-Host");
            source.ShouldNotContain("ReadLine");
            source.ShouldNotContain("Console.In");
        }
        script.ShouldContain("LAND-GRAPH-NOTICE");
        script.ShouldContain("Refuse(\"property-or-construct\")");
        script.ShouldContain("Refuse(\"malformed-manifest\")");
        script.ShouldContain("Refuse(\"outside-batch\")");
        script.ShouldContain("Refuse(\"check-after-refresh\")");
        script.ShouldContain("reason=inherited-mismatch");

        var audit = File.ReadAllText(Path.Combine(root, "scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs"));
        audit.ShouldContain("--land-refresh");
        audit.ShouldContain("only for the land-graph-land-refresh command");
        audit.ShouldContain("commit source and pins before auditing");
        // The pending pins a land refresh stages are hashed from the working tree (the script's body is C# now).
        var gate = File.ReadAllText(Path.Combine(root, "tools", "Antiphon.LandGraphAudit.Core", "LandGraphAuditGate.cs"));
        gate.ShouldContain("private string PinsPath => PsPath.Join(_repoRoot, PinsRelative);");
        gate.ShouldContain("? GetGitValue(\"hash-object\", PinsPath)");
    }

    /// <summary>
    /// Windows git writes loose objects read-only, and <c>Directory.Delete</c> throws
    /// <see cref="UnauthorizedAccessException"/> on them. Linux unlinks a read-only file
    /// from a writable directory, so the witness is that dispose calls
    /// <see cref="StartRefGit.DeleteDirectory"/> and that call removes the object.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public void dispose_removes_a_readonly_git_object_through_startrefgit()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1753-ro-" + Guid.NewGuid().ToString("N"));
        var loose = Path.Combine(root, "repo", ".git", "objects", "47", "a139e2691d0b91fe8cfc80f190e885a30de8");
        Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
        File.WriteAllText(loose, "loose");
        File.SetAttributes(loose, FileAttributes.ReadOnly);
        try
        {
            var delete = Fixture.DeleteRoot;
            delete.Method.DeclaringType.ShouldBe(typeof(StartRefGit));
            delete.Method.Name.ShouldBe(nameof(StartRefGit.DeleteDirectory));
            using (Fixture.Adopt(root))
            {
            }

            Directory.Exists(root).ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(loose))
                File.SetAttributes(loose, FileAttributes.Normal);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The production audit, not the stub. A changed definition in a throwaway repo must refresh,
    /// commit only the land-graph files, and leave the second -Check at exit 0.
    /// </summary>
    [Test]
    [Timeout(600_000)]
    public async Task real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero()
    {
        await using var repo = await LandGraphEvaluationTests.ScriptRepoAsync(new()
        {
            ["App/App.csproj"] = RealSdkProject(""),
        });
        await ConfigureGit(repo.Root);
        var update = await LandGraphEvaluationTests.RunScriptAsync(repo.Root, "-Update");
        update.ExitCode.ShouldBe(0, update.Text);
        await Git(repo.Root, "add", "-A");
        await Git(repo.Root, "commit", "-m", "records");
        var baseSha = (await GitText(repo.Root, "rev-parse", "HEAD")).Trim();

        var project = repo.Full("App/App.csproj");
        var body = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project, body.Replace("</Project>", "<!-- batch -->\n</Project>", StringComparison.Ordinal));
        await Git(repo.Root, "add", "--", "App/App.csproj");
        await Git(repo.Root, "commit", "-m", "batch");
        var batch = (await GitText(repo.Root, "rev-parse", "HEAD")).Trim();

        var result = await RunRealRefresh(repo.Root, baseSha, TimeSpan.FromSeconds(540));
        result.Code.ShouldBe(0, result.Text);
        result.Text.ShouldContain("action=refresh");
        result.Text.ShouldNotContain("action=refuse");

        var names = (await GitText(repo.Root, "diff", "--name-only", batch, "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.Length.ShouldBeGreaterThan(0, result.Text);
        foreach (var name in names)
            (name is Manifest or Evaluation or PinsFile).ShouldBeTrue(name + " " + result.Text);
        names.ShouldContain(Manifest);
        names.ShouldContain(Evaluation);
        (await GitText(repo.Root, "status", "--porcelain")).Trim().ShouldBeEmpty();
        (await GitText(repo.Root, "log", "-1", "--format=%s")).Trim().ShouldBe(CommitSubject);

        var evaluation = await File.ReadAllLinesAsync(repo.Full(Evaluation));
        evaluation.Length.ShouldBe(3);
        var pinsHash = (await GitText(repo.Root, "hash-object", "--", PinsFile)).Trim();
        evaluation[2].ShouldBe(pinsHash, result.Text);
    }

    /// <summary>
    /// The real pre-check refuses a property the audit cannot approve. The refresh classifies that
    /// phrase as property-or-construct and does not echo it or commit.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task real_audit_property_refusal_does_not_refresh()
    {
        await using var repo = await LandGraphEvaluationTests.ScriptRepoAsync(new()
        {
            ["App/App.csproj"] = RealSdkProject("<CustomAfterMicrosoftCommonTargets>../docs/evil.md</CustomAfterMicrosoftCommonTargets>"),
        });
        await ConfigureGit(repo.Root);
        var head = (await GitText(repo.Root, "rev-parse", "HEAD")).Trim();
        var check = await LandGraphEvaluationTests.RunScriptAsync(repo.Root, "-Check");
        check.ExitCode.ShouldNotBe(0, check.Text);
        check.Text.ShouldContain("-Update cannot approve it");
        check.Text.ShouldContain("property the audit does not allow");
        var result = await RunRealRefresh(repo.Root, head, TimeSpan.FromSeconds(120));
        result.Code.ShouldBe(1, result.Text);
        result.Text.ShouldContain("action=refuse reason=property-or-construct");
        result.Text.ShouldNotContain("action=refresh");
        (await GitText(repo.Root, "rev-parse", "HEAD")).Trim().ShouldBe(head);
    }

    private static Dictionary<string, string> AssertRefreshNotice(
        string output, (string Digest, int Count) seeded, Fixture fixture, string changedPath)
    {
        var notice = Notice(output);
        notice["action"].ShouldBe("refresh");
        notice["old-digest"].ShouldBe(seeded.Digest);
        notice["old-count"].ShouldBe(seeded.Count.ToString());
        var expected = Fingerprint(Selected(fixture.Repo, "HEAD"));
        notice["new-digest"].ShouldBe(expected.Digest);
        notice["new-count"].ShouldBe(expected.Count.ToString());
        notice["old-digest"].Length.ShouldBe(64);
        notice["new-digest"].Length.ShouldBe(64);
        output.ShouldContain("changed-records:" + changedPath);
        output.ShouldContain("card-note:");
        notice["old-pins"].ShouldNotBeNullOrWhiteSpace();
        notice["new-pins"].ShouldNotBeNullOrWhiteSpace();
        return notice;
    }

    private static void AssertLandGraphCommit(Fixture fixture, string? beforeDigest)
    {
        fixture.Out("log", "-1", "--format=%s").Trim().ShouldBe(CommitSubject);
        var names = fixture.Out("show", "--name-only", "--pretty=format:", "HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.Length.ShouldBeGreaterThan(0);
        foreach (var name in names)
            (name is Manifest or Evaluation or PinsFile).ShouldBeTrue(name);
        names.ShouldContain(Manifest);
        if (beforeDigest is not null)
            fixture.Show("HEAD:" + Manifest).ShouldContain(Fingerprint(Selected(fixture.Repo, "HEAD")).Digest);
    }

    private static void AssertNoQuestion(string output)
    {
        output.ShouldNotContain("Read-Host");
        output.ShouldNotContain("stop and ask");
        output.ShouldNotContain("operator approval");
        output.ShouldNotContain("approve?");
        output.ShouldNotContain("Should I");
    }

    private static Dictionary<string, string> Notice(string output)
    {
        var notice = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("LAND-GRAPH-NOTICE ", StringComparison.Ordinal))
            {
                foreach (var part in line["LAND-GRAPH-NOTICE ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    eq.ShouldBeGreaterThan(0, line);
                    notice[part[..eq]] = part[(eq + 1)..];
                }
            }
            else if (line.StartsWith("changed-records:", StringComparison.Ordinal))
            {
                notice["changed"] = line["changed-records:".Length..];
            }
            else
            {
                var eq = line.IndexOf('=');
                if (eq > 0 && line[..eq] is "old-digest" or "new-digest" or "old-count" or "new-count" or "old-pins" or "new-pins")
                    notice[line[..eq]] = line[(eq + 1)..];
            }
        }
        return notice;
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var from = 0;
        while ((from = text.IndexOf(value, from, StringComparison.Ordinal)) >= 0)
        {
            count++;
            from += value.Length;
        }
        return count;
    }

    private static string Project(string framework) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup><TargetFramework>" + framework + "</TargetFramework></PropertyGroup>\n</Project>\n";

    private static string RealSdkProject(string body) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net9.0</TargetFramework>\n    "
        + body + "\n  </PropertyGroup>\n</Project>\n";

    private static async Task ConfigureGit(string root)
    {
        await Git(root, "config", "user.email", "land-graph-test@example.com");
        await Git(root, "config", "user.name", "land-graph-test");
        await Git(root, "config", "commit.gpgsign", "false");
        await Git(root, "config", "core.autocrlf", "false");
    }

    // The land-graph-land-refresh command in process with its production steps: the land-graph-audit command (the staged
    // tool, which the fixture has no project to rebuild) and the tool's --print-pins, real git, no card.
    private static async Task<(int Code, string Text)> RunRealRefresh(string root, string baseSha, TimeSpan timeout)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var options = LandGraphLandRefreshOptions.Parse(["--base", baseSha, "--repo", root], root, "", "", LandGraphCommandHost.Environment());
                var code = LandGraphLandRefresh.Run(options, LandGraphCommandHost.Create(output, error), new FileSystem(), UnreachableBoard);
                return (code, output.ToString() + error.ToString());
            }).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("real land-graph refresh exceeded " + timeout.TotalSeconds.ToString("0") + " s");
        }
    }

    // The board at 127.0.0.1:1: every post fails to connect.
    private static NightlyReportHttpResponse UnreachableBoard(string method, string uri, IReadOnlyDictionary<string, string> headers, string body) =>
        throw new HttpRequestException("connection refused: " + uri);

    private static Task Git(string root, params string[] args) => GitText(root, args);

    private static async Task<string> GitText(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var outText = await stdout;
        var errText = await stderr;
        process.ExitCode.ShouldBe(0, "git " + string.Join(' ', args) + ": " + errText + outText);
        return outText;
    }

    private static string PinLine(string path, string selector, string hash) => path + "\t" + selector + "\t" + hash + "\n";

    private static (int Code, string? Text, string? SecondText) Check(string text, int firstCode = 1, int secondCode = 0, string? secondText = "ok") =>
        (firstCode, text, secondCode == 0 && secondText == "ok" ? "ok" : secondText);

    private static (string Digest, int Count) Fingerprint(string[] rows)
    {
        var payload = string.Join('\0', rows) + "\0";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return (digest, rows.Length);
    }

    private static string[] Selected(string repo, string tree)
    {
        var bytes = GitBytes(repo, "ls-tree", "-r", "-z", tree);
        (bytes.Length > 0 && bytes[^1] == 0).ShouldBeTrue("ls-tree trailing NUL");
        var parts = Encoding.UTF8.GetString(bytes).Split('\0');
        var rows = new List<string>();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var row = parts[i];
            var tab = row.IndexOf('\t');
            tab.ShouldBeGreaterThan(0);
            if (IsDefinition(row[(tab + 1)..])) rows.Add(row);
        }
        var ordered = rows.ToArray();
        Array.Sort(ordered, StringComparer.Ordinal);
        return ordered;
    }

    private static bool IsDefinition(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension.EndsWith("proj", StringComparison.Ordinal)
            || extension is ".props" or ".targets" or ".sln" or ".slnx" or ".slnf" or ".rsp"
            || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("proj.user", StringComparison.OrdinalIgnoreCase)
            || name.ToLowerInvariant() is "global.json" or "nuget.config" or "packages.lock.json"
                or "packages.config" or "directory.packages.props";
    }

    private static byte[] GitBytes(string repo, params string[] args)
    {
        var start = GitStart(repo, args);
        start.RedirectStandardOutput = true;
        using var process = Process.Start(start)!;
        using var memory = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(memory);
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, stderr.GetAwaiter().GetResult());
        return memory.ToArray();
    }

    private static ProcessStartInfo GitStart(string repo, string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Repo { get; }
        public string BaseSha { get; private set; } = "";
        private readonly string _log;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "c1753-" + Guid.NewGuid().ToString("N"));
            Repo = Path.Combine(Root, "repo");
            Directory.CreateDirectory(Repo);
            _log = Path.Combine(Root, "audit.log");
            Git("init", "-b", "main");
            Git("config", "user.email", "land-graph-test@example.com");
            Git("config", "user.name", "land-graph-test");
            Git("config", "core.autocrlf", "false");
            Git("config", "commit.gpgsign", "false");
        }

        public (string Digest, int Count) Seed(string pinsBody, params (string Path, string Content)[] files)
        {
            foreach (var file in files) Write(file.Path, file.Content);
            Write(PinsFile, pinsBody);
            Commit("sources");
            var selected = Fingerprint(Selected(Repo, "HEAD"));
            Write(Manifest, selected.Digest + "\n" + selected.Count.ToString() + "\n");
            var pinsBlob = Out("hash-object", Path.Combine(Repo, PinsFile.Replace('/', Path.DirectorySeparatorChar))).Trim();
            Write(Evaluation, selected.Digest + "\n9.0.100\n" + pinsBlob + "\n");
            Commit("records");
            BaseSha = Head();
            return selected;
        }

        // The land-graph-land-refresh command in process on this fixture, its audit and pin printer answered as the former
        // -AuditScript and -PinScript stubs did: the first --check answers check, every later one the second answer; an
        // --update logs whether it came with --land-refresh and then writes the new records (fresh bytes, so git's stat
        // check sees them; the real --update writes them too) unless updateCode fails it; the printer prints printedPins.
        // The board defaults to http://127.0.0.1:1, which no post reaches.
        public (int Code, string Text) Run(
            (int Code, string? Text, string? SecondText) check,
            string printedPins,
            string? notePath = null,
            string? card = null,
            int updateCode = 0,
            string? baseSha = null,
            string token = "",
            NightlyReportHttp? board = null)
        {
            var head = Fingerprint(Selected(Repo, "HEAD"));
            var utf8 = new UTF8Encoding(false);
            var newManifest = head.Digest + "\n" + head.Count.ToString() + "\n";
            var pinCopy = Path.Combine(Root, "printed-pins.txt");
            File.WriteAllText(pinCopy, printedPins, utf8);
            var pinBlob = Out("hash-object", pinCopy).Trim();
            var newEvaluation = head.Digest + "\n9.0.100\n" + pinBlob + "\n";
            var secondFails = check.SecondText is not null && check.SecondText != "ok";
            var checks = 0;
            var steps = new LandGraphRefreshSteps
            {
                Audit = options =>
                {
                    options.RepoRoot.ShouldBe(Path.GetFullPath(Repo));
                    options.CaptureOutput.ShouldBeTrue();
                    options.TimeoutMilliseconds.ShouldBe(LandGraphLandRefresh.AuditTimeoutMilliseconds);
                    options.Check.ShouldBe(!options.Update);
                    if (options.Update)
                    {
                        File.AppendAllText(_log, "update landrefresh=" + (options.LandRefresh ? "True" : "False") + "\n");
                        if (updateCode != 0) return new LandGraphStep(updateCode, "");
                        File.WriteAllText(Path.Combine(Repo, Manifest.Replace('/', Path.DirectorySeparatorChar)), newManifest, utf8);
                        File.WriteAllText(Path.Combine(Repo, Evaluation.Replace('/', Path.DirectorySeparatorChar)), newEvaluation, utf8);
                        return new LandGraphStep(0, "");
                    }
                    options.LandRefresh.ShouldBeFalse();
                    File.AppendAllText(_log, "check\n");
                    return ++checks == 1
                        ? new LandGraphStep(check.Code, check.Text ?? "")
                        : new LandGraphStep(secondFails ? 1 : 0, check.SecondText ?? "");
                },
                PrintPins = repo =>
                {
                    repo.ShouldBe(Path.GetFullPath(Repo));
                    return new LandGraphStep(0, printedPins);
                },
            };
            var arguments = new List<string> { "--base", baseSha ?? BaseSha, "--repo", Repo };
            if (notePath is not null) arguments.AddRange(["--note-path", notePath]);
            if (card is not null) arguments.AddRange(["--card", card]);
            var refresh = LandGraphLandRefreshOptions.Parse(arguments, Repo, "http://127.0.0.1:1", token, LandGraphCommandHost.Environment());
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = LandGraphLandRefresh.Run(refresh, LandGraphCommandHost.Create(output, error), new FileSystem(), board ?? new NightlyReportHttp(UnreachableBoard), steps);
            return (code, output.ToString() + error.ToString());
        }

        public void Write(string relative, string content)
        {
            var path = Path.Combine(Repo, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        public void Commit(string message)
        {
            Git("add", "-A");
            Git("commit", "-m", message);
        }

        public string Head() => Out("rev-parse", "HEAD").Trim();

        public string Show(string spec) => Out("show", spec);

        public string Out(params string[] args) => GitText(args, expectZero: true);

        public int UpdateCount()
        {
            if (!File.Exists(_log)) return 0;
            return File.ReadAllLines(_log).Count(line => line.StartsWith("update ", StringComparison.Ordinal));
        }

        public string LogText() => File.Exists(_log) ? File.ReadAllText(_log) : "";

        // Directory.Delete throws on Windows when a loose object is read-only.
        // AuditRepo already clears that bit through StartRefGit.DeleteDirectory.
        internal static Action<string> DeleteRoot { get; set; } = StartRefGit.DeleteDirectory;

        internal static Fixture Adopt(string root) => new(root);

        public void Dispose() => DeleteRoot(Root);

        private Fixture(string existingRoot)
        {
            Root = existingRoot;
            Repo = Path.Combine(existingRoot, "repo");
            _log = Path.Combine(existingRoot, "audit.log");
        }

        private void Git(params string[] args)
        {
            var text = GitText(args, expectZero: true);
            if (text.Length > 0 && args.Length > 0 && args[0] == "commit")
                return;
        }

        private string GitText(string[] args, bool expectZero)
        {
            var start = GitStart(Repo, args);
            start.RedirectStandardOutput = true;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (expectZero)
                process.ExitCode.ShouldBe(0, stderr + stdout);
            return stdout;
        }
    }
}
