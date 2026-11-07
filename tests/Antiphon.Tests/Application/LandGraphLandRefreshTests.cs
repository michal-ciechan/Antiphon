using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1753. A landing batch whose own build-definition records or target pins drifted
/// refreshes the land-graph files, notifies, and never asks. Property and construct refusals,
/// a malformed manifest, a refresh outside the batch's files, and a check that still fails
/// after the refresh stay hard failures. A bare -Update outside that path stays forbidden.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandGraphLandRefreshTests
{
    private const string Manifest = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";
    private const string Evaluation = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";
    private const string PinsFile = "server/Application/Services/LandVerificationBuildGraph.pins.txt";
    private const string CommitSubject = "chore: refresh land-graph records for the landing batch";

    private const string AuditStub = """
        param([switch]$Check, [switch]$Update, [switch]$LandRefresh)
        $ErrorActionPreference = 'Stop'
        $log = $env:C1753_LOG
        if ($Update) {
            Add-Content -LiteralPath $log -Value ('update landrefresh=' + $LandRefresh.IsPresent)
            if ($env:C1753_UPDATE_CODE -eq '1') { exit 1 }
            Copy-Item -LiteralPath $env:C1753_NEW_MANIFEST -Destination $env:C1753_MANIFEST -Force
            Copy-Item -LiteralPath $env:C1753_NEW_EVAL -Destination $env:C1753_EVAL -Force
            exit 0
        }
        $prior = 0
        if (Test-Path -LiteralPath $log) {
            foreach ($line in [System.IO.File]::ReadAllLines($log)) {
                if ($line.StartsWith('check')) { $prior++ }
            }
        }
        Add-Content -LiteralPath $log -Value 'check'
        if ($prior -eq 0) {
            if (-not [string]::IsNullOrEmpty($env:C1753_FIRST_TEXT)) { Write-Output $env:C1753_FIRST_TEXT }
            exit ([int]$env:C1753_FIRST_CODE)
        }
        if (-not [string]::IsNullOrEmpty($env:C1753_SECOND_TEXT)) { Write-Output $env:C1753_SECOND_TEXT }
        exit ([int]$env:C1753_SECOND_CODE)
        """;

    private const string PinStub = """
        param([string]$Repo)
        $ErrorActionPreference = 'Stop'
        [Console]::Out.Write([System.IO.File]::ReadAllText($env:C1753_PIN_TEXT))
        exit 0
        """;

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
        rule.ShouldContain("scripts/land-graph-land-refresh.ps1");
        rule.ShouldContain("never asks");
        rule.ShouldContain(forbidden);
        rule.ShouldContain("policyHash");
        rule.ShouldContain("-PrintInputDigest");
        rule.ShouldContain("_PtyHostArtifactsArguments");
        rule.ShouldContain("malformed manifest");
        Count(loop, "<a id=\"land-graph-refresh\"></a>").ShouldBe(1);

        var author = loop.IndexOf("Author-time `-Update` requires", StringComparison.Ordinal);
        var runtime = loop.IndexOf("adds zero land-time runtime work", author, StringComparison.Ordinal);
        var authorWindow = loop[author..runtime];
        authorWindow.ShouldContain("scripts/land-graph-land-refresh.ps1");
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
        bundle.ShouldContain("docs/orchestration-loop.md#land-graph-refresh");
        bundle.ShouldContain("never asks");
        bundle.ShouldContain("docs/orchestration-loop.md#standing-operating-rules");
        bundle.ShouldContain("never Reply to a complete report Blocked only by runner sync");
        bundle.ShouldContain("full report");

        var skill = File.ReadAllText(Path.Combine(root, ".claude", "skills", "antiphon-rc-qualification", "SKILL.md"));
        skill.ShouldContain("Fixers never run native tests, tags, or land graph `-Update`.");
        skill.ShouldNotContain("may use the documented `-Update` once");
        skill.ShouldContain("scripts/land-graph-land-refresh.ps1");

        var scriptPath = Path.Combine(root, "scripts", "land-graph-land-refresh.ps1");
        File.Exists(scriptPath).ShouldBeTrue(scriptPath);
        var script = File.ReadAllText(scriptPath);
        script.All(static c => c <= 127).ShouldBeTrue("land-graph-land-refresh.ps1 must stay ASCII");
        script.ShouldNotContain("Read-Host");
        script.ShouldContain("LAND-GRAPH-NOTICE");
        script.ShouldContain("reason=property-or-construct");
        script.ShouldContain("reason=malformed-manifest");
        script.ShouldContain("reason=outside-batch");
        script.ShouldContain("reason=check-after-refresh");
        script.ShouldContain("reason=inherited-mismatch");

        var audit = File.ReadAllText(Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        audit.ShouldContain("-LandRefresh");
        audit.ShouldContain("only for scripts/land-graph-land-refresh.ps1");
        audit.ShouldContain("commit source and pins before auditing");
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
        private readonly string _audit;
        private readonly string _pinsScript;
        private readonly string _log;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "c1753-" + Guid.NewGuid().ToString("N"));
            Repo = Path.Combine(Root, "repo");
            Directory.CreateDirectory(Repo);
            _audit = Path.Combine(Root, "audit.ps1");
            _pinsScript = Path.Combine(Root, "pins.ps1");
            _log = Path.Combine(Root, "audit.log");
            File.WriteAllText(_audit, AuditStub, new UTF8Encoding(false));
            File.WriteAllText(_pinsScript, PinStub, new UTF8Encoding(false));
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

        public (int Code, string Text) Run(
            (int Code, string? Text, string? SecondText) check,
            string printedPins,
            string? notePath = null,
            string? card = null)
        {
            var head = Fingerprint(Selected(Repo, "HEAD"));
            var manifestCopy = Path.Combine(Root, "new-manifest.txt");
            var evalCopy = Path.Combine(Root, "new-eval.txt");
            var pinCopy = Path.Combine(Root, "printed-pins.txt");
            File.WriteAllText(manifestCopy, head.Digest + "\n" + head.Count.ToString() + "\n", new UTF8Encoding(false));
            File.WriteAllText(pinCopy, printedPins, new UTF8Encoding(false));
            var pinBlob = Out("hash-object", pinCopy).Trim();
            File.WriteAllText(evalCopy, head.Digest + "\n9.0.100\n" + pinBlob + "\n", new UTF8Encoding(false));
            var secondFails = check.SecondText is not null && check.SecondText != "ok";
            var env = new Dictionary<string, string>
            {
                ["C1753_LOG"] = _log,
                ["C1753_FIRST_CODE"] = check.Code.ToString(),
                ["C1753_FIRST_TEXT"] = check.Text ?? "",
                ["C1753_SECOND_CODE"] = secondFails ? "1" : "0",
                ["C1753_SECOND_TEXT"] = check.SecondText ?? "",
                ["C1753_UPDATE_CODE"] = "0",
                ["C1753_NEW_MANIFEST"] = manifestCopy,
                ["C1753_NEW_EVAL"] = evalCopy,
                ["C1753_MANIFEST"] = Path.Combine(Repo, Manifest.Replace('/', Path.DirectorySeparatorChar)),
                ["C1753_EVAL"] = Path.Combine(Repo, Evaluation.Replace('/', Path.DirectorySeparatorChar)),
                ["C1753_PIN_TEXT"] = pinCopy,
                ["ANTIPHON_API"] = "http://127.0.0.1:1",
                ["ANTIPHON_TASK_TOKEN"] = "",
            };
            return RunScript(env, notePath, card);
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

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private (int Code, string Text) RunScript(Dictionary<string, string> env, string? notePath, string? card)
        {
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "land-graph-land-refresh.ps1");
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = Repo,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Base", BaseSha, "-Repo", Repo, "-AuditScript", _audit, "-PinScript", _pinsScript })
                start.ArgumentList.Add(arg);
            if (notePath is not null)
            {
                start.ArgumentList.Add("-NotePath");
                start.ArgumentList.Add(notePath);
            }
            if (card is not null)
            {
                start.ArgumentList.Add("-Card");
                start.ArgumentList.Add(card);
            }
            foreach (var pair in env) start.Environment[pair.Key] = pair.Value;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(45_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("land-graph refresh script exceeded 45 s");
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
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
