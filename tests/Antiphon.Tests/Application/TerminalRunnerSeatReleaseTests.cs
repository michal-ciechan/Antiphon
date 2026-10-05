using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class TerminalRunnerSeatReleaseTests
{
    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Completed_attempt_registers_release_debt(AgentTaskStatus status)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(status);
        await f.RunAsync();
        await f.RunAsync();
        await using var db = f.Db();
        var rows = await db.RunnerSeatReleases.ToListAsync();
        rows.Count.ShouldBe(1, $"{status} ledger count");
        rows[0].TaskId.ShouldBe(f.TaskId); rows[0].Attempt.ShouldBe(1);
        rows[0].RunnerStoreId.ShouldBe(f.Directory.StoreId); rows[0].SessionId.ShouldBe(f.SessionId);
        rows[0].AcceptedStartedAt.ShouldBe(f.Observation.ExpectedAcceptedStartedAt);
        rows[0].SettlementRevision.ShouldBe((await db.AgentTasks.SingleAsync()).ConcurrencyToken);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        // The one migration includes all nullable answer fields and permits full Unicode input.
        var task = await db.AgentTasks.SingleAsync();
        task.ReleasedSeatAnswer.ShouldBeNull(); task.ReleasedSeatAnswerId.ShouldBeNull();
        task.ReleasedSeatAnswerRoundId.ShouldBeNull(); task.ReleasedSeatAnswerReleaseId.ShouldBeNull();
        task.ReleasedSeatAnswerTargetAttempt.ShouldBeNull(); task.ReleasedSeatAnswerAcceptedAt.ShouldBeNull();
        var body = new string('界', 5001);
        task.ReleasedSeatAnswer = body; task.ReleasedSeatAnswerId = Guid.NewGuid();
        task.ReleasedSeatAnswerRoundId = Guid.NewGuid(); task.ReleasedSeatAnswerReleaseId = rows[0].Id;
        task.ReleasedSeatAnswerTargetAttempt = 2; task.ReleasedSeatAnswerAcceptedAt = f.Now;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync()).ReleasedSeatAnswer.ShouldBe(body);
        // No FK cascade may erase rowless custody.
        await db.AgentTasks.ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync();
        (await db.RunnerSeatReleases.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Incomplete_settlements_never_authorize_release()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        await f.EditAsync((task, _) => { task.Status = AgentTaskStatus.Working; task.CompletedAt = null; });
        await using (var writer = f.Db())
        {
            await using var tx = await writer.Database.BeginTransactionAsync();
            var t = await writer.AgentTasks.SingleAsync(); t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = f.Now.AddMinutes(-3);
            await writer.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "uncommitted settlement");
            await tx.RollbackAsync();
        }
        await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Succeeded);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "null CompletedAt");
        await f.EditAsync((t, _) => { t.Status = AgentTaskStatus.Blocked; t.CompletedAt = f.Now.AddMinutes(-3); t.Result = null; t.ReportEvidence = AgentTaskReportEvidence.Legacy; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteReport, "routing hold");
        f.Wire.ConditionalCommands.ShouldBe(0);
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
        await f.EditAsync((t, _) => { t.Result = "runner-sync blocked report"; t.ReportEvidence = AgentTaskReportEvidence.Marked; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "completed marked Blocked remains eligible");
    }

    [Test]
    public async Task Unsettled_blocked_or_queued_owner_is_preserved()
    {
        foreach (var bySession in new[] { true, false })
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var owner = new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Status = status,
                AgentSessionId = bySession ? f.SessionId : Guid.NewGuid(), AgentId = bySession ? Guid.NewGuid() : f.AgentId,
                CreatedAt = f.Now };
            db.AgentTasks.Add(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Owned, $"{status} owner by {(bySession ? "session" : "agent")}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.AgentTasks.Remove(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "otherwise eligible");
        }
        await using var noAgent = await RunnerSeatReleaseFixture.CreateAsync();
        await noAgent.EditAsync((t, _) => t.AgentId = null);
        (await noAgent.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "null agent is not abandonment authority or a veto");
    }

    [Test]
    public async Task Standing_warm_and_verification_owners_are_preserved()
    {
        var variants = new (TerminalRunnerSeatDecision Decision, Action<AgentTask, Agent> Edit)[]
        {
            (TerminalRunnerSeatDecision.StandingOwner, (_, a) => a.IsPoolDelegate = false),
            (TerminalRunnerSeatDecision.AlwaysOnOwner, (_, a) => a.AlwaysOn = true),
            (TerminalRunnerSeatDecision.BoardOwner, (_, a) => a.BoardId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistRole = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistOwnerId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (t, _) => t.Role = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.WarmPool, (t, a) => { t.Workspace = WorkspaceMode.Shared; a.PoolIdleSince = DateTime.UtcNow; }),
            (TerminalRunnerSeatDecision.VerificationOwner, (_, _) => { }),
        };
        foreach (var variant in variants)
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(sourced: variant.Decision == TerminalRunnerSeatDecision.VerificationOwner);
            // A real board satisfies the FK; other variants each change only their own guard.
            await using var db = f.Db();
            if (variant.Decision == TerminalRunnerSeatDecision.BoardOwner)
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "seat ownership project" };
                db.Projects.Add(project); await db.SaveChangesAsync();
                var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "seat owner" };
                db.Boards.Add(board); await db.SaveChangesAsync();
                await f.EditAsync((_, a) => a.BoardId = board.Id);
            }
            else await f.EditAsync(variant.Edit);
            (await f.RunAsync()).Decision.ShouldBe(variant.Decision);
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
            (await db.Agents.CountAsync(a => a.Id == f.AgentId)).ShouldBe(1);
        }
        await using var eligible = await RunnerSeatReleaseFixture.CreateAsync();
        (await eligible.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved);
    }

    [Test]
    public async Task Settlement_age_has_its_own_safety_margin()
    {
        foreach (var seconds in new[] { 0d, 119.999, 120, 120.001 })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await f.EditAsync((t, _) => t.CompletedAt = f.Now.AddSeconds(-seconds));
            (await f.RunAsync()).Decision.ShouldBe(seconds < 120 ? TerminalRunnerSeatDecision.SettlementTooYoung : TerminalRunnerSeatDecision.Reserved,
                $"server settlement age {seconds}; runner already qualified with an independent clock");
            f.Wire.ConditionalCommands.ShouldBe(0); // S3a stops at reservation.
        }
    }

    [Test]
    public async Task Pending_delivery_prevents_release()
    {
        var inputs = new (string Kind, QueuedMessageOrigin Origin)[]
        {
            ("brief", QueuedMessageOrigin.Delegation), ("answer", QueuedMessageOrigin.Delegation),
            ("channel", QueuedMessageOrigin.Channel), ("mention", QueuedMessageOrigin.Mention),
            ("completion", QueuedMessageOrigin.Delegation), ("continuation", QueuedMessageOrigin.System),
            ("recovery", QueuedMessageOrigin.System),
        };
        foreach (var input in inputs)
        foreach (var shape in new[] { "pending", "attempted", "held" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var message = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Body = $"{input.Kind}: complete delivery canary", Origin = input.Origin, CreatedAt = f.Now,
                Status = shape == "attempted" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending,
                DeliveryAttempts = shape == "attempted" ? 1 : 0,
                LastDeliveryStartedAt = shape == "attempted" ? f.Now : null,
                HoldUntil = shape == "held" ? f.Now.AddHours(1) : null };
            db.SessionQueuedMessages.Add(message); await db.SaveChangesAsync();
            var before = JsonSerializer.Serialize(message);
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PendingDelivery, $"{input.Kind}/{shape}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.ChangeTracker.Clear();
            JsonSerializer.Serialize(await db.SessionQueuedMessages.SingleAsync()).ShouldBe(before, "pending bytes/status/attempt evidence retained");
            await db.SessionQueuedMessages.ExecuteDeleteAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "pending guard is the only veto");
        }
    }

    [Test]
    public async Task Concurrent_reservations_have_one_winner()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        // Registration first, then two independent connections race on the SAME existing row.
        f.Wire.Unsupported = true;
        await f.RunAsync();
        f.Wire.Unsupported = false;
        await using var read = f.Db();
        var release = await read.RunnerSeatReleases.SingleAsync();
        var task = await read.AgentTasks.SingleAsync();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Race()
        {
            using var scope = f.Harness.Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
            await barrier.Task;
            return await service.TryReserveAsync(release, task, f.Wire.Qualified, CancellationToken.None);
        }
        var first = Race(); var second = Race(); barrier.SetResult();
        var winners = await Task.WhenAll(first, second);
        winners.Sum().ShouldBe(1, "exactly one revision-conditional reservation winner");
        read.ChangeTracker.Clear();
        var saved = await read.RunnerSeatReleases.SingleAsync();
        saved.Revision.ShouldBe(release.Revision + 1); saved.ActionId.ShouldNotBeNull();
    }

    [Test]
    public async Task Unsupported_server_transport_never_falls_back_to_force()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        f.Wire.Unsupported = true;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0); f.Wire.ConditionalCommands.ShouldBe(0);
        var request = new TerminalSeatReleaseRequest(Guid.NewGuid(), f.Observation, "token");
        var routed = new RoutingSessionRunnerClient(f.Directory);
        (await routed.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0);

        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: f.Schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync(); host.Directory.MarkRecovered(live);
        var force = 0;
        peer.Reply = frame =>
        {
            if (frame.Operation is PhoneHomeOperation.ReleaseSlot or PhoneHomeOperation.KillGeneration) force++;
            return frame.Operation is PhoneHomeOperation.ObserveTerminalSeat or PhoneHomeOperation.ReleaseTerminalSeat
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new { code = "unsupported_operation" })) : null;
        };
        f.Directory.Client = new RunnerScopedSessionRunnerClient(host.Directory, host.AllowedRunnerId);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        (await f.Directory.Client.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        force.ShouldBe(0, "old phone-home peer never falls back to force/generation kill");
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
        f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Disabled);
        new TerminalRunnerSeatReleaseOptions().AutomaticEnabled.ShouldBeFalse();
    }
}
