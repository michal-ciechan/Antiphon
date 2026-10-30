using System.Collections;
using System.Diagnostics;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
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
}
