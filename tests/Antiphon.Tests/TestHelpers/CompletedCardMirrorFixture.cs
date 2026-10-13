using TUnit.Core;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

// S8: Direct binds through RunnerWorkspaceService; Ready and Busy prepare over the real
// phone-home socket (C1032_RecoveryQueuesBusyAndReady preparation vectors).
internal enum MirrorPreparation { Direct, Ready, Busy }

internal sealed class CompletedCardMirrorFixture : IAsyncDisposable
{
    public CompletedCardCleanupFixture Card { get; private set; } = null!;
    public PhoneHomeTestHost Wire { get; private set; } = null!;
    public SessionRunnerRuntime Runtime { get; private set; } = null!;
    public PhoneHomeConnectionService Connection { get; private set; } = null!;
    public CompletedCardWorkspaceBinding Binding { get; private set; } = null!;
    public RunnerWorkspaceUseGate Gate { get; private set; } = null!;
    public RunnerWorkspaceRemovalJournal Journal { get; private set; } = null!;
    public RunnerWorkspaceService Workspace { get; private set; } = null!;
    public Guid EndpointId { get; private set; }
    public string Mirror => Binding.WorktreePath;
    public string Sentinel => Path.Combine(Mirror, ".antiphon", "checkpoints", "sentinel.trx");
    private readonly CancellationTokenSource _stop = new();
    public Func<CompletedCardWorkspaceRemovalResponse, CompletedCardWorkspaceRemovalResponse>? AlterReceipt { get; set; }
    public Func<CancellationToken, Task>? BeforeIntent { get; set; }
    public Func<CancellationToken, Task>? AfterIntent { get; set; }
    public Func<CancellationToken, Task>? BeforeInspection { get; set; }
    public bool FailReceiptImport { get; set; }
    // Returns a refusal instead of calling the inner client and asserts that reply has Complete=false.
    // RemovalRequests counts entries to this wrapper, including intercepted and
    // capability-preflight refusals; it is not a recipient removal-command counter.
    public Func<CompletedCardWorkspaceCommand, CompletedCardWorkspaceRemovalResponse?>? InterceptRemoval { get; set; }
    public int RemovalRequests => _removalRequests;
    private int _removalRequests;
    public int InspectionRequests => _inspectionRequests;
    private int _inspectionRequests;
    // S8 cut seams at the server client: BeforeRemoval may throw before the inner request is
    // sent; AfterRemoval sees the real recipient reply and may return a loss to throw instead.
    public Func<CompletedCardWorkspaceCommand, CancellationToken, Task>? BeforeRemoval { get; set; }
    public Func<CompletedCardWorkspaceRemovalResponse, Exception?>? AfterRemoval { get; set; }
    // Operation ids and canonical bytes of every command that reached the inner client.
    public List<Guid> SentOperationIds { get; } = [];
    public List<string> SentRequests { get; } = [];
    public FakeTimeProvider Clock => (FakeTimeProvider)Card.Host.Clock;
    public const string BusyBranch = "c1032-busy-holder";
    private PhoneHomeSettings _settings = null!;
    private string _storePath = "";
    // A reconnect keeps the runner boot identity; a store replacement is a new boot.
    private PhoneHomeProcessIdentity _identity = new();
    private int _holdArmed;
    private TaskCompletionSource _holderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _holderRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<CompletedCardMirrorFixture> CreateAsync(CancellationToken ct = default) =>
        await CreateAsync(MirrorPreparation.Direct, ct);

