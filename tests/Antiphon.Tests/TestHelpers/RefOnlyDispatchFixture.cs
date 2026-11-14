using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Application;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real dispatcher/claim/preparer and scratch Git; only the remote transport and launch sink are fake.</summary>
internal sealed class RefOnlyDispatchFixture : IAsyncDisposable
{
    public RunnerSettlementSyncTests.SyncWorld Git { get; private init; } = null!;
    public RemoteWorkspacePreparerTests.Rig Rig { get; private init; } = null!;
    public MutableMonitor<RefOnlyDesktopSettings> Gate { get; private init; } = null!;
    public PublicationBoundary Boundary { get; private init; } = null!;
    internal sealed class PublicationBoundary
    {
        public Func<string, CancellationToken, Task>? Callback { get; set; }
    }
    internal sealed record SharedTransport(FakeTimeProvider Clock, PhoneHomeTestHost Host,
        PhoneHomeScriptedPeer Peer, PhoneHomeLiveConnection Live);
    private static readonly Lazy<Task<SharedTransport>> Shared = new(CreateSharedAsync);
    private static readonly Lazy<Task<RunnerSettlementSyncTests.SyncWorld>> Seed = new(CreateSeedAsync);

    internal static async Task<string> SeedOriginAsync() => (await Seed.Value).Origin;

    private static async Task<RunnerSettlementSyncTests.SyncWorld> CreateSeedAsync()
    {
        using var phase = TestTiming.BeginFixturePhase("c0824-producer-seed", TestPhaseKind.FixtureSetup, "seed");
        return await RunnerSettlementSyncTests.SyncWorld.CreateAsync(pushBranch: false);
    }

    private static async Task<SharedTransport> CreateSharedAsync()
    {
        using var phase = TestTiming.BeginFixturePhase("c0824-producer-transport", TestPhaseKind.HostStartup, "start");
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        var host = await PhoneHomeTestHost.StartAsync(clock);
        // Independent isolated task databases share the transport, not a capacity scenario.
        host.Capacity = 2;
        try
        {
            var peer = await host.ConnectPeerAsync();
            peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            var live = await host.WaitLiveAsync(runnerId: host.AllowedRunnerId);
            host.Directory.MarkRecovered(live);
            return new(clock, host, peer, live);
        }
        catch { await host.DisposeAsync(); throw; }
    }

    internal static async Task DisposeSharedAsync()
    {
        try
        {
            if (Shared.IsValueCreated)
            {
                using var phase = TestTiming.BeginFixturePhase("c0824-producer-transport", TestPhaseKind.HostShutdown, "stop");
                var transport = await Shared.Value;
                await transport.Peer.DisposeAsync();
                await transport.Host.DisposeAsync();
            }
        }
        finally
        {
            if (Seed.IsValueCreated) await (await Seed.Value).DisposeAsync();
        }
    }

