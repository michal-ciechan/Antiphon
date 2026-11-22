using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: the test helpers of the nightly port (plan section 2 rule 5) do what the c487-harness functions they
/// replace did: NightlyFixture (New-C487OwnedClone, Write-C487Trx/Execution/Discovery/Evidence, the host in place of
/// Write-C487Seams), FakeTestHost.NativePass (Write-C487NativePassSeams), RecordingLauncher, FakeGit, FakeProcessTable.
/// Nothing here starts a process.
/// </summary>
[Category("Unit")]
public sealed class NightlyTestHelpersTests
{
    private static readonly NightlyTrxRow[] Rows =
    [
        new("id-1", "Antiphon.Tests.Alpha.AlphaTests", "Passes"),
        new("id-2", "Antiphon.Tests.Alpha.AlphaTests", "Fails", "Failed"),
    ];

    private static readonly NightlyTestNode[] Nodes =
    [
        new("uid-1", "AlphaTests", "Passes", "Antiphon.Tests.Alpha"),
        new("uid-2", "AlphaTests", "Fails", "Antiphon.Tests.Alpha"),
    ];

    [Test]
    public void Owned_clone_has_the_git_config_head_and_owner_marker()
    {
        using var fixture = NightlyFixture.Create();
        var clone = fixture.NewOwnedClone("https://example.invalid/origin");

        clone.ShouldBe(fixture.PathOf("checkout"));
        File.ReadAllText(Path.Combine(clone, ".git", "config")).ShouldBe("[core]\n\tlongpaths = true\n[remote \"origin\"]\n\turl = https://example.invalid/origin\n");
        File.ReadAllText(Path.Combine(clone, ".git", "HEAD")).ShouldBe("ref: refs/heads/master\n");
        using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(clone, ".antiphon-nightly-owned")));
        marker.RootElement.GetProperty("origin").GetString().ShouldBe("https://example.invalid/origin");
        marker.RootElement.GetProperty("kind").GetString().ShouldBe("nightly-clone");
        Directory.Exists(Path.Combine(clone, "scripts")).ShouldBeFalse();
    }

    [Test]
    public void Owned_clone_with_scripts_copies_the_entries_lib_and_core_dependency_without_build_output()
    {
        using var fixture = NightlyFixture.Create();
        var clone = fixture.NewOwnedClone(copyScripts: true);

        File.Exists(Path.Combine(clone, "scripts", "nightly-run.ps1")).ShouldBeTrue("nightly-run.ps1");
        // W2-nightly S17: nightly-tests.ps1 is deleted; the nightly-tests command's sources are scripts/lib.
        File.Exists(Path.Combine(clone, "scripts", "nightly-tests.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(clone, "scripts", "lib", "Antiphon.Scripts", "NightlyTestsCommand.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(clone, "scripts", "lib", "nightly-common.ps1")).ShouldBeTrue();
        File.Exists(Path.Combine(clone, "scripts", "lib", "Antiphon.Nightly", "Antiphon.Nightly.csproj")).ShouldBeTrue();
        Directory.Exists(Path.Combine(clone, "scripts", "lib", "Antiphon.Nightly", "bin")).ShouldBeFalse();
        Directory.Exists(Path.Combine(clone, "scripts", "lib", "Antiphon.Nightly", "obj")).ShouldBeFalse();
        File.Exists(Path.Combine(clone, "src", "Antiphon.IO.WriteBehind", "Antiphon.IO.WriteBehind.csproj")).ShouldBeTrue();
    }

    [Test]
    public void Trx_is_read_back_by_the_core_reader()
    {
        using var fixture = NightlyFixture.Create();
        var path = fixture.PathOf("results", "run.trx");
        NightlyFixture.WriteTrx(path, Rows);

        var identities = NightlyTrx.ReadIdentities(path);
        identities.Select(row => (row.TestId, row.Identity, row.Outcome)).ShouldBe(new[]
        {
            ("id-1", "Antiphon.Tests.Alpha.AlphaTests.Passes", "Passed"),
            ("id-2", "Antiphon.Tests.Alpha.AlphaTests.Fails", "Failed"),
        });
    }

    [Test]
    public void Execution_discovery_and_evidence_documents_have_the_c487_shape()
    {
        using var fixture = NightlyFixture.Create();
        NightlyFixture.WriteExecution(fixture.PathOf("exec.json"), Nodes.Take(1));
        NightlyFixture.WriteDiscovery(fixture.PathOf("disc.json"), Nodes);
        var evidence = fixture.WriteEvidence("C487_Case", new { passed = true });

        using var execution = JsonDocument.Parse(File.ReadAllText(fixture.PathOf("exec.json")));
        execution.RootElement.GetProperty("format").GetString().ShouldBe("antiphon-tunit-execution-v1");
        execution.RootElement.GetProperty("tunitVersion").GetString().ShouldBe("1.44.0");
        execution.RootElement.GetProperty("mtpVersion").GetString().ShouldBe("2.2.2");
        var node = execution.RootElement.GetProperty("nodes")[0];
        node.GetProperty("state").GetString().ShouldBe("Passed");
        node.GetProperty("className").GetString().ShouldBe("AlphaTests");
        node.GetProperty("namespace").GetString().ShouldBe("Antiphon.Tests.Alpha");

        using var discovery = JsonDocument.Parse(File.ReadAllText(fixture.PathOf("disc.json")));
        discovery.RootElement.GetProperty("format").GetString().ShouldBe("antiphon-tunit-discovery-v1");
        discovery.RootElement.GetProperty("assemblyHash").GetString().ShouldBe("c487-test-hash");
        discovery.RootElement.GetProperty("nodes").GetArrayLength().ShouldBe(2);
        discovery.RootElement.GetProperty("nodes")[1].GetProperty("state").GetString().ShouldBe("Discovered");

        evidence.ShouldBe(fixture.PathOf("C487_Case.json"));
        using var body = JsonDocument.Parse(File.ReadAllText(evidence));
        body.RootElement.GetProperty("passed").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void Native_pass_writes_the_trx_discovery_diagnostic_and_json_result_from_the_argv()
    {
        using var fixture = NightlyFixture.Create();
        var host = FakeTestHost.NativePass(Rows, Nodes);
        var results = fixture.PathOf("results");
        var diagnostics = fixture.PathOf("diag");

        var run = host(new NightlyLaunch("dotnet", ["exec", "tests.dll", "--results-directory", results, "--report-trx-filename", "chunk.trx",
            "--diagnostic-output-directory", diagnostics], fixture.Root));
        run.ExitCode.ShouldBe(0);
        run.ChildrenExited.ShouldBeTrue();
        run.LogText.ShouldBe("C487_INVOCATION fake");
        NightlyTrx.ReadIdentities(Path.Combine(results, "chunk.trx")).Length.ShouldBe(2);
        File.Exists(Path.Combine(results, "chunk.discovery.json")).ShouldBeTrue();
        using (var log = JsonDocument.Parse(File.ReadAllText(Path.Combine(diagnostics, "log.diag"))))
        {
            log.RootElement.GetProperty("format").GetString().ShouldBe("antiphon-tunit-execution-v1");
            log.RootElement.GetProperty("nodes")[1].GetProperty("state").GetString().ShouldBe("Failed");
        }

        host(new NightlyLaunch("dotnet", ["exec", "tests.dll", "--list-tests", "--diagnostic-output-directory", diagnostics], fixture.Root));
        using (var listing = JsonDocument.Parse(File.ReadAllText(Path.Combine(diagnostics, "log.diag"))))
            listing.RootElement.GetProperty("format").GetString().ShouldBe("antiphon-tunit-discovery-v1");

        var json = fixture.PathOf("npm", "result.json");
        host(new NightlyLaunch("pwsh", ["-File", "npm.ps1", "-JsonResultPath", json], fixture.Root)).ExitCode.ShouldBe(0);
        File.ReadAllText(json).ShouldContain("\"numPassedTests\": 1");

        var refused = host(new NightlyLaunch("dotnet", ["--results-directory", results, "--report-trx-filename", "sub/chunk.trx"], fixture.Root));
        refused.ExitCode.ShouldBe(5);
        refused.LogText.ShouldEndWith("trx-filename-not-basename");

        var omitted = FakeTestHost.NativePass(Rows, Nodes, omitDiscovery: true);
        omitted(new NightlyLaunch("dotnet", ["--results-directory", results, "--report-trx-filename", "other.trx"], fixture.Root));
        File.Exists(Path.Combine(results, "other.trx")).ShouldBeTrue();
        File.Exists(Path.Combine(results, "other.discovery.json")).ShouldBeFalse();
    }

    [Test]
    public void Recording_launcher_records_each_launch_and_answers_from_its_behaviour()
    {
        using var fixture = NightlyFixture.Create();
        var plain = new RecordingLauncher();
        var passed = plain.Run(new NightlyLaunch("pwsh", ["-File", "nightly-tests.ps1", "-Suites", "a,b"], fixture.Root));
        passed.ExitCode.ShouldBe(0);
        passed.Pid.ShouldBe(2);
        plain.Lines.ShouldBe(new[] { "EXEC pwsh -File nightly-tests.ps1 -Suites a,b" });
        plain.WaitCleanup(passed).ShouldBeTrue();
        plain.WaitCleanup(new NightlyLaunchResult { ChildrenExited = false }).ShouldBeFalse();
        plain.NativeActiveCount = 3;
        ((INightlyLauncher)plain).NativeActiveCount.ShouldBe(3);

        var failing = new RecordingLauncher(launch => new NightlyLaunchResult { ExitCode = 7, Pid = 9 });
        failing.Run(new NightlyLaunch("git", ["status"], fixture.Root)).ExitCode.ShouldBe(7);
        failing.Launches.Single().FileName.ShouldBe("git");
    }

    [Test]
    public void Fake_git_answers_from_its_table_records_calls_and_holds_a_gated_subcommand()
    {
        var git = new FakeGit();
        git.Run("/repo", ["rev-parse", "HEAD"]).Output.ShouldBe(FakeGit.DefaultSha);
        git.Run("/repo", ["config", "--get", "core.longpaths"]).Output.ShouldBe("true");
        git.Reply("fetch", new NightlyGitResult(128, "fatal: offline"));
        git.Run("/repo", ["fetch", "origin"]).ExitCode.ShouldBe(128);
        git.Reply("fetch", new NightlyGitResult(0, "fetched"));
        git.Run("/repo", ["fetch", "origin"]).Output.ShouldBe("fetched");
        git.Lines.ShouldBe(new[] { "GIT rev-parse HEAD", "GIT config --get core.longpaths", "GIT fetch origin", "GIT fetch origin" });
        git.Calls[0].WorkingDirectory.ShouldBe("/repo");

        var gate = git.Gate("reset");
        var held = Task.Run(() => git.Run("/repo", ["reset", "--hard"]));
        gate.WaitEntered(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        held.IsCompleted.ShouldBeFalse();
        git.Run("/repo", ["status"]).ExitCode.ShouldBe(0);
        gate.Release();
        held.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        held.Result.Output.ShouldBe(FakeGit.DefaultSha);
    }

    [Test]
    public void Fake_process_table_answers_alive_start_parent_and_identities()
    {
        var started = new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc);
        var table = new FakeProcessTable { CurrentPid = 100, Boot = "boot-a", Host = "host-a" }
            .Start(200, started)
            .SetParent(100, 200, started);

        table.IsAlive(0).ShouldBeFalse();
        table.IsAlive(-1).ShouldBeFalse();
        table.IsAlive(12345).ShouldBeTrue();
        table.StartTimeUtc(200).ShouldBe(started);
        table.StartTimeUtc(300).ShouldBeNull();
        table.Parent(100).ShouldBe(new NightlyParent(200, started));
        table.Parent(300).ShouldBe(NightlyParent.None);
        table.Exit(200);
        table.IsAlive(200).ShouldBeFalse();
        table.StartTimeUtc(200).ShouldBeNull();
        ((INightlyProcessTable)table).CurrentPid.ShouldBe(100);
        table.BootId().ShouldBe("boot-a");
        table.HostId().ShouldBe("host-a");
    }

    [Test]
    public void Fixture_host_traces_git_and_launches_on_the_fake_clock()
    {
        using var fixture = NightlyFixture.Create();
        var git = new FakeGit();
        var launcher = new RecordingLauncher();
        var host = fixture.Host(git, launcher);

        host.RunGit(fixture.Root, "rev-parse", "HEAD").Output.ShouldBe(FakeGit.DefaultSha);
        host.Launch(new NightlyLaunch("pwsh", ["-File", "x.ps1"], fixture.Root)).ExitCode.ShouldBe(0);
        host.Launch(new NightlyLaunch("dotnet", ["test.dll"], fixture.Root, Custody: NightlyCustody.Owned)).ExitCode.ShouldBe(0);

        host.UtcNow.ShouldBe(NightlyFixture.DefaultNow.UtcDateTime);
        fixture.Trace.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(new[]
        {
            "2026-09-11T12:00:00.0000000Z git rev-parse HEAD",
            "2026-09-11T12:00:00.0000000Z exec pwsh -File x.ps1",
            "2026-09-11T12:00:00.0000000Z proc dotnet test.dll",
        });
        git.Lines.ShouldBe(new[] { "GIT rev-parse HEAD" });
        launcher.Launches.Count.ShouldBe(2);
        host.Probe.Docker().Version.ShouldBe("fake");
        host.Probe.DiskFree().FreeBytes.ShouldBe(50L * 1024 * 1024 * 1024);
        host.Probe.ToolVersion("git").ShouldBe("git version 2.42.0");
        host.Processes.IsAlive(7).ShouldBeTrue();
    }
}
