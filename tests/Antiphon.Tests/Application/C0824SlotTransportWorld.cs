using System.Diagnostics;
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
        Directory.CreateDirectory(work);
        Git(Root, "clone", Origin, repository);
        var service = new RunnerWorkspaceService(repository, work, Origin, publishedBranch: "main");
        var settings = new PhoneHomeSettings { Enabled = true, RunnerId = runnerId, AllowedCwd = work,
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
        Git(Origin, "branch", "-f", RemoteWorkspaceService.OwnedBranch(id), Base);
        return id;
    }

    private void BuildRepository()
    {
        Directory.CreateDirectory(Origin);
        Directory.CreateDirectory(Work);
        Git(Origin, "init", "--initial-branch=main");
        Git(Origin, "config", "user.email", "c0824@localhost");
        Git(Origin, "config", "user.name", "c0824");
        File.WriteAllText(Path.Combine(Origin, "README.md"), "c0824 transport");
        File.WriteAllText(Path.Combine(Origin, ".gitignore"), "bin/\nobj/\n.antiphon/\n");
        Git(Origin, "add", "-A");
        Git(Origin, "commit", "-m", "base");
        Base = Git(Origin, "rev-parse", "HEAD").Trim();
        Git(Root, "clone", Origin, Clone);
        Git(Clone, "config", "user.email", "c0824@localhost");
        Git(Clone, "config", "user.name", "c0824");
    }

    // Scratch commands share the existing custodied runner's single start/exit/drain deadline.
    // Provisional cold-process sample (CARD-1514/1404): 372 commands in CP-6 at 259c2cde,
    // max 325.291 ms. Round twice that observed maximum up to one second, including both drains.
    internal static readonly TimeSpan GitBudget = TimeSpan.FromSeconds(1);

    public static string Git(string workingDirectory, params string[] args) =>
        RunGit(workingDirectory, args,
            TUnit.Core.TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None, GitBudget);

    internal static string RunGit(string workingDirectory, string[] args, CancellationToken ct, TimeSpan budget)
    {
        using var phase = Antiphon.Tests.TestHelpers.TestTiming.BeginPhase(
            Antiphon.Tests.TestHelpers.TestPhaseKind.ProcessWait, "scratch-git");
        using var span = Antiphon.Tests.TestHelpers.TestTrace.Span("git " + (args.FirstOrDefault() ?? ""));
        var psi = new ProcessStartInfo
        {
            FileName = "git",
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
