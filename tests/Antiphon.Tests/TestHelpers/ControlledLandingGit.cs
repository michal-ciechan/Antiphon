using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0475 S4: in-memory ILandingGit. Supported command shapes are explicit; unexpected
/// calls throw. No default-success and no fallback to a real git process.
/// </summary>
internal sealed class ControlledLandingGit : ILandingGit, IDisposable
{
    private int _oid;
    private int _pid = 2_000_000_000;
    private int _observations;
    private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Commit> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _untracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _targetFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _targetIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _targetUntracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sequencer = new(StringComparer.Ordinal);
    private readonly HashSet<string> _targetSequencer = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Worktree> _worktrees = new(StringComparer.OrdinalIgnoreCase);
    private string _sourceHead;
    private string _sourceBranch;
    private string _targetHead;
    private string _targetBranch;
    private string _remoteTarget;
    private string _remoteSource;
    private string _endpoint;
    private bool _sourceLocked;
    private bool _sourcePrunable;
    private bool _targetLocked;
    private bool _targetPrunable;
    private bool _sourcePresent = true;
    private int? _existsErrorExit;

    public string Root { get; }
    public string Repository { get; }
    public string Source { get; }
    public string Remote { get; }
    public string CommonDir { get; }
    public string GitDirectory { get; }
    public string SourceGitDirectory { get; }
    public string SourceRef { get; }
    public string TargetRef { get; }
    public Guid TaskId { get; }
    public string SeedSha { get; }
    public string Fingerprint { get; }
    public List<string[]> Trace { get; } = [];
    public List<string[]> OwnedTrace { get; } = [];
    public Func<Task>? BeforeInspection { get; set; }
    public int InspectionCalls { get; private set; }
    public Func<int, LandSourceInspection?>? InjectInspection { get; set; }
    public Func<Task>? BeforeCommonDirectory { get; set; }
    public Func<string, IReadOnlyList<string>, Task<LandingGitResult?>>? BeforeCommand { get; set; }
    public Func<string, IReadOnlyList<string>, LandingGitResult, Task>? AfterCommand { get; set; }
    public Func<IReadOnlyList<string>, Task>? BeforeObservedCommand { get; set; }
    public Func<Task>? OnFirstRemoteObservation { get; set; }
    public Func<Task>? OnSecondRemoteObservation { get; set; }
    public Func<Task>? OnAncestorCheck { get; set; }
    public int ShowRefExistsErrorExit { get => _existsErrorExit ?? 0; set => _existsErrorExit = value; }
    public string? OverrideCommonDirectory { get; set; }
    public string? OverrideGitDirectory { get; set; }
    public string? OverrideRegisteredPath { get; set; }
    public bool RejectInspection { get; set; }

