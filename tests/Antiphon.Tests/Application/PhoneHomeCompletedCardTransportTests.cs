using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow")]
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PhoneHomeCompletedCardTransportTests
{
    // PC-588: omit server capability guard/fallback to legacy -> unsupported or retained tree oracle fails.
    [Test] public async Task C1032_TransportClientUnsupported()
    {
        RequireClient();
        await using var w = await World.Start(false);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await Call(w.Client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
        response.GetProperty("complete").GetBoolean().ShouldBeFalse();
        response.GetProperty("reason").GetString().ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
        w.Journal.Read()!.Count.ShouldBe(0);
        File.ReadAllText(Path.Combine(w.F.Checkout, "source")).ShouldBe("source");
        Directory.Exists(w.F.Binding.GitDirectory).ShouldBeTrue();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var client = new PhoneHomeRunnerClient(live);
        var advertise = false; var ack = false;
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.Capabilities
            ? new(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new RunnerCapabilitiesDto("test", "test", "test", false,
                    Features: advertise ? [RunnerCapabilityFeatures.CompletedCardWorkspaceV1] : []), PhoneHomeFraming.Json))
            : ack ? new(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new { complete = true }, PhoneHomeFraming.Json))
            : new(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedOperation, StatusCode: 400);
        foreach (var supported in new[] { false, true })
        {
            advertise = supported;
            var removal = await Call(client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
            removal.GetProperty("complete").GetBoolean().ShouldBeFalse();
            removal.GetProperty("reason").GetString().ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
            var inspection = await Call(client, "InspectCompletedCardWorkspaceAsync", new CompletedCardWorkspaceInspectRequest(1, w.Command.Binding), bound.Token);
            inspection.GetProperty("reason").GetString().ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
            peer.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(0);
        }
        peer.RequestCount(PhoneHomeOperation.CompletedCardWorkspaceRemove).ShouldBe(1);
        ack = true;
        var incomplete = await Call(client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
        incomplete.GetProperty("complete").GetBoolean().ShouldBeFalse();
        incomplete.GetProperty("reason").GetString().ShouldBe("recipient_receipt_unreadable");
    }

    // PC-589: bypass MutationLock or reply before durable receipt -> busy ordering/durable/physical oracles fail.
    [NativeLinuxOnly("Real phone-home runner join qualified on Linux")]
    [Test] public async Task C1032_TransportQueueRecipientReplay()
    {
        RequireClient();
        if (!OperatingSystem.IsLinux()) throw new SkipTestException("Real phone-home runner join qualified on Linux");
        await using var w = await World.Start(true);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var inspect = await Call(w.Client, "InspectCompletedCardWorkspaceAsync", new CompletedCardWorkspaceInspectRequest(1, w.Command.Binding), bound.Token);
        inspect.GetProperty("headSha").GetString().ShouldBe(w.F.Sha);
        w.Journal.Read()!.Count.ShouldBe(0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Executor.AfterInitialInspectionForTest = async () => { entered.TrySetResult(); await release.Task.WaitAsync(bound.Token); };
        Task<JsonElement>? first = null, queued = null;
        try
        {
            first = Call(w.Client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
            await entered.Task.WaitAsync(bound.Token);
            queued = Call(w.Client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
            while (w.Connection.InFlight < 2) await Task.Delay(10, bound.Token);
            queued.IsCompleted.ShouldBeFalse("second real socket request waits on dispatcher MutationLock");
            w.Journal.Read()!.Count.ShouldBe(0); Directory.Exists(w.F.Checkout).ShouldBeTrue();
            release.TrySetResult();
            var result = await first; var replay = await queued;
            result.GetProperty("complete").GetBoolean().ShouldBeTrue();
            replay.GetRawText().ShouldBe(result.GetRawText());
            var record = new RunnerWorkspaceRemovalJournal(w.F.State, w.F.StoreId).Read()!.Single();
            result.GetProperty("receipt").GetRawText().ShouldBe(JsonSerializer.Serialize(record.Receipt, PhoneHomeFraming.Json));
            record.Command.Binding.TaskId.ShouldBe(w.F.Binding.TaskId);
            record.Command.Binding.PreparationId.ShouldBe(w.F.Binding.PreparationId);
            record.Command.Binding.RunnerStoreId.ShouldBe(w.F.StoreId);
            Directory.Exists(w.F.Checkout).ShouldBeFalse(); Directory.Exists(w.F.Binding.GitDirectory).ShouldBeFalse();
            CompletedCardRunnerFixture.Git(w.F.Repository, "rev-parse", w.Command.Binding.SourceFullRef).Trim().ShouldBe(w.F.Sha);
            File.ReadAllText(Path.Combine(w.F.Root, "outside", "sentinel")).ShouldBe("preserve");
            // A new recipient connection/runtime uses the same external journal and immutable command.
            var epoch = w.Connection.Epoch;
            await w.Reconnect();
            w.Connection.Epoch.ShouldBeGreaterThan(epoch);
            Directory.CreateDirectory(w.F.Checkout); File.WriteAllText(Path.Combine(w.F.Checkout, "replacement"), "keep");
            var restarted = await Call(w.Client, "RemoveCompletedCardWorkspaceAsync", w.Command, bound.Token);
            restarted.GetRawText().ShouldBe(result.GetRawText());
            File.ReadAllText(Path.Combine(w.F.Checkout, "replacement")).ShouldBe("keep");
        }
        finally
        {
            release.TrySetResult();
            if (first is not null) { try { await first; } catch (OperationCanceledException) { } }
            if (queued is not null) { try { await queued; } catch (OperationCanceledException) { } }
        }
    }

    private static void RequireClient()
    {
        typeof(PhoneHomeRunnerClient).GetMethod("RemoveCompletedCardWorkspaceAsync").ShouldNotBeNull("S6 server typed removal transport seam");
        typeof(PhoneHomeRunnerClient).GetMethod("InspectCompletedCardWorkspaceAsync").ShouldNotBeNull("S6 server typed inspection transport seam");
    }
    private static async Task<JsonElement> Call(object client, string method, object command, CancellationToken ct)
    {
        var call = (Task)client.GetType().GetMethod(method)!.Invoke(client, [command, ct])!;
        await call;
        return JsonSerializer.SerializeToElement(call.GetType().GetProperty("Result")!.GetValue(call), PhoneHomeFraming.Json);
    }

    private sealed class World : IAsyncDisposable
    {
        public CompletedCardRunnerFixture F { get; } = new("grok-linux");
        public RunnerWorkspaceRemovalJournal Journal { get; private set; }
        public RunnerWorkspaceUseGate Gate { get; private set; }
        public RunnerWorkspaceService Workspace { get; private set; }
        public RunnerCompletedCardWorkspaceRemoval Executor { get; private set; }
        public CompletedCardWorkspaceCommand Command { get; }
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeConnectionService Connection { get; private set; } = null!;
        public PhoneHomeRunnerClient Client { get; private set; } = null!;
        private CancellationTokenSource _stop = new();
        private readonly PhoneHomeProcessIdentity _identity = new();
        private bool _configured;
        private World()
        {
            Journal = new(F.State, F.StoreId); Gate = new(Journal); Gate.CompleteCensus();
            Workspace = F.Workspace(false, "main"); Workspace.WorkspaceUseGate = Gate;
            Executor = new(Workspace);
            Command = F.Command() with { Proof = F.Command().Proof with { DestinationFingerprint = F.Binding.RepositoryIdentity } };
        }
        public static async Task<World> Start(bool configured)
        {
            var w = new World { _configured = configured };
            try { w.Host = await PhoneHomeTestHost.StartAsync(); await w.Connect(); return w; }
            catch { await w.DisposeAsync(); throw; }
        }
        private async Task Connect()
        {
            var state = Path.Combine(F.Root, "connection"); Directory.CreateDirectory(state);
            var secretPath = Path.Combine(state, "secret"); File.WriteAllText(secretPath, Host.Secret);
            var storePath = Path.Combine(state, "store"); File.WriteAllText(storePath, F.StoreId);
            var settings = new PhoneHomeSettings
            {
                Enabled = true, RunnerId = Host.AllowedRunnerId, ServerOrigin = Host.Http.BaseAddress!.ToString(),
                SecretPath = secretPath, StoreIdPath = storePath, AllowedCwd = _configured ? Path.Combine(F.Root, "work") : "/work",
                RunnerRepository = _configured ? F.Repository : "/work/repos/antiphon", RunnerCloneSource = F.Origin,
                LaunchGenerationsPath = Path.Combine(state, "launch"), CapacityStatePath = Path.Combine(state, "capacity"),
                Capacity = 2, ProbeSecondaryRepositoryPushAccess = false,
            };
            var runtime = F.NewRuntime();
            if (_configured)
            {
                var type = typeof(SessionRunnerRuntime).Assembly.GetType("Antiphon.SessionRunner.RunnerCompletedCardWorkspaceTransport");
                type.ShouldNotBeNull("S6 explicit runner transport");
                var transport = Activator.CreateInstance(type!, Workspace, Journal, Gate)!;
                // Reuse the concrete executor only to hold the real removal at the deterministic seam.
                Executor = (RunnerCompletedCardWorkspaceRemoval)type!.GetProperty("Removal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transport)!;
                typeof(SessionRunnerRuntime).GetProperty("CompletedCardWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, transport);
            }
            var adoption = new PhoneHomeAdoptionGate();
            Connection = new(Options.Create(settings), adoption,
                new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(runtime, new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)), settings, identity: _identity),
                runtime, new Factory(), TimeProvider.System, NullLogger<PhoneHomeConnectionService>.Instance, identity: _identity);
            await Connection.StartAsync(_stop.Token); adoption.SignalReady();
            var live = await Host.WaitLiveAsync(TimeSpan.FromSeconds(30));
            Client = new(live);
        }
        public async Task Reconnect()
        {
            _stop.Cancel(); await Connection.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            _stop.Dispose(); _stop = new(); Executor.AfterInitialInspectionForTest = null;
            Journal = new(F.State, F.StoreId); Gate = new(Journal); Gate.CompleteCensus();
            Workspace = F.Workspace(false, "main"); Workspace.WorkspaceUseGate = Gate;
            await Connect();
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            if (Connection is not null) await Connection.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            if (Host is not null) await Host.DisposeAsync();
            await F.DisposeAsync(); _stop.Dispose();
        }
        private sealed class Factory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    }
}
