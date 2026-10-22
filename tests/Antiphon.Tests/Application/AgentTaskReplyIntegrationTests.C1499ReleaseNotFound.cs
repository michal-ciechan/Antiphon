using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1499: a phone-home <c>ReleaseSlot</c> evicts the runner's record once the process is gone,
/// so the read that follows a release the runner answered as exited is a 404 "Session '...' was not
/// found". Settlement used to record that as an undelivered kill and raise a false
/// <see cref="AgentIncidentKind.DelegateReleaseUnresolved"/> Error. The guards keep the Error for a
/// kill that failed, a read that timed out, a release that did not report an exit and a runner
/// that still lists the session live.
/// </summary>
public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments("killed")]
    [Arguments("already-released")]
    public async Task C1499_a_release_the_runner_answers_exited_then_not_found_is_a_normal_release(string answer)
    {
        var runner = new ReleaseEvictingRunner
        {
            KillAnswer = answer == "killed"
                ? id => new SessionRunnerSessionDto(id, 4242, DateTime.UtcNow, "Exited", 137, AgentExitReason.KilledByRequest, 9)
                // SessionRunnerRuntime.ReleaseSlotUnderGateAsync's answer for a session it no longer holds.
                : id => new SessionRunnerSessionDto(id, null, DateTime.UtcNow, "Exited", null, AgentExitReason.Unknown, 0),
        };

        await using var h = await CreateReleaseEvictingHarnessAsync(runner);
        var (agentId, sessionId) = await SettleThroughReleaseEvictingRunnerAsync(h, runner);

        runner.Kills.ShouldBe([sessionId]);
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeFalse("a confirmed release retires the pool row");
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Stopped);
        session.FailureReason.ShouldBeNull("the kill was delivered and confirmed");
        (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId
            && (i.Kind == AgentIncidentKind.DelegateReleaseUnresolved || i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent)))
            .ShouldBe(0, "no Error and no deferred kill for a session the runner already released");
    }

    [Test]
    [Arguments("kill-fails")]
    [Arguments("read-times-out")]
    [Arguments("release-not-exited")]
    [Arguments("runner-lists-live")]
    public async Task C1499_a_release_that_is_not_confirmed_still_records_the_Error(string failure)
    {
        var runner = new ReleaseEvictingRunner();
        switch (failure)
        {
            case "kill-fails":
                runner.KillAnswer = id => throw new ConflictException(
                    $"Session '{id:D}' is still live after slot release; custody was retained.", "phone_home_error");
                break;
            case "read-times-out":
                runner.ReadFailure = _ => new TimeoutException("runner get timed out (c1499)");
                break;
            case "release-not-exited":
                runner.KillAnswer = id => new SessionRunnerSessionDto(id, 4242, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 9);
                break;
            case "runner-lists-live":
                runner.ListsLive = true;
                break;
        }

        await using var h = await CreateReleaseEvictingHarnessAsync(runner);
        var (agentId, sessionId) = await SettleThroughReleaseEvictingRunnerAsync(h, runner);

        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleOrDefaultAsync(a => a.Id == agentId);
        agent.ShouldNotBeNull("an unconfirmed release keeps the process's only owner");
        agent.Status.ShouldBe(AgentStatus.Idle);
        var incident = (await verify.AgentIncidents
            .Where(i => i.AgentId == agentId && i.Kind == AgentIncidentKind.DelegateReleaseUnresolved)
            .ToListAsync()).ShouldHaveSingleItem();
        incident.SessionId.ShouldBe(sessionId);
        incident.Severity.ShouldBe(AlertSeverity.Error);
    }

    // The harness deletes its agent and sessions on dispose, so the caller asserts inside its lifetime.
    private static Task<BridgeQueueHarness> CreateReleaseEvictingHarnessAsync(ReleaseEvictingRunner runner) =>
        BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConfigureServices = services => services.AddSingleton<ISessionRunnerClient>(runner),
        });

    private static async Task<(Guid AgentId, Guid SessionId)> SettleThroughReleaseEvictingRunnerAsync(
        BridgeQueueHarness h, ReleaseEvictingRunner runner)
    {
        await using (var db = CreateContext())
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.IsPoolDelegate, true).SetProperty(a => a.BoardId, (Guid?)null));
        var (task, sessionId) = await SeedDispatchedTaskAsync(h.TempRoot, configure: t =>
        {
            t.AgentId = h.AgentId;
            t.Workspace = WorkspaceMode.Worktree;
        });
        await BindAgentSessionAsync(h.AgentId, sessionId);
        runner.SessionId = sessionId;
        using var sessionScope = h.Provider.CreateScope();
        using var factory = new TestScopeFactory(configureServices: services =>
        {
            services.AddSingleton(h.Runtime);
            services.AddSingleton<IDelegateSessionStopper>(sessionScope.ServiceProvider.GetRequiredService<AgentSessionService>());
        });
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");

        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using (var settled = CreateContext())
            (await settled.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        return (h.AgentId, sessionId);
    }

    /// <summary>
    /// The phone-home runner's release shape: <c>KillAsync</c> is <c>ReleaseSlot</c>, which drops the
    /// record, so a later <c>GetAsync</c> answers the runner's 404 <c>not_found</c> frame
    /// (<c>PhoneHomeCommandDispatcher</c>'s <c>KeyNotFoundException</c> arm).
    /// </summary>
    private sealed class ReleaseEvictingRunner : ISessionRunnerClient
    {
        public Guid SessionId { get; set; }
        public bool ListsLive { get; set; }
        public Func<Guid, SessionRunnerSessionDto> KillAnswer { get; set; } =
            id => new SessionRunnerSessionDto(id, 4242, DateTime.UtcNow, "Exited", 137, AgentExitReason.KilledByRequest, 9);
        public Func<Guid, Exception> ReadFailure { get; set; } =
            id => new RunnerProblemException(404, $"Session '{id}' was not found.", "not_found");
        public List<Guid> Kills { get; } = [];

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(ListsLive
                ? [new SessionRunnerSessionDto(SessionId, 4242, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 9)]
                : []);

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Kills.Add(sessionId);
            return Task.FromResult(KillAnswer(sessionId));
        }

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            sessionId == SessionId ? Task.FromException<SessionRunnerSessionDto>(ReadFailure(sessionId))
                : throw new NotSupportedException();

        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(null);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