    // hostClock drives the server phone-home directory (owner lease) for a store-replacement cut.
    public static async Task<CompletedCardMirrorFixture> CreateAsync(MirrorPreparation preparation, CancellationToken ct = default,
        TimeProvider? hostClock = null)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.");
        var f = new CompletedCardMirrorFixture();
        try { await f.InitializeAsync(preparation, hostClock, ct); return f; }
        catch { await f.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(MirrorPreparation preparation, TimeProvider? hostClock, CancellationToken ct)
    {
        Card = await CompletedCardCleanupFixture.CreateAsync(clock: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow));
        var g = Card.Host.Fixture;
        var task = await TaskAsync();
        await g.RequiredAsync(Card.Tree, "push", "origin", "HEAD:refs/heads/" + task.WorktreeBranch);
        await g.RequiredAsync(Card.Tree, "push", "origin", "HEAD:refs/heads/" + BusyBranch);
        var repo = Path.Combine(g.Root, "runner-repository");
        await g.RequiredAsync(g.Root, "clone", g.Remote, repo);
        Wire = await PhoneHomeTestHost.StartAsync(clock: hostClock);
        var state = Path.Combine(g.Root, "runner-state"); Directory.CreateDirectory(state);
        var allowed = Path.Combine(g.Root, "runner-cwd"); Directory.CreateDirectory(allowed);
        var store = Guid.NewGuid();
        var storePath = Path.Combine(state, "store"); var secretPath = Path.Combine(state, "secret"); _storePath = storePath;
        await File.WriteAllTextAsync(storePath, store.ToString("D"), ct);
        await File.WriteAllTextAsync(secretPath, Wire.Secret, ct);
        Workspace = new RunnerWorkspaceService(repo, allowed, g.Remote, System.Diagnostics.Process.Start);
        Workspace.BindRecipient(Wire.AllowedRunnerId, store);
        // The holder blocks inside MirrorAsync, under the runner dispatcher's MutationLock.
        Workspace.WorkspaceAdmissionForTest = async () =>
        {
            if (Interlocked.Exchange(ref _holdArmed, 0) == 1) { _holderEntered.TrySetResult(); await _holderRelease.Task; }
        };
        PhoneHomeWorkspaceMirrorRequest mirror = new(task.WorktreeBranch!, g.SeedSha,
            "task-" + task.Id.ToString("N")[..8], Binding: new(task.Id, task.Attempt, Guid.NewGuid(),
                Wire.AllowedRunnerId, store.ToString("D")));
        if (preparation == MirrorPreparation.Direct) Binding = (await Workspace.MirrorAsync(mirror, ct)).Binding!;
        Journal = new(Path.Combine(state, "removals"), store.ToString("D"));
        Gate = new(Journal); Gate.CompleteCensus(); Workspace.WorkspaceUseGate = Gate;
        Runtime = new(Options.Create(new SessionRunnerSettings { SessionLogPath = Path.Combine(state, "sessions") }),
            NullLogger<SessionRunnerRuntime>.Instance);
        Runtime.WorkspaceUseGate = Gate;
        Runtime.CompletedCardWorkspace = new(Workspace, Journal, Gate, Wire.Secret);
        _settings = new PhoneHomeSettings
        {
            Enabled = true, RunnerId = Wire.AllowedRunnerId, ServerOrigin = Wire.Http.BaseAddress!.ToString(),
            StoreIdPath = storePath, SecretPath = secretPath, AllowedCwd = allowed,
            RunnerRepository = repo, RunnerCloneSource = g.Remote, ProbeSecondaryRepositoryPushAccess = false,
            CapacityStatePath = Path.Combine(state, "capacity"), LaunchGenerationsPath = Path.Combine(state, "generations"),
        };
        await ConnectAsync();
        if (preparation != MirrorPreparation.Direct)
        {
            var holder = preparation == MirrorPreparation.Busy ? await HoldMutationTurnAsync() : null;
            var prepared = Client.MirrorWorkspaceAsync(mirror, ct);
            if (holder is not null) await ReleaseWhenQueuedAsync(holder, prepared);
            Binding = (await prepared).Binding.ShouldNotBeNull("real recipient returns its completed binding");
            (await g.RequiredAsync(Binding.WorktreePath, "rev-parse", "HEAD")).Trim().ShouldBe(g.SeedSha);
            File.Exists(RunnerWorkspaceBindingStore.OwnerStampPath(Binding.GitDirectory)).ShouldBeTrue("runner owner stamp");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Sentinel)!);
        await File.WriteAllTextAsync(Sentinel, "mirror evidence\n", ct);
        await AddBindingAsync(Binding);
        var saved = await Card.Host.Services.GetRequiredService<IAgentReportStore>().StoreAsync(task, ct);
        saved.Succeeded.ShouldBeTrue();
        await Card.ChangeTaskAsync(t => t.ResultFilePath = saved.Path);
    }

    public PhoneHomeRunnerClient Client => (PhoneHomeRunnerClient)Wire.Directory.Resolve(Wire.AllowedRunnerId);
    public PhoneHomeLiveConnection Live => Wire.Directory.SnapshotLive(Wire.AllowedRunnerId).ShouldNotBeNull();

    private async Task ConnectAsync()
    {
        var adoption = new PhoneHomeAdoptionGate();
        var dispatcher = new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(Runtime, new("test", null, DateTime.UtcNow, DateTime.UtcNow)), _settings, identity: _identity);
        // The dispatcher otherwise builds its own service from the same repository settings; the
        // fixture instance carries the holder hook that runs inside the dispatcher's MutationLock.
        typeof(PhoneHomeCommandDispatcher).GetField("_workspace", BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldNotBeNull().SetValue(dispatcher, Workspace);
        Connection = new(Options.Create(_settings), adoption, dispatcher,
            Runtime, new HttpFactory(), TimeProvider.System, new Microsoft.Extensions.Logging.Logger<PhoneHomeConnectionService>(
                new Microsoft.Extensions.Logging.LoggerFactory([Wire.Logs])), _identity);
        await Connection.StartAsync(_stop.Token); adoption.SignalReady();
        PhoneHomeLiveConnection live;
        try { live = await Wire.WaitLiveAsync(TimeSpan.FromSeconds(45)); }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(ex.Message + " Logs: " + string.Join(" | ", Wire.Logs.Entries.TakeLast(12)), ex);
        }
        Wire.Directory.MarkRecovered(live);
    }

    // The runner process stays; only its phone-home connection ends.
    public async Task DisconnectAsync()
    {
        await Connection.StopAsync(CancellationToken.None); Connection.Dispose();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Wire.Directory.SnapshotLive(Wire.AllowedRunnerId) is not null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("runner connection did not end");
            await Task.Delay(20);
        }
    }

    // A replacement store id is written where the runner reads its persistent store before registering.
    public async Task ReconnectAsync(Guid? replacementStore = null)
    {
        if (replacementStore is Guid store) { await File.WriteAllTextAsync(_storePath, store.ToString("D")); _identity = new(); }
        await ConnectAsync();
    }

    // RemoteWorkspacePreparerTests.Rig.ReplaceRunnerStoreAsync recipe: after the owner lease expires,
    // a retire/clear state pair authorizes one replacement store for a new runner boot.
    public async Task<Guid> ReplaceStoreAsync(FakeTimeProvider hostClock)
    {
        await DisconnectAsync(); hostClock.Advance(TimeSpan.FromSeconds(91));
        var now = hostClock.GetUtcNow();
        Wire.Directory.ApplyState(Wire.AllowedRunnerId, new RunnerState(true, now, "retire-for-store", null, false, null, now, "store-replaced"));
        Wire.Directory.ApplyState(Wire.AllowedRunnerId, new RunnerState(false, null, null, null, false, null, null, null));
        var store = Guid.NewGuid(); await ReconnectAsync(store); return store;
    }

    // Sends an earlier actual WorkspaceMirror that holds the runner mutation turn until released.
    public async Task<Task> HoldMutationTurnAsync()
    {
        _holderEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _holderRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _holdArmed, 1);
        var holder = Client.MirrorWorkspaceAsync(new(BusyBranch, Card.Host.Fixture.SeedSha,
            "task-" + Guid.NewGuid().ToString("N")[..8]), CancellationToken.None);
        await _holderEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return holder;
    }

    // Releases the holder only after the tested request is on the socket (server in-flight count 2).
    public async Task ReleaseWhenQueuedAsync(Task holder, Task tested)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (Live.InFlight < 2)
            {
                if (tested.IsCompleted) await tested;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("tested request never reached the socket");
                await Task.Delay(10);
            }
            tested.IsCompleted.ShouldBeFalse("the tested request waits behind the held mutation turn");
        }
        finally { _holderRelease.TrySetResult(); }
        await holder;
    }

    public async Task<Guid> AddBindingAsync(CompletedCardWorkspaceBinding b)
    {
        await using var db = Card.Host.CreateContext();
        var row = new TaskWorktreeMirrorBinding
        {
            Id = Guid.NewGuid(), TaskId = b.TaskId, Attempt = b.Attempt, BindingId = b.BindingId,
            PreparationId = b.PreparationId, RunnerId = b.RunnerId, RunnerStoreId = b.RunnerStoreId,
            RepositoryIdentity = b.RepositoryIdentity, CommonDirectory = b.CommonDirectory, GitDirectory = b.GitDirectory,
            WorktreePath = b.WorktreePath, SourceFullRef = b.SourceFullRef, CreationBaselineSha = b.CreationBaselineSha,
            CreatedAt = Card.Host.Clock.GetUtcNow().UtcDateTime.AddHours(-4),
        };
        db.TaskWorktreeMirrorBindings.Add(row); await db.SaveChangesAsync(); return row.Id;
    }

    public async Task DiscoverAsync()
    {
        await using var db = Card.Host.CreateContext();
        await new CardWorktreeCleanupService(db, Card.Host.Clock).DiscoverAsync(Card.CardId, CancellationToken.None);
        var endpoints = await db.CardWorktreeCleanupEndpoints.AsNoTracking().Where(e => e.MirrorBindingId != null
            && e.Target.TaskId == Card.TaskId).ToListAsync();
        endpoints.ShouldNotBeEmpty("S7 must discover independently bound mirror endpoints");
        EndpointId = endpoints.Single(e => e.WorktreePath == Mirror && e.RunnerStoreId == Binding.RunnerStoreId).Id;
    }

    public async Task<CompletedCardWorkspaceRemovalResponse> TryAsync(CancellationToken ct = default)
    {
        if (EndpointId == Guid.Empty) await DiscoverAsync();
        await using var db = Card.Host.CreateContext();
        db.SavingChanges += (_, _) =>
        {
            if (FailReceiptImport && db.ChangeTracker.Entries<CardWorktreeCleanupEndpoint>().Any(e => e.Entity.ImportedReceipt is not null))
                throw new IOException("fixture receipt import failure");
        };
        var type = typeof(CardWorktreeCleanupService).Assembly.GetType("Antiphon.Server.Application.Services.CardWorktreeMirrorCleanupService");
        type.ShouldNotBeNull("S7 requires the authoritative mirror admission producer");
        await using var scope = Card.Host.Services.CreateAsyncScope();
        var producer = Activator.CreateInstance(type!, db, new ClientDirectory(Wire.Directory, this), Card.Host.Fixture.Git,
            Card.Host.Services.GetRequiredService<CardDoneArtifactPreservation>(),
            scope.ServiceProvider.GetRequiredService<WorkspaceUseAdmission>(), Card.Host.Clock, Options.Create(Card.Residue), new FaultClient(() => (ICompletedCardWorkspaceClient)Wire.Directory.Resolve(Binding.RunnerId), this), Options.Create(new Antiphon.Server.Application.Settings.PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = Binding.RunnerId, SharedSecret = Wire.Secret }))!;
        type!.GetProperty("BeforeIntentAsync", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(producer, BeforeIntent);
        type.GetProperty("AfterIntentAsync", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(producer, AfterIntent);
        return await (Task<CompletedCardWorkspaceRemovalResponse>)type.GetMethod("TryAsync")!.Invoke(producer, [EndpointId, null, ct])!;
    }

    public async Task<AgentTask> TaskAsync()
    { await using var db = Card.Host.CreateContext(); return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Card.TaskId); }
    public async Task<CardWorktreeCleanupEndpoint> EndpointAsync()
    { await using var db = Card.Host.CreateContext(); return await db.CardWorktreeCleanupEndpoints.AsNoTracking().SingleAsync(e => e.Id == EndpointId); }
    public async Task HeldAsync(string reason)
    {
        var result = await TryAsync(); result.Complete.ShouldBeFalse(); result.Reason.ShouldBe(reason);
        (await File.ReadAllTextAsync(Sentinel)).ShouldBe("mirror evidence\n"); Journal.Read()!.Count.ShouldBe(0);
    }
    public async Task RemovedAsync()
    {
        var result = await TryAsync(); result.Complete.ShouldBeTrue(result.Reason);
        Directory.Exists(Mirror).ShouldBeFalse(); Directory.Exists(Binding.GitDirectory).ShouldBeFalse();
        var endpoint = await EndpointAsync(); endpoint.State.ShouldBe(CardWorktreeCleanupEndpointState.Complete);
        endpoint.ImportedReceipt.ShouldNotBeNull();
        var receipt = JsonSerializer.Deserialize<CompletedCardWorkspaceReceipt>(endpoint.ImportedReceipt!, PhoneHomeFraming.Json)!;
        receipt.TaskId.ShouldBe(Card.TaskId); receipt.DoneRevisionId.ShouldBe((await CommandAsync()).DoneRevisionId);
        receipt.RunnerStoreId.ShouldBe(Binding.RunnerStoreId);
        Journal.Read()!.Single().Receipt.ShouldBe(receipt);
    }
    public async Task<CompletedCardWorkspaceCommand> CommandAsync() =>
        JsonSerializer.Deserialize<CompletedCardWorkspaceCommand>((await EndpointAsync()).ImmutableRequest!, PhoneHomeFraming.Json)!;

    public async ValueTask DisposeAsync()
    {
        _holderRelease.TrySetResult(); _stop.Cancel(); if (Connection is not null) { await Connection.StopAsync(CancellationToken.None); Connection.Dispose(); }
        if (Runtime is not null) await Runtime.DisposeAsync();
        if (Wire is not null) await Wire.DisposeAsync(); if (Card is not null) await Card.DisposeAsync(); _stop.Dispose();
    }
    private sealed class HttpFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class ClientDirectory(ISessionRunnerDirectory inner, CompletedCardMirrorFixture world) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => inner.Local;
        public ISessionRunnerClient Resolve(string? id) => inner.Resolve(id);
        public ICompletedCardWorkspaceClient CompletedClient(string id) => new FaultClient(() => (ICompletedCardWorkspaceClient)inner.Resolve(id), world);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => inner.GetOwnerAsync(id, ct);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => inner.GetBindingAsync(id, ct);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => inner.GetInventoryAsync(id, ct);
        public IReadOnlyList<string> KnownRunnerIds => inner.KnownRunnerIds;
        public Guid? GetLiveStoreId(string? id) => inner.GetLiveStoreId(id);
    }
    // Resolves the live client per call, so a disconnected runner fails at the call as in production.
    private sealed class FaultClient(Func<ICompletedCardWorkspaceClient> inner, CompletedCardMirrorFixture world) : ICompletedCardWorkspaceClient
    {
        public async Task<CompletedCardWorkspaceInspectionResponse> InspectCompletedCardWorkspaceAsync(CompletedCardWorkspaceInspectRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref world._inspectionRequests);
            if (world.BeforeInspection is not null) await world.BeforeInspection(ct);
            return await inner().InspectCompletedCardWorkspaceAsync(request, ct);
        }
        public async Task<CompletedCardWorkspaceRemovalResponse> RemoveCompletedCardWorkspaceAsync(CompletedCardWorkspaceCommand command, CancellationToken ct)
        {
            Interlocked.Increment(ref world._removalRequests);
            if (world.InterceptRemoval?.Invoke(command) is { } refusal)
            { refusal.Complete.ShouldBeFalse("fixture may only intercept with a refusal"); return refusal; }
            if (world.BeforeRemoval is not null) await world.BeforeRemoval(command, ct);
            lock (world.SentOperationIds)
            {
                world.SentOperationIds.Add(command.OperationId);
                world.SentRequests.Add(JsonSerializer.Serialize(command, PhoneHomeFraming.Json));
            }
            var result = await inner().RemoveCompletedCardWorkspaceAsync(command, ct);
            if (world.AfterRemoval?.Invoke(result) is { } lost) throw lost;
            return world.AlterReceipt?.Invoke(result) ?? result;
        }
    }
}
