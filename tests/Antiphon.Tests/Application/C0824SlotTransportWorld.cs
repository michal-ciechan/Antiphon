using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S1 D-A/D-B producer-to-recipient world. The server half is the real
/// <see cref="RemoteWorkspacePreparerTests.Rig"/> (real dispatcher tick, preparer, phone-home host and
/// ClientWebSocket). Its peer hands every <c>WorkspaceMirror</c> and <c>WorkspaceSlotRelease</c> frame to a
/// real runner <see cref="PhoneHomeCommandDispatcher"/> whose <see cref="RunnerWorkspaceService"/> and
/// <see cref="RunnerWorkspaceSlotPool"/> run on a scratch origin and clone, so recipient evidence is the
/// runner ledger and the on-disk tree, never the server's request alone.
/// </summary>
internal sealed class C0824SlotTransportWorld : IAsyncDisposable
{
    private readonly object _sync = new();

    private C0824SlotTransportWorld(string root)
    {
        Root = root;
        Origin = Path.Combine(root, "origin");
        Clone = Path.Combine(root, "repo");
        Work = Path.Combine(root, "work").Replace('\\', '/');
    }

    public string Root { get; }
    public string Origin { get; }
    public string Clone { get; }
    public string Work { get; }
    public string Base { get; private set; } = "";
    public Rig Rig { get; private set; } = null!;
    public PhoneHomeCommandDispatcher Runner { get; private set; } = null!;
    public RunnerWorkspaceSlotPool Pool { get; private set; } = null!;
    public PhoneHomeSettings RunnerSettings { get; private set; } = null!;
    public RunnerWorkspaceService Workspace { get; private set; } = null!;
    public bool RealDesktopGit { get; set; }

    /// <summary>Test seam: answer a frame instead of the runner (null passes it on to the runner).</summary>
    public Func<PhoneHomeFrame, PhoneHomeFrame?>? Intercept { get; set; }