    public int MirrorCount(Guid id) => MirrorRequests(id).Length;
    private PhoneHomeFrame[] MirrorRequests(Guid id) => Rig.Peer.Incoming.ToArray()
        .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.WorkspaceMirror)
        .Where(f => f.Payload?.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json) is { } request
            && (request.Binding?.TaskId == id || request.Binding is null && request.Name == RemoteWorkspaceService.MirrorName(id)))
        .ToArray();

    public async Task<PhoneHomeFrame> WaitForMirrorAsync(Guid id, int count = 1)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            var requests = MirrorRequests(id);
            if (requests.Length >= count) return requests[count - 1];
            await Task.Delay(20);
        }
        throw new TimeoutException($"Expected {count} task-owned WorkspaceMirror requests; saw {MirrorCount(id)}.");
    }

    public string Root => Path.Combine(Git.Root, "managed");

    public static async Task<RefOnlyDispatchFixture> CreateAsync(bool enabled = true, bool slots = false, int capacity = 1)
    {
        var seed = await Seed.Value;
        var git = await RunnerSettlementSyncTests.SyncWorld.CreateAsync(pushBranch: false, immutableSeedOrigin: seed.Origin);
        var monitor = new MutableMonitor<RefOnlyDesktopSettings>(new() { Enabled = enabled, Roles = "Plan" });
        var boundary = new PublicationBoundary();
        try
        {
            // Lost-reply/store/clock recovery is the explicit fresh-transport exception.
            var transport = slots ? null : await Shared.Value;
            var rig = await RemoteWorkspacePreparerTests.Rig.StartAsync(
                tweak: s => s.WorkspaceSlotsEnabled = slots, capacity: capacity, transport: transport,
                capabilities: slots ? new("InboxConhost", "inbox", "test", false,
                    Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null) : null,
                configure: services =>
            {
                services.AddSingleton<IRepositoryMutationLease>(git.Leases);
                services.AddSingleton<ILandingGit>(git.Git);
                services.AddSingleton<ITaskProgressGit>(git.Git);
                services.AddSingleton(Options.Create(new GitSettings { WorktreeBasePath = Path.Combine(git.Root, "managed") }));
                services.AddSingleton<IOptionsMonitor<RefOnlyDesktopSettings>>(monitor);
                services.AddSingleton<RemoteSpillCourier>();
                services.AddScoped(sp =>
                {
                    var producer = ActivatorUtilities.CreateInstance<RefOnlyDesktopProvisioner>(sp);
                    producer.PreparationBoundaryAsync = (site, ct) => boundary.Callback?.Invoke(site, ct) ?? Task.CompletedTask;
                    return producer;
                });
            });
            monitor.CurrentValue.RunnerIds = [rig.RunnerId];
            rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            return new() { Git = git, Rig = rig, Gate = monitor, Boundary = boundary };
        }
        catch { await git.DisposeAsync(); throw; }
    }

    public Task<Guid> SeedAsync(Action<AgentTask>? change = null) => Rig.SeedAsync(t =>
    {
        t.RepoPath = Git.Desktop; t.WorkingDirectory = Git.Desktop;
        t.WorktreePath = null; t.WorktreeBranch = null; t.Role = AgentTaskRole.Plan;
        t.WorktreeBaseRequestedRef = Git.Baseline; t.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target;
        change?.Invoke(t);
    });

    public async Task<AgentTask> PrepareAsync(Guid id)
    {
        var next = MirrorCount(id) + 1;
        await Rig.TickAsync();
        var queued = await Rig.ReadTaskAsync(id);
        queued.Status.ShouldBe(AgentTaskStatus.Queued, queued.FailureReason);
        PhoneHomeFrame request;
        try { request = await WaitForMirrorAsync(id, next); }
        catch (TimeoutException ex)
        {
            await using var db = Rig.NewDb();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            var state = await db.TaskRefWorkspaceBindings.AsNoTracking().Where(b => b.TaskId == id)
                .Select(b => new { b.PreparationState, b.Revision }).SingleOrDefaultAsync();
            var events = await db.AgentTaskEvents.Where(e => e.AgentTaskId == id).OrderByDescending(e => e.At)
                .Take(5).Select(e => new { e.Type, e.Detail }).ToListAsync();
            var diagnostic = System.Text.Json.JsonSerializer.Serialize(new
                { task.Status, task.FailureReason, task.RemotePrepFailures, state, progress = Rig.Preparer.Progress(id), events });
            throw new TimeoutException("Mirror request missing: " + diagnostic, ex);
        }
        var path = "/runner/ref-only/" + id.ToString("N");
        await Rig.Peer.EmitAsync(RemoteWorkspacePreparerTests.MirrorResult(request, path));
        await Rig.Preparer.WhenIdleAsync();
        return await Rig.ReadTaskAsync(id);
    }

    public async Task<AgentTask> LaunchAsync(Guid id)
    {
        await PrepareAsync(id);
        await Rig.TickAsync();
        var task = await Rig.ReadTaskAsync(id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        return task;
    }

    /// <summary>Author in a separate clone, never a desktop identity or source checkout.</summary>
    public async Task<string> CommitAsync(AgentTask task, IReadOnlyDictionary<string, byte[]> files)
    {
        var author = Path.Combine(Git.Root, "produced-author-" + task.Id.ToString("N"));
        if (!Directory.Exists(author))
        {
            await Git.RunAsync(Git.Root, "clone", "-b", task.WorktreeBranch!, Git.Origin, author);
            await Git.RunAsync(author, "config", "user.name", "Ref-only author");
            await Git.RunAsync(author, "config", "user.email", "ref-only@example.invalid");
            await Git.RunAsync(author, "config", "core.autocrlf", "false");
        }
        foreach (var file in files)
        {
            var path = Path.Combine(author, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.Value);
            var blob = await Git.RunAsync(author, "hash-object", "--no-filters", "-w", "--", file.Key);
            await Git.RunAsync(author, "update-index", "--add", "--cacheinfo", "100644", blob, file.Key);
        }
        await Git.RunAsync(author, "commit", "-m", "producer lifecycle source");
        await Git.RunAsync(author, "push", "origin", "HEAD:refs/heads/" + task.WorktreeBranch);
        return await Git.RunAsync(author, "rev-parse", "HEAD");
    }

    public async Task<string> PublishForeignBranchAsync(string branch, string label)
    {
        var sha = await Git.RunAsync(Git.Desktop, "commit-tree", Git.Baseline + "^{tree}",
            "-p", Git.Baseline, "-m", "foreign fixture " + label);
        await Git.RunAsync(Git.Desktop, "update-ref", "refs/heads/" + branch, sha, new string('0', sha.Length));
        await Git.RunAsync(Git.Desktop, "push", "origin", sha + ":refs/heads/" + branch);
        return sha;
    }

    public async ValueTask DisposeAsync()
    {
        await Rig.DisposeAsync();
        await Git.DisposeAsync();
    }
}
