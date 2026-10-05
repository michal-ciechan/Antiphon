using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class RunnerSeatOrphanSweepTests
{
    [Test]
    public async Task Claim_between_inventory_and_release_vetoes_action()
    {
        // Each edit changes only its own reservation guard; other expected facts stay valid.
        foreach (var variant in new[] { "attempt", "revision", "status" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await f.ReleaseAsync(async (cut, _) =>
            {
                if (cut != "BeforeReservation") return;
                await f.EditAsync((t, _) =>
                {
                    if (variant == "attempt") t.Attempt++;
                    if (variant == "revision") t.ConcurrencyToken = Guid.NewGuid();
                    if (variant == "status") t.Status = AgentTaskStatus.Failed;
                });
            });
            await using var db = f.Db();
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0, variant);
            f.Wire.ConditionalCommands.ShouldBe(0, variant);
        }
        foreach (var bySession in new[] { true, false })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            var reached = false;
            await f.ReleaseAsync(async (cut, _) =>
            {
                if (cut != "BeforeDispatch") return;
                reached = true;
                await using var db = f.Db();
                db.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(),
                    Status = AgentTaskStatus.Working, CreatedAt = f.Now,
                    AgentSessionId = bySession ? f.SessionId : Guid.NewGuid(),
                    AgentId = bySession ? Guid.NewGuid() : f.AgentId });
                await db.SaveChangesAsync();
            });
            reached.ShouldBeTrue("the claim must commit after reservation and before the command");
            f.Wire.ConditionalCommands.ShouldBe(0, "fresh ownership must veto the reserved action");
            await using var read = f.Db();
            (await read.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
            (await read.RunnerSeatReleases.SingleAsync()).State.ShouldNotBe(RunnerSeatReleaseState.Confirmed);
        }
        await using var eligible = await RunnerSeatReleaseFixture.CreateAsync();
        await eligible.ReleaseAsync();
        eligible.Wire.ConditionalCommands.ShouldBe(1, "otherwise eligible control reaches the real transport");
    }

    [Test]
    public async Task Response_does_not_stop_a_replacement()
    {
        foreach (var variant in new[] { "runner", "store", "generation", "session", "owner", "stopped" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            var replacementId = Guid.NewGuid();
            var end = f.Now.AddMinutes(-1);
            f.Wire.AtCommand = async _ =>
            {
                await using var db = f.Db();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
                if (variant == "runner") session.RunnerId = "replacement";
                if (variant == "store") session.RunnerStoreId = replacementId;
                if (variant == "generation") session.StartedAt = session.StartedAt!.Value.AddSeconds(1);
                if (variant == "session")
                {
                    db.AgentSessions.Add(new AgentSession { Id = replacementId, Status = SessionStatus.Running,
                        RunnerId = session.RunnerId, RunnerStoreId = session.RunnerStoreId, StartedAt = session.StartedAt });
                    (await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId)).AgentSessionId = replacementId;
                }
                if (variant == "owner") session.StandingAgentId = replacementId;
                if (variant == "stopped")
                {
                    session.Status = SessionStatus.Stopped; session.TerminationSource = SessionTerminationSource.OperatorRequest;
                    session.EndedAt = end; session.LastSeenAt = end;
                }
                await db.SaveChangesAsync();
            };
            await f.ReleaseAsync();
            f.Wire.ConditionalCommands.ShouldBe(1, variant);
            await using var read = f.Db();
            var saved = await read.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            var receipt = await read.RunnerSeatReleases.SingleAsync();
            if (variant == "stopped")
            {
                saved.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
                saved.EndedAt.ShouldBe(end); saved.LastSeenAt.ShouldBe(end);
                receipt.State.ShouldBe(RunnerSeatReleaseState.Confirmed);
            }
            else
            {
                saved.Status.ShouldBe(SessionStatus.Running, variant);
                receipt.State.ShouldNotBe(RunnerSeatReleaseState.Confirmed, variant);
                if (variant == "owner") saved.StandingAgentId.ShouldBe(replacementId);
                if (variant == "session")
                    (await read.AgentSessions.SingleAsync(s => s.Id == replacementId)).Status.ShouldBe(SessionStatus.Running);
            }
            (await read.Agents.CountAsync(a => a.Id == f.AgentId)).ShouldBe(1);
        }
        foreach (var part in new[] { "session", "action", "generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            f.Wire.RewriteReply = r => part switch
            {
                "session" => r with { SessionId = Guid.NewGuid() },
                "action" => r with { ActionId = Guid.NewGuid() },
                _ => r with { AcceptedStartedAt = r.AcceptedStartedAt!.Value.AddSeconds(1) }
            };
            await f.ReleaseAsync();
            await using var db = f.Db();
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running, part);
            (await db.RunnerSeatReleases.SingleAsync()).State.ShouldNotBe(RunnerSeatReleaseState.Confirmed, part);
        }
    }

    [Test]
    public async Task Missing_or_stale_runner_evidence_is_not_absence()
    {
        foreach (var variant in new[] { "unavailable", "partial", "timeout", "stale", "adopting", "store", "disconnect-after-list", "live", "unknown-generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            f.Wire.DropReply = true;
            await f.ReleaseAsync();
            f.Wire.ConditionalCommands.ShouldBe(1, "the ambiguous command really crossed the transport");
            f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
            if (variant is "unavailable" or "partial")
                f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable(variant));
            if (variant == "timeout") f.Directory.Inventory = () => throw new TimeoutException("fixture list timeout");
            if (variant == "stale") f.Directory.Stale = true;
            if (variant == "adopting") f.Directory.Recovered = false;
            if (variant == "store") f.Directory.LiveStoreOverride = Guid.NewGuid();
            if (variant == "disconnect-after-list") f.Directory.Inventory = () =>
            {
                f.Directory.Available = false;
                return Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
            };
            if (variant is "live" or "unknown-generation") f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(
                new RunnerInventory.Available([new SessionRunnerSessionDto(f.SessionId, 123, f.Now.AddHours(-1),
                    "Running", null, default, 0, AcceptedStartedAt: variant == "live" ? f.Observation.ExpectedAcceptedStartedAt : null)]));
            await f.ReleaseAsync(); // A fresh DI scope/connection, with no in-memory action result.
            await using var db = f.Db();
            var debt = await db.RunnerSeatReleases.SingleAsync();
            debt.State.ShouldBe(RunnerSeatReleaseState.Unresolved, variant);
            debt.ConfirmedAt.ShouldBeNull(variant);
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running, variant);
            f.Wire.ConditionalCommands.ShouldBe(1, variant);
            f.Wire.ForceCommands.ShouldBe(0);
        }
    }

    [Test]
    public async Task Reservation_precedes_the_runner_command()
    {
        foreach (var outcome in Enum.GetValues<TerminalSeatReleaseOutcome>())
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            f.Wire.Outcome = outcome;
            var witnessed = false;
            f.Wire.AtCommand = async request =>
            {
                await using var db = f.Db();
                var receipt = await db.RunnerSeatReleases.SingleAsync();
                receipt.ActionId.ShouldBe(request.ActionId, "a separate connection reads committed action-before-wire");
                receipt.State.ShouldBe(RunnerSeatReleaseState.Unresolved, "the send intent survives an interrupted caller");
                receipt.ObservationToken.ShouldBe(request.Token);
                receipt.AcceptedStartedAt.ShouldBe(request.Observation.ExpectedAcceptedStartedAt);
                witnessed = true;
            };
            await f.ReleaseAsync();
            witnessed.ShouldBeTrue("the conditional command must actually be sent");
            await using var read = f.Db();
            var receipt = await read.RunnerSeatReleases.SingleAsync();
            var confirmed = outcome is TerminalSeatReleaseOutcome.Released or TerminalSeatReleaseOutcome.AlreadyExited or TerminalSeatReleaseOutcome.AlreadyAbsent;
            receipt.State.ShouldBe(confirmed ? RunnerSeatReleaseState.Confirmed : RunnerSeatReleaseState.Unresolved, outcome.ToString());
            var session = await read.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            session.Status.ShouldBe(confirmed ? SessionStatus.Stopped : SessionStatus.Running, outcome.ToString());
            if (confirmed)
            {
                session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
                session.EndedAt.ShouldBe(f.Now); session.LastSeenAt.ShouldBe(f.Now);
                receipt.ConfirmedAt.ShouldBe(f.Now);
            }
            (await read.AgentTasks.SingleAsync(t => t.Id == f.TaskId)).Result.ShouldBe("completed report");
            f.Wire.ForceCommands.ShouldBe(0);
        }
    }

    [Test]
    public async Task Lost_reply_reconciles_without_blind_second_kill()
    {
        foreach (var exited in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            f.Wire.DropReply = true;
            f.Wire.AtCommand = _ =>
            {
                f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available(exited
                    ? [new SessionRunnerSessionDto(f.SessionId, null, f.Now.AddHours(-1), "Exited", 0, default, 0,
                        AcceptedStartedAt: f.Observation.ExpectedAcceptedStartedAt)] : []));
                return Task.CompletedTask;
            };
            await f.ReleaseAsync();
            f.Wire.ConditionalCommands.ShouldBe(1, "first mutation executed but its reply was lost");
            await using (var pending = f.Db())
                (await pending.RunnerSeatReleases.SingleAsync()).State.ShouldBe(RunnerSeatReleaseState.Unresolved);
            await f.ReleaseAsync();
            await f.ReleaseAsync();
            f.Wire.ConditionalCommands.ShouldBe(1, "recovery must observe before considering another mutation");
            f.Directory.InventoryCalls.ShouldBe(1, "confirmed receipt is durable and idempotent");
            await using var read = f.Db();
            var receipt = await read.RunnerSeatReleases.SingleAsync();
            receipt.State.ShouldBe(RunnerSeatReleaseState.Confirmed);
            receipt.ActionId.ShouldBe(f.Wire.Requests.Single().ActionId);
            receipt.OutcomeCode.ShouldBe(exited ? "AlreadyExited" : "AlreadyAbsent");
            (await read.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Stopped);
        }
    }
}
