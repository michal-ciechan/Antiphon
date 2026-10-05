using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
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
    public async Task Sweep_budget_is_bounded_and_resumes_fairly()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", rowless: true);
        var ids = Enumerable.Range(1, 8).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        var inventory = ids.Select(id => new SessionRunnerSessionDto(id, null, f.Now.AddDays(-3), "Running",
            null, AgentExitReason.Unknown, 0, AcceptedStartedAt: f.Now.AddDays(-3))).ToArray();
        f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available(inventory));
        RunnerSeatDiscoveryCursor? cursor = null;
        var seen = new HashSet<Guid>();
        for (var tick = 0; tick < 3; tick++)
        {
            var observations = f.Live!.Observations.Count;
            var result = await f.DiscoverAsync(cursor: cursor, boundary: (cut, _) =>
                cut == "Discovery:" + ids[0].ToString("D") ? throw new IOException("poisoned candidate") : Task.CompletedTask);
            result.Candidates.Count.ShouldBe(3, "G-52: supplied budget must be used and never exceeded");
            result.InventoryCalls.ShouldBe(1, "paging candidates must not repeat the full List RPC");
            (f.Live.Observations.Count - observations).ShouldBeLessThanOrEqualTo(6,
                "candidate observations are bounded along with ledger processing");
            await using var db = f.Db();
            (await db.RunnerSeatReleases.CountAsync()).ShouldBeLessThanOrEqualTo((tick + 1) * 3);
            seen.UnionWith(result.Candidates.Select(c => c.SessionId));
            cursor = result.Continuation;
        }
        seen.Count.ShouldBe(8, "PC-52: the saved continuation advances past a poisoned first candidate");
        await using (var db = f.Db())
            (await db.RunnerSeatReleases.CountAsync()).ShouldBe(7, "all non-poison candidates reach durable discovery");
        f.Live!.ConditionalCommands.ShouldBe(0);
    }

    [Test]
    public async Task One_runner_failure_does_not_hide_other_candidates()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", rowless: true);
        var live = f.Live!;
        await live.SubmitAsync("healthy runner candidate");
        var poison = new SessionRunnerSessionDto(Guid.Empty, null, f.Now, "Running", null,
            AgentExitReason.Unknown, 0, AcceptedStartedAt: f.Now);
        f.Directory.RunnerIds = ["broken", "fixture"];
        f.Directory.InventoryByRunner = async id => id == "broken"
            ? throw new IOException("unavailable runner")
            : new RunnerInventory.Available(new[] { poison }.Concat(await live.Client.ListAsync(default)).ToArray());
        Task Boundary(string cut, CancellationToken _) => cut == "Discovery:" + Guid.Empty.ToString("D")
            ? throw new IOException("candidate failure") : Task.CompletedTask;
        await f.DiscoverAsync(boundary: Boundary);
        live.Clock.Advance(TimeSpan.FromSeconds(120));
        var result = await f.DiscoverAsync(boundary: Boundary);
        result.Released.ShouldBe(1, "PC-53: later runner and later candidate must confirm release");
        live.Child.Kills.ShouldBe(1); live.ForceCommands.ShouldBe(0);
        await using var db = f.Db();
        (await db.RunnerSeatReleases.SingleAsync()).State.ShouldBe(RunnerSeatReleaseState.Confirmed);
    }

    [Test]
    public async Task Unknown_server_session_with_idle_runner_is_released()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", phoneHome: phoneHome, rowless: true);
            f.Directory.IsLocal = !phoneHome;
            f.Directory.RunnerId = phoneHome ? "fixture" : "desktop";
            var live = f.Live!;
            await live.SubmitAsync("rowless task with genuine native receipt");
            var first = await f.DiscoverAsync();
            first.Released.ShouldBe(0);
            first.Candidates.ShouldHaveSingleItem().Disposition.ShouldBe("Waiting", "rowlessness cannot backdate the first observation");
            live.Clock.Advance(TimeSpan.FromSeconds(119.999));
            (await f.DiscoverAsync()).Released.ShouldBe(0);
            live.Clock.Advance(TimeSpan.FromMilliseconds(1));
            (await f.DiscoverAsync()).Released.ShouldBe(1, "PC-55: " + (phoneHome ? "phone-home" : "local"));
            live.Child.Kills.ShouldBe(1); live.Runtime.LiveSessionCount.ShouldBe(0);
            live.ConditionalCommands.ShouldBe(1); live.ForceCommands.ShouldBe(0);
            live.Observations.ShouldAllBe(r => r.UseCapturedDeliveryEvidence && r.PromptBindingIdentity == "" && r.PromptFloorRevision == -1);
            await using var db = f.Db();
            (await db.AgentSessions.AnyAsync(s => s.Id == live.SessionId)).ShouldBeFalse("never synthesize a session row");
            (await db.RunnerSeatReleases.SingleAsync()).State.ShouldBe(RunnerSeatReleaseState.Confirmed);
        }
    }

    [Test]
    public async Task Discovery_is_idempotent_across_restart()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", phoneHome: phoneHome, rowless: true);
            var live = f.Live!;
            await live.SubmitAsync("deduplicated rowless generation");
            var concurrent = await Task.WhenAll(f.DiscoverAsync(), f.DiscoverAsync());
            Guid id;
            await using (var db = f.Db())
                id = (await db.RunnerSeatReleases.ToListAsync()).ShouldHaveSingleItem("PC-56: concurrent tuple upsert").Id;
            foreach (var result in concurrent)
                result.Candidates.ShouldHaveSingleItem().ReleaseId.ShouldBe(id,
                    "PC-56: a duplicate insert failure must not masquerade as successful deduplication");
            await f.DiscoverAsync();
            await f.RestartAsync();
            live.Clock.Advance(TimeSpan.FromSeconds(120));
            await Task.WhenAll(f.DiscoverAsync(), f.DiscoverAsync());
            // An overlapping pass may see the queue gate held; a normal later pass finishes it.
            await f.DiscoverAsync();
            await f.RestartAsync();
            await f.DiscoverAsync();
            await using var read = f.Db();
            var release = (await read.RunnerSeatReleases.ToListAsync()).ShouldHaveSingleItem("PC-56: restart/repeat preserves tuple identity");
            release.Id.ShouldBe(id); release.ActionId.ShouldNotBeNull();
            release.State.ShouldBe(RunnerSeatReleaseState.Confirmed);
            live.ConditionalCommands.ShouldBe(1, "one durable action per generation");
            live.Child.Kills.ShouldBe(1); live.ForceCommands.ShouldBe(0);
        }
    }

    [Test]
    public async Task Discovery_request_uses_runner_owned_delivery_evidence()
    {
        foreach (var phoneHome in new[] { false, true })
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: provider, phoneHome: phoneHome, rowless: true);
            var live = f.Live!;
            await using (var db = f.Db())
            {
                (await db.AgentSessions.AnyAsync(s => s.Id == f.CandidateId)).ShouldBeFalse();
                (await db.SessionQueuedMessages.AnyAsync(s => s.AgentSessionId == f.CandidateId)).ShouldBeFalse();
            }
            var missing = await f.AcquireAsync();
            var request = missing.Request.ShouldNotBeNull("G-100: production factory must emit a captured request without a server row");
            request.UseCapturedDeliveryEvidence.ShouldBeTrue("PC-100");
            request.PromptBindingIdentity.ShouldBe(""); request.PromptFloorRevision.ShouldBe(-1);
            missing.Hold.ShouldBe(TerminalRunnerSeatDecision.Unknown);
            live.SwallowNextEnter = true;
            const string body = "actual current task body\nreceived through the server client";
            await live.SubmitAsync(body);
            live.NativeSubmissions.ShouldBeEmpty("swallowed Enter must not fabricate native evidence");
            (await f.AcquireAsync()).Observation!.Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt);
            live.Clock.Advance(TimeSpan.FromSeconds(120));
            (await f.AcquireAsync()).Observation!.Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt);
            live.Child.Kills.ShouldBe(0);
            await live.Client.SendInputAsync(live.SessionId, "\r", default);
            live.NativeSubmissions.ShouldBe([body]);
            (await f.AcquireAsync()).Hold.ShouldBe(TerminalRunnerSeatDecision.Waiting);
            live.Clock.Advance(TimeSpan.FromSeconds(120));
            var qualified = await f.AcquireAsync();
            qualified.Hold.ShouldBeNull();
            qualified.Observation!.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
            foreach (var emitted in live.Observations)
            {
                emitted.UseCapturedDeliveryEvidence.ShouldBeTrue("PC-100: actual wire request");
                emitted.PromptBindingIdentity.ShouldBe(""); emitted.PromptFloorRevision.ShouldBe(-1);
            }
            var receipt = await live.Client.ReleaseTerminalSeatAsync(live.SessionId,
                new(Guid.NewGuid(), qualified.Request!, qualified.Observation.Token!), default);
            receipt.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released);
            live.Child.Kills.ShouldBe(1); live.ConditionalCommands.ShouldBe(1); live.ForceCommands.ShouldBe(0);
            (await live.Client.ListAsync(default)).ShouldBeEmpty();
            await using var read = f.Db();
            (await read.RunnerSeatReleases.CountAsync()).ShouldBe(0, "S3h acquisition does not reserve rowless debt");
        }
    }

    [Test]
    public async Task Server_restart_reacquires_runner_delivery_evidence()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", phoneHome: phoneHome);
            var live = f.Live!;
            await live.SubmitAsync("completed owned attempt");
            var first = await f.AcquireAsync();
            first.Hold.ShouldBe(TerminalRunnerSeatDecision.Waiting, "G-101: acquire real evidence before persisting the reservation");
            live.Clock.Advance(TimeSpan.FromSeconds(120));
            var qualified = await f.AcquireAsync();
            qualified.Hold.ShouldBeNull();
            var reservation = await f.ReserveAsync(qualified.Request!);
            reservation.Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved);
            var releaseId = reservation.ReleaseId.ShouldNotBeNull();
            await using (var db = f.Db())
            {
                var row = await db.RunnerSeatReleases.SingleAsync();
                row.Id.ShouldBe(releaseId); row.ObservationToken.ShouldNotBeNullOrWhiteSpace();
            }
            var calls = live.Observations.Count;
            var runner = live.Runtime;
            await f.RestartAsync();
            live.Runtime.ShouldBeSameAs(runner);
            var afterServer = await f.AcquireAsync();
            live.Observations.Count.ShouldBe(calls + 1, "PC-101: server restart must make a fresh runner observation");
            afterServer.Hold.ShouldBeNull();
            afterServer.Request.ShouldBe(qualified.Request);
            await using (var db = f.Db())
                (await db.RunnerSeatReleases.SingleAsync()).Id.ShouldBe(releaseId);

            await live.RestartRunnerAsync();
            f.Directory.Client = live.Client;
            calls = live.Observations.Count;
            var afterRunner = await f.AcquireAsync();
            live.Observations.Count.ShouldBe(calls + 1);
            afterRunner.Hold.ShouldBe(TerminalRunnerSeatDecision.Unknown);
            afterRunner.Observation!.Token.ShouldBeNull();
            await f.AdvanceAsync(releaseId, qualified.Request!);
            live.ConditionalCommands.ShouldBe(0, "a persisted reservation cannot manufacture fresh capture");
            live.Child.Kills.ShouldBe(0); live.Runtime.LiveSessionCount.ShouldBe(1);
            // A later legitimate delivery can reacquire proof; time/idle alone cannot.
            await live.SubmitAsync("legitimate post-restart work");
            (await f.AcquireAsync()).Hold.ShouldBe(TerminalRunnerSeatDecision.Waiting);
            live.Clock.Advance(TimeSpan.FromSeconds(120));
            (await f.AcquireAsync()).Hold.ShouldBeNull();
            live.ForceCommands.ShouldBe(0);
        }
    }

    [Test]
    public async Task Evidence_missing_or_peer_unsupported_defers_discovery()
    {
        foreach (var phoneHome in new[] { false, true })
        foreach (var variant in new[] { "release-only", "evidence-only", "missing", "invalid", "malformed", "lost", "unavailable", "stale", "adopting", "store", "valid" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(provider: "Codex", phoneHome: phoneHome, rowless: true);
            var live = f.Live!;
            if (variant != "missing") await live.SubmitAsync("real task for refusal control");
            if (variant == "release-only") f.Directory.FeaturesOverride = [RunnerCapabilityFeatures.TerminalSeatReleaseV1];
            if (variant == "evidence-only") f.Directory.FeaturesOverride = [RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1];
            if (variant == "invalid")
            {
                await live.Client.SendInputAsync(live.SessionId, "\u001b[A", default);
                await live.Client.SendInputAsync(live.SessionId, "\r", default);
            }
            if (variant is "malformed" or "lost") live.ResponseFault = variant;
            if (variant == "unavailable") f.Directory.Available = false;
            if (variant == "stale") f.Directory.Stale = true;
            if (variant == "adopting") f.Directory.Recovered = false;
            if (variant == "store") f.Directory.LiveStoreOverride = Guid.NewGuid();
            var result = await f.AcquireAsync();
            result.Hold.ShouldBe(variant switch
            {
                "release-only" or "evidence-only" => TerminalRunnerSeatDecision.Unsupported,
                "store" => TerminalRunnerSeatDecision.IdentityUnknown,
                "valid" => TerminalRunnerSeatDecision.Waiting,
                _ => TerminalRunnerSeatDecision.Unknown
            }, "G-102: " + variant);
            if (variant is "release-only" or "evidence-only" or "unavailable" or "stale" or "adopting" or "store")
                live.Observations.ShouldBeEmpty("PC-102: no captured RPC to an unsupported/unavailable peer");
            else live.Observations.Count.ShouldBe(1, "refusal must exercise the real wire");
            live.ForceCommands.ShouldBe(0); live.ConditionalCommands.ShouldBe(0); live.Child.Kills.ShouldBe(0);
            live.Runtime.LiveSessionCount.ShouldBe(1);
            await using var db = f.Db();
            (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
        }
    }

    [Test]
    public async Task Claim_between_inventory_and_release_vetoes_action()
    {
        // Each edit changes only its own reservation guard; other expected facts stay valid.
        foreach (var variant in new[] { "attempt", "revision", "status", "event" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            if (variant == "event")
            {
                await using var seed = f.Db();
                seed.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.TaskId,
                    At = f.Now.AddMinutes(-3), Type = AgentTaskEventType.Completed });
                await seed.SaveChangesAsync();
            }
            await f.ReleaseAsync(async (cut, _) =>
            {
                if (cut != "BeforeReservation") return;
                await f.EditAsync((t, _) =>
                {
                    if (variant == "attempt") t.Attempt++;
                    if (variant == "revision") t.ConcurrencyToken = Guid.NewGuid();
                    if (variant == "status") t.Status = AgentTaskStatus.Failed;
                });
                if (variant == "event")
                {
                    await using var db = f.Db();
                    await db.AgentTaskEvents.ExecuteDeleteAsync();
                    db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.TaskId,
                        At = f.Now.AddMinutes(-3), Type = AgentTaskEventType.Completed });
                    await db.SaveChangesAsync();
                }
            });
            await using var db = f.Db();
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0, variant);
            f.Wire.ConditionalCommands.ShouldBe(0, variant);
        }
        foreach (var bySession in new[] { true, false })
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            var reached = false;
            await f.ReleaseAsync(async (cut, _) =>
            {
                if (cut != "BeforeDispatch") return;
                reached = true;
                await using var db = f.Db();
                db.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(),
                    Status = status, CreatedAt = f.Now,
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
        foreach (var variant in new[] { "attempt", "revision", "status", "queue", "working" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await f.ReleaseAsync(async (cut, _) =>
            {
                if (cut != "BeforeDispatch") return;
                await f.EditAsync((t, _) =>
                {
                    if (variant == "attempt") t.Attempt++;
                    if (variant == "revision") t.ConcurrencyToken = Guid.NewGuid();
                    if (variant == "status") t.Status = AgentTaskStatus.Failed;
                });
                if (variant == "queue")
                {
                    await using var db = f.Db();
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                        Body = "unsent answer canary", CreatedAt = f.Now, Status = QueuedMessageStatus.Pending });
                    await db.SaveChangesAsync();
                }
                if (variant == "working")
                    await f.IngestAsync(TranscriptKinds.UserPrompt, "new work", f.Now);
            });
            f.Wire.ConditionalCommands.ShouldBe(0, $"pre-command {variant}");
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
                if (variant == "generation") session.StartedAt = session.StartedAt.AddSeconds(1);
                if (variant == "session")
                {
                    db.AgentSessions.Add(new AgentSession { Id = replacementId, Status = SessionStatus.Running,
                        RunnerId = session.RunnerId, RunnerStoreId = session.RunnerStoreId,
                        RunnerCwd = session.RunnerCwd, StartedAt = session.StartedAt });
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
            f.Wire.CallbackFailure.ShouldBeNull("the concurrent replacement must commit successfully");
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
            RunnerSeatRelease? atWire = null;
            TerminalSeatReleaseRequest? command = null;
            f.Wire.AtCommand = async request =>
            {
                await using var db = f.Db();
                command = request;
                atWire = await db.RunnerSeatReleases.SingleOrDefaultAsync();
            };
            await f.ReleaseAsync();
            command.ShouldNotBeNull("the conditional command must actually be sent");
            atWire.ShouldNotBeNull("a separate connection must see the committed reservation at wire entry");
            atWire.ActionId.ShouldBe(command.ActionId, "committed action-before-wire");
            atWire.State.ShouldBe(RunnerSeatReleaseState.Unresolved, "the send intent survives an interrupted caller");
            atWire.ObservationToken.ShouldBe(command.Token);
            atWire.AcceptedStartedAt.ShouldBe(command.Observation.ExpectedAcceptedStartedAt);
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
