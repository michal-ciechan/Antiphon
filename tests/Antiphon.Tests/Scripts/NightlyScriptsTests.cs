using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0124 S1 — pin the nightly scripts: ASCII-only, no git mutations or
/// alternate OutputPath in the runner, isolated clone named by the bootstrap,
/// shared-tree guard refuses <c>C:\src\Antiphon</c>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyScriptsTests
{
    /// <summary>
    /// CARD-2044, in-process since W2-nightly S20 (was the scripts/test-nightly-run.ps1 case on the PowerShell entry and its
    /// seams): an rc run (-NoSync -WhatIf on a committed candidate) without -ReleaseRoot, or with a blank one, refuses before
    /// recording a run and names the refusal in the completion record; an explicit root receives the selection.
    /// </summary>
    [Test, Timeout(120_000)]
    public void C2044_RcReleaseRootRefusal(CancellationToken token)
    {
        using var chain = new Antiphon.Tests.Scripts.Nightly.NightlyRcChain("c2044", "rc/2026-11-30").Publish();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        (Antiphon.Nightly.NightlyRunResult Result, System.Text.Json.JsonElement Record, string State) Run(string row, string? releaseRoot)
        {
            var options = chain.Options("c2044-" + row, o => o.ReleaseRoot = releaseRoot ?? "");
            var before = chain.Fixture.Out.ToString().Length;
            var result = chain.Run(options);
            var last = chain.Fixture.Out.ToString()[before..].Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1];
            using var record = System.Text.Json.JsonDocument.Parse(last);
            var state = Path.Combine(options.StateRoot, "last-run.json");
            return (result, record.RootElement.Clone(), File.Exists(state) ? File.ReadAllText(state) : "");
        }
        foreach (var (row, root) in new[] { ("omitted", (string?)null), ("blank", "   ") })
        {
            var run = Run(row, root);
            rows.Check(run.Result.ExitCode == 3 && run.Record.GetProperty("exitCode").GetInt32() == 3 && run.Record.GetProperty("refusal").GetString() == "rc-release-root-required"
                && run.Record.GetProperty("nativeRunId").GetString() == "" && run.State.Length == 0,
                "C2044 " + row + " ReleaseRoot is refused before recording a run and named in the final JSON", run.Record.ToString());
        }
        var release = chain.Fixture.PathOf("rc-releases");
        var explicitRun = Run("explicit", release);
        var id = chain.CandidateId();
        rows.Check(explicitRun.Result.ExitCode == 0 && explicitRun.Record.GetProperty("exitCode").GetInt32() == 0
            && explicitRun.Record.GetProperty("nativeRunId").GetString() == "c2044-explicit" && explicitRun.State.Length > 0 && System.Text.Json.Nodes.JsonNode.Parse(explicitRun.State)?["profile"]?.GetValue<string>() == "rc"
            && File.Exists(Path.Combine(release, "candidates", id, "attempts", "c2044-explicit", "selection.json")),
            "C2044 explicit ReleaseRoot creates the selection in the supplied root", explicitRun.Record + " " + explicitRun.Result.Refusal);
        rows.ShouldAllPass(3, "C2044 omitted ReleaseRoot is refused before recording a run and named in the final JSON",
            "C2044 blank ReleaseRoot is refused before recording a run and named in the final JSON",
            "C2044 explicit ReleaseRoot creates the selection in the supplied root");
    }

    /// <summary>
    /// CARD-1854, in-process since W2-nightly S19 (was the scripts/test-nightly-run.ps1 case): real tracked files, with only
    /// the native stdout decoding and quoting simulated in the git seam; NightlyRun.RepairLineEndings is the C# of
    /// Repair-NightlyCheckoutLineEndings.
    /// </summary>
    [Test]
    public void C1854_EolRepairHardening()
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("c1854");
        var seed = fixture.PathOf("seed");
        var clone = fixture.PathOf("clone");
        Directory.CreateDirectory(seed);
        string Git(string directory, params string[] arguments) => Antiphon.Tests.Scripts.Nightly.NightlyRunWorld.FixtureGit(directory, arguments);
        string[] commit = ["-c", "user.name=Eol Test", "-c", "user.email=eol@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m"];
        const string plain = "selector [one].ps1";
        var unicode = "unicode-" + (char)0x00e9 + ".ps1";
        var quoted = "quoted-" + (char)0x4e2d + ".ps1";
        Git(seed, "init", "-q", "-b", "master");
        Git(seed, "config", "--local", "core.autocrlf", "false");
        foreach (var name in new[] { plain, unicode, quoted }) File.WriteAllBytes(Path.Combine(seed, name), "a\nb\n"u8.ToArray());
        Git(seed, "add", ".");
        Git(seed, [.. commit, "before-attribute"]);
        Git(fixture.Root, "clone", "-q", "-c", "core.autocrlf=true", seed, clone);
        File.WriteAllBytes(Path.Combine(seed, ".gitattributes"), "*.ps1 text eol=lf\n"u8.ToArray());
        Git(seed, "add", ".gitattributes");
        Git(seed, [.. commit, "pin-lf"]);
        Git(clone, "fetch", "-q", "origin", "master");
        Git(clone, "reset", "--hard", "FETCH_HEAD");
        bool Crlf(string name) => File.ReadAllBytes(Path.Combine(clone, name)).Contains((byte)13);
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var crlf = new[] { plain, unicode, quoted }.Count(Crlf);
        rows.Check(crlf == 3, "C1854 real reused clone has three stale tracked files", crlf.ToString());

        // Model UTF-8 bytes decoded with the console OEM code page on Windows PS 5.1.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var mangled = Encoding.GetEncoding(437).GetString(Encoding.UTF8.GetBytes(unicode));
        var quotedLine = Git(clone, "-c", "core.quotePath=true", "ls-files", "--eol").Split('\n').First(l => l.Contains("\"quoted-", StringComparison.Ordinal));
        var quotedPath = quotedLine.TrimEnd('\r').Split('\t', 2)[1];
        rows.Check(mangled != unicode && !File.Exists(Path.Combine(clone, mangled)) && quotedPath.StartsWith('"'), "C1854 missing decoded and C-quoted paths are discriminating inputs");
        var checkouts = new List<string>();
        Antiphon.Nightly.NightlyHost Host(Antiphon.Nightly.NightlyGit git) => new()
        {
            Clock = TimeProvider.System, Git = git, Launcher = new Antiphon.Tests.Scripts.Nightly.RecordingLauncher(),
            Processes = new Antiphon.Tests.Scripts.Nightly.FakeProcessTable(), Probe = new Antiphon.Tests.Scripts.Nightly.FakeHostProbe(),
            Out = fixture.Out, Error = fixture.Error, Trace = TextWriter.Null,
        };
        var seamed = Host((directory, arguments) =>
        {
            var run = Antiphon.Nightly.NightlyNativeGit.Run(directory, arguments);
            var text = run.Output;
            if (arguments.Contains("ls-files")) text = text.Replace(unicode, mangled).Replace(quoted, quotedPath);
            if (arguments.Contains("checkout-index")) checkouts.Add(string.Join("|", arguments));
            return new Antiphon.Nightly.NightlyGitResult(run.ExitCode, text);
        });
        string Repair(Antiphon.Nightly.NightlyHost host)
        {
            var before = fixture.Out.ToString().Length;
            Antiphon.Nightly.NightlyRun.RepairLineEndings(host, clone);
            return fixture.Out.ToString()[before..];
        }
        var failure = "";
        var log = "";
        try { log = Repair(seamed); }
        catch (Exception ex) { failure = ex.Message; }
        rows.Check(failure.Length == 0, "C1854 mixed repair completes despite missing decoded path", failure);
        rows.Check(!Crlf(plain), "C1854 valid literal path is rewritten to LF");
        rows.Check(Crlf(unicode) && Crlf(quoted), "C1854 skipped files retain their bytes");
        rows.Check(checkouts.Count == 1 && checkouts[0] == "checkout-index|-f|-u|--|" + plain, "C1854 checkout receives only the existing unquoted path", string.Join(";", checkouts));
        rows.Check(Regex.IsMatch(log, @"nightly sync: rewriting 1 working file\(s\).*skipped=2"), "C1854 mixed repair logs two skipped paths", log);
        checkouts.Clear();
        log = Repair(seamed);
        rows.Check(checkouts.Count == 0 && Regex.IsMatch(log, @"nightly sync: rewriting 0 working file\(s\).*skipped=2"), "C1854 skipped-only repair logs count without checkout", log);
        // The real git with correctly decoded non-ASCII paths (NightlyNativeGit reads UTF-8).
        var real = Host(Antiphon.Nightly.NightlyNativeGit.Run);
        Repair(real);
        var remaining = new[] { plain, unicode, quoted }.Count(Crlf);
        rows.Check(remaining == 0, "C1854 real non-ASCII paths repair when decoded correctly", remaining.ToString());
        log = Repair(real);
        rows.Check(string.IsNullOrWhiteSpace(log), "C1854 clean repair remains silent", log);
        var status = Git(clone, "status", "--porcelain", "--untracked-files=no");
        rows.Check(string.IsNullOrWhiteSpace(status), "C1854 repaired clone has clean tracked status", status);
        rows.ShouldAllPass(11);
    }

    /// <summary>
    /// rc ae84478d g126, in-process since W2-nightly S19: the sync rewrites working files whose bytes predate an eol attribute.
    /// A reused clone made under core.autocrlf=true before *.ps1 eol=lf kept CRLF through fetch and reset --hard; the fixture
    /// sets core.autocrlf in the clone itself.
    /// </summary>
    [Test]
    public void C1313_ReusedCloneEolRepair()
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("c1313");
        var seed = fixture.PathOf("seed");
        var remote = seed.Replace('\\', '/');
        var clone = fixture.PathOf("clone");
        var state = fixture.PathOf("state");
        Directory.CreateDirectory(seed);
        string Git(string directory, params string[] arguments) => Antiphon.Tests.Scripts.Nightly.NightlyRunWorld.FixtureGit(directory, arguments);
        string[] commit = ["-c", "user.name=Eol Test", "-c", "user.email=eol@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m"];
        Git(seed, "init", "-q", "-b", "master");
        Git(seed, "config", "--local", "core.autocrlf", "false");
        File.WriteAllBytes(Path.Combine(seed, "sel.ps1"), "a\nb\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(seed, "other.txt"), "c\nd\n"u8.ToArray());
        Git(seed, "add", "sel.ps1", "other.txt");
        Git(seed, [.. commit, "before-attribute"]);
        Git(fixture.Root, "clone", "-q", "-c", "core.autocrlf=true", remote, clone);
        File.WriteAllText(Path.Combine(clone, ".antiphon-nightly-owned"), "{\"origin\":\"" + remote + "\",\"kind\":\"nightly-clone\"}");
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var before = File.ReadAllBytes(Path.Combine(clone, "sel.ps1"));
        rows.Check(before.Contains((byte)13), "C1313 reused clone checked the selector out CRLF", before.Length.ToString());
        // The attribute lands later; the selector blob itself does not change.
        File.WriteAllBytes(Path.Combine(seed, ".gitattributes"), "*.ps1 text eol=lf\n"u8.ToArray());
        Git(seed, "add", ".gitattributes");
        Git(seed, [.. commit, "pin-ps1-lf"]);
        var run = Antiphon.Nightly.NightlyRun.Run(new Antiphon.Nightly.NightlyRunOptions
        {
            CheckoutRoot = clone, StateRoot = state, LogRoot = Path.Combine(state, "logs"), RemoteUrl = remote, Ref = "master", Trigger = "manual", WhatIf = true,
            CoordinationRoot = fixture.PathOf("coord"),
        }, Antiphon.Tests.Scripts.Nightly.NightlyRunTests.RealGitHost(fixture));
        rows.Check(run.ExitCode == 0 && run.Phase == "whatif", "C1313 reused clone sync completes", run.Refusal + " " + run.Phase + " " + run.ExitCode + "\n" + fixture.Out);
        var after = File.ReadAllBytes(Path.Combine(clone, "sel.ps1"));
        rows.Check(!after.Contains((byte)13) && after.Length == 4, "C1313 reused clone selector is LF after sync", after.Length.ToString());
        var other = File.ReadAllBytes(Path.Combine(clone, "other.txt"));
        rows.Check(other.Contains((byte)13), "C1313 reused clone leaves a file without an eol attribute as configured", other.Length.ToString());
        // The ownership marker is untracked by design; only tracked paths must be clean.
        var status = Git(clone, "status", "--porcelain", "--untracked-files=no");
        rows.Check(string.IsNullOrWhiteSpace(status), "C1313 reused clone is clean after the eol repair", status);
        rows.ShouldAllPass(5);
    }

    [Test]
    public void The_three_scripts_are_ascii_only()
    {
        foreach (var name in new[]
                 {
                     "nightly-run.ps1", "nightly-health.ps1",
                     "deploy-nightly-watchdog.ps1", "test-deploy-nightly-watchdog.ps1",
                     Path.Combine("lib", "nightly-common.ps1"),
                     // W2-nightly S27: lib/nightly-coverage.ps1 is deleted.
                     // W2-nightly S20: the nightly-run command (nightly-run-impl.ps1 and test-nightly-run.ps1 are deleted).
                     Path.Combine("lib", "Antiphon.Scripts", "NightlyRunCommand.cs"),
                     // W2-nightly S17: the nightly tests run (nightly-tests.ps1 and nightly-tests-impl.ps1 are deleted) and the
                     // C487 harness's in-process run.
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyTests.cs"),
                     Path.Combine("lib", "c487-nightly-tests.ps1"),
                     // W2-nightly S05: the readiness evaluator's C# and the launcher it runs through.
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyHealth.cs"),
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyHealthWindmill.cs"),
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyHealthNotifications.cs"),
                     Path.Combine("lib", "antiphon-cli.ps1"),
                     // W2-nightly S06: the nightly report's C# and its command.
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyReport.cs"),
                     Path.Combine("lib", "Antiphon.Scripts", "NightlyReportCommand.cs"),
                     // W2-nightly S19: the run core's C# and the nightly-tests command.
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyRun.cs"),
                     Path.Combine("lib", "Antiphon.Nightly", "NightlyRunOptions.cs"),
                     Path.Combine("lib", "Antiphon.Scripts", "NightlyTestsCommand.cs"),
                     Path.Combine("lib", "c487-harness.ps1"),
                     // CARD-1698: the toolchain floor library and the restart entry points that load it.
                     Path.Combine("lib", "toolchain-floor.ps1"),
                     "session-runner-restart-health.ps1",
                     Path.Combine("..", "restart-server.ps1"),
                 })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
    }

    [Test]
    public void Nightly_tests_script_has_no_git_mutations_or_alternate_output_path()
    {
        // W2-nightly S17: the nightly tests run is the nightly-tests command (scripts/nightly-tests.ps1 is deleted).
        var lib = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib");
        var text = File.ReadAllText(Path.Combine(lib, "Antiphon.Nightly", "NightlyTests.cs"), Encoding.UTF8)
            + File.ReadAllText(Path.Combine(lib, "Antiphon.Scripts", "NightlyTestsCommand.cs"), Encoding.UTF8);
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-tests.ps1")).ShouldBeFalse();
        text.ShouldNotContain("git pull");
        text.ShouldNotContain("git checkout");
        text.ShouldNotContain("git stash");
        text.ShouldNotContain("OutputPath=");
    }

    [Test]
    public void Nightly_run_script_names_the_isolated_clone_and_origin_master()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1");
        var text = File.ReadAllText(path, Encoding.UTF8);
        text.ShouldContain(@"C:\Antiphon\nightly\checkout");
        text.ShouldContain("origin/master");
    }

    /// <summary>
    /// CARD-0616 — the offline run harness must give every fixture a private coordination
    /// root. Without one the run core falls back to the machine-global
    /// <c>C:\Antiphon\verification\native-run.lock</c>, so ~91 harness rows contend with a
    /// real nightly or RC run (and with each other) over a shared lock they do not own.
    /// </summary>
    [Test]
    public void Offline_run_harness_gives_every_fixture_a_private_coordination_root()
    {
        // W2-nightly S20: the run fixtures are C# (scripts/test-nightly-run.ps1 is deleted). Each world's options name a
        // coordination root under the world's own root, never the machine-global verification root.
        using (var world = new Antiphon.Tests.Scripts.Nightly.NightlyRunWorld("c616-coord"))
        {
            var options = world.Options();
            options.CoordinationRoot.ShouldStartWith(world.Root, Case.Insensitive);
            options.CoordinationRoot.ShouldNotContain(@"C:\Antiphon\verification", Case.Insensitive);
        }
        using (var chain = new Antiphon.Tests.Scripts.Nightly.NightlyRcChain("c616-coord-rc"))
        {
            var options = chain.Options("run");
            options.CoordinationRoot.ShouldStartWith(chain.Root, Case.Insensitive);
        }
        foreach (var name in new[] { "NightlyRunWorld.cs", "NightlyRunHops.cs" })
            File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "Scripts", "Nightly", name), Encoding.UTF8)
                .ShouldNotContain(@"C:\Antiphon\verification");
    }

    [Test]
    public void Shared_tree_WhatIf_exits_3_naming_the_guard()
    {
        // W2-nightly S15: in-process on NightlyTests.Run (the nightly-tests command since S17); the guard refuses before anything
        // under the shared tree is read.
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("s15-shared-tree");
        var options = Antiphon.Nightly.NightlyTestsOptions.Parse(["--repo-root", @"C:\src\Antiphon", "--suites", "client", "--what-if"]);
        var result = Antiphon.Nightly.NightlyTests.Run(options, fixture.Host());
        var output = fixture.Out.ToString() + fixture.Error;

        result.ProcessExitCode.ShouldBe(3, output);
        result.Refusal.ShouldBe("shared-tree");
        output.ShouldContain("REFUSED");
        output.ShouldContain(@"C:\src\Antiphon");
        // W2-nightly S17: the command names its own flag.
        output.ShouldContain("--allow-shared-tree");
        output.ShouldNotContain("WhatIf: would run");
    }
}
