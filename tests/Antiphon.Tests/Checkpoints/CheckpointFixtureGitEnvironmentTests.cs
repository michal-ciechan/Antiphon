using System.Collections;
using System.Diagnostics;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointFixtureGitEnvironmentTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public void fixture_commit_matches_hardened_status_under_hostile_config()
    {
        var repo = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(repo);
        using var environment = CheckpointFixtureGitEnvironment.Create(TempDir());
        environment.Git(repo, "init", "-b", "card1199");
        var payload = Path.Combine(repo, "payload.txt");
        File.WriteAllBytes(payload, "line\r\n"u8.ToArray());
        environment.Git(repo, "add", "--", "payload.txt");
        environment.Git(repo, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--no-verify");
        File.SetLastWriteTimeUtc(payload, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var status = HardenedStatus(repo);
        status.Contains("payload.txt", StringComparison.Ordinal).ShouldBeFalse("dirty-baseline payload.txt\n" + status);
    }

    [Test]
    [Timeout(120_000)]
    public void child_environment_isolated_without_changing_parent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = ParentEnvironment();
        using var first = CheckpointFixtureGitEnvironment.Create(Path.Combine(TempDir(), "iso-a"));
        using var second = CheckpointFixtureGitEnvironment.Create(Path.Combine(TempDir(), "iso-b"));
        AssertParentUnchanged(before, ParentEnvironment());
        first.GlobalConfigPath.Equals(second.GlobalConfigPath, StringComparison.Ordinal).ShouldBeFalse("child roots share a global config");
        first.SystemConfigPath.Equals(second.SystemConfigPath, StringComparison.Ordinal).ShouldBeFalse("child roots share a system config");
        File.WriteAllText(Path.Combine(first.Home, ".gitconfig"), "[antiphon]\n\tsentinel = c1199-home\n");
        File.WriteAllText(first.SystemConfigPath, "[antiphon]\n\tsentinel = c1199-system\n");
        var repo = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(repo);
        first.Git(repo, "init", "-b", "card1199");

        var local = Run(first, repo, "git", "config", "--local", "--get", "core.autocrlf");
        local.Exit.ShouldBe(0, local.Stderr);
        local.Stdout.Trim().ShouldBe("false");
        var origin = Run(first, repo, "git", "config", "--show-origin", "--get", "core.autocrlf");
        origin.Stdout.Contains(".git/config", StringComparison.Ordinal).ShouldBeTrue("local autocrlf origin");
        origin.Stdout.Contains("false", StringComparison.Ordinal).ShouldBeTrue("local autocrlf value");
        var sentinel = Run(first, repo, "git", "config", "--get", "antiphon.sentinel");
        sentinel.Stdout.Trim().ShouldBeEmpty();
        var global = Run(first, repo, "pwsh", "-NoProfile", "-NonInteractive", "-Command", "[Console]::Write($env:GIT_CONFIG_GLOBAL)");
        global.Stdout.ShouldBe(first.GlobalConfigPath);
        var system = Run(first, repo, "pwsh", "-NoProfile", "-NonInteractive", "-Command", "[Console]::Write($env:GIT_CONFIG_SYSTEM)");
        system.Stdout.ShouldBe(first.SystemConfigPath);
        File.ReadAllText(first.GlobalConfigPath).Contains("c1199-home", StringComparison.Ordinal).ShouldBeFalse("sentinel leaked into the global file");

        var parentList = ListSources(new ProcessStartInfo("dotnet"));
        var childList = ListSources(first.Command("dotnet", repo), first.RestoreConfigPath);
        AssertSameSources(parentList, childList);
        var expectedMaps = 0;
        foreach (var path in CheckpointFixtureGitEnvironment.NugetConfigCandidates())
        {
            if (!File.Exists(path))
                continue;
            expectedMaps += XDocument.Load(path).Descendants().Count(element => element.Name.LocalName == "packageSourceMapping");
        }

        var written = XDocument.Load(first.RestoreConfigPath);
        written.Descendants().Count(element => element.Name.LocalName == "packageSourceMapping").ShouldBe(expectedMaps, "package source mapping count");
        written.Descendants().Any(element => element.Name.LocalName.Contains("Credential", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse("credential element");
    }

    [Test]
    [Timeout(120_000)]
    public async Task real_driver_keeps_confinement_and_cancellation(CancellationToken cancellationToken)
    {
        var repo = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(repo);
        CheckpointFixtureProcess.Git(repo, "init", "-b", "card1199");
        File.WriteAllText(Path.Combine(repo, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        CheckpointFixtureProcess.Git(repo, "add", "--", "App.csproj");
        CheckpointFixtureProcess.Git(repo, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--no-verify");
        using var environment = CheckpointFixtureGitEnvironment.Create(Path.Combine(TempDir(), "iso"));
        var inner = new ProcessDriver();
        var driver = environment.Driver(inner);
        var request = new DriverRequest("dotnet", ["build", "App.csproj", "--property:OutputPath=bin-probe/"], repo);
        var forwarded = driver.CaptureLedger(request);
        var direct = inner.CaptureLedger(request);
        forwarded.Refusal.ShouldBe(direct.Refusal);
        forwarded.Detail.ShouldBe(direct.Detail);
        (forwarded.Value is null).ShouldBe(direct.Value is null);
        (forwarded.Value is null).ShouldBeFalse("real confinement ledger");

        var lines = new List<string>();
        var echoed = await driver.RunAsync(
            new DriverRequest("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine('c1199-driver')"], repo, OnOutput: line => { if (line is not null) lines.Add(line); }),
            cancellationToken);
        echoed.ExitCode.ShouldBe(0, echoed.Stderr);
        echoed.Exited.HasValue.ShouldBeTrue("driver exit stamp");
        lines.Contains("c1199-driver").ShouldBeTrue("driver output");
        var shown = await driver.RunAsync(
            new DriverRequest("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Write($env:GIT_CONFIG_GLOBAL)"], repo),
            cancellationToken);
        shown.ExitCode.ShouldBe(0, shown.Stderr);
        shown.Stdout.TrimEnd('\r', '\n').ShouldBe(environment.GlobalConfigPath);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Should.ThrowAsync<OperationCanceledException>(() =>
            driver.RunAsync(new DriverRequest("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], repo), cancel.Token));

        var sleep = driver.RunAsync(new DriverRequest("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], repo), cancellationToken);
        await Task.Delay(500, cancellationToken);
        driver.Kill(true);
        await sleep.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public void legacy_hostile_config_produces_hardened_status_differential(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var repo = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(repo);
        var hostile = Path.Combine(TempDir(), "hostile.gitconfig");
        File.WriteAllText(hostile, """
            [core]
            	autocrlf = true
            [antiphon]
            	sentinel = c1199-hostile
            """);
        var child = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_GLOBAL"] = hostile,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };
        CheckpointFixtureProcess.Git(repo, child, "init", "-b", "card1199");
        var payload = Path.Combine(repo, "payload.txt");
        File.WriteAllBytes(payload, "line\r\n"u8.ToArray());
        CheckpointFixtureProcess.Git(repo, child, "add", "--", "payload.txt");
        CheckpointFixtureProcess.Git(repo, child, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--no-verify");
        File.SetLastWriteTimeUtc(payload, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var sentinel = CheckpointFixtureProcess.Git(repo, child, "config", "--get", "antiphon.sentinel");
        sentinel.Trim().ShouldBe("c1199-hostile");
        var blob = CheckpointFixtureProcess.Git(repo, child, "cat-file", "blob", "HEAD:payload.txt");
        blob.ShouldBe("line\n");
        File.ReadAllBytes(payload).ShouldBe("line\r\n"u8.ToArray());
        var status = HardenedStatus(repo);
        status.Contains(" M payload.txt", StringComparison.Ordinal).ShouldBeTrue("dirty-baseline payload.txt\n" + status);
    }

    private static CheckpointFixtureProcess.Output Run(CheckpointFixtureGitEnvironment environment, string repository, string fileName, params string[] args)
    {
        var start = environment.Command(fileName, repository);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return CheckpointFixtureProcess.Run(start, 30_000);
    }

    private static string ListSources(ProcessStartInfo start, string? configFile = null)
    {
        start.ArgumentList.Add("nuget");
        start.ArgumentList.Add("list");
        start.ArgumentList.Add("source");
        start.ArgumentList.Add("--format");
        start.ArgumentList.Add("detailed");
        if (configFile is not null)
        {
            start.ArgumentList.Add("--configfile");
            start.ArgumentList.Add(configFile);
        }
        var output = CheckpointFixtureProcess.Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stderr);
        return output.Stdout;
    }

    private static void AssertSameSources(string before, string after)
    {
        var left = CheckpointFixtureGitEnvironment.ParseSources(before);
        var right = CheckpointFixtureGitEnvironment.ParseSources(after);
        (left.Count > 0).ShouldBeTrue("restore sources unresolved");
        left.Count.ShouldBe(right.Count, "restore source count");
        for (var i = 0; i < left.Count; i++)
        {
            left[i].Name.ShouldBe(right[i].Name, "restore source name");
            left[i].Url.Equals(right[i].Url, StringComparison.Ordinal).ShouldBeTrue("restore source mismatch: " + left[i].Name);
        }
    }

    private static Dictionary<string, string> ParentEnvironment()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            snapshot[(string)entry.Key!] = (string)entry.Value!;
        return snapshot;
    }

    private static void AssertParentUnchanged(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        before.Count.ShouldBe(after.Count, "parent environment size changed");
        foreach (var pair in before)
        {
            after.ContainsKey(pair.Key).ShouldBeTrue("parent environment lost " + pair.Key);
            after[pair.Key].Equals(pair.Value, StringComparison.Ordinal).ShouldBeTrue("parent environment changed " + pair.Key);
        }
    }

    private static string HardenedStatus(string repository)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repository };
        start.ArgumentList.Add("status");
        start.ArgumentList.Add("--porcelain=v1");
        var result = CheckpointProcessRunner.Run(start, 30_000, git: true);
        result.TimedOut.ShouldBeFalse("hardened status timed out");
        result.Exit.ShouldBe(0, result.Stderr);
        return result.Stdout;
    }

    // FixtureGitInProcess.TryRun (used by CheckpointFixtureGitEnvironment.Git and OutputCleanupTests) sets fixture
    // repositories up in process for the common shapes (init [-q] -b, add -A, add [-f] -- <paths>, the fixture commit,
    // rev-parse HEAD). What it does in process must leave the repository git
    // itself sees as the git commands would have.
    [Test]
    [Timeout(120_000)]
    public void fixture_setup_in_process_leaves_what_git_would(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = TempDir();
        using var git = CheckpointFixtureGitEnvironment.Create(Path.Combine(scope, "git-isolation"));
        var root = Directory.CreateDirectory(Path.Combine(scope, "repo")).FullName;

        git.Git(root, "init", "-b", "card1199");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin-*/\nobj/\n");
        Directory.CreateDirectory(Path.Combine(root, "App", "obj"));
        File.WriteAllText(Path.Combine(root, "App", "App.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(root, "App", "Program.cs"), "class P { }\r\n");
        File.WriteAllText(Path.Combine(root, "App", "obj", "project.assets.json"), "{}\n");
        git.Git(root, "add", "-A");
        git.Git(root, "add", "-f", "--", Path.Combine(root, "App", "obj", "project.assets.json"));
        git.Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");

        // What real git sees afterwards.
        string Real(params string[] args) => CheckpointFixtureProcess.Git(root, args);
        Real("rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("card1199");
        Real("status", "--porcelain", "--untracked-files=all").ShouldBeEmpty("everything staged is committed, and the rest is ignored");
        Real("ls-files").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)
            .ShouldBe([".gitignore", "App/App.csproj", "App/Program.cs", "App/obj/project.assets.json"]);
        Real("log", "-1", "--format=%an <%ae> %s").Trim().ShouldBe("card-1199 <card-1199@example.invalid> fixture");
        Real("config", "core.autocrlf").Trim().ShouldBe("false");
        Real("cat-file", "-p", "HEAD:App/Program.cs").ShouldBe("class P { }\r\n", "bytes stored as written");
        git.Git(root, "rev-parse", "HEAD").ShouldBe(Real("rev-parse", "HEAD"));
        git.GitChildren.ShouldBe(0, "the common fixture setup ran in process");

        // A second commit with the options the other way round, from a force-added ignored directory.
        Directory.CreateDirectory(Path.Combine(root, "Lib", "bin-probe"));
        File.WriteAllText(Path.Combine(root, "Lib", "bin-probe", "Lib.dll"), "dll\n");
        git.Git(root, "add", "-f", "--", Path.Combine(root, "Lib", "bin-probe"));
        git.Git(root, "-c", "user.email=card1734@example.invalid", "-c", "user.name=card1734",
            "-c", "commit.gpgsign=false", "commit", "--no-verify", "-m", "second");
        git.GitChildren.ShouldBe(0);
        Real("log", "-1", "--format=%s").Trim().ShouldBe("second");
        Real("ls-files", "--", "Lib").Trim().ShouldBe("Lib/bin-probe/Lib.dll");
        Real("status", "--porcelain", "--untracked-files=all").ShouldBeEmpty();

        // init -q -b and add -- <paths> (without -f: ignored files under a named directory are left out), in process.
        string RealIn(string at, params string[] args) => CheckpointFixtureProcess.Git(at, args);
        var quiet = Directory.CreateDirectory(Path.Combine(scope, "quiet")).FullName;
        git.Git(quiet, "init", "-q", "-b", "inner");
        File.WriteAllText(Path.Combine(quiet, ".gitignore"), "obj/\n");
        Directory.CreateDirectory(Path.Combine(quiet, "src", "obj"));
        File.WriteAllText(Path.Combine(quiet, "src", "kept.cs"), "class K { }\n");
        File.WriteAllText(Path.Combine(quiet, "src", "obj", "out.o"), "o\n");
        File.WriteAllText(Path.Combine(quiet, "loose.txt"), "loose\n");
        git.Git(quiet, "add", "--", "src", Path.Combine(quiet, ".gitignore"));
        git.Git(quiet, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");
        git.GitChildren.ShouldBe(0);
        RealIn(quiet, "rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe("inner");
        RealIn(quiet, "ls-files").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)
            .ShouldBe([".gitignore", "src/kept.cs"]);
        RealIn(quiet, "status", "--porcelain", "--untracked-files=all").Trim().ShouldBe("?? loose.txt");

        // A nested repository goes to git, which stages it as a gitlink.
        var vendor = Directory.CreateDirectory(Path.Combine(quiet, "vendor")).FullName;
        git.Git(vendor, "init", "-b", "vendor");
        File.WriteAllText(Path.Combine(vendor, "v.txt"), "v\n");
        git.Git(vendor, "add", "-A");
        git.Git(vendor, "-c", "user.email=v@example.invalid", "-c", "user.name=v",
            "-c", "commit.gpgsign=false", "commit", "-m", "vendor", "--no-verify");
        git.GitChildren.ShouldBe(0);
        git.Git(quiet, "add", "-A");
        git.GitChildren.ShouldBe(1, "add -A over a nested repository went to git");
        RealIn(quiet, "ls-files", "-s", "--", "vendor").ShouldStartWith("160000 ");

        // Where git refuses, git answers (and refuses): each of these goes to a git child, which fails.
        void Refused(string repository, string because, params string[] args)
        {
            var before = git.GitChildren;
            Should.Throw<ShouldAssertException>(() => git.Git(repository, args)).Message.ShouldContain(because);
            git.GitChildren.ShouldBe(before + 1);
        }
        Refused(root, "did not match any files", "add", "-f", "--", Path.Combine(root, "missing.txt"));
        Refused(quiet, "ignored", "add", "--", "src/obj/out.o");
        Refused(quiet, "did not match any files", "add", "--", "loose.txt/");
        File.WriteAllText(Path.Combine(root, "staged.txt"), "staged\n");
        git.Git(root, "add", "-A");
        Refused(root, "empty commit message", "-c", "user.email=a@example.invalid", "-c", "user.name=a",
            "-c", "commit.gpgsign=false", "commit", "-m", " ", "--no-verify");
        Refused(Directory.CreateDirectory(Path.Combine(scope, "bad-branch")).FullName, "invalid initial branch name", "init", "-b", "bad..name");
        var fresh = Directory.CreateDirectory(Path.Combine(scope, "fresh")).FullName;
        var beforeInit = git.GitChildren;
        git.Git(fresh, "init", "-b", "card1199");
        git.GitChildren.ShouldBe(beforeInit, "a fresh init runs in process");
        Refused(fresh, "nothing to commit", "-c", "user.email=a@example.invalid", "-c", "user.name=a",
            "-c", "commit.gpgsign=false", "commit", "-m", "empty", "--no-verify");
    }
}