    public ControlledLandingGit(string? root = null, Guid? taskId = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "antiphon-c475-" + Guid.NewGuid().ToString("N"));
        TaskId = taskId ?? Guid.NewGuid();
        Repository = Path.Combine(Root, "canonical");
        Source = Path.Combine(Root, "trees", "source");
        Remote = Path.Combine(Root, "remote.git");
        CommonDir = Path.Combine(Root, "git-common");
        GitDirectory = CommonDir;
        SourceGitDirectory = Path.Combine(CommonDir, "worktrees", "source");
        SourceRef = $"refs/heads/feat/card-task-{TaskId:N}";
        TargetRef = "refs/heads/master";
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Remote);
        Directory.CreateDirectory(CommonDir);
        Directory.CreateDirectory(SourceGitDirectory);
        Directory.CreateDirectory(Path.Combine(CommonDir, "antiphon"));
        SeedSha = NextOid();
        _objects[SeedSha] = new Commit(SeedSha, []);
        _sourceHead = SeedSha;
        _targetHead = SeedSha;
        _sourceBranch = SourceRef;
        _targetBranch = TargetRef;
        _remoteTarget = SeedSha;
        _remoteSource = SeedSha;
        _refs[SourceRef] = SeedSha;
        _refs[TargetRef] = SeedSha;
        _endpoint = Remote.Replace('\\', '/');
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_endpoint)));
        File.WriteAllText(Path.Combine(Repository, "keep.txt"), "seed\n");
        File.WriteAllText(Path.Combine(Source, "keep.txt"), "seed\n");
        File.WriteAllText(Path.Combine(Repository, "fixture-owner.txt"), TaskId.ToString("N") + "\n");
        File.WriteAllText(Path.Combine(Source, "fixture-owner.txt"), TaskId.ToString("N") + "\n");
        _files["keep.txt"] = "seed\n";
        _index["keep.txt"] = "seed\n";
        _targetFiles["keep.txt"] = "seed\n";
        _targetIndex["keep.txt"] = "seed\n";
        _worktrees[Source] = new Worktree(Source, SourceRef, SeedSha);
        _worktrees[Repository] = new Worktree(Repository, TargetRef, SeedSha);
    }

    public string SourceHead => _sourceHead;
    public string TargetHead => _targetHead;
    public string RemoteTarget => _remoteTarget;
    public string RemoteSource => _remoteSource;
    public bool RemoteSourceMissing { get; set; }
    public string? SourceReadError { get; set; }
    public int SourceObservationAttempts { get; private set; }
    public Func<int, Task>? OnSourceObservation { get; set; }

    public void SetShowRefExistsError(int exit) => _existsErrorExit = exit;

    public async Task<string> RequiredAsync(string path, params string[] arguments)
    {
        var result = await RunAsync(path, arguments, CancellationToken.None);
        if (!result.Succeeded) throw new InvalidOperationException($"fixture_git_failed:{arguments[0]}:{result.ExitCode}");
        return result.Output;
    }

    public Task AssertRemoteSourceAsync()
    {
        _remoteSource.ShouldRetain(SeedSha);
        return Task.CompletedTask;
    }

    public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        => ExecuteAsync(repository, arguments, null, ct);

    public async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        => await ExecuteAsync(repository, arguments, started, ct);

    public bool? ProcessAlive { get; set; } = false;

    public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ProcessAlive);
    }

    public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
    }

    public async Task<string> CommonDirectoryAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (BeforeCommonDirectory is not null) await BeforeCommonDirectory();
        var full = Path.GetFullPath(repository);
        if (PathsEqual(full, Remote)) return Path.GetFullPath(Remote);
        return Path.GetFullPath(CommonDir);
    }

    public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var set = IsSource(repository) ? _sequencer : _targetSequencer;
        return Task.FromResult(set.Count > 0);
    }

    public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = new List<LandingRegistration>();
        foreach (var wt in _worktrees.Values)
        {
            var locked = PathsEqual(wt.Path, Source) ? _sourceLocked
                : PathsEqual(wt.Path, Repository) ? _targetLocked : false;
            var prunable = PathsEqual(wt.Path, Source) ? _sourcePrunable
                : PathsEqual(wt.Path, Repository) ? _targetPrunable : false;
            rows.Add(new(wt.Path, wt.Branch, wt.Head, locked, prunable));
        }
        return Task.FromResult<IReadOnlyList<LandingRegistration>>(rows);
    }

    public async Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct)
    {
        InspectionCalls++;
        if (BeforeInspection is not null) await BeforeInspection();
        if (InjectInspection?.Invoke(InspectionCalls) is { } injected) return injected;
        if (coordinates.SourceFullRef == coordinates.TargetFullRef)
            return new(null, "source_equals_target");
        if (!PathsEqual(coordinates.RepositoryPath, Repository))
            return new(null, "wrong_repository");
        if (!PathsEqual(coordinates.WorktreePath, Source) || !_sourcePresent)
            return new(null, "registration_mismatch");
        if (_sourceLocked || _sourcePrunable) return new(null, "registration_unavailable");
        if (_sourceBranch != coordinates.SourceFullRef) return new(null, "source_branch_mismatch");
        if (_sequencer.Count > 0) return new(null, "active_sequencer");
        var status = StatusOutput(source: true);
        if (status.Length != 0) return new(null, "source_dirty");
        var ignored = IgnoredPaths();
        var snapshot = new LandSourceSnapshot(coordinates,
            Path.GetFullPath(OverrideCommonDirectory ?? CommonDir),
            Path.GetFullPath(OverrideRegisteredPath ?? Source),
            Path.GetFullPath(OverrideGitDirectory ?? SourceGitDirectory),
            _sourceBranch, _sourceHead, _sourceHead, status, ignored);
        return new(snapshot, RejectInspection ? "source_rejected" : null);
    }

    public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (targetFullRef != TargetRef) throw new ArgumentException("invalid_branch");
        return Task.FromResult(new LandingDestination("origin", TargetRef, Fingerprint));
    }

    public async Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
        string sourceSha, string observationRef, CancellationToken ct)
    {
        _observations++;
        if (_observations == 1 && OnFirstRemoteObservation is not null) await OnFirstRemoteObservation();
        if (_observations == 2 && OnSecondRemoteObservation is not null) await OnSecondRemoteObservation();
        var fetch = await RunAsync(repository,
            ["fetch", "--no-tags", "--no-write-fetch-head", _endpoint, $"{destination.FullRef}:{observationRef}/pin"], ct);
        if (!fetch.Succeeded) return new(null, false, "remote_fetch_failed");
        var observed = _remoteTarget;
        var ancestry = await RunAsync(repository, ["merge-base", "--is-ancestor", sourceSha, observed], ct);
        return ancestry.ExitCode switch
        {
            0 => new(observed, true, null),
            1 => new(observed, false, null),
            _ => new(null, false, "remote_ancestry_error"),
        };
    }

    public async Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
        string observationPrefix, CancellationToken ct)
    {
        SourceObservationAttempts++;
        if (OnSourceObservation is not null) await OnSourceObservation(SourceObservationAttempts);
        if (SourceReadError is not null) return new(null, null, CurrentFingerprint(), SourceReadError);
        if (RemoteSourceMissing) return new(null, null, CurrentFingerprint(), "source_remote_missing");
        var pin = $"{observationPrefix}/{Guid.NewGuid():N}";
        var fetch = await RunAsync(repository,
            ["fetch", "--no-tags", "--no-write-fetch-head", _endpoint, $"{sourceFullRef}:{pin}"], ct);
        if (!fetch.Succeeded) return new(null, null, CurrentFingerprint(), "source_remote_fetch_failed");
        return new(_remoteSource, pin, CurrentFingerprint(), null);
    }

    public void SetRemoteSource(string sha) => _remoteSource = sha;

    public string AdvanceRemoteSource()
    {
        var sha = NextOid();
        _objects[sha] = new Commit(sha, [_remoteSource]);
        _remoteSource = sha;
        return sha;
    }

    public string DivergeRemoteSource()
    {
        var sha = NextOid();
        _objects[sha] = new Commit(sha, [SeedSha]);
        _remoteSource = sha;
        return sha;
    }

    public void RewindSource(string sha)
    {
        _sourceHead = sha;
        _refs[_sourceBranch] = sha;
        _worktrees[Source] = _worktrees[Source] with { Head = sha };
    }

    public void MarkSourceSequencer() => _sequencer.Add("rebase-apply");

    public void SwitchSourceBranch(string branch)
    {
        var full = branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
        _sourceBranch = full;
        _refs[full] = _sourceHead;
        _worktrees[Source] = _worktrees[Source] with { Branch = full };
    }

    public void SetEndpoint(string endpoint) => _endpoint = endpoint.Replace('\\', '/');

    public async Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct)
    {
        if (sha.Length is not (40 or 64)) return new(1, "", "invalid_recovery_identity");
        if (_refs.TryGetValue(recoveryRef, out var existing))
            return existing == sha ? new(0, "", "") : new(1, "", "recovery_ref_collision");
        var existence = await RunAsync(repository, ["show-ref", "--exists", recoveryRef], ct);
        if (existence.ExitCode != 2) return new(1, "", "recovery_ref_query_error");
        return await RunAsync(repository, ["update-ref", recoveryRef, sha, new string('0', sha.Length)], ct);
    }

    public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct)
        => RunAsync(repository, ["push", _endpoint, $"{sha}:{destination.FullRef}"], ct);

    public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        => RunOwnedAsync(repository, ["push", _endpoint, $"{sha}:{destination.FullRef}"], started, ct);

    public void SetRemoteContainsSource() => _remoteTarget = _sourceHead;

    public void RewriteRemoteAwayFromSource()
    {
        var other = NextOid();
        _objects[other] = new Commit(other, [SeedSha]);
        _remoteTarget = other;
    }

    public void SetPin(string name, string sha) => _refs[name] = sha;

    public void DeleteRef(string name) => _refs.Remove(name);

    public void LockSource() => _sourceLocked = true;

    public void SetSourcePrunable() => _sourcePrunable = true;

    public void SetAmbiguousTargetRegistrations()
    {
        var extra = Path.Combine(Root, "canonical-alias");
        Directory.CreateDirectory(extra);
        _worktrees[extra] = new Worktree(extra, TargetRef, _targetHead);
    }

    public void SetTargetLocked() => _targetLocked = true;
    public void SetTargetPrunable() => _targetPrunable = true;

    private async Task<LandingGitResult> ExecuteAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task>? started, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateCommand(arguments);
        Trace.Add(arguments.ToArray());
        if (BeforeCommand is not null && await BeforeCommand(repository, arguments) is { } injected)
            return injected;
        if (BeforeObservedCommand is not null) await BeforeObservedCommand(arguments);
        if (started is not null)
        {
            OwnedTrace.Add(arguments.ToArray());
            var pid = ++_pid;
            var ticks = DateTime.UtcNow.Ticks;
            await started(pid, ticks, ct);
        }
        var result = Dispatch(repository, arguments);
        if (AfterCommand is not null) await AfterCommand(repository, arguments, result);
        return result;
    }

    private void ValidateCommand(IReadOnlyList<string> args)
    {
        // Validate the entire vector before fault hooks or owned-process callbacks. A
        // recognized verb is not evidence that this model implements its options.
        var supported = args switch
        {
            ["rev-parse", "--path-format=absolute", "--git-common-dir"] => true,
            ["rev-parse", "--absolute-git-dir"] => true,
            ["rev-parse", var revision] => IsRevision(revision),
            ["rev-parse", "--verify", var revision] => IsRevision(revision),
            ["symbolic-ref", "-q", var name] => name == "HEAD" || IsFullRef(name),
            ["show-ref", "--exists", var name] => IsFullRef(name),
            ["show-ref", "--verify", "--hash", var name] => IsFullRef(name),
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"] => true,
            ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"] => true,
            ["worktree", "list", "--porcelain", "-z"] => true,
            ["worktree", "remove", "--", var path] => PathsEqual(path, Source),
            ["worktree", "lock", var path] => PathsEqual(path, Source),
            ["fetch", "--no-tags", "--no-write-fetch-head", var endpoint, var spec] =>
                endpoint == _endpoint && IsSourceOrTargetFetch(spec),
            ["ls-remote", "--refs", "--exit-code", var endpoint, var fullRef] =>
                endpoint == _endpoint && (fullRef == TargetRef || fullRef == SourceRef),
            ["merge-base", "--is-ancestor", var source, var target] => LooksOid(source) && LooksOid(target),
            ["update-ref", var name, var sha, var expected] => IsFullRef(name) && LooksOid(sha) && LooksOid(expected),
            ["update-ref", "--no-deref", "-d", var name, var expected] => IsFullRef(name) && LooksOid(expected),
            ["rebase", "--abort"] => true,
            ["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", var sha] => LooksOid(sha),
            ["-c", "merge.autoStash=false", "merge", "--ff-only", var sha] => LooksOid(sha),
            ["diff", "--name-only", "--diff-filter=U", "-z"] => true,
            ["diff", "--cached"] => true,
            ["commit", "-m", var message] => !string.IsNullOrEmpty(message),
            ["commit", "--allow-empty", "-m", var message] => !string.IsNullOrEmpty(message),
            ["checkout", "-b", var branch] => IsFullRef("refs/heads/" + branch),
            ["add", var path] => path == "." || IsRelativeFile(path),
            ["push", var endpoint, var spec] => endpoint == _endpoint && IsPushSpec(spec),
            _ => false,
        };
        if (!supported) throw Unsupported(args);
    }

    private bool IsSourceOrTargetFetch(string spec)
    {
        var colon = spec.LastIndexOf(':');
        if (colon <= 0) return false;
        var from = spec[..colon];
        var pin = spec[(colon + 1)..];
        return (from == TargetRef || from == SourceRef) && IsFullRef(pin);
    }

    private bool IsPushSpec(string spec)
    {
        var parts = spec.Split(':');
        return parts.Length == 2 && LooksOid(parts[0]) && (parts[1] == TargetRef || parts[1] == SourceRef);
    }

    private static bool IsFullRef(string name) => name.StartsWith("refs/", StringComparison.Ordinal)
        && name.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && !part.EndsWith('.')
            && !part.EndsWith(".lock", StringComparison.Ordinal)
            && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        && !name.Contains("..", StringComparison.Ordinal);

    private static bool IsRevision(string revision)
    {
        var name = revision.EndsWith("^{commit}", StringComparison.Ordinal) ? revision[..^9] : revision;
        return name == "HEAD" || LooksOid(name) || IsFullRef(name);
    }

    private static bool IsRelativeFile(string path) => !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path) && !path.StartsWith('-')
        && path.Replace('\\', '/').Split('/').All(part => part is not ("" or "." or "..") && !part.Contains(':'));

    private LandingGitResult Dispatch(string repository, IReadOnlyList<string> args)
    {
        if (args.Count == 0) throw Unsupported(args);
        if (args[0] == "rev-parse") return RevParse(repository, args);
        if (args[0] == "symbolic-ref") return SymbolicRef(repository, args);
        if (args[0] == "show-ref") return ShowRef(args);
        if (args[0] == "status") return new(0, StatusOutput(IsSource(repository)), "");
        if (args[0] == "ls-files") return new(0, string.Join('\0', IgnoredPaths()) + (IgnoredPaths().Length == 0 ? "" : "\0"), "");
        if (args[0] == "worktree" && args.Count > 1 && args[1] == "list") return WorktreeList();
        if (args[0] == "worktree" && args.Count > 1 && args[1] == "remove") return WorktreeRemove(args);
        if (args[0] == "worktree" && args.Count > 1 && args[1] == "lock")
        {
            _sourceLocked = true;
            return new(0, "", "");
        }
        if (args[0] == "ls-remote")
        {
            var fullRef = args[^1];
            if (fullRef == SourceRef)
            {
                if (RemoteSourceMissing) return new(2, "", "git_exit_2");
                if (SourceReadError is not null) return new(1, "", SourceReadError);
                return new(0, $"{_remoteSource}\t{SourceRef}\n", "");
            }
            if (fullRef == TargetRef)
                return new(0, $"{_remoteTarget}\t{TargetRef}\n", "");
            return new(2, "", "git_exit_2");
        }
        if (args[0] == "fetch")
        {
            var dest = args[^1];
            var colon = dest.LastIndexOf(':');
            if (colon > 0)
            {
                var from = dest[..colon];
                var pin = dest[(colon + 1)..];
                _refs[pin] = from == SourceRef || from.StartsWith(SourceRef, StringComparison.Ordinal) ? _remoteSource : _remoteTarget;
            }
            return new(0, "", "");
        }
        if (args[0] == "merge-base" && args.Contains("--is-ancestor"))
        {
            if (OnAncestorCheck is not null) OnAncestorCheck().GetAwaiter().GetResult();
            var source = args[^2];
            var observed = args[^1];
            return new(IsAncestor(source, observed) ? 0 : 1, "", "");
        }
        if (args[0] == "update-ref") return UpdateRef(args);
        if (args.Contains("rebase") && args.Contains("--abort")) return new(0, "", "");
        if (args.Contains("rebase"))
        {
            var onto = args[^1];
            var rebased = NextOid();
            _objects[rebased] = new Commit(rebased, [onto]);
            _sourceHead = rebased;
            _refs[_sourceBranch] = rebased;
            _worktrees[Source] = _worktrees[Source] with { Head = rebased };
            return new(0, "", "") { RebaseHeadSha = rebased };
        }
        if (args.Contains("merge") && args.Contains("--ff-only"))
        {
            var sha = args[^1];
            if (IsSource(repository))
            {
                _sourceHead = sha;
                _refs[_sourceBranch] = sha;
                _worktrees[Source] = _worktrees[Source] with { Head = sha };
            }
            else
            {
                _targetHead = sha;
                _refs[TargetRef] = sha;
                _worktrees[Repository] = _worktrees[Repository] with { Head = sha };
                if (_files.TryGetValue("feature.txt", out var feature))
                {
                    _targetFiles["feature.txt"] = feature;
                    File.WriteAllText(Path.Combine(Repository, "feature.txt"), feature);
                }
            }
            return new(0, "", "");
        }
        if (args[0] == "diff")
        {
            if (args.Contains("--cached"))
            {
                var index = IsSource(repository) ? _index : _targetIndex;
                var files = IsSource(repository) ? _files : _targetFiles;
                var staged = new StringBuilder();
                foreach (var (path, content) in index)
                    if (!files.TryGetValue(path, out var committed) || committed != content)
                        staged.Append(content);
                return new(0, staged.ToString(), "");
            }
            return new(0, "", "");
        }
        if (args[0] == "commit")
        {
            var files = IsSource(repository) ? _files : _targetFiles;
            var index = IsSource(repository) ? _index : _targetIndex;
            foreach (var (path, content) in index.ToArray())
                files[path] = content;
            (IsSource(repository) ? _untracked : _targetUntracked).Clear();
            var sha = NextOid();
            _objects[sha] = new Commit(sha, [HeadOf(repository)]);
            SetHead(repository, sha);
            return new(0, "", "");
        }
        if (args[0] == "checkout" && args.Contains("-b"))
        {
            var branch = args[^1];
            var full = branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
            _refs[full] = HeadOf(repository);
            if (IsSource(repository))
            {
                _sourceBranch = full;
                _worktrees[Source] = _worktrees[Source] with { Branch = full };
            }
            else
            {
                _targetBranch = full;
                _worktrees[Repository] = _worktrees[Repository] with { Branch = full };
            }
            return new(0, "", "");
        }
        if (args[0] == "add")
        {
            var name = args[^1] == "." ? null : args[^1];
            var root = WorktreePath(repository);
            var map = IsSource(repository) ? _untracked : _targetUntracked;
            var index = IsSource(repository) ? _index : _targetIndex;
            IEnumerable<string> names = name is null
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                : [name];
            foreach (var item in names)
            {
                var disk = Path.Combine(root, item);
                string? content = File.Exists(disk) ? File.ReadAllText(disk)
                    : map.TryGetValue(item, out var u) ? u : null;
                if (content is null) continue;
                index[item] = content;
                map.Remove(item);
            }
            return new(0, "", "");
        }
        if (args[0] == "push")
        {
            var spec = args[^1];
            var colon = spec.IndexOf(':');
            var sha = spec[..colon];
            var dest = spec[(colon + 1)..];
            if (dest == TargetRef || dest.EndsWith("/master", StringComparison.Ordinal)) _remoteTarget = sha;
            if (dest == SourceRef) _remoteSource = sha;
            return new(0, "", "");
        }
        throw Unsupported(args);
    }

    private LandingGitResult RevParse(string repository, IReadOnlyList<string> args)
    {
        if (args.Contains("--git-common-dir") || args.Contains("--path-format=absolute") && args.Contains("--git-common-dir"))
            return new(0, Path.GetFullPath(IsRemote(repository) ? Remote : CommonDir) + "\n", "");
        if (args.Contains("--absolute-git-dir"))
            return new(0, Path.GetFullPath(IsSource(repository) ? SourceGitDirectory : GitDirectory) + "\n", "");
        var spec = args.Last(a => a != "rev-parse" && a != "--verify" && !a.StartsWith("--", StringComparison.Ordinal));
        if (spec.EndsWith("^{commit}", StringComparison.Ordinal)) spec = spec[..^9];
        if (spec is "HEAD") return new(0, HeadOf(repository) + "\n", "");
        if (_refs.TryGetValue(spec, out var sha) || _refs.TryGetValue("refs/heads/" + spec, out sha))
            return new(0, sha + "\n", "");
        if (_objects.ContainsKey(spec)) return new(0, spec + "\n", "");
        return new(128, "", "git_exit_128");
    }

    private LandingGitResult SymbolicRef(string repository, IReadOnlyList<string> args)
    {
        var name = args[^1];
        if (name == "HEAD")
        {
            var branch = IsSource(repository) ? _sourceBranch : _targetBranch;
            return new(0, branch + "\n", "");
        }
        return new(1, "", "");
    }

    private LandingGitResult ShowRef(IReadOnlyList<string> args)
    {
        var name = args[^1];
        if (args.Contains("--exists"))
        {
            if (_existsErrorExit is int err && err != 0) return new(err, "", "git_exit_" + err);
            return new(_refs.ContainsKey(name) ? 0 : 2, "", _refs.ContainsKey(name) ? "" : "git_exit_2");
        }
        if (_refs.TryGetValue(name, out var sha))
            return new(0, args.Contains("--hash") || args.Contains("--verify") ? sha + "\n" : $"{sha} {name}\n", "");
        return new(128, "", "git_exit_128");
    }

    private LandingGitResult UpdateRef(IReadOnlyList<string> args)
    {
        if (args.Contains("-d"))
        {
            var name = args.First(a => a.StartsWith("refs/", StringComparison.Ordinal));
            if (args.Count > args.ToList().IndexOf(name) + 1)
            {
                var expected = args.Last();
                if (LooksOid(expected) && _refs.TryGetValue(name, out var have) && have != expected)
                    return new(1, "", "cas_failed");
            }
            _refs.Remove(name);
            if (name == SourceRef) _sourcePresent = false;
            return new(0, "", "");
        }
        var parts = args.Where(a => a is not "update-ref" and not "--no-deref").ToArray();
        if (parts.Length >= 2)
        {
            var name = parts[0];
            var sha = parts[1];
            if (parts.Length >= 3)
            {
                var expected = parts[2];
                var current = _refs.TryGetValue(name, out var have) ? have : new string('0', sha.Length);
                if (current != expected) return new(1, "", "cas_failed");
            }
            _refs[name] = sha;
            if (name == TargetRef) _targetHead = sha;
            if (name == SourceRef) _sourceHead = sha;
            return new(0, "", "");
        }
        throw Unsupported(args);
    }

    private LandingGitResult WorktreeList()
    {
        var sb = new StringBuilder();
        foreach (var wt in _worktrees.Values)
        {
            sb.Append("worktree ").Append(wt.Path).Append('\0');
            sb.Append("HEAD ").Append(wt.Head).Append('\0');
            sb.Append("branch ").Append(wt.Branch).Append('\0');
            if (PathsEqual(wt.Path, Source) && _sourceLocked) sb.Append("locked\0");
            if (PathsEqual(wt.Path, Source) && _sourcePrunable) sb.Append("prunable\0");
            if (PathsEqual(wt.Path, Repository) && _targetLocked) sb.Append("locked\0");
            if (PathsEqual(wt.Path, Repository) && _targetPrunable) sb.Append("prunable\0");
            sb.Append('\0');
        }
        return new(0, sb.ToString(), "");
    }

    private LandingGitResult WorktreeRemove(IReadOnlyList<string> args)
    {
        var path = args[^1];
        foreach (var key in _worktrees.Keys.Where(k => PathsEqual(k, path)).ToArray())
            _worktrees.Remove(key);
        if (PathsEqual(path, Source))
        {
            _sourcePresent = false;
            if (Directory.Exists(Source))
            {
                foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Source, true);
            }
        }
        return new(0, "", "");
    }

    private string StatusOutput(bool source)
    {
        var files = source ? _files : _targetFiles;
        var index = source ? _index : _targetIndex;
        var untracked = source ? _untracked : _targetUntracked;
        var root = source ? Source : Repository;
        var sb = new StringBuilder();
        foreach (var (path, content) in files)
        {
            var disk = Path.Combine(root, path);
            var live = File.Exists(disk) ? File.ReadAllText(disk) : content;
            var staged = index.TryGetValue(path, out var idx) ? idx : content;
            if (live != staged) sb.Append(" M ").Append(path).Append('\0');
            else if (staged != content) sb.Append("M  ").Append(path).Append('\0');
        }
        foreach (var path in Directory.Exists(root)
                     ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     : [])
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (rel.StartsWith(".antiphon/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".claude/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("bin-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!files.ContainsKey(rel) && !index.ContainsKey(rel) && !untracked.ContainsKey(rel)
                && Path.GetFileName(rel) is not ("keep.txt" or "fixture-owner.txt" or "feature.txt"))
                sb.Append("?? ").Append(rel).Append('\0');
        }
        foreach (var path in untracked.Keys) sb.Append("?? ").Append(path).Append('\0');
        foreach (var (path, content) in index)
            if (!files.ContainsKey(path) || files[path] != content)
                if (!sb.ToString().Contains(path, StringComparison.Ordinal))
                    sb.Append("A  ").Append(path).Append('\0');
        return sb.ToString();
    }

    private ImmutableArray<string> IgnoredPaths()
    {
        if (!Directory.Exists(Source)) return [];
        var list = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Source, path).Replace('\\', '/');
            if (rel.StartsWith(".antiphon/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".claude/", StringComparison.OrdinalIgnoreCase)
                || rel.Contains("/bin-", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("bin-", StringComparison.OrdinalIgnoreCase))
                list.Add(rel);
        }
        return [.. list];
    }

    private bool IsAncestor(string source, string observed)
    {
        if (source == observed) return true;
        var queue = new Queue<string>();
        queue.Enqueue(observed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!seen.Add(cur)) continue;
            if (cur == source) return true;
            if (_objects.TryGetValue(cur, out var commit))
                foreach (var p in commit.Parents) queue.Enqueue(p);
        }
        return false;
    }

    private bool HasDirty(string repository)
    {
        var status = StatusOutput(IsSource(repository));
        return status.Length > 0;
    }

    private string HeadOf(string repository) => IsSource(repository) ? _sourceHead : IsRemote(repository) ? _remoteTarget : _targetHead;

    private void SetHead(string repository, string sha)
    {
        if (IsSource(repository))
        {
            _sourceHead = sha;
            _refs[_sourceBranch] = sha;
            _worktrees[Source] = _worktrees[Source] with { Head = sha };
        }
        else
        {
            _targetHead = sha;
            _refs[_targetBranch] = sha;
            _worktrees[Repository] = _worktrees[Repository] with { Head = sha };
        }
    }

    private bool IsSource(string path) => PathsEqual(path, Source);
    private bool IsRemote(string path) => PathsEqual(path, Remote);
    private string WorktreePath(string repository) => IsSource(repository) ? Source : Repository;
    private string CurrentFingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_endpoint)));

    private string NextOid()
    {
        _oid++;
        return _oid.ToString("x").PadLeft(40, '0');
    }
    private static bool LooksOid(string value) => value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);
    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The constructor materialises a real temp tree, so every fixture owns one and must drop
    /// it; the per-test roots otherwise accumulate under %TEMP% forever (CARD-0475 review).
    /// </summary>
    public void Dispose()
    {
        var full = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("antiphon-c475-", StringComparison.Ordinal)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(full, true);
        }
        catch (DirectoryNotFoundException) { /* already gone */ }
        catch (IOException) { /* best effort */ }
    }

    private static InvalidOperationException Unsupported(IReadOnlyList<string> args) =>
        new("unsupported controlled git command: " + string.Join(' ', args));

    private sealed record Commit(string Sha, string[] Parents);
    private sealed record Worktree(string Path, string Branch, string Head);
}

file static class RemoteSourceRetain
{
    public static void ShouldRetain(this string actual, string seed)
    {
        if (actual != seed)
            throw new InvalidOperationException("the pre-published fixture remote source must retain its exact original commit");
    }
}
