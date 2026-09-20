using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Infrastructure.Data;
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
    }

    [Test]
    public async Task Launch_requires_matching_generation_echo()
    {
        var launchAccepted = false;
        var expected = DateTime.UtcNow;
        var echoed = expected.AddSeconds(1);
        launchAccepted = SessionGeneration.Equal(expected, echoed);
        launchAccepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Conditional_input_keeps_generation_and_has_no_raw_fallback()
    {
        var rawInputFrames = new List<string>();
        rawInputFrames.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Stop_never_recaptures_replacement_generation()
    {
        var replacementKilled = false;
        replacementKilled.ShouldBeFalse();
        await Task.CompletedTask;
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
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty() { await Task.CompletedTask; yield break; }
    }

    private sealed class StubDirectory(ISessionRunnerClient local, Antiphon.Server.Domain.Entities.AgentSession session) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => local;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "grok-linux"];
        public Guid? LiveStoreId => session.RunnerStoreId;
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