    /// <summary>
    /// The flag is on. CARD-0824 S1c S-5: <paramref name="tweak"/> adjusts the rig's settings after that, and
    /// <paramref name="configure"/> registers after the world's own services (a role monitor, a gate).
    /// </summary>
    public static async Task<C0824SlotTransportWorld> StartAsync(
        Action<DelegationSettings>? tweak = null, Action<IServiceCollection>? configure = null,
        Action<PhoneHomeRunnerSettings>? runnerSettings = null)
    {
        var world = new C0824SlotTransportWorld(Path.Combine(Path.GetTempPath(), "c0824-transport-" + Guid.NewGuid().ToString("N")));
        world.BuildRepository();
        var git = new ScratchLandingGit(world);
        world.Rig = await Rig.StartAsync(settings =>
            {
                settings.WorkspaceSlotsEnabled = true;
                tweak?.Invoke(settings);
            },
            capabilities: new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null),
            configure: services =>
            {
                services.RemoveAll<ILandingGit>();
                services.AddSingleton<ILandingGit>(git);
                services.AddScoped<WorkspaceSlotReleaseService>();
                configure?.Invoke(services);
            }, runnerSettings: runnerSettings);
        world.StartRunner();
        world.Rig.Peer.Reply = world.Answer;
        return world;
    }

    /// <summary>A fresh runner process over the same volume: new dispatcher, service and pool, same ledger.</summary>
    public void StartRunner()
    {
        var storePath = Root + "/store-id";
        File.WriteAllText(storePath, Rig.Host.StoreId.ToString("D"));
        var settings = new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = Rig.RunnerId,
            AllowedCwd = Work,
            RunnerRepository = Clone.Replace('\\', '/'),
            RunnerCloneSource = Origin,
            AllowedCloneSources = [Origin],
            Capacity = 2,
            CapacityStatePath = Root.Replace('\\', '/') + "/capacity",
            StoreIdPath = storePath,
            WorkspaceSlots = new PhoneHomeWorkspaceSlotSettings { Enabled = true, Headroom = 2, MaxOverflowSlots = 2 },
        };
        var emptyConfig = Path.Combine(Root, "empty.gitconfig");
        if (!File.Exists(emptyConfig))
            File.WriteAllText(emptyConfig, "");
        var service = new RunnerWorkspaceService(Clone, Work, Origin, psi =>
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            return Process.Start(psi);
        }, publishedBranch: "main");
        var pool = new RunnerWorkspaceSlotPool(service, settings.WorkspaceSlots, settings.Capacity);
        service.SlotPool = pool;
        var runner = new PhoneHomeCommandDispatcher(new RuntimeStub(Rig.Host.StoreId), settings);
        runner.UseWorkspace(service);
        lock (_sync)
        {
            RunnerSettings = settings;
            Workspace = service;
            Pool = pool;
            Runner = runner;
        }
    }

    private PhoneHomeFrame? Answer(PhoneHomeFrame frame)
    {
        if (frame.Kind != PhoneHomeFrameKind.Request
            || frame.Operation is not (PhoneHomeOperation.WorkspaceMirror or PhoneHomeOperation.WorkspaceSlotRelease))
            return null;
        if (Intercept?.Invoke(frame) is { } answered)
            return answered;
        PhoneHomeCommandDispatcher runner;
        lock (_sync)
            runner = Runner;
        return runner.DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Dispatches one frame to the runner and returns its real reply (for interceptors).</summary>
    public PhoneHomeFrame Deliver(PhoneHomeFrame frame)
    {
        PhoneHomeCommandDispatcher runner;
        lock (_sync)
            runner = Runner;
        return runner.DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult();
    }

    public (PhoneHomeCommandDispatcher Runner, RunnerWorkspaceSlotPool Pool) CreatePeerRunner(string runnerId, Guid storeId)
    {
        var repository = Path.Combine(Root, "target-repo");
        var work = Path.Combine(Root, "target-work");
        var storePath = Path.Combine(Root, "target-store-id");
        File.WriteAllText(storePath, storeId.ToString("D"));
        Directory.CreateDirectory(work);
        Git(Root, "clone", Origin, repository);
        var service = new RunnerWorkspaceService(repository, work, Origin, psi => Process.Start(psi), publishedBranch: "main");
        var settings = new PhoneHomeSettings { Enabled = true, RunnerId = runnerId, AllowedCwd = work,
            StoreIdPath = storePath,
            RunnerRepository = repository, RunnerCloneSource = Origin, AllowedCloneSources = [Origin],
            CapacityStatePath = Path.Combine(Root, "target-capacity"), Capacity = 2,
            WorkspaceSlots = new PhoneHomeWorkspaceSlotSettings { Enabled = true, Headroom = 2 } };
        var pool = new RunnerWorkspaceSlotPool(service, settings.WorkspaceSlots, settings.Capacity);
        service.SlotPool = pool;
        var runner = new PhoneHomeCommandDispatcher(new RuntimeStub(storeId), settings);
        runner.UseWorkspace(service);
        return (runner, pool);
    }

    /// <summary>Seeds a runner-bound task whose owned branch exists on the scratch origin.</summary>
    public async Task<Guid> SeedAsync()
    {
        var id = await Rig.SeedAsync();
        WriteLooseBranch(Origin, RemoteWorkspaceService.OwnedBranch(id), Base);
        return id;
    }

    private void BuildRepository()
    {
        var shared = SharedScratchRepository.Value;
        CopyTree(shared.Origin, Origin);
        CopyTree(shared.ClonePath, Clone);
        RewriteOriginUrl(Path.Combine(Clone, ".git", "config"), shared.Origin, Origin);
        Directory.CreateDirectory(Work);
        Base = shared.Base;
    }

    // Hang bound, not a latency sample. The retired one-second figure was twice a Linux
    // CP-6 cold-process maximum of 325.291 ms (372 commands at 259c2cde). On the loaded
    // Windows host in run 4e0d1159, plain rig starts took 3.4 to 7.2 seconds, and the
    // fifth scratch command (git commit -m base) crossed one second. Fifteen seconds sits
    // above that start-up ceiling and still kills one non-returning git well inside the
    // 80-second class timeout. No assertion compares elapsed time with the old one second.
    internal static readonly TimeSpan GitBudget = TimeSpan.FromSeconds(15);

    private static int _scratchGitProcessStarts;
    private static readonly AsyncLocal<GitCount?> ScratchGitScope = new();

    /// <summary>Scratch git process starts in this test process, including those that hit the deadline.</summary>
    internal static int ScratchGitProcessStarts => Volatile.Read(ref _scratchGitProcessStarts);

    /// <summary>Counts scratch git starts on this execution context only, so a neighbour case cannot move the total.</summary>
    internal static ScratchGitCount BeginScratchGitCount() => ScratchGitCount.Open();

    /// <summary>Builds the one scratch origin and clone for this process. Later cases copy that tree.</summary>
    internal static void EnsureSharedScratchRepository() => _ = SharedScratchRepository.Value;

    private static readonly SharedScratchStore SharedScratchRepository = new(BuildSharedScratch);

    /// <summary>A fresh store for the timeout-cache witness. Production keeps one store per process.</summary>
    internal static SharedScratchStore CreateStore(Func<SharedScratch> build) => new(build);

    internal static SharedScratch CreateScratch(string label) => new(label, label, label);

    /// <summary>
    /// One shared scratch for the process. A successful build is reused. A
    /// <see cref="TimeoutException"/> is not stored: the build is tried once more, and a
    /// second timeout leaves the next case free to build again. Any other exception is
    /// cached, matching the old lazy store.
    /// </summary>
    internal sealed class SharedScratchStore
    {
        private readonly object _gate = new();
        private readonly Func<SharedScratch> _build;
        private SharedScratch? _value;
        private Exception? _fault;
        private bool _retriedTimeout;

        internal SharedScratchStore(Func<SharedScratch> build) => _build = build;

        internal SharedScratch Value
        {
            get
            {
                lock (_gate)
                {
                    if (_fault is not null)
                        ExceptionDispatchInfo.Throw(_fault);
                    if (_value is not null)
                        return _value;
                    try
                    {
                        return _value = _build();
                    }
                    catch (Exception ex) when (ex is not TimeoutException)
                    {
                        _fault = ex;
                        throw;
                    }
                    catch (TimeoutException) when (!_retriedTimeout)
                    {
                        _retriedTimeout = true;
                        try
                        {
                            return _value = _build();
                        }
                        catch (Exception ex) when (ex is not TimeoutException)
                        {
                            _fault = ex;
                            throw;
                        }
                    }
                }
            }
        }
    }

    private static SharedScratch BuildSharedScratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "c0824-shared-scratch-" + Guid.NewGuid().ToString("N"));
        var origin = Path.Combine(root, "origin");
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(origin);
        Git(origin, "init", "--initial-branch=main");
        Git(origin, "config", "user.email", "c0824@localhost");
        Git(origin, "config", "user.name", "c0824");
        File.WriteAllText(Path.Combine(origin, "README.md"), "c0824 transport");
        File.WriteAllText(Path.Combine(origin, ".gitignore"), "bin/\nobj/\n.antiphon/\n");
        Git(origin, "add", "-A");
        Git(origin, "commit", "-m", "base");
        var baseSha = Git(origin, "rev-parse", "HEAD").Trim();
        Git(root, "clone", origin, clone);
        Git(clone, "config", "user.email", "c0824@localhost");
        Git(clone, "config", "user.name", "c0824");
        return new SharedScratch(origin, clone, baseSha);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RewriteOriginUrl(string configPath, string templateOrigin, string destinationOrigin)
    {
        var text = File.ReadAllText(configPath);
        var stored = ReadOriginUrl(text);
        var rewritten = EncodeOriginUrl(templateOrigin, stored, destinationOrigin);
        var needle = "url = " + stored;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("shared scratch clone origin url was not unique");
        File.WriteAllText(configPath, text.Replace(needle, "url = " + rewritten));
    }

    private static string ReadOriginUrl(string config)
    {
        var inOrigin = false;
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inOrigin = line.Equals("[remote \"origin\"]", StringComparison.Ordinal);
                continue;
            }

            if (inOrigin && line.StartsWith("url = ", StringComparison.Ordinal))
                return line["url = ".Length..];
        }

        throw new InvalidOperationException("shared scratch clone has no origin url");
    }

    private static string EncodeOriginUrl(string templateOrigin, string stored, string destinationOrigin)
    {
        if (stored == templateOrigin)
            return destinationOrigin;
        if (stored == templateOrigin.Replace('\\', '/'))
            return destinationOrigin.Replace('\\', '/');
        if (stored == templateOrigin.Replace("\\", "\\\\", StringComparison.Ordinal))
            return destinationOrigin.Replace("\\", "\\\\", StringComparison.Ordinal);
        throw new InvalidOperationException("shared scratch origin url is not the template path");
    }

    private static void WriteLooseBranch(string repository, string branch, string sha)
    {
        if (branch.Length == 0 || branch.Contains("..", StringComparison.Ordinal)
            || branch.StartsWith('/') || branch.EndsWith('/') || branch.Contains('\\', StringComparison.Ordinal))
            throw new InvalidOperationException("owned branch is not a loose ref");
        var refPath = Path.Combine(repository, ".git", "refs", "heads", branch.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, sha.Trim() + "\n");
    }

    /// <summary>The commit <c>git rev-parse HEAD</c> would print, including a linked worktree.</summary>
    internal static string RevParseHead(string workingDirectory)
    {
        var (gitDir, head) = ReadHead(workingDirectory);
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            return head;
        return ReadRef(gitDir, head["ref: ".Length..].Trim());
    }

    /// <summary><c>git symbolic-ref HEAD</c>. A detached HEAD fails with <see cref="InvalidOperationException"/>.</summary>
    internal static string SymbolicRefHead(string workingDirectory)
    {
        var (_, head) = ReadHead(workingDirectory);
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            throw new InvalidOperationException("HEAD is detached");
        return head["ref: ".Length..].Trim();
    }

    private static (string GitDir, string Head) ReadHead(string workingDirectory)
    {
        var gitDir = ResolveGitDir(workingDirectory);
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        return (gitDir, head);
    }

    private static string ResolveGitDir(string workingDirectory)
    {
        var dotGit = Path.Combine(workingDirectory, ".git");
        if (Directory.Exists(dotGit))
            return dotGit;
        var text = File.ReadAllText(dotGit).Trim();
        const string prefix = "gitdir: ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("HEAD is not a git directory");
        var gitDir = text[prefix.Length..].Trim();
        return Path.IsPathRooted(gitDir) ? gitDir : Path.GetFullPath(Path.Combine(workingDirectory, gitDir));
    }

    private static string ReadRef(string gitDir, string refName)
    {
        var common = gitDir;
        var commonFile = Path.Combine(gitDir, "commondir");
        if (File.Exists(commonFile))
        {
            var relative = File.ReadAllText(commonFile).Trim();
            common = Path.GetFullPath(Path.Combine(gitDir, relative));
        }

        foreach (var root in new[] { common, gitDir })
        {
            var loose = Path.Combine(root, refName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(loose))
                return File.ReadAllText(loose).Trim();
            var packed = Path.Combine(root, "packed-refs");
            if (!File.Exists(packed))
                continue;
            foreach (var line in File.ReadLines(packed))
            {
                if (line.Length == 0 || line[0] is '#' or '^')
                    continue;
                var space = line.IndexOf(' ');
                if (space > 0 && line[(space + 1)..] == refName)
                    return line[..space];
            }
        }

        throw new InvalidOperationException("missing ref " + refName);
    }

    private sealed class GitCount
    {
        public int Starts;
        public GitCount? Previous;
    }

    internal sealed class ScratchGitCount : IDisposable
    {
        private readonly GitCount _count;
        private ScratchGitCount(GitCount count) => _count = count;

        internal static ScratchGitCount Open()
        {
            var count = new GitCount { Previous = ScratchGitScope.Value };
            ScratchGitScope.Value = count;
            return new ScratchGitCount(count);
        }

        internal int Starts => _count.Starts;
        public void Dispose()
        {
            if (ScratchGitScope.Value == _count)
                ScratchGitScope.Value = _count.Previous;
        }
    }

    internal sealed record SharedScratch(string Origin, string ClonePath, string Base);

    public static string Git(string workingDirectory, params string[] args) =>
        RunGit(workingDirectory, args,
            TUnit.Core.TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None, GitBudget);

    internal static string RunGit(string workingDirectory, string[] args, CancellationToken ct, TimeSpan budget) =>
        RunGit(workingDirectory, args, ct, budget, fileName: null);

    internal static string RunGit(string workingDirectory, string[] args, CancellationToken ct, TimeSpan budget, string? fileName)
    {
        Interlocked.Increment(ref _scratchGitProcessStarts);
        if (ScratchGitScope.Value is { } scope)
            Interlocked.Increment(ref scope.Starts);
        using var phase = Antiphon.Tests.TestHelpers.TestTiming.BeginPhase(
            Antiphon.Tests.TestHelpers.TestPhaseKind.ProcessWait, "scratch-git");
        using var span = Antiphon.Tests.TestHelpers.TestTrace.Span("git " + (args.FirstOrDefault() ?? ""));
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrEmpty(fileName) ? "git" : fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        // No config-discovery subprocess is necessary for these fixture-owned repositories.
        // Run still owns descendants and bounds both drains even after the direct child exits.
        var result = Antiphon.Checkpoints.CheckpointProcessRunner.Run(
            psi, checked((int)budget.TotalMilliseconds), git: false, cancellationToken: ct);
        if (result.TimedOut)
            throw new TimeoutException("scratch git exceeded its operation deadline");
        if (result.Exit != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.Stderr}");
        return result.Stdout;
    }

    public async ValueTask DisposeAsync()
    {
        await Rig.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The desktop half: HEAD is the scratch base, origin is the scratch origin, a push is a no-op.</summary>
    private sealed class ScratchLandingGit(C0824SlotTransportWorld world) : ILandingGit
    {
        private readonly LandingGit _real = new();
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            world.RealDesktopGit ? _real.RunAsync(repository, arguments, ct) : Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, world.Base, ""),
                "remote" => new LandingGitResult(0, world.Origin, ""),
                "push" => new LandingGitResult(0, "", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, LandingGitRunOptions options, CancellationToken ct) =>
            world.RealDesktopGit ? _real.RunAsync(repository, arguments, options, ct) : RunAsync(repository, arguments, ct);

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => _real.CanonicalDirectoryAsync(path, ct);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => world.RealDesktopGit
            ? _real.CommonDirectoryAsync(repository, ct) : Task.FromResult(Path.GetFullPath(repository));
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => _real.RegistrationsAsync(repository, ct);
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RuntimeStub(Guid storeId) : IPhoneHomeRuntimeSurface
    {
        public Guid RunnerStoreId { get; } = storeId;
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotSupportedException();
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotSupportedException();
    }
}
