using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Supervision;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ClaudeDialogDeliveryTests
{
    private const string Modal = "Which option?\n❯ 1. First\n2. Second\nEnter to select · Esc to cancel";

    [Test]
    public async Task C680_QueueAndExpectationKeepDialogInputPending()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        adapter.ClaudeComposerChrome = true;
        await f.Harness.MarkWorkingAsync();
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "held ordinary message", MessageSendMode.WhenIdle, CancellationToken.None);
        adapter.RenderedScreenOverride = Modal;
        var nudge = await f.NudgeAsync();
        var result = await f.DeliverAsync(nudge.Id);
        result.Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("pre-attempt dialog veto writes no body, Enter or recovery key");
        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptStartedAt.ShouldBeNull("refusal must precede attempt custody");
        var queue = await f.Harness.Queue.GetQueueAsync(f.SessionId, CancellationToken.None);
        queue.ModalBlocked.ShouldBeTrue();
        queue.Messages.ShouldHaveSingleItem();
        queue.Messages[0].DeliveryAttempts.ShouldBe(0);
        await f.Harness.Queue.SendNowAsync(f.SessionId, queue.Messages[0].Id, CancellationToken.None);
        adapter.Inputs.ShouldBeEmpty();

        adapter.RenderedScreenOverride = null;
        var control = await f.NudgeAsync("A distinct complete clear-screen control prompt for CARD-0680");
        (await f.DeliverAsync(control.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        adapter.SubmittedBodies.ShouldBe([control.Body]);
        (await f.ReloadAsync(control.Id)).ReceiptAt.ShouldNotBeNull();
        await using var db = f.Db();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);

        await using var beforeBody = await ExpectationDeliveryFixture.CreateAsync();
        beforeBody.Harness.Adapter.BeforeConditionalInput = () => beforeBody.Harness.Adapter.RenderedScreenOverride = Modal;
        var vetoed = await beforeBody.NudgeAsync();
        (await beforeBody.DeliverAsync(vetoed.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        beforeBody.Harness.Adapter.Inputs.ShouldBeEmpty();
        (await beforeBody.ReloadAsync(vetoed.Id)).AttemptStartedAt.ShouldBeNull("a final pre-body veto consumes no input attempt");

        await using var late = await ExpectationDeliveryFixture.CreateAsync();
        var lateAdapter = late.Harness.Adapter;
        lateAdapter.BeforeInput = (input, _) =>
        {
            if (input != "\r") lateAdapter.RenderedScreenOverride = Modal;
            return Task.CompletedTask;
        };
        var held = await late.NudgeAsync();
        (await late.DeliverAsync(held.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        lateAdapter.Inputs.Count.ShouldBe(1, "body landed; Enter and all recovery keys are withheld");
        lateAdapter.SubmittedBodies.ShouldBeEmpty();
        (await late.ReloadAsync(held.Id)).AttemptStartedAt.ShouldNotBeNull();
        lateAdapter.RenderedScreenOverride = null;
        lateAdapter.BeforeInput = null;
        var later = await late.NudgeAsync();
        (await late.DeliverAsync(later.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        lateAdapter.Inputs.Count.ShouldBe(1, "a clear dialog state cannot release an existing typed-body hold");

        await using var retry = await ExpectationDeliveryFixture.CreateAsync();
        retry.Harness.Adapter.SwallowSubmits = 99;
        retry.Harness.Adapter.BeforeInput = (input, _) =>
        {
            if (input == "\r") retry.Harness.Adapter.RenderedScreenOverride = Modal;
            return Task.CompletedTask;
        };
        var retried = await retry.NudgeAsync();
        (await retry.DeliverAsync(retried.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        retry.Harness.Adapter.Inputs.Count.ShouldBe(2, "a dialog after the first Enter vetoes every retry");
        retry.Harness.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task C680_AllAutomatedWritersUseTheGuard()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var h = f.Harness;
        h.Adapter.RenderedScreenOverride = Modal;
        await Should.ThrowAsync<ConflictException>(() => h.Runtime.SendInputAsync(f.SessionId, "direct", CancellationToken.None));
        await using var db = f.Db();
        var channel = new AgentChannelService(db, h.Runtime, h.Scope.ServiceProvider.GetRequiredService<CardService>(),
            h.EventBus, NullLogger<AgentChannelService>.Instance);
        await Should.ThrowAsync<ConflictException>(() => channel.SendToSessionAsync(null, f.SessionId, "channel", CancellationToken.None));
        var health = new SessionHealthActions(h.Queue, h.Provider.GetRequiredService<AgentSessionService>(), h.Runner,
            h.Runtime, h.Provider.GetRequiredService<IServiceScopeFactory>());
        await Should.ThrowAsync<ConflictException>(() => health.SendRawInputAsync(f.SessionId, "maintenance", CancellationToken.None));
        var watchdog = new WatchdogService(db, h.Runtime, new WatchdogMatcher(), new WatchdogCooldownStore(), h.EventBus,
            Options.Create(new WatchdogSettings { Enabled = true, Rules = [new() { Name = "fixture", Pattern = "Which option?", Response = "\r" }] }),
            TimeProvider.System, NullLogger<WatchdogService>.Instance);
        (await watchdog.ScanAsync(CancellationToken.None)).ShouldBe(0);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "WatchdogAutoResponded" || e.EventName == "AgentChannelMessage");

        var before = await h.Adapter.ObserveDialogAsync(f.SessionId, CancellationToken.None);
        await h.Provider.GetRequiredService<AgentSessionService>().SendInputAsync(f.SessionId, "2", CancellationToken.None);
        h.Adapter.Inputs.ShouldBe(["2"], "the authenticated interactive route remains intentional");
        (await h.Adapter.ObserveDialogAsync(f.SessionId, CancellationToken.None))!.InputRevision
            .ShouldBeGreaterThan(before!.InputRevision);

        await using var maintenance = await ExpectationDeliveryFixture.CreateAsync();
        maintenance.Harness.Adapter.RemoteControlMenuOpen = true;
        await maintenance.Harness.Runtime.DismissRemoteControlAsync(maintenance.SessionId, CancellationToken.None);
        maintenance.Harness.Adapter.Inputs.ShouldBe(["\u001b"]);
        maintenance.Harness.Adapter.RenderedScreenOverride = FakeAgentProtocolAdapter.RemoteControlMenuScreenText + "\n" + Modal;
        await Should.ThrowAsync<ConflictException>(() => maintenance.Harness.Runtime.DismissRemoteControlAsync(
            maintenance.SessionId, CancellationToken.None));
        maintenance.Harness.Adapter.Inputs.ShouldBe(["\u001b"]);
        maintenance.Harness.Adapter.RenderedScreenOverride = null;
        maintenance.Harness.Adapter.DialogGuardEnabled = false;
        await Should.ThrowAsync<ConflictException>(() => maintenance.Harness.Runtime.SendInputAsync(
            maintenance.SessionId, "unsupported", CancellationToken.None));
        maintenance.Harness.Adapter.Inputs.ShouldBe(["\u001b"]);

        foreach (var verified in new[] { false, true })
        {
            var client = new GuardClient { Screen = Modal };
            await using var adapter = new RunnerClaudeAdapter(client, Options.Create(new AgentRegistrySettings()),
                Options.Create(new SupervisionSettings { DeliveryVerification = new() { Enabled = verified } }));
            await adapter.StartAsync(new("claude", AgentKind.ClaudeCode, "fixture", [], new Dictionary<string, string>(),
                Path.GetTempPath(), 120, 40, SessionId: client.Id, AcceptedStartedAt: client.Generation), CancellationToken.None);
            await Should.ThrowAsync<ConflictException>(() => adapter.SendPromptAsync("boot prompt", CancellationToken.None));
            client.Inputs.ShouldBeEmpty();
        }
        var mid = new GuardClient { ModalAfterBody = true };
        await using (var adapter = new RunnerClaudeAdapter(mid, Options.Create(new AgentRegistrySettings()),
            Options.Create(new SupervisionSettings { DeliveryVerification = new() { Enabled = false } })))
        {
            await adapter.StartAsync(new("claude", AgentKind.ClaudeCode, "fixture", [], new Dictionary<string, string>(),
                Path.GetTempPath(), 120, 40, SessionId: mid.Id, AcceptedStartedAt: mid.Generation), CancellationToken.None);
            await Should.ThrowAsync<ConflictException>(() => adapter.SendPromptAsync("whole boot body", CancellationToken.None));
            mid.Inputs.ShouldBe(["whole boot body"]);
        }
        await CheckTransportsAsync(f);
    }

    private static async Task CheckTransportsAsync(ExpectationDeliveryFixture fixture)
    {
        var id = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var observation = new ProviderDialogObservation(id, Guid.NewGuid(), generation, 17, 23,
            ProviderDialogState.Clear, InputHistoryKnown: true);
        var request = new RunnerConditionalInputRequest(generation, 17, "body", true, 23, observation.RuntimeEpoch);
        var supported = true;
        RunnerConditionalInputRequest? captured = null;
        object Reply(string operation, JsonElement? payload)
        {
            if (operation == "capabilities") return new RunnerCapabilitiesDto("fixture", "fixture", "fixture", false, [],
                Features: supported ? [ProviderDialogObservation.Capability] : []);
            if (operation == "snapshot") return new RunnerSnapshotDto(id, "", "", 17, generation, generation, observation);
            if (operation == "conditional-input")
            {
                captured = payload!.Value.Deserialize<RunnerConditionalInputRequest>(PhoneHomeFraming.Json);
                return new RunnerConditionalInputResult(id, ConditionalInputOutcomes.ModalBlocked);
            }
            throw new InvalidOperationException("Unexpected raw/fallback operation: " + operation);
        }
        using var handler = new WireHandler(async message =>
        {
            JsonElement? payload = message.Content is null ? null
                : JsonSerializer.Deserialize<JsonElement>(await message.Content.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(Reply(message.RequestUri!.Segments[^1], payload), options: PhoneHomeFraming.Json) };
        });
        using var http = new HttpClient(handler, disposeHandler: false);
        var httpClient = new SessionRunnerHttpClient(http, new Factory(handler),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://fixture" }));
        (await httpClient.GetSnapshotAsync(id, CancellationToken.None)).Dialog.ShouldBe(observation);
        (await httpClient.SendConditionalInputAsync(id, request, CancellationToken.None)).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        captured.ShouldBe(request);

        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: fixture.Schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        peer.Reply = frame =>
        {
            if (frame.Operation is not (PhoneHomeOperation.Capabilities or PhoneHomeOperation.Snapshot or PhoneHomeOperation.ConditionalInput))
                return null;
            return new(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(Reply(frame.Operation switch
                {
                    PhoneHomeOperation.Capabilities => "capabilities",
                    PhoneHomeOperation.Snapshot => "snapshot",
                    _ => "conditional-input"
                }, frame.Payload), PhoneHomeFraming.Json));
        };
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var phone = new PhoneHomeRunnerClient(live);
        (await phone.GetSnapshotAsync(id, CancellationToken.None)).Dialog.ShouldBe(observation);
        captured = null;
        (await phone.SendConditionalInputAsync(id, request, CancellationToken.None)).Outcome.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        captured.ShouldBe(request);
        supported = false;
        captured = null;
        (await phone.SendConditionalInputAsync(id, request, CancellationToken.None)).Outcome.ShouldBe(ConditionalInputOutcomes.Unsupported);
        captured.ShouldBeNull();
        peer.Inputs.ShouldBeEmpty();
        // A new HTTP client has no cached capability from the supported control.
        using var unsupportedWire = new HttpClient(handler, disposeHandler: false);
        var unsupportedHttp = new SessionRunnerHttpClient(unsupportedWire, new Factory(handler),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://fixture" }));
        (await unsupportedHttp.SendConditionalInputAsync(id, request, CancellationToken.None)).Outcome.ShouldBe(ConditionalInputOutcomes.Unsupported);
        captured.ShouldBeNull();
    }

    private sealed class WireHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class GuardClient : ISessionRunnerClient
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        private readonly Guid _epoch = Guid.NewGuid();
        public string Screen { get; set; } = "────────────────────\n❯ \n────────────────────\n? for shortcuts";
        public bool ModalAfterBody { get; init; }
        public List<string> Inputs { get; } = [];
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => GetAsync(id, ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerSessionDto(
            id, 1, Generation, "Running", null, AgentExitReason.Unknown, Inputs.Count, AcceptedStartedAt: Generation));
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerBufferDto(id, "", Inputs.Count));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerSnapshotDto(
            id, Screen, Screen, Inputs.Count, Generation, Generation,
            new(id, _epoch, Generation, Inputs.Count, Inputs.Count, ClaudeDialogObservation.Observe(Screen).State == ClaudeDialogState.Clear
                ? ProviderDialogState.Clear : ProviderDialogState.Blocked, InputHistoryKnown: true)));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) { Inputs.Add(input); return Task.CompletedTask; }
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid id, RunnerConditionalInputRequest request, CancellationToken ct)
        {
            if (request.RequireClaudeDialogClear && ClaudeDialogObservation.Observe(Screen).State != ClaudeDialogState.Clear)
                return Task.FromResult(new RunnerConditionalInputResult(id, ConditionalInputOutcomes.ModalBlocked));
            Inputs.Add(request.Input);
            if (ModalAfterBody) Screen = Modal;
            return Task.FromResult(new RunnerConditionalInputResult(id, ConditionalInputOutcomes.Written));
        }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => GetAsync(id, ct);
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield break; }
    }
}
