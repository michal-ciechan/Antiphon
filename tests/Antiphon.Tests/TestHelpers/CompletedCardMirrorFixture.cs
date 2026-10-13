using TUnit.Core;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

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

    public static async Task<CompletedCardMirrorFixture> CreateAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("S7 real phone-home recipient fixture requires Linux; Windows handle qualification is separate.");
        var f = new CompletedCardMirrorFixture();
        try { await f.InitializeAsync(ct); return f; }
        catch { await f.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        Card = await CompletedCardCleanupFixture.CreateAsync(clock: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow));
        var g = Card.Host.Fixture;
        var task = await TaskAsync();
        await g.RequiredAsync(Card.Tree, "push", "origin", "HEAD:refs/heads/" + task.WorktreeBranch);
        var repo = Path.Combine(g.Root, "runner-repository");
        await g.RequiredAsync(g.Root, "clone", g.Remote, repo);
        Wire = await PhoneHomeTestHost.StartAsync();
        var state = Path.Combine(g.Root, "runner-state"); Directory.CreateDirectory(state);
        var allowed = Path.Combine(g.Root, "runner-cwd"); Directory.CreateDirectory(allowed);
        var store = Guid.NewGuid();
        var storePath = Path.Combine(state, "store"); var secretPath = Path.Combine(state, "secret");
        await File.WriteAllTextAsync(storePath, store.ToString("D"), ct);
        await File.WriteAllTextAsync(secretPath, Wire.Secret, ct);
        Workspace = new RunnerWorkspaceService(repo, allowed, g.Remote, System.Diagnostics.Process.Start);
        Workspace.BindRecipient(Wire.AllowedRunnerId, store);
        Binding = (await Workspace.MirrorAsync(new(task.WorktreeBranch!, g.SeedSha,
            "task-" + task.Id.ToString("N")[..8], Binding: new(task.Id, task.Attempt, Guid.NewGuid(),
                Wire.AllowedRunnerId, store.ToString("D"))), ct)).Binding!;
        Journal = new(Path.Combine(state, "removals"), store.ToString("D"));
        Gate = new(Journal); Gate.CompleteCensus(); Workspace.WorkspaceUseGate = Gate;
        Runtime = new(Options.Create(new SessionRunnerSettings { SessionLogPath = Path.Combine(state, "sessions") }),
            NullLogger<SessionRunnerRuntime>.Instance);
        Runtime.WorkspaceUseGate = Gate;
        Runtime.CompletedCardWorkspace = new(Workspace, Journal, Gate, Wire.Secret);
        Directory.CreateDirectory(Path.GetDirectoryName(Sentinel)!);
        await File.WriteAllTextAsync(Sentinel, "mirror evidence\n", ct);
        var settings = new PhoneHomeSettings
        {
            Enabled = true, RunnerId = Wire.AllowedRunnerId, ServerOrigin = Wire.Http.BaseAddress!.ToString(),
            StoreIdPath = storePath, SecretPath = secretPath, AllowedCwd = allowed,
            RunnerRepository = repo, RunnerCloneSource = g.Remote, ProbeSecondaryRepositoryPushAccess = false,
            CapacityStatePath = Path.Combine(state, "capacity"), LaunchGenerationsPath = Path.Combine(state, "generations"),
        };
        var adoption = new PhoneHomeAdoptionGate();
        Connection = new(Options.Create(settings), adoption,
            new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(Runtime, new("test", null, DateTime.UtcNow, DateTime.UtcNow)), settings),
            Runtime, new HttpFactory(), TimeProvider.System, NullLogger<PhoneHomeConnectionService>.Instance);
        await Connection.StartAsync(_stop.Token); adoption.SignalReady();
        var live = await Wire.WaitLiveAsync(TimeSpan.FromSeconds(45)); Wire.Directory.MarkRecovered(live);
        await AddBindingAsync(Binding);
        var saved = await Card.Host.Services.GetRequiredService<IAgentReportStore>().StoreAsync(task, ct);
        saved.Succeeded.ShouldBeTrue();
        await Card.ChangeTaskAsync(t => t.ResultFilePath = saved.Path);
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
        var type = typeof(CardWorktreeCleanupService).Assembly.GetType("Antiphon.Server.Application.Services.CardWorktreeMirrorCleanupService");
        type.ShouldNotBeNull("S7 requires the authoritative mirror admission producer");
        await using var scope = Card.Host.Services.CreateAsyncScope();
        var producer = Activator.CreateInstance(type!, db, new ClientDirectory(Wire.Directory, this), Card.Host.Fixture.Git,
            Card.Host.Services.GetRequiredService<CardDoneArtifactPreservation>(),
            scope.ServiceProvider.GetRequiredService<WorkspaceUseAdmission>(), Card.Host.Clock, Options.Create(Card.Residue), new FaultClient((ICompletedCardWorkspaceClient)Wire.Directory.Resolve(Binding.RunnerId), this), Options.Create(new Antiphon.Server.Application.Settings.PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = Binding.RunnerId, SharedSecret = Wire.Secret }))!;
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
        _stop.Cancel(); if (Connection is not null) { await Connection.StopAsync(CancellationToken.None); Connection.Dispose(); }
        if (Runtime is not null) await Runtime.DisposeAsync();
        if (Wire is not null) await Wire.DisposeAsync(); if (Card is not null) await Card.DisposeAsync(); _stop.Dispose();
    }
    private sealed class HttpFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class ClientDirectory(ISessionRunnerDirectory inner, CompletedCardMirrorFixture world) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => inner.Local;
        public ISessionRunnerClient Resolve(string? id) => inner.Resolve(id);
        public ICompletedCardWorkspaceClient CompletedClient(string id) => new FaultClient((ICompletedCardWorkspaceClient)inner.Resolve(id), world);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => inner.GetOwnerAsync(id, ct);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => inner.GetBindingAsync(id, ct);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => inner.GetInventoryAsync(id, ct);
        public IReadOnlyList<string> KnownRunnerIds => inner.KnownRunnerIds;
        public Guid? GetLiveStoreId(string? id) => inner.GetLiveStoreId(id);
    }
    private sealed class FaultClient(ICompletedCardWorkspaceClient inner, CompletedCardMirrorFixture world) : ICompletedCardWorkspaceClient
    {
        public Task<CompletedCardWorkspaceInspectionResponse> InspectCompletedCardWorkspaceAsync(CompletedCardWorkspaceInspectRequest request, CancellationToken ct) => inner.InspectCompletedCardWorkspaceAsync(request, ct);
        public async Task<CompletedCardWorkspaceRemovalResponse> RemoveCompletedCardWorkspaceAsync(CompletedCardWorkspaceCommand command, CancellationToken ct)
        { var result = await inner.RemoveCompletedCardWorkspaceAsync(command, ct); return world.AlterReceipt?.Invoke(result) ?? result; }
    }
}
