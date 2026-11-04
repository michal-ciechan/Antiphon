using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// CARD-1704 S1 (E-1..E-5): the E2E land evidence moves from <c>EvidenceGit</c>'s RunAsync override to
/// <see cref="ProtocolGitRecorder"/>, an observer of every git call. Portable (no land, no ConPty).
/// </summary>
[NotInParallel("C467LandDelivery")]
public class C1704ProtocolGitRecorderTests
{
    /// <summary>E-1: one committed record per git call, with the base fields plus backend and kind.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Recorder_commits_one_record_per_process(CancellationToken ct)
    {
        var (root, repo) = Scratch();
        try
        {
            var git = new LandingGit(observer: new ProtocolGitRecorder(root));
            (await git.ReadCommitAsync(repo, "HEAD", ct)).Sha.ShouldNotBeNull();
            (await git.ReadRefAsync(repo, "refs/heads/master", ct)).State.ShouldBe(GitRefState.Exists);
            (await git.ReadWorktreeStatusAsync(repo, true, ct)).Result.Succeeded.ShouldBeTrue();
            var head = Git(repo, "rev-parse", "HEAD");
            var pin = $"refs/antiphon/land/{Guid.NewGuid():N}/c1704";
            Git(repo, "update-ref", pin, head);
            (await git.UpdateRefsAsync(repo, [(pin, head)], ct)).Succeeded.ShouldBeTrue();

            var records = Directory.GetFiles(root, "protocol-git-*.json");
            records.Length.ShouldBe(4);
            Directory.GetFiles(root, "*.tmp").ShouldBeEmpty();
            var kinds = new List<string>();
            foreach (var file in records)
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
                var e = json.RootElement;
                e.GetProperty("repository").GetString().ShouldBe(repo);
                e.GetProperty("arguments").GetArrayLength().ShouldBeGreaterThan(0);
                e.TryGetProperty("ExitCode", out _).ShouldBeTrue();
                e.TryGetProperty("at", out _).ShouldBeTrue();
                e.GetProperty("pid").GetInt32().ShouldBe(Environment.ProcessId);
                e.GetProperty("backend").GetString().ShouldBe("Cli");
                kinds.Add(e.GetProperty("kind").GetString()!);
            }
            kinds.ShouldAllBe(k => k == "process" || k == "stdin" || k == "inproc");
            kinds.Count(k => k == "stdin").ShouldBe(1, "the update-ref --stdin batch is recorded");
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>E-2 (G-10): the recorder's records are what the C467 receipt wait counts as land progress.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Recorder_records_renew_protocol_progress(CancellationToken ct)
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var (scratch, repo) = Scratch();
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            var git = new LandingGit(observer: new ProtocolGitRecorder(f.Root));
            await git.ReadCommitAsync(repo, "HEAD", ct);
            await git.RunAsync(repo, ["status", "--porcelain=v1"], ct);
            f.ProtocolProgress().ShouldBe(2);
            f.ProtocolProgress().ShouldBe(Directory.GetFiles(f.Root, "protocol-git-*.json").LongLength);
        }
        finally
        {
            Directory.Delete(f.Root, true);
            Directory.Delete(scratch, true);
        }
    }

    /// <summary>E-3 (G-11): a recorder that cannot write never fails git.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Recorder_write_failure_never_fails_git(CancellationToken ct)
    {
        var (root, repo) = Scratch();
        try
        {
            var notADirectory = Path.Combine(root, "evidence-file");
            await File.WriteAllTextAsync(notADirectory, "x", ct);
            var git = new LandingGit(observer: new ProtocolGitRecorder(notADirectory));
            var expected = await new LandingGit().RunAsync(repo, ["status", "--porcelain=v1", "-z"], ct);
            var actual = await git.RunAsync(repo, ["status", "--porcelain=v1", "-z"], ct);
            actual.ShouldBe(expected);
            git.ObserverFailures.ShouldBe(0, "the recorder swallows its own I/O failure");
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>E-4 (M-3): an EvidenceGit stable-cache hit starts nothing and writes no record (the base behaviour kept).</summary>
    [Test]
    [Timeout(15_000)]
    public async Task EvidenceGit_stable_cache_hit_writes_no_record(CancellationToken ct)
    {
        var (root, repo) = Scratch();
        var evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        try
        {
            var type = typeof(LandDeliveryFixture).GetNestedType("EvidenceGit", BindingFlags.NonPublic).ShouldNotBeNull();
            var git = (LandingGit)Activator.CreateInstance(type, evidence)!;
            string[] argv = ["rev-parse", "--path-format=absolute", "--git-common-dir"];
            var first = await git.RunAsync(repo, argv, ct);
            var second = await git.RunAsync(repo, argv, ct);
            second.ShouldBe(first);
            Directory.GetFiles(evidence, "protocol-git-*.json").Length.ShouldBe(1);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>E-5 (G-3, the E2E half of T-0): no E2E double hides or implicitly implements a typed read.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task E2E_doubles_map_typed_reads_to_LandingGit(CancellationToken ct)
    {
        string[] reads = ["ReadCommitAsync", "ReadAncestryAsync", "ReadRefAsync", "ReadSymbolicRefAsync", "ReadGitDirectoryAsync",
            "ReadConfigAsync", "ReadTreeZAsync", "ReadDiffTreeZAsync", "ReadWorktreeStatusAsync"];
        var implementors = typeof(C1704ProtocolGitRecorderTests).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && typeof(ILandingGit).IsAssignableFrom(t))
            .ToList();
        implementors.ShouldContain(t => t.Name == "EvidenceGit");
        foreach (var type in implementors)
        {
            var map = type.GetInterfaceMap(typeof(ILandingGit));
            foreach (var name in reads)
            {
                var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);
                index.ShouldBeGreaterThanOrEqualTo(0, name);
                var expected = typeof(LandingGit).IsAssignableFrom(type) ? typeof(LandingGit) : typeof(ILandingGit);
                map.TargetMethods[index].DeclaringType.ShouldBe(expected, $"{type.FullName}.{name}");
            }
        }
        await Task.CompletedTask;
    }

    private static (string Root, string Repo) Scratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c1704-e2e-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-b", "master");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "a\n");
        Git(repo, "add", "a.txt");
        Git(repo, "-c", "user.name=C1704", "-c", "user.email=c1704@example.invalid", "-c", "commit.gpgSign=false", "commit", "-m", "a");
        return (root, repo);
    }

    private static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"scratch_git_failed:{arguments[0]}:{process.ExitCode}");
        return output.Trim();
    }
}
