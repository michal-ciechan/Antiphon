using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-1704: the nine typed reads, their expected argv (today's call-site commands) and a uniform call.</summary>
internal static class C1704Reads
{
    public const string Repo = "/c1704/repo";
    public const string Oid = "0123456789abcdef0123456789abcdef01234567";
    public const string Other = "89abcdef0123456789abcdef0123456789abcdef";

    public static readonly string[] Methods =
        ["commit", "ancestry", "ref", "symbolic-ref", "git-dir", "config", "tree", "diff-tree", "status"];

    public static string[] ExpectedArgv(string method) => method switch
    {
        "commit" => ["rev-parse", "--verify", "HEAD^{commit}"],
        "ancestry" => ["merge-base", "--is-ancestor", Oid, Other],
        "ref" => ["show-ref", "--exists", "refs/heads/master"],
        "symbolic-ref" => ["symbolic-ref", "-q", "HEAD"],
        "git-dir" => ["rev-parse", "--absolute-git-dir"],
        "config" => ["config", "--get", "core.autocrlf"],
        "tree" => ["ls-tree", "-r", "-z", Oid],
        "diff-tree" => ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", Oid, Other],
        "status" => ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"],
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    /// <summary>Calls the typed read and returns its record (every record carries the raw result).</summary>
    public static async Task<object> CallAsync(ILandingGit git, string method, CancellationToken ct = default) => method switch
    {
        "commit" => await git.ReadCommitAsync(Repo, "HEAD", ct),
        "ancestry" => await git.ReadAncestryAsync(Repo, Oid, Other, ct),
        "ref" => await git.ReadRefAsync(Repo, "refs/heads/master", ct),
        "symbolic-ref" => await git.ReadSymbolicRefAsync(Repo, "HEAD", ct),
        "git-dir" => await git.ReadGitDirectoryAsync(Repo, ct),
        "config" => await git.ReadConfigAsync(Repo, "core.autocrlf", ct),
        "tree" => await git.ReadTreeZAsync(Repo, Oid, ct),
        "diff-tree" => await git.ReadDiffTreeZAsync(Repo, Oid, Other, ct),
        "status" => await git.ReadWorktreeStatusAsync(Repo, true, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public static LandingGitResult ResultOf(object read) =>
        (LandingGitResult)read.GetType().GetProperty("Result")!.GetValue(read)!;

    /// <summary>Calls every typed read once and returns the recorded argv sequence.</summary>
    public static async Task<List<string>> TraceAllAsync(ILandingGit git, Func<IReadOnlyList<string[]>> recorded)
    {
        foreach (var method in Methods) await CallAsync(git, method);
        return recorded().Select(a => string.Join(' ', a)).ToList();
    }
}

/// <summary>CARD-1704 F-2: a <see cref="LandingGit"/> whose run overloads record and answer from a script; no process starts.</summary>
internal sealed class RecordingGit(LandGitBackend? backend = null, IGitOperationObserver? observer = null)
    : LandingGit(observer: observer, backend: backend)
{
    private readonly List<string[]> _calls = [];
    public IReadOnlyList<string[]> Calls { get { lock (_calls) return _calls.ToList(); } }
    public Func<IReadOnlyList<string>, LandingGitResult> Script { get; set; } = _ => new(0, "", "");

    private LandingGitResult Record(IReadOnlyList<string> arguments)
    {
        lock (_calls) _calls.Add(arguments.ToArray());
        return Script(arguments);
    }

    public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        => Task.FromResult(Record(arguments));
    public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments,
        LandingGitRunOptions options, CancellationToken ct) => Task.FromResult(Record(arguments));
    public override Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct) => Task.FromResult(Record(arguments));
    protected override Process? StartProcess(ProcessStartInfo start) => throw new InvalidOperationException("no_process_expected");
}

/// <summary>CARD-1704 F-2b: implements only the abstract members of <see cref="ILandingGit"/>; RunAsync records.</summary>
internal sealed class DirectFake : ILandingGit
{
    public List<string[]> Calls { get; } = [];
    public LandingGitResult Next { get; set; } = new(0, "", "");

    public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls.Add(arguments.ToArray());
        return Task.FromResult(Next);
    }
    public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha,
        string observationRef, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix,
        CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
    public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>CARD-1704 F-4: appends "session" on dispose to a shared log and counts live instances.</summary>
internal sealed class RecordingSession(List<string> log, int number) : IGitSession
{
    private static int _live;
    public static int Live => Volatile.Read(ref _live);
    public int Number { get; } = number;
    public bool Disposed { get; private set; }
    public string Backend => "Recording";

    public static RecordingSession Create(List<string> log, ref int count)
    {
        var number = Interlocked.Increment(ref count);
        lock (log) log.Add("create#" + number);
        Interlocked.Increment(ref _live);
        return new RecordingSession(log, number);
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Interlocked.Decrement(ref _live);
        lock (log) { log.Add("session"); log.Add("dispose#" + Number); }
    }

    private static GitAnswer<LandingGitResult> No => GitAnswer<LandingGitResult>.NotHandled("recording");
    public GitAnswer<LandingGitResult> ReadCommit(string repository, string revision) => No;
    public GitAnswer<LandingGitResult> ReadAncestry(string repository, string ancestor, string descendant) => No;
    public GitAnswer<LandingGitResult> ReadRef(string repository, string fullRef) => No;
    public GitAnswer<LandingGitResult> ReadSymbolicRef(string repository, string name) => No;
    public GitAnswer<LandingGitResult> ReadGitDirectory(string repository) => No;
    public GitAnswer<LandingGitResult> ReadConfig(string repository, string key) => No;
    public GitAnswer<LandingGitResult> ReadTreeZ(string repository, string treeish) => No;
    public GitAnswer<LandingGitResult> ReadDiffTreeZ(string repository, string from, string to) => No;
    public GitAnswer<LandingGitResult> ReadWorktreeStatus(string repository, bool untracked) => No;
}
