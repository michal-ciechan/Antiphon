using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1432 S1 (R3, V-1/V-2): no server git child runs auto gc or auto maintenance.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ServerGitProcessTests
{
    [Test]
    public async Task C1432_land_git_reads_gc_auto_zero()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "config", "gc.auto", "6700");
        await fixture.RequiredAsync(fixture.Repository, "config", "maintenance.auto", "true");

        (await ReadAsync(fixture, "gc.auto")).ShouldBe("0", "the repository's own gc.auto=6700 must be overridden");
        (await ReadAsync(fixture, "maintenance.auto")).ShouldBe("false");
        // The fixture's GIT_CONFIG_COUNT=3 entries were appended to, not replaced.
        (await ReadAsync(fixture, "commit.gpgSign")).ShouldBe("false");
    }

    [Test]
    public void C1432_append_preserves_existing_entries()
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = "2";
        start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
        start.Environment["GIT_CONFIG_VALUE_0"] = "false";
        start.Environment["GIT_CONFIG_KEY_1"] = "core.hooksPath";
        start.Environment["GIT_CONFIG_VALUE_1"] = "/none";
        start.ArgumentList.Add("status");

        ServerGitProcess.DisableAutoMaintenance(start);

        start.Environment["GIT_CONFIG_COUNT"].ShouldBe("4");
        start.Environment["GIT_CONFIG_KEY_0"].ShouldBe("commit.gpgSign");
        start.Environment["GIT_CONFIG_VALUE_0"].ShouldBe("false");
        start.Environment["GIT_CONFIG_KEY_1"].ShouldBe("core.hooksPath");
        start.Environment["GIT_CONFIG_VALUE_1"].ShouldBe("/none");
        start.Environment["GIT_CONFIG_KEY_2"].ShouldBe("gc.auto");
        start.Environment["GIT_CONFIG_VALUE_2"].ShouldBe("0");
        start.Environment["GIT_CONFIG_KEY_3"].ShouldBe("maintenance.auto");
        start.Environment["GIT_CONFIG_VALUE_3"].ShouldBe("false");
        start.ArgumentList.ShouldBe(["status"], "the argument vector the test seams read is unchanged");

        var empty = new ProcessStartInfo("git");
        empty.Environment.Remove("GIT_CONFIG_COUNT");
        ServerGitProcess.DisableAutoMaintenance(empty);
        empty.Environment["GIT_CONFIG_COUNT"].ShouldBe("2");
        empty.Environment["GIT_CONFIG_KEY_0"].ShouldBe("gc.auto");
        empty.Environment["GIT_CONFIG_KEY_1"].ShouldBe("maintenance.auto");
    }

    [Test]
    [Arguments("x")]
    [Arguments("-1")]
    [Arguments(" ")]
    public void C1432_invalid_count_throws(string count)
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = count;
        Should.Throw<InvalidOperationException>(() => ServerGitProcess.DisableAutoMaintenance(start))
            .Message.ShouldBe("git_config_count_invalid");
        start.Environment["GIT_CONFIG_COUNT"].ShouldBe(count, "an invalid count is never overwritten");
    }

    // D-2: GitTool (agent-supplied git) and the runner are deliberately excluded.
    private static readonly (string File, int Sites)[] Roster =
    [
        ("server/Infrastructure/Git/LandingGit.cs", 2),
        ("server/Infrastructure/Git/GitService.cs", 2),
        ("server/Infrastructure/Git/WorktreeManager.cs", 1),
        ("server/Infrastructure/Git/CardFileRepository.cs", 1),
        ("server/Application/Services/GitWorkspaceService.cs", 2),
        ("server/Application/Services/DelegationWorktreeService.cs", 1),
        ("server/Application/Services/DelegationWorkspaceResolver.cs", 1),
        ("server/Application/Services/OrchestratorWorkspaceLayout.cs", 1),
        ("server/Application/Services/AgentTaskReplyService.cs", 1),
        ("server/Infrastructure/ExternalChanges/ChangeDetectionService.cs", 1),
    ];

    [Test]
    public void C1432_every_server_git_start_site_disables_auto_maintenance()
    {
        Roster.Sum(r => r.Sites).ShouldBe(13, "T-1 names thirteen server git start sites");
        var root = RepositoryRoot();
        var missing = new List<string>();
        foreach (var (file, sites) in Roster)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            var starts = Regex.Matches(text, @"new (System\.Diagnostics\.)?ProcessStartInfo\b").Select(m => m.Index).ToList();
            starts.Count.ShouldBe(sites, $"{file} ProcessStartInfo constructions");
            for (var i = 0; i < starts.Count; i++)
            {
                // Each construction's body runs to the next construction (or the file end); the guard
                // must sit between it and the process start.
                var end = i + 1 < starts.Count ? starts[i + 1] : text.Length;
                var body = text[starts[i]..end];
                var guard = body.IndexOf("ServerGitProcess.DisableAutoMaintenance(", StringComparison.Ordinal);
                var launch = Regex.Match(body, @"(Process\.Start\(|StartProcess\(|\.Start\(\))");
                if (guard < 0 || (launch.Success && launch.Index < guard))
                    missing.Add($"{file}:{LineOf(text, starts[i])}");
            }
        }
        missing.ShouldBeEmpty();
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static async Task<string> ReadAsync(LandingGitFixture fixture, string key)
    {
        var result = await fixture.Git.RunAsync(fixture.Repository, ["config", "--get", key], CancellationToken.None);
        result.Succeeded.ShouldBeTrue(key);
        return result.Output.Trim();
    }
}
