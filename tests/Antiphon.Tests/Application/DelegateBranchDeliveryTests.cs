using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateBranchDeliveryTests
{
    [Test]
    [Arguments(null)]
    [Arguments("windows")]
    [Arguments("linux")]
    [Timeout(180_000)]
    public async Task C1558_WireRegistersReportedPlatform(string? reportedPlatform, CancellationToken ct)
    {
        await using var world = await RunnerSettlementSyncTests.SyncWorld.CreateAsync();
        await using var wire = await Wire.CreateAsync(world, false, ct, reportedPlatform);
        var expected = reportedPlatform ?? (OperatingSystem.IsWindows() ? "windows" : "linux");
        wire.Surface.Capabilities().Platform.ShouldBe(expected);
        (await wire.Host.Directory.DescribeAsync(wire.Host.AllowedRunnerId, ct))!.Platform.ShouldBe(expected);
        await wire.ReconnectAsync(ct);
        (await wire.Host.Directory.DescribeAsync(wire.Host.AllowedRunnerId, ct))!.Platform.ShouldBe(expected);
        await wire.Client.ListAsync(ct);
    }

    [Test]
    public async Task C1558_InconsistentPlatformIsRefused(CancellationToken ct)
    {
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings()), NullLogger<SessionRunnerRuntime>.Instance);
        var capabilities = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve()).Capabilities()
            with { Platform = "windows" };
        await using var host = await PhoneHomeTestHost.StartAsync();
        var error = await Should.ThrowAsync<HttpRequestException>(() =>
            host.RegisterAsync(platform: "linux", capabilities: capabilities));
        error.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        host.Directory.SnapshotLive(host.AllowedRunnerId).ShouldBeNull();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1213_OldRunnerReceivesLegacy(CancellationToken ct)
    {
        // ignore receiver capability -> old-peer-receipt new ref; capable control goes red before S1.
        foreach (var old in new[] { false, true })
        {
            await using var world = await RunnerSettlementSyncTests.SyncWorld.CreateAsync();
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var wire = await Wire.CreateAsync(world, old, ct);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-1213");
            var task = new AgentTask { Id = Guid.NewGuid(), CardId = card.Id, RepoPath = world.Desktop, WorkingDirectory = world.Desktop,
                Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, RunnerId = wire.Host.AllowedRunnerId, Attempt = 1, CreatedAt = DateTime.UtcNow, Goal = "delivery", Status = AgentTaskStatus.Queued };
            task.RootTaskId = task.Id; db.AgentTasks.Add(task); await db.SaveChangesAsync(ct);
            await using (var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, Path.Combine(world.Root, "trees"), runners: wire.Host.Directory, cardBranchNames: true))
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, ct);
            db.Update(task); await db.SaveChangesAsync(ct);
            var expected = (old ? "feat/card-task-" : "feat/card-1213-code-") + task.Id.ToString("N")[..8];
            task.WorktreeBranch.ShouldBe(expected, "old-peer-receipt");
            await PrepareAsync(world, wire, schema, task.Id, ct);
            var row = await db.TaskWorktreeMirrorBindings.AsNoTracking().SingleAsync(r => r.TaskId == task.Id, ct);
            row.BindingId.ShouldNotBe(Guid.Empty, "recipient-binding");
            row.SourceFullRef.ShouldBe("refs/heads/" + expected);
            wire.MirrorRequests.Single(x => x.Binding?.TaskId == task.Id).Branch.ShouldBe(expected, "outgoing-request");
            (await world.RunAsync(row.WorktreePath, "symbolic-ref", "HEAD")).ShouldBe("refs/heads/" + expected);
            (await world.RunAsync(row.WorktreePath, "rev-parse", "HEAD")).ShouldBe(task.WorktreeBaseSha);
            wire.Workspace.OpenBindingStore().Read(row.PreparationId).Kind.ShouldBe(BindingReadKind.Complete);
            AssertOwnerStamp(row);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1213_QueuedBranchesReachRecipient(CancellationToken ct)
    {
        // derive old ref / bypass mutation wait -> recipient-ref/busy-recipient fail.
        foreach (var modern in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
            await using var world = await RunnerSettlementSyncTests.SyncWorld.CreateAsync(cardBranch: modern);
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var wire = await Wire.CreateAsync(world, false, ct);
            world.Task.RunnerId = wire.Host.AllowedRunnerId; world.Task.Attempt = 1;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            { db.AgentTasks.Add(world.Task); await db.SaveChangesAsync(ct); }
            await GatedAsync(wire, PhoneHomeOperation.WorkspaceMirror, busy,
                () => PrepareAsync(world, wire, schema, world.TaskId, ct), ct);
            var mirror = await ReadMirrorAsync(schema, world.TaskId, ct);
            world.Task.RemoteWorktreePath = mirror.WorktreePath;
            mirror.SourceFullRef.ShouldBe(world.FullRef, "D1 recipient-ref");
            AssertOwnerStamp(mirror);
            (await world.RunAsync(mirror.WorktreePath, "symbolic-ref", "HEAD")).ShouldBe(world.FullRef);
            await File.WriteAllTextAsync(Path.Combine(mirror.WorktreePath, "work.txt"), "recipient bytes", ct);
            await world.RunAsync(mirror.WorktreePath, "add", "work.txt"); await world.RunAsync(mirror.WorktreePath, "commit", "-m", "recipient work");
            var tip = await world.RunAsync(mirror.WorktreePath, "rev-parse", "HEAD");
            var sibling = await world.RunAsync(world.Origin, "rev-parse", "master");
            RemoteSettlementSyncResult? synced = null;
            await GatedAsync(wire, PhoneHomeOperation.WorkspacePublish, busy, async () =>
            { synced = await world.Service(publisher: new PhoneHomeRunnerMirrorPublisher(wire.Host.Directory)).SyncAsync(world.Task, ct); }, ct);
            synced!.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "D2 recipient-ref: " + synced.Reason);
            (await world.RunAsync(world.Origin, "rev-parse", world.Branch)).ShouldBe(tip);
            (await world.HeadAsync()).ShouldBe(tip); File.ReadAllText(Path.Combine(world.Worktree, "work.txt")).ShouldBe("recipient bytes");
            (await world.RunAsync(world.Origin, "rev-parse", "master")).ShouldBe(sibling, "unrelated-ref-canary");
            await PersistSyncEvidenceAsync(schema, world.TaskId, synced!, false, ct);
            (await ReadTaskAsync(schema, world.TaskId, ct)).CompletionProgressEvidenceJson.ShouldNotBeNull("durable-sync-proof");
        }
        foreach (var modern in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
            await using var park = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true, cardBranch: modern);
            await using var wire = await Wire.CreateForParkAsync(park, ct);
            park.RemoteDirectoryOverride = new ParkWireDirectory(park.Directory, wire);
            var tip = await park.CommitAsync("park.txt");
            await using (var db = park.Fixture.Db()) (await park.Service(db).CaptureSourceIdentityAsync(park.ParkId, ct)).Captured.ShouldBeTrue("committed-park-intent");
            TaskParkPublicationResult? result = null;
            await GatedAsync(wire, PhoneHomeOperation.WorkspacePark, busy, async () => { result = await park.PrepareAsync(); }, ct);
            result!.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, "D3 recipient-ref: " + result.Reason);
            (await park.RemoteShaAsync()).ShouldBe(tip);
            (await park.VerifyAsync()).Evidence!.SourceSha.ShouldBe(tip);
            (await park.RowAsync()).PublicationReceiptId.ShouldNotBeNull("durable-park-proof");
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1213_QueuedBranchRecovery(CancellationToken ct)
    {
        // trust lost ACK / recover with legacy ref -> enqueue-failure/lost-reply/recovered-ref fail.
        foreach (var modern in new[] { false, true })
        foreach (var cut in new[] { "persisted", "before-send", "received", "effect", "reply-before-save", "saved" })
        {
            await using var world = await RunnerSettlementSyncTests.SyncWorld.CreateAsync(cardBranch: modern);
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var wire = await Wire.CreateAsync(world, false, ct);
            world.Task.RunnerId = wire.Host.AllowedRunnerId; world.Task.Attempt = 1;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            { db.AgentTasks.Add(world.Task); await db.SaveChangesAsync(ct); }
            if (cut == "before-send") await wire.CloseCurrentAsync();
            if (cut is "received" or "effect") wire.Cut = (PhoneHomeOperation.WorkspaceMirror, cut);
            if (cut == "reply-before-save") wire.BeforeMirrorCapture = true;
            await PrepareAsync(world, wire, schema, world.TaskId, ct);
            var rows = await MirrorRowsAsync(schema, world.TaskId, ct);
            var preparation = rows.SingleOrDefault()?.PreparationId;
            if (cut is "before-send" or "received" or "effect" or "reply-before-save")
                rows.ShouldAllBe(r => r.BindingId == Guid.Empty, "enqueue-failure/lost-reply: no confirmed binding");
            wire.BeforeMirrorCapture = false;
            if (cut is "before-send" or "received" or "effect") await wire.ReconnectAsync(ct);
            await PrepareAsync(world, wire, schema, world.TaskId, ct);
            var mirror = await ReadMirrorAsync(schema, world.TaskId, ct);
            if (preparation is Guid original) mirror.PreparationId.ShouldBe(original, "recovered-preparation");
            mirror.SourceFullRef.ShouldBe(world.FullRef, "recovered-ref");
            AssertOwnerStamp(mirror);
            (await world.RunAsync(mirror.WorktreePath, "rev-parse", "HEAD")).ShouldBe(world.Baseline);
            world.Task.RemoteWorktreePath = mirror.WorktreePath;
            await File.WriteAllTextAsync(Path.Combine(mirror.WorktreePath, "work.txt"), "recovered publication", ct);
            await world.RunAsync(mirror.WorktreePath, "add", "work.txt"); await world.RunAsync(mirror.WorktreePath, "commit", "-m", "recovery work");
            var tip = await world.RunAsync(mirror.WorktreePath, "rev-parse", "HEAD");
            if (cut == "before-send") await wire.CloseCurrentAsync();
            if (cut is "received" or "effect") wire.Cut = (PhoneHomeOperation.WorkspacePublish, cut);
            var restoredTask = await ReadTaskAsync(schema, world.TaskId, ct);
            restoredTask.WorktreeBranch.ShouldBe(world.Branch, "persisted-recovery-ref");
            world.Git.Clear();
            var result = await world.Service(publisher: new PhoneHomeRunnerMirrorPublisher(wire.Host.Directory)).SyncAsync(restoredTask, ct);
            if (cut is "before-send" or "received")
            {
                result.State.ShouldNotBe(RemoteSettlementSyncState.Synchronized, "unsent work cannot be synchronized");
                result.DesktopAfterSha.ShouldNotBe(tip, "no confirmation of recipient tip");
                (await world.HeadAsync()).ShouldBe(world.Baseline, "canonical baseline retained");
                (await world.RunAsync(world.Origin, "rev-parse", world.Branch)).ShouldBe(world.Baseline, "origin baseline retained");
            }
            if (cut == "effect" && result.State == RemoteSettlementSyncState.Synchronized)
            {
                result.FullRef.ShouldBe(world.FullRef, "lost-reply reobserved exact ref");
                result.DesktopAfterSha.ShouldBe(tip, "lost-reply requires full Git receipt");
                (await world.HeadAsync()).ShouldBe(tip);
                (await world.RunAsync(world.Origin, "rev-parse", world.Branch)).ShouldBe(tip);
                world.Git.Commands.Count(c => c.StartsWith("ls-remote ", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2, "lost-reply fresh observation");
            }
            if (cut == "reply-before-save")
            {
                await Should.ThrowAsync<IOException>(() => PersistSyncEvidenceAsync(schema, world.TaskId, result, true, ct));
                (await ReadTaskAsync(schema, world.TaskId, ct)).CompletionProgressEvidenceJson.ShouldBeNull("received reply before failed save");
            }
            if (cut == "saved") await PersistSyncEvidenceAsync(schema, world.TaskId, result, false, ct);
            if (cut is "before-send" or "received" or "effect") await wire.ReconnectAsync(ct);
            restoredTask = await ReadTaskAsync(schema, world.TaskId, ct);
            result = await world.Service(publisher: new PhoneHomeRunnerMirrorPublisher(wire.Host.Directory)).SyncAsync(restoredTask, ct);
            result.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "recovered-ref: " + result.Reason);
            (await world.HeadAsync()).ShouldBe(tip); (await world.RunAsync(world.Origin, "rev-parse", world.Branch)).ShouldBe(tip);
            await PersistSyncEvidenceAsync(schema, world.TaskId, result, false, ct);
            var facts = TaskProgressJson.TryReadEvidence((await ReadTaskAsync(schema, world.TaskId, ct)).CompletionProgressEvidenceJson)!.RemoteSync!;
            facts.FullRef.ShouldBe(world.FullRef, "durable-recovered-ref"); facts.ConfirmedSha.ShouldBe(tip, "durable-recipient-proof");
        }
        foreach (var modern in new[] { false, true })
        foreach (var cut in new[] { "persisted", "before-send", "received", "effect", "reply-before-save", "saved" })
        {
            await using var park = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true, cardBranch: modern);
            await using var wire = await Wire.CreateForParkAsync(park, ct);
            park.RemoteDirectoryOverride = new ParkWireDirectory(park.Directory, wire);
            var tip = await park.CommitAsync("recovery.txt");
            if (cut == "before-send") await wire.CloseCurrentAsync();
            if (cut is "received" or "effect") wire.Cut = (PhoneHomeOperation.WorkspacePark, cut);
            if (cut == "reply-before-save") park.BeforeReceiptSave = _ => throw new IOException("cut before server save");
            TaskParkPublicationResult? result = null;
            try { result = await park.PrepareAsync(); } catch (Exception ex) when (ex is IOException or PhoneHomeTransportException) { }
            if (cut is "before-send" or "received" or "effect" or "reply-before-save")
                (await park.RowAsync()).PublicationReceiptId.ShouldBeNull("lost-reply: no saved proof");
            park.BeforeReceiptSave = null;
            if (cut is "before-send" or "received" or "effect") await wire.ReconnectAsync(ct);
            var saved = await park.RowAsync();
            result = saved.PublicationReceiptId is null ? await park.PrepareAsync() : await park.VerifyAsync();
            result.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, "recovered-ref: " + result.Reason);
            (await park.RemoteShaAsync()).ShouldBe(tip);
            var row = await park.RowAsync(); row.Id.ShouldBe(park.ParkId); row.PublicationReceiptId.ShouldNotBeNull();
            var proof = result.Evidence!;
            proof.Request.Binding.FullRef.ShouldBe(park.FullRef);
            proof.Request.Binding.BaselineSha.ShouldBe(park.Baseline);
            (await park.VerifyAsync()).Evidence.ShouldBe(proof, "saved-replay-proof");
        }
    }

    private static void AssertOwnerStamp(TaskWorktreeMirrorBinding row)
    {
        var path = RunnerWorkspaceBindingStore.OwnerStampPath(row.GitDirectory);
        File.Exists(path).ShouldBeTrue("recipient-owner-stamp");
        var stamp = JsonSerializer.Deserialize<MirrorCreationOwnerStamp>(File.ReadAllText(path), PhoneHomeFraming.Json).ShouldNotBeNull();
        stamp.TaskId.ShouldBe(row.TaskId); stamp.CreationGeneration.ShouldBe(row.BindingId); stamp.PreparationId.ShouldBe(row.PreparationId);
    }
    private static async Task<AgentTask> ReadTaskAsync(IsolatedTestSchema schema, Guid taskId, CancellationToken ct)
    { await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)); return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct); }
    private static async Task PersistSyncEvidenceAsync(IsolatedTestSchema schema, Guid taskId, RemoteSettlementSyncResult result, bool fail, CancellationToken ct)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<AppDbContext>(o => { o.UseNpgsql(schema.ConnectionString); if (fail) o.AddInterceptors(new FailEvidenceSave()); });
        services.AddSingleton<IEventBus, MockEventBus>(); services.AddSingleton(TimeProvider.System);
        services.AddGitWorkspaceService();
        services.AddSingleton(Options.Create(new DelegationSettings())); services.AddSingleton<AgentTaskReplyService>();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        // Reach the real settlement evidence writer without introducing provider/transcript input.
        var writer = typeof(AgentTaskReplyService).GetMethod("RecordRemoteEvidenceAsync", BindingFlags.NonPublic | BindingFlags.Instance).ShouldNotBeNull();
        await (Task)writer.Invoke(provider.GetRequiredService<AgentTaskReplyService>(), [scope.ServiceProvider, task, "", result, ct])!;
        await db.SaveChangesAsync(ct);
    }
    private sealed class FailEvidenceSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.CompletionProgressEvidenceJson is not null)) throw new IOException("cut before sync evidence save");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task GatedAsync(Wire wire, PhoneHomeOperation operation, bool busy, Func<Task> action, CancellationToken ct)
    {
        if (!busy) { await action(); return; }
        wire.Surface.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Surface.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupying = wire.Client.ClearLiveBufferAsync(Guid.NewGuid(), ct);
        await wire.Surface.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        wire.QueuedOperation = operation; wire.Queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = Volatile.Read(ref wire.GitStarts); var parkEntries = wire.Surface.ParkEntries;
        var pending = action();
        try
        {
            await wire.Queued.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Volatile.Read(ref wire.GitStarts).ShouldBe(before, "busy-recipient: no Git entry before release");
            wire.Surface.ParkEntries.ShouldBe(parkEntries, "busy-recipient: no runtime entry before release");
            pending.IsCompleted.ShouldBeFalse("no premature completion");
        }
        finally { wire.Surface.Release.TrySetResult(); await occupying; await pending; }
    }

    private static async Task PrepareAsync(RunnerSettlementSyncTests.SyncWorld world, Wire wire, IsolatedTestSchema schema, Guid taskId, CancellationToken ct)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton(TimeProvider.System); services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddSingleton<ISessionRunnerDirectory>(wire.Host.Directory); services.AddSingleton<ILandingGit>(world.Git);
        services.AddScoped<RemoteWorkspaceService>(); services.AddSingleton<RemoteWorkspacePreparer>();
        if (wire.BeforeMirrorCapture) services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString).AddInterceptors(new FailBindingSave()));
        await using var provider = services.BuildServiceProvider(); var preparer = provider.GetRequiredService<RemoteWorkspacePreparer>();
        preparer.TryBegin(taskId, wire.Host.AllowedRunnerId).ShouldBeTrue(); await preparer.WhenIdleAsync(ct);
    }
    private sealed class FailBindingSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TaskWorktreeMirrorBinding>().Any(e => e.Entity.BindingId != Guid.Empty)) throw new IOException("cut before binding save");
            return ValueTask.FromResult(result);
        }
    }
    private static async Task<List<TaskWorktreeMirrorBinding>> MirrorRowsAsync(IsolatedTestSchema schema, Guid taskId, CancellationToken ct)
    { await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)); return await db.TaskWorktreeMirrorBindings.AsNoTracking().Where(r => r.TaskId == taskId).ToListAsync(ct); }
    private static async Task<TaskWorktreeMirrorBinding> ReadMirrorAsync(IsolatedTestSchema schema, Guid taskId, CancellationToken ct)
    { var rows = await MirrorRowsAsync(schema, taskId, ct); rows.ShouldHaveSingleItem(); rows[0].BindingId.ShouldNotBe(Guid.Empty, "recipient durable binding"); return rows[0]; }

    private sealed class ParkWireDirectory(TaskParkPublicationTests.ParkDirectory source, Wire wire) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => wire.Client;
        public ISessionRunnerClient Resolve(string? runnerId) => wire.Client;
        public IReadOnlyList<string> KnownRunnerIds => source.KnownRunnerIds;
        public Guid? GetLiveStoreId(string? runnerId) => source.GetLiveStoreId(runnerId);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => source.GetOwnerAsync(sessionId, ct);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => source.GetBindingAsync(sessionId, ct);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => source.GetInventoryAsync(runnerId, ct);
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var capabilities = wire.Surface.Capabilities();
            return Task.FromResult<RunnerDescriptor?>(new(runnerId!, "wire", capabilities.Platform, null, true, true, false, 2, capabilities));
        }
    }

    private sealed class Wire : IAsyncDisposable
    {
        private ClientWebSocket? _socket;
        private CancellationTokenSource _stop = new();
        private Task? _pump;
        private readonly SemaphoreSlim _write = new(1, 1);
        private readonly List<Task> _children = [];
        private readonly string _root;
        private readonly string _repository;
        private readonly string _allowed;
        private readonly string _origin;
        private readonly Guid _store;
        private readonly Guid _boot = Guid.NewGuid();
        private readonly bool _old;
        private readonly string? _reportedPlatform;
        private readonly ReadySignal _ready = new();
        private SessionRunnerRuntime _runtime = null!;
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public RuntimeSurface Surface { get; private set; } = null!;
        public RunnerWorkspaceService Workspace { get; private set; } = null!;
        public PhoneHomeRunnerClient Client { get; private set; } = null!;
        private PhoneHomeLiveConnection _live = null!;
        public List<PhoneHomeWorkspaceMirrorRequest> MirrorRequests { get; } = [];
        public (PhoneHomeOperation Operation, string Phase)? Cut { get; set; }
        public bool BeforeMirrorCapture { get; set; }
        public PhoneHomeOperation? QueuedOperation { get; set; }
        public TaskCompletionSource Queued { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GitStarts;
        private Wire(string root, string repository, string allowed, string origin, Guid store, bool old, string? reportedPlatform = null)
        { _reportedPlatform = reportedPlatform; _root = root; _repository = repository; _allowed = allowed; _origin = origin; _store = store; _old = old; }
        public static async Task<Wire> CreateAsync(RunnerSettlementSyncTests.SyncWorld world, bool old, CancellationToken ct, string? reportedPlatform = null)
        {
            var repo = Path.Combine(world.Root, "recipient-repo");
            await world.RunAsync(world.Root, "clone", world.Origin, repo);
            await world.RunAsync(repo, "config", "user.name", "c1213 wire"); await world.RunAsync(repo, "config", "user.email", "c1213@localhost");
            var w = new Wire(Path.Combine(world.Root, "wire-state"), repo, Path.Combine(world.Root, "recipient"), world.Origin, Guid.NewGuid(), old, reportedPlatform);
            try { await w.StartAsync(ct); return w; } catch { await w.DisposeAsync(); throw; }
        }
        public static async Task<Wire> CreateForParkAsync(TaskParkPublicationTests.PublicationWorld park, CancellationToken ct)
        {
            await using var db = park.Fixture.Db();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == park.Fixture.SessionId, ct);
            var w = new Wire(Path.Combine(park.Root, "wire-state"), Path.Combine(park.Root, "runner-repo"), Path.Combine(park.Root, "remote"), park.Origin, session.RunnerStoreId!.Value, false);
            try { await w.StartAsync(ct); return w; } catch { await w.DisposeAsync(); throw; }
        }
        private async Task StartAsync(CancellationToken ct)
        {
            Directory.CreateDirectory(_root); Directory.CreateDirectory(_allowed);
            _runtime = new(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = _root }), NullLogger<SessionRunnerRuntime>.Instance);
            Workspace = new(_repository, _allowed, _origin, start => { Interlocked.Increment(ref GitStarts); TaskParkPublicationTests.ParkGit.Isolate(start); return Process.Start(start); });
            Surface = new RuntimeSurface(this, new PhoneHomeRuntimeAdapter(_runtime, RunnerBuildIdentity.Resolve()));
            Host = await PhoneHomeTestHost.StartAsync(observer: _ready, configureServices: s => s.AddSingleton<ILoggerProvider>(_ready));
            _ready.Directory = Host.Directory;
            await ReconnectAsync(ct);
        }
        public async Task CloseCurrentAsync() { Host.Directory.Disconnect(_live, "test cut"); await _live.DisposeAsync("test cut"); _socket!.Abort(); }
        public async Task ReconnectAsync(CancellationToken ct)
        {
            if (_socket is not null)
            {
                Host.Directory.Disconnect(_live, "test reconnect"); await _live.DisposeAsync("test reconnect");
                _stop.Cancel(); _socket.Abort(); if (_pump is not null) await _pump;
                await Task.WhenAll(_children); _children.Clear(); _socket.Dispose(); _stop.Dispose(); _stop = new();
            }
            _ready.Reset();
            var capabilities = Surface.Capabilities();
            var ticket = await Host.RegisterAsync(storeId: _store, bootId: _boot,
                platform: capabilities.Platform, capabilities: capabilities);
            _socket = new(); _socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
            await _socket.ConnectAsync(Host.ConnectUri, ct);
            var storePath = Path.Combine(_root, "store-id"); await File.WriteAllTextAsync(storePath, _store.ToString("D"), ct);
            var dispatcher = new PhoneHomeCommandDispatcher(Surface, new PhoneHomeSettings { Enabled = true, RunnerId = Host.AllowedRunnerId, StoreIdPath = storePath, AllowedCwd = _allowed, RunnerRepository = _repository, RunnerCloneSource = _origin, LaunchGenerationsPath = Path.Combine(_root, "generations") });
            typeof(PhoneHomeCommandDispatcher).GetField("_workspace", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dispatcher, Workspace);
            _pump = PumpAsync(dispatcher, _stop.Token);
            _live = await _ready.Live.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); Client = new(_live);
            await Client.ListAsync(ct);
        }
        private async Task PumpAsync(PhoneHomeCommandDispatcher dispatcher, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await PhoneHomeFraming.ReadFrameAsync(_socket!, 16 * 1024 * 1024, ct);
                    if (frame is null) return;
                    if (frame.Kind != PhoneHomeFrameKind.Request) continue;
                    if (frame.Operation == PhoneHomeOperation.WorkspaceMirror) MirrorRequests.Add(frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!);
                    if (Cut is { } before && before.Operation == frame.Operation && before.Phase == "received")
                    { Cut = null; _socket!.Abort(); return; }
                    var pending = dispatcher.DispatchAsync(frame, ct);
                    if (QueuedOperation == frame.Operation) Queued.TrySetResult();
                    _children.Add(ReplyAsync(frame, pending, ct));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _socket?.State == WebSocketState.Aborted) { }
            catch (WebSocketException) { }
        }
        private async Task ReplyAsync(PhoneHomeFrame request, Task<PhoneHomeFrame> pending, CancellationToken ct)
        {
            try
            {
                var response = await pending;
                if (Cut is { } cut && cut.Operation == request.Operation && cut.Phase == "effect") { Cut = null; _socket!.Abort(); return; }
                await _write.WaitAsync(ct);
                try { await PhoneHomeFraming.WriteFrameAsync(_socket!, response, 16 * 1024 * 1024, ct); }
                finally { _write.Release(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _socket?.State == WebSocketState.Aborted) { }
            catch (WebSocketException) { }
        }
        public async ValueTask DisposeAsync()
        {
            Surface?.Release.TrySetResult(); await _stop.CancelAsync(); _socket?.Abort();
            if (_pump is not null) await _pump; await Task.WhenAll(_children); _socket?.Dispose();
            if (Host is not null) await Host.DisposeAsync(); if (_runtime is not null) await _runtime.DisposeAsync();
            _stop.Dispose(); _write.Dispose();
        }
        public sealed class RuntimeSurface(Wire owner, PhoneHomeRuntimeAdapter adapter) : IPhoneHomeRuntimeSurface
        {
            public TaskCompletionSource Entered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int ParkEntries;
            public RunnerCapabilitiesDto Capabilities()
            {
                var caps = adapter.Capabilities();
                return caps with { Platform = owner._reportedPlatform ?? caps.Platform, Features = [.. caps.Features!.Where(f => !owner._old || f != "workspaceCardBranchV1"), RunnerCapabilityFeatures.WorkspaceParkV1, RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1], RunnerStoreId = owner._store };
            }
            public Guid RunnerStoreId => owner._store;
            public string Health() => "healthy"; public IReadOnlyList<RunnerSessionDto> List() => [];
            public int OwnedSessionCount => 0;
            public async Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            public Task<WorkspaceParkResult> ParkWorkspaceAsync(WorkspaceParkCommand request, CancellationToken ct)
            {
                Interlocked.Increment(ref ParkEntries); var park = new RunnerWorkspaceParkService(owner.Workspace);
                return request.Prepare is { } p ? park.PrepareAsync(p, ct) : park.VerifyAsync(request.Verify!, ct);
            }
            public Task<WorkspaceRepositoryIdentityResult> ReadWorkspaceRepositoryIdentityAsync(WorkspaceRepositoryIdentityRequest request, CancellationToken ct) => new RunnerWorkspaceParkService(owner.Workspace).ReadIdentityAsync(request, request.Path, ct);
            public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
            public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
            public RunnerBufferDto GetBuffer(Guid sessionId) => throw new NotSupportedException(); public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException(); public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new NotSupportedException();
            public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
            public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
            public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new NotSupportedException();
        }
        private sealed class ReadySignal : IRunnerEligibilityObserver, ILoggerProvider, ILogger
        {
            public PhoneHomeRunnerDirectory Directory { get; set; } = null!;
            public TaskCompletionSource<PhoneHomeLiveConnection> Live { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Reset() => Live = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Changed(string runnerId) { if (Directory.SnapshotLive(runnerId) is { DispatchEligible: true } live) Live.TrySetResult(live); }
            public ILogger CreateLogger(string categoryName) => this; public void Dispose() { }
            public bool IsEnabled(LogLevel level) => level == LogLevel.Information;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> p && p.Any(x => x.Key == "{OriginalFormat}" && x.Value is string template && template.StartsWith("Phone-home connection {RunnerId} epoch {Epoch} accepted:", StringComparison.Ordinal)))
                { var runner = (string)p.Single(x => x.Key == "RunnerId").Value!; Directory.MarkRecovered(Directory.SnapshotLive(runner)); }
            }
        }
    }
}
