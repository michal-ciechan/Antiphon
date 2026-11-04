using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1704 S1 (O-1..O-5): <see cref="IGitOperationObserver"/> is told once per git call, with the argv, the exit
/// and whether a process ran; it never changes git. Real repositories (F-1 <c>plain</c>).
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class C1704GitObserverTests
{
    private static C1704Corpus _corpus = null!;

    [Before(Class)]
    public static async Task BuildCorpus() => _corpus = await C1704Corpus.CreateAsync();

    [After(Class)]
    public static async Task DropCorpus() => await _corpus.DisposeAsync();

    /// <summary>O-1 (G-6): one record per call; process records equal the started processes, argv and exit equal.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task One_record_per_process_with_its_argv(CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        var observer = new RecordingObserver();
        var git = _corpus.Landing(observer);
        var results = new List<(string[] Argv, LandingGitResult Result)>();

        var commit = await git.ReadCommitAsync(repo, "HEAD", ct);
        results.Add((GitReadArguments.Commit("HEAD"), commit.Result));
        var missing = await git.ReadRefAsync(repo, "refs/heads/c1704-missing", ct);
        missing.State.ShouldBe(GitRefState.Missing);
        results.Add((GitReadArguments.Ref("refs/heads/c1704-missing"), missing.Result));
        string[] failing = ["rev-parse", "--verify", "c1704-nope^{commit}"];
        var failed = await git.RunAsync(repo, failing, ct);
        failed.ExitCode.ShouldBe(128);
        results.Add((failing, failed));
        string[] owned = ["status", "--porcelain=v1", "-z", "--untracked-files=no"];
        var ownedResult = await git.RunOwnedAsync(repo, owned, (_, _, _) => Task.CompletedTask, ct);
        results.Add((owned, ownedResult));

        var records = observer.Records;
        records.Count.ShouldBe(results.Count, "exactly one record per git call");
        for (var i = 0; i < results.Count; i++)
        {
            records[i].Arguments.ShouldBe(results[i].Argv);
            records[i].ExitCode.ShouldBe(results[i].Result.ExitCode);
            records[i].Repository.ShouldBe(repo);
            records[i].Backend.ShouldBe(LandGitBackendNames.Cli);
            records[i].Kind.ShouldBeOneOf(GitOperationKind.Process, GitOperationKind.InProcess);
        }
        records.Count(r => r.Kind == GitOperationKind.Process).ShouldBe(git.Started, "every started process is recorded once");
        records[2].Kind.ShouldBe(GitOperationKind.Process, "a git failure always comes from git");
        records[3].Kind.ShouldBe(GitOperationKind.Process, "an owned command always runs git");
    }

    /// <summary>O-2 (G-7): the stdin path records once per process.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Stdin_path_records_once(CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        var observer = new RecordingObserver();
        var git = _corpus.Landing(observer);

        string[] checkAttr = ["check-attr", "--stdin", "-z", "text"];
        var attributes = await git.StdinAsync(repo, checkAttr, Encoding.UTF8.GetBytes("a.txt\0"), ct);
        attributes.ExitCode.ShouldBe(0);
        var land = $"refs/antiphon/land/{Guid.NewGuid():N}/c1704";
        var head = _corpus.Required(repo, "rev-parse", "HEAD").Trim();
        _corpus.Required(repo, "update-ref", land, head);
        var before = observer.Records.Count;
        var deleted = await git.UpdateRefsAsync(repo, [(land, head)], ct);
        deleted.Succeeded.ShouldBeTrue(deleted.Diagnostic);

        var records = observer.Records;
        records.Count(r => r.Kind == GitOperationKind.Stdin).ShouldBe(2);
        records[0].Kind.ShouldBe(GitOperationKind.Stdin);
        records[0].Arguments.ShouldBe(checkAttr);
        records[0].ExitCode.ShouldBe(0);
        var batch = records.Skip(before).Where(r => r.Kind == GitOperationKind.Stdin).ShouldHaveSingleItem();
        batch.Arguments.ShouldContain("update-ref");
        batch.Arguments.ShouldContain("--stdin");
        batch.ExitCode.ShouldBe(0);
    }

    /// <summary>O-3 (G-6, G-7): over a real harness land every started process has exactly one process/stdin record,
    /// and every RunAsync-level call is recorded.</summary>
    [Test]
    [Timeout(180_000)]
    public async Task Harness_land_records_every_process_once(CancellationToken ct)
    {
        var observer = new RecordingObserver();
        await using var land = new LandingSafetyHarness();
        land.Fixture.Git.Relay!.Target = observer;
        await land.InitializeAsync();
        await land.AddSourceAsync();
        var skip = observer.Records.Count;
        lock (land.Fixture.Git.Started) land.Fixture.Git.Started.Clear();
        land.Fixture.Git.Commands.Clear();
        land.Fixture.Git.Channels.Clear();
        (await land.RunAsync()).ShouldBe(LandRunResult.Complete);

        var records = observer.Records.Skip(skip).ToList();
        List<string> started;
        lock (land.Fixture.Git.Started) started = land.Fixture.Git.Started.Select(s => Key(s.Directory, s.Arguments)).ToList();
        var processRecords = records.Where(r => r.Kind != GitOperationKind.InProcess).Select(r => Key(r.Repository, r.Arguments)).ToList();
        Difference(started, processRecords).ShouldBeEmpty("started processes without exactly one record");
        Difference(processRecords, started).ShouldBeEmpty("records without a started process");
        var commands = land.Fixture.Git.Commands.Select(c => Key(c.Directory, c.Arguments)).ToList();
        Difference(commands, records.Select(r => Key(r.Repository, r.Arguments)).ToList())
            .ShouldBeEmpty("every RunAsync-level call that reached LandingGit is recorded");
        Console.WriteLine($"c1704 O-3 records={records.Count} process={processRecords.Count} started={started.Count} commands={commands.Count}");
    }

    private static string Key(string directory, IEnumerable<string> arguments) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + "|" + string.Join('\u001f', arguments);

    private static List<string> Difference(List<string> left, List<string> right)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in left) counts[key] = counts.GetValueOrDefault(key) + 1;
        foreach (var key in right) counts[key] = counts.GetValueOrDefault(key) - 1;
        return counts.Where(p => p.Value > 0).Select(p => $"{p.Value}x {p.Key}").ToList();
    }

    /// <summary>O-4 (G-9): an observer that throws never changes the git result.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("process")]
    [Arguments("stdin")]
    public async Task Observer_exceptions_never_fail_git(string when, CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        var quiet = _corpus.Landing();
        var throwing = new RecordingObserver { Throw = true };
        var git = _corpus.Landing(throwing);
        if (when == "process")
        {
            string[] argv = ["status", "--porcelain=v1", "-z", "--untracked-files=no"];
            var expected = await quiet.RunAsync(repo, argv, ct);
            var actual = await git.RunAsync(repo, argv, ct);
            actual.ShouldBe(expected);
        }
        else
        {
            string[] argv = ["check-attr", "--stdin", "-z", "text"];
            var input = Encoding.UTF8.GetBytes("a.txt\0");
            var expected = await quiet.StdinAsync(repo, argv, input, ct);
            var actual = await git.StdinAsync(repo, argv, input, ct);
            actual.ExitCode.ShouldBe(expected.ExitCode);
            actual.Output.ShouldBe(expected.Output);
        }
        throwing.Records.Count.ShouldBe(1);
        git.ObserverFailures.ShouldBe(1);
    }

    /// <summary>O-5 (G-8): a process killed at its budget is recorded exactly once, with no exit code.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Timed_out_or_thrown_process_records_once(CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        var observer = new RecordingObserver();
        var git = _corpus.Landing(observer);
        git.SwapStart = start =>
        {
            var sleeper = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("powershell") { ArgumentList = { "-NoProfile", "-Command", "Start-Sleep 5" } }
                : new ProcessStartInfo("sleep") { ArgumentList = { "5" } };
            sleeper.WorkingDirectory = start.WorkingDirectory;
            sleeper.UseShellExecute = false;
            sleeper.RedirectStandardOutput = true;
            sleeper.RedirectStandardError = true;
            sleeper.CreateNoWindow = true;
            return sleeper;
        };
        var thrown = await Should.ThrowAsync<TimeoutException>(() => git.RunAsync(repo, ["status", "--porcelain=v1"],
            new LandingGitRunOptions(Budget: TimeSpan.FromMilliseconds(200)), ct));
        thrown.Message.ShouldBe("git_timeout");
        var record = observer.Records.ShouldHaveSingleItem();
        record.Kind.ShouldBe(GitOperationKind.Process);
        record.ExitCode.ShouldBeNull();
        record.Arguments.ShouldBe(["status", "--porcelain=v1"]);
    }
}
