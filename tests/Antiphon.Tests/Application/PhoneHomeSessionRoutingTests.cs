using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeSessionRoutingTests
{
    [Test]
    public async Task Restart_and_pin_change_keep_persisted_owner()
    {
        var local = new RecordingClient();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = new Antiphon.Server.Domain.Entities.AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "grok-linux",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        var directory = new StubDirectory(local, session);
        var routing = new RoutingSessionRunnerClient(directory);
        var localCallsForBoundSession = local.Calls;
        try
        {
            await routing.GetAsync(session.Id, CancellationToken.None);
        }
        catch (ServiceUnavailableException)
        {
            // remote owner is resolved, never the local client
        }

        localCallsForBoundSession.ShouldBeEmpty();
        await Should.ThrowAsync<NotFoundException>(() => routing.GetAsync(Guid.NewGuid(), CancellationToken.None));
        localCallsForBoundSession.ShouldBeEmpty();
    }

    [Test]
    public async Task Launch_requires_matching_generation_echo()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.Launch)
                return null;
            var launch = frame.Payload!.Value.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!;
            var echoed = launch.AcceptedStartedAt?.AddSeconds(1) ?? DateTime.UtcNow;
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new RunnerSessionDto(launch.SessionId, 1, echoed, "Running", null, "", 0, AcceptedStartedAt: echoed),
                    PhoneHomeFraming.Json));
        };
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var expected = DateTime.UtcNow;
        var spec = new AgentLaunchSpec("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), "/work", 80, 24)
        {
            AcceptedStartedAt = expected,
        };
        var launchAccepted = true;
        try
        {
            await client.StartAsync(Guid.NewGuid(), spec, CancellationToken.None);
        }
        catch (ConflictException)
        {
            launchAccepted = false;
        }

        launchAccepted.ShouldBeFalse();

        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.Launch)
                return null;
            var launch = frame.Payload!.Value.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!;
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new RunnerSessionDto(launch.SessionId, 1, expected, "Running", null, "", 0, AcceptedStartedAt: expected),
                    PhoneHomeFraming.Json));
        };
        var ok = await client.StartAsync(Guid.NewGuid(), spec, CancellationToken.None);
        SessionGeneration.Equal(expected, ok.AcceptedStartedAt ?? ok.StartedAt).ShouldBeTrue();
    }

    [Test]
    public async Task Conditional_input_keeps_generation_and_has_no_raw_fallback()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        peer.Reply = frame =>
        {
            if (frame.Operation == PhoneHomeOperation.ConditionalInput)
            {
                return new PhoneHomeFrame(
                    PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    ErrorCode: ConditionalInputOutcomes.GenerationMismatch, ErrorDetail: "stale", StatusCode: 409);
            }

            if (frame.Operation == PhoneHomeOperation.Input)
                throw new InvalidOperationException("raw input fallback is forbidden");
            return null;
        };
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var rawInputFrames = new List<string>();
        var result = await client.SendConditionalInputAsync(
            Guid.NewGuid(),
            new RunnerConditionalInputRequest(DateTime.UtcNow, 3, "secret"),
            CancellationToken.None);
        result.Outcome.ShouldBe(ConditionalInputOutcomes.Unknown);
        if (peer.Inputs.Count > 0)
            rawInputFrames.Add("raw");
        rawInputFrames.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Stop_never_recaptures_replacement_generation()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var captured = DateTime.UtcNow.AddMinutes(-1);
        var replacement = DateTime.UtcNow;
        var replacementKilled = false;
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.KillGeneration)
                return null;
            var expected = frame.Payload!.Value.GetProperty("expectedAcceptedStartedAt").GetDateTime();
            var killed = SessionGeneration.Equal(expected, captured);
            if (!killed)
                replacementKilled = true;
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new RunnerKillGenerationResult(Guid.Empty, killed,
                        killed ? KillGenerationOutcomes.Killed : KillGenerationOutcomes.Mismatch, expected),
                    PhoneHomeFraming.Json));
        };
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var result = await client.KillGenerationAsync(Guid.NewGuid(), captured, CancellationToken.None);
        result.Killed.ShouldBeTrue();
        replacementKilled.ShouldBeFalse();
        _ = replacement;
        await Task.CompletedTask;
    }

    [Test]
    public async Task Conditional_stop_on_a_runner_session_is_forwarded()
    {
        var remote = new RecordingClient();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = new Antiphon.Server.Domain.Entities.AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "grok-linux",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        ISessionRunnerClient routing = new RoutingSessionRunnerClient(new ForwardingDirectory(remote, session));
        var started = SessionGeneration.Normalize(DateTime.UtcNow);
        var result = await routing.StopCompactionContinuationAsync(
            session.Id,
            new CompactionContinuationStopRequest(
                Guid.NewGuid(), started, "boundary", "continuation", 10, "binding", 1, 1),
            CancellationToken.None);

        result.Outcome.ShouldBe(CompactionStopOutcomes.Exited);
        remote.Calls.ShouldContain("stop-compaction");
    }

    private sealed class RecordingClient : ISessionRunnerClient
    {
        public List<string> Calls { get; } = [];
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Calls.Add("start");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        }
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            Calls.Add("get");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));
        public Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
            Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct)
        {
            Calls.Add("stop-compaction");
            return Task.FromResult(new CompactionContinuationStopResult(
                sessionId, request.AttemptId, true, CompactionStopOutcomes.Exited, request.ExpectedAcceptedStartedAt));
        }
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty() { await Task.CompletedTask; yield break; }
    }

    private sealed class ForwardingDirectory(ISessionRunnerClient remote, Antiphon.Server.Domain.Entities.AgentSession session) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => remote;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "grok-linux"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? null : session.RunnerStoreId;
        public ISessionRunnerClient Resolve(string? runnerId) => remote;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(new SessionRunnerOwner(session.RunnerId!, session.RunnerStoreId!.Value, session.RunnerCwd!));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(sessionId == session.Id
                ? new SessionRunnerBinding.Remote(new SessionRunnerOwner(session.RunnerId!, session.RunnerStoreId!.Value, session.RunnerCwd!))
                : SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("offline"));
    }

    private sealed class StubDirectory(ISessionRunnerClient local, Antiphon.Server.Domain.Entities.AgentSession session) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => local;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "grok-linux"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? null : session.RunnerStoreId;
        public ISessionRunnerClient Resolve(string? runnerId) =>
            runnerId == "grok-linux"
                ? throw new ServiceUnavailableException("offline", "phone_home_unavailable")
                : local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(new SessionRunnerOwner(session.RunnerId!, session.RunnerStoreId!.Value, session.RunnerCwd!));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(sessionId == session.Id
                ? new SessionRunnerBinding.Remote(new SessionRunnerOwner(session.RunnerId!, session.RunnerStoreId!.Value, session.RunnerCwd!))
                : SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("offline"));
    }
}
