using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using RunnerSettings = Antiphon.SessionRunner.SessionRunnerSettings;
using SessionRunnerSettings = Antiphon.Server.Application.Settings.SessionRunnerSettings;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1153 S4: the dispatcher over the production runner clients. Design: V-14..V-20, V-24.
/// Fixture: the isolated PostgreSQL schema, <c>BridgeQueueHarness</c>/<c>OpenSweep</c> from the
/// S1 partial, the production <c>SessionRunnerHttpClient</c> against the production
/// <c>AbsenceEvidenceRoutes</c> and session/transcript routes hosted on a random-port Kestrel
/// with the real evidence service over a temp root and a synthetic key; for the remote case the
/// real <c>PhoneHomeRunnerClient</c> over <c>PhoneHomeTestHost</c> with a real
/// <c>PhoneHomeCommandDispatcher</c> as the scripted peer. Never a fake empty transcript.
/// Shapes a real runner cannot produce (an unsigned-nonce, wrong-generation, incomplete or
/// transcript-shaped answer, a slow or unreachable route) come from a signing stub handler under
/// the same production HTTP client and the same validator.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// V-14. Refused enqueue after a successful prepare, real transcript 404, due sweeps.
    /// Decisive: Blocked, CompletedAt null, FailureReason DispatchLaunchAbsentReason, one Blocked
    /// event, brief/spill bytes identical, queued parent note with SourceTaskId and stable reason,
    /// Starts/Kills/Releases/Inputs 0, stopper empty, exactly one certify request observed on the
    /// host, record state ClosedUnused on disk.
    /// </summary>
    [Test]
    [Arguments("local-http")]
    [Arguments("remote-phone-home")]
    public async Task C1153_Real_client_certificate_holds_original_input(string transport)
    {
        if (transport == "local-http")
        {
            await RealClientLocalHoldAsync();
            return;
        }

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var remote = await RemoteEvidence.StartAsync(schema.ConnectionString);
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Parent = true, RunnerId = remote.Host.AllowedRunnerId,
        });
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            await db.AgentSessions.Where(s => s.Id == seeded.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunnerStoreId, remote.Runtime.RunnerStoreId));
        // The cold-dispatch prepare call (the dispatcher's own call site is V-18 cold-fresh) is the
        // same routed client method; here it travels the authenticated phone-home connection.
        var routing = new RoutingSessionRunnerClient(remote.Host.Directory);
        (await routing.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, remote.Runtime.RunnerStoreId, default))
            .Prepared.ShouldBeTrue(transport);
        // A misleading local list names the session; the remote owner's inventory decides.
        var runner = new CountingRunner { Evidence = routing };
        runner.Sessions.Add(Listed(seeded.SessionId, "Running", seeded.StartedAt, null, seeded.StartedAt));
        var stopper = new RecordingSessionStopper();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock,
            new DeadSessionFirstSeenState(), directory: remote.Host.Directory))
        {
            await host.DueAsync();
            await host.SweepAsync();
        }

        await AssertHeldAsync(schema.ConnectionString, seeded, transport);
        runner.Certifies.ShouldBe(1, transport);
        remote.CertifyFrames.ShouldBe(1, transport);
        remote.Runtime.AbsenceEvidence!.Store.Read(seeded.SessionId).Record?.State
            .ShouldBe(RunnerAbsenceRecordState.ClosedUnused, transport);
        await Should.ThrowAsync<Exception>(() => routing.GetTranscriptAsync(seeded.SessionId, default));
        runner.Starts.ShouldBe(0, transport);
        runner.Kills.ShouldBe(0, transport);
        runner.Releases.ShouldBe(0, transport);
        runner.Inputs.ShouldBe(0, transport);
        stopper.Killed.ShouldBeEmpty(transport);
    }

    private static async Task RealClientLocalHoldAsync()
    {
        const string label = "local-http";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var evidence = await EvidenceHost.StartAsync(clock);
        var runner = new CountingRunner { Evidence = evidence.Client };
        var sink = new RefusingLaunchSink();
        var dispatched = await DispatchColdAsync(schema.ConnectionString, runner, sink, parent: true);
        sink.Calls.ShouldBe(1, label);
        runner.Prepares.ShouldBe(1, label);
        evidence.Prepares.ShouldBe(1, label);
        evidence.State(dispatched.SessionId).ShouldBe(RunnerAbsenceRecordState.Prepared, label);
        (await evidence.RawGetAsync($"/sessions/{dispatched.SessionId:D}/transcript")).ShouldBe(HttpStatusCode.NotFound, label);
        (await evidence.RawGetAsync($"/sessions/{dispatched.SessionId:D}")).ShouldBe(HttpStatusCode.NotFound, label);
        await Should.ThrowAsync<Exception>(() => evidence.Client.GetTranscriptAsync(dispatched.SessionId, default));
        await FailRunnerUnknownAsync(schema.ConnectionString, dispatched.SessionId);

        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, new DeadSessionFirstSeenState()))
        {
            await host.DueAsync();
            await host.SweepAsync();
        }

        await AssertHeldAsync(schema.ConnectionString, dispatched, label);
        runner.Certifies.ShouldBe(1, label);
        evidence.Certifies.ShouldBe(1, label);
        evidence.State(dispatched.SessionId).ShouldBe(RunnerAbsenceRecordState.ClosedUnused, label);
        (await evidence.RawGetAsync($"/sessions/{dispatched.SessionId:D}/transcript")).ShouldBe(HttpStatusCode.NotFound, label);
        sink.Calls.ShouldBe(1, label);
        Quiet(runner, stopper, label);
    }

    /// <summary>
    /// V-15. Independently known absent inventory; the evidence route answers the named shape.
    /// Decisive: Failed, CompletedAt set, zero Blocked events, bytes retained, Quiet.
    /// </summary>
    [Test]
    [Arguments("plain404")]
    [Arguments("old-runner")]
    [Arguments("evidence-unreachable")]
    [Arguments("timeout")]
    [Arguments("stale-nonce")]
    [Arguments("mismatched-generation")]
    [Arguments("incomplete")]
    [Arguments("empty-transcript")]
    public async Task C1153_Real_client_bad_evidence_keeps_failure(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true });
        EvidenceHost? real = null;
        SigningStub? stub = null;
        try
        {
            var runner = new CountingRunner();
            if (shape == "plain404")
            {
                // The real runner, never prepared for this id: 404 with no certificate.
                real = await EvidenceHost.StartAsync(clock);
                runner.Evidence = real.Client;
            }
            else
            {
                stub = new SigningStub(clock, shape);
                runner.Evidence = stub.Client;
            }

            runner.Sessions.ShouldBeEmpty("known absent local inventory");
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, new DeadSessionFirstSeenState()))
                await host.DueAsync();

            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Failed, shape);
            task.CompletedAt.ShouldNotBeNull(shape);
            task.FailureReason!.Contains(SessionReconciliationService.RunnerUnknownSessionReason, StringComparison.Ordinal)
                .ShouldBeTrue(shape);
            task.FailureReason.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse(shape);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0, shape);
            var brief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
            Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, shape);
            Encoding.UTF8.GetBytes(brief.RemoteSpillBody!).ShouldBe(seeded.Spill, shape);
            runner.Certifies.ShouldBe(1, shape);
            if (stub is not null)
            {
                stub.CapabilityReads.ShouldBeGreaterThan(0, shape);
                stub.Posts.ShouldBe(shape == "old-runner" ? 0 : 1, shape);
            }
            else
            {
                real!.Certifies.ShouldBe(1, shape);
                real.LastCertifyStatus.ShouldBe(404, shape);
            }

            Quiet(runner, stopper, shape);
        }
        finally
        {
            if (real is not null) await real.DisposeAsync();
            stub?.Dispose();
        }
    }

    /// <summary>
    /// V-16. The condition changes between the first evidence read and StageBlocked (fault
    /// interceptor on the FOR UPDATE). Decisive: no Blocked row or event committed; task
    /// unchanged; the existing safety withhold where applicable.
    /// </summary>
    [Test]
    [Arguments("changed-generation")]
    [Arguments("store-or-epoch-change")]
    [Arguments("expired-proof")]
    public async Task C1153_Final_certificate_is_revalidated_under_lock(string condition)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var evidence = await EvidenceHost.StartAsync(clock);
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true });
        (await evidence.Client.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, null, default))
            .Prepared.ShouldBeTrue(condition);
        var change = new LockedReadChange(schema.ConnectionString, seeded.SessionId, condition, clock);
        var runner = new CountingRunner { Evidence = evidence.Client };
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, new DeadSessionFirstSeenState(), change))
        {
            await host.SweepAsync();
            clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
            change.Armed = true;
            await host.SweepAsync();
        }

        change.Fired.ShouldBe(1, condition);
        runner.Certifies.ShouldBe(1, condition);
        evidence.Certifies.ShouldBe(1, condition);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, condition);
        task.CompletedAt.ShouldBeNull(condition);
        task.FailureReason.ShouldBeNull(condition);
        task.Attempt.ShouldBe(seeded.Attempt, condition);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId)).ShouldBe(0, condition);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId)).ShouldBe(0, condition);
        var brief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
        Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, condition);
        Quiet(runner, stopper, condition);
    }

    /// <summary>
    /// V-17. Decisive: zero prepare/certify requests on the host, task and session unchanged,
    /// no fail/hold/kill/start. unavailable-owning-inventory pairs an UnavailableDirectory with
    /// a misleading empty local list.
    /// </summary>
    [Test]
    [Arguments("working-task")]
    [Arguments("working-transcript")]
    [Arguments("unavailable-owning-inventory")]
    public async Task C1153_Working_or_unknown_inventory_withholds(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var evidence = await EvidenceHost.StartAsync(clock);
        var remote = shape == "unavailable-owning-inventory";
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            Parent = true,
            Status = shape == "working-task" ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched,
            Prompt = shape == "working-transcript",
            RunnerId = remote ? "remote-owner" : null,
        });
        // A prepared id: a certificate request, had one been made, would have succeeded.
        (await evidence.Client.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, null, default))
            .Prepared.ShouldBeTrue(shape);
        var runner = new CountingRunner { Evidence = evidence.Client };
        runner.Sessions.ShouldBeEmpty("misleading empty local list");
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock,
            new DeadSessionFirstSeenState(), unavailable: remote))
        {
            await host.DueAsync();
            await host.SweepAsync();
        }

        runner.Certifies.ShouldBe(0, shape);
        evidence.Certifies.ShouldBe(0, shape);
        evidence.State(seeded.SessionId).ShouldBe(RunnerAbsenceRecordState.Prepared, shape);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(shape == "working-task" ? AgentTaskStatus.Working : AgentTaskStatus.Dispatched, shape);
        task.CompletedAt.ShouldBeNull(shape);
        task.FailureReason.ShouldBeNull(shape);
        task.Attempt.ShouldBe(seeded.Attempt, shape);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId)).ShouldBe(0, shape);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId)).ShouldBe(0, shape);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed, shape);
        Quiet(runner, stopper, shape);
    }

    /// <summary>
    /// V-18. cold-fresh: one prepare for the new id after the claim commit (land boundary
    /// dispatch-warning-claim-committed) and before the sink's first Enqueue. warm-reuse,
    /// recovery, resume: zero prepare requests. prepare-faulted: the route throws/503, the launch
    /// still enqueues and the task stays Dispatched. remote-old-runner: capabilities without the
    /// token, zero PrepareAbsenceEvidence frames.
    /// </summary>
    [Test]
    [Arguments("cold-fresh")]
    [Arguments("warm-reuse")]
    [Arguments("recovery")]
    [Arguments("resume")]
    [Arguments("prepare-faulted")]
    [Arguments("remote-old-runner")]
    public async Task C1153_Only_new_cold_dispatch_prepares_evidence(string path)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        switch (path)
        {
            case "cold-fresh":
            {
                await using var evidence = await EvidenceHost.StartAsync(clock);
                var order = new List<string>();
                var sink = new OrderSink(order);
                var runner = new CountingRunner { Evidence = evidence.Client };
                runner.OnPrepare = async id =>
                {
                    // Committed: a fresh context already sees the claim and the Starting session.
                    await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                    var committed = await db.AgentTasks.AsNoTracking().AnyAsync(t => t.AgentSessionId == id
                        && t.Status == AgentTaskStatus.Dispatched)
                        && await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == id && s.Status == SessionStatus.Starting);
                    order.Add(committed ? "prepare-after-commit" : "prepare-before-commit");
                };
                var dispatched = await DispatchColdAsync(schema.ConnectionString, runner, sink, parent: false);
                order.ShouldBe(["prepare-after-commit", "enqueue"], path);
                sink.SessionIds.ShouldBe([dispatched.SessionId], path);
                runner.Prepares.ShouldBe(1, path);
                evidence.Prepares.ShouldBe(1, path);
                evidence.State(dispatched.SessionId).ShouldBe(RunnerAbsenceRecordState.Prepared, path);
                break;
            }
            case "prepare-faulted":
            {
                using var stub = new SigningStub(clock, "prepare-faulted");
                var order = new List<string>();
                var sink = new OrderSink(order);
                var runner = new CountingRunner { Evidence = stub.Client };
                runner.OnPrepare = _ => { order.Add("prepare"); return Task.CompletedTask; };
                var dispatched = await DispatchColdAsync(schema.ConnectionString, runner, sink, parent: false);
                order.ShouldBe(["prepare", "enqueue"], path);
                stub.Posts.ShouldBe(1, path);
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                (await db.AgentTasks.SingleAsync(t => t.Id == dispatched.TaskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, path);
                (await db.AgentSessions.SingleAsync(s => s.Id == dispatched.SessionId)).Status.ShouldBe(SessionStatus.Starting, path);
                (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == dispatched.TaskId && e.Type == AgentTaskEventType.Warning))
                    .ShouldBe(0, path);
                break;
            }
            case "warm-reuse":
                await WarmReuseSendsNoPrepareAsync(schema.ConnectionString, path);
                break;
            case "recovery":
                await BootWedgeRelaunchSendsNoPrepareAsync(schema.ConnectionString, path);
                break;
            case "resume":
                await InterruptedLaunchResumeSendsNoPrepareAsync(schema.ConnectionString, path);
                break;
            case "remote-old-runner":
            {
                await using var remote = await RemoteEvidence.StartAsync(schema.ConnectionString, oldRunner: true);
                var sessionId = Guid.NewGuid();
                var routing = new RoutingSessionRunnerClient(remote.Host.Directory);
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                {
                    var now = Pg(DateTime.UtcNow);
                    db.AgentSessions.Add(new AgentSession
                    {
                        Id = sessionId, DefinitionName = "old-runner", AgentKind = AgentKind.ClaudeCode,
                        Status = SessionStatus.Starting, Cwd = Path.GetTempPath(), CreatedAt = now, StartedAt = now,
                        LastSeenAt = now, RunnerId = remote.Host.AllowedRunnerId,
                        RunnerStoreId = remote.Runtime.RunnerStoreId, RunnerCwd = Path.GetTempPath(),
                    });
                    await db.SaveChangesAsync();
                }

                var prepared = await routing.PrepareAbsenceEvidenceAsync(sessionId, DateTime.UtcNow, remote.Runtime.RunnerStoreId, default);
                prepared.Prepared.ShouldBeFalse(path);
                prepared.Reason.ShouldBe("absence_evidence_capability_absent", path);
                remote.PrepareFrames.ShouldBe(0, path);
                remote.Runtime.AbsenceEvidence!.Store.Read(sessionId).Record.ShouldBeNull(path);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    /// <summary>
    /// V-19. Decisive: zero certify requests on the host, record still Prepared on disk, and the
    /// previous whitelist outcome (Failed) for attempted-delivery, extra-related-row and
    /// native-attempt.
    /// </summary>
    [Test]
    [Arguments("attempted-delivery")]
    [Arguments("extra-related-row")]
    [Arguments("native-attempt")]
    public async Task C1153_Nonpristine_brief_never_closes_identity(string condition)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projects = Directory.CreateTempSubdirectory("c1153-prescreen-").FullName;
        try
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var evidence = await EvidenceHost.StartAsync(clock);
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                Parent = false,
                DeliveryAttempts = condition == "attempted-delivery" ? 1 : 0,
            });
            if (condition == "extra-related-row")
            {
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(), AgentSessionId = seeded.SessionId, Body = "retained follow-up",
                    Origin = QueuedMessageOrigin.Delegation, Sequence = 2, CreatedAt = seeded.DispatchedAt.AddSeconds(1),
                });
                await db.SaveChangesAsync();
            }

            if (condition == "native-attempt")
                WriteNativeAttempt(seeded, projects, "user");
            (await evidence.Client.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, null, default))
                .Prepared.ShouldBeTrue(condition);
            var runner = new CountingRunner { Evidence = evidence.Client };
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock,
                new DeadSessionFirstSeenState(), projectsRoot: projects))
                await host.DueAsync();

            runner.Certifies.ShouldBe(0, condition);
            evidence.Certifies.ShouldBe(0, condition);
            evidence.State(seeded.SessionId).ShouldBe(RunnerAbsenceRecordState.Prepared, condition);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Failed, condition);
            task.CompletedAt.ShouldNotBeNull(condition);
            task.FailureReason!.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse(condition);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0, condition);
            var brief = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
            Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, condition);
            Quiet(runner, stopper, condition);
        }
        finally { Directory.Delete(projects, true); }
    }

    /// <summary>
    /// V-20. Certificate passes, BlockedSaveFault fails the hold save, a later unrelated save in
    /// the same context persists nothing of the staged task/event/note; the next due tick holds.
    /// </summary>
    [Test]
    public async Task C1153_Certificate_failure_does_not_leak_a_hold_on_later_save()
    {
        const string label = "V-20";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var evidence = await EvidenceHost.StartAsync(clock);
        var victimId = Guid.Parse("00000000-0000-0000-0000-000000001153");
        var survivorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffff1153");
        var victim = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true, TaskId = victimId });
        var survivor = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true, TaskId = survivorId });
        foreach (var seeded in new[] { victim, survivor })
            (await evidence.Client.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, null, default))
                .Prepared.ShouldBeTrue(label);
        var fault = new VictimHoldSaveFault { VictimId = victimId };
        var runner = new CountingRunner { Evidence = evidence.Client };
        var stopper = new RecordingSessionStopper();
        var seen = new DeadSessionFirstSeenState();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen, fault))
            await host.DueAsync();

        fault.Fired.ShouldBeTrue(label);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var untouched = await db.AgentTasks.SingleAsync(t => t.Id == victimId);
            untouched.Status.ShouldBe(AgentTaskStatus.Dispatched, label);
            untouched.FailureReason.ShouldBeNull(label);
            untouched.CompletedAt.ShouldBeNull(label);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == victimId)).ShouldBe(0, label);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == victimId)).ShouldBe(0, label);
            (await db.AgentTasks.SingleAsync(t => t.Id == survivorId)).Status.ShouldBe(AgentTaskStatus.Blocked, label);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == survivorId)).ShouldBe(1, label);
        }

        await using (var again = OpenSweep(schema.ConnectionString, runner, stopper, clock, seen))
            await again.SweepAsync();
        await AssertHeldAsync(schema.ConnectionString, victim, label + "-recovered");
        evidence.State(victim.SessionId)
            .ShouldBe(RunnerAbsenceRecordState.ClosedUnused, label);
        Quiet(runner, stopper, label);
    }

    /// <summary>
    /// V-24. FullCommandCounter over the due hold with the real HTTP client. Code measures and
    /// pins the exact totals; the pinned value must be at most 40 without a parent note and at
    /// most 48 with one, and the roster is printed for Review.
    /// </summary>
    [Test]
    [Arguments("no-parent", 40)]
    [Arguments("parent", 48)]
    public async Task C1153_Due_hold_statement_ceiling(string shape, int ceiling)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var evidence = await EvidenceHost.StartAsync(clock);
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = shape == "parent" });
        (await evidence.Client.PrepareAbsenceEvidenceAsync(seeded.SessionId, seeded.StartedAt, null, default))
            .Prepared.ShouldBeTrue(shape);
        var counter = new FullCommandCounter();
        var runner = new CountingRunner { Evidence = evidence.Client };
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(schema.ConnectionString, runner, stopper, clock, new DeadSessionFirstSeenState(), counter))
        {
            await host.SweepAsync();
            clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1));
            counter.Reset();
            await host.SweepAsync();
        }

        var total = counter.Total;
        var roster = counter.Roster();
        Console.WriteLine($"C1153-BUDGET {shape} total={total}");
        Console.WriteLine(roster);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Blocked, roster);
        evidence.Certifies.ShouldBe(1, shape);
        total.ShouldBeLessThanOrEqualTo(ceiling, roster);
        total.ShouldBe(shape == "parent" ? DueHoldStatementsWithParent : DueHoldStatementsNoParent, roster);
    }

    // V-24 pins, measured by this method at the S4 commit (C1153-BUDGET lines in the TRX).
    private const int DueHoldStatementsNoParent = 0;
    private const int DueHoldStatementsWithParent = 0;

    private static async Task AssertHeldAsync(string connection, SeededAbsent seeded, string label)
    {
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var held = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, label);
        held.CompletedAt.ShouldBeNull(label);
        held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, label);
        held.Attempt.ShouldBe(seeded.Attempt, label);
        held.AgentSessionId.ShouldBe(seeded.SessionId, label);
        held.DispatchedAt.ShouldBe(seeded.DispatchedAt, label);
        held.Goal.ShouldBe(seeded.Goal, label);
        var blocked = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked).ToListAsync();
        blocked.Count.ShouldBe(1, label);
        blocked[0].Detail.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, label);
        var brief = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
        brief.Status.ShouldBe(QueuedMessageStatus.Pending, label);
        Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, label);
        (brief.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(brief.RemoteSpillBody)).ShouldBe(seeded.Spill, label);
        if (seeded.ParentId is Guid parentId)
        {
            var note = (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == parentId).ToListAsync())
                .ShouldHaveSingleItem(label);
            note.SourceTaskId.ShouldBe(seeded.TaskId, label);
            note.ExecutionTaskId.ShouldBeNull(label);
            note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(AgentTaskDispatcher.DispatchLaunchAbsentReason), label);
        }
    }

    /// <summary>
    /// The V-1 cold dispatch: a Shared-workspace task claimed by the real dispatcher through the
    /// harness, with this runner as the dispatcher's runner client and <paramref name="sink"/> as
    /// the launch sink. Returns the persisted identity, byte images and the spill.
    /// </summary>
    private static async Task<SeededAbsent> DispatchColdAsync(
        string connection, CountingRunner runner, IAgentTaskLaunchSink sink, bool parent,
        Func<BridgeQueueHarness, SeededAbsent, Task>? afterDispatch = null)
    {
        var goal = "Keep this goal exact.\nLine two café ☃.\n" + new string('x', 1200);
        var settings = new DelegationSettings { MaxConcurrentTasks = 16 };
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            Delegation = settings,
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter));
                services.AddSingleton(sink);
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "claude",
                        Definitions =
                        {
                            ["claude"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                            },
                        },
                    }));
            },
        });
        settings.AllowedRoots = [harness.TempRoot];
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "worker")).FullName;
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        Guid? parentId = parent ? Guid.NewGuid() : null;
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            var boardId = await db.Agents.Where(a => a.Id == harness.AgentId).Select(a => a.BoardId).SingleAsync();
            db.Agents.Add(new Agent
            {
                Id = agentId, BoardId = boardId, Name = "absent worker",
                Slug = "absent-" + agentId.ToString("N")[..12], Kind = AgentKind.ClaudeCode,
                WorkingDirectory = directory, AlwaysOn = false,
            });
            if (parentId is Guid p)
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = p, DefinitionName = "parent", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Stopped,
                    Cwd = directory, CreatedAt = now, StartedAt = now, LastSeenAt = now, EndedAt = now,
                });
            }

            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Retain the refused launch", Goal = goal,
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier, Role = AgentTaskRole.Custom,
                Workspace = WorkspaceMode.Shared,
                ReplyTo = parentId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session, ParentSessionId = parentId,
                WorkingDirectory = directory, AgentId = agentId, Status = AgentTaskStatus.Queued,
                CreatedAt = now.AddMinutes(-2), ExecutionDeadlineAt = now.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        var sessionId = task.AgentSessionId.ShouldNotBeNull();
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Starting);
        var brief = (await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId).ToListAsync())
            .ShouldHaveSingleItem();
        var cold = new SeededAbsent(taskId, sessionId, parentId, brief.Id, Encoding.UTF8.GetBytes(brief.Body),
            brief.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(brief.RemoteSpillBody),
            task.DispatchedAt!.Value, session.StartedAt, task.Attempt, goal, directory);
        if (afterDispatch is not null)
            await afterDispatch(harness, cold);
        return cold;
    }

    /// <summary>The V-1 reconciliation step: the runner-unknown failure after the 90 s grace.</summary>
    private static async Task FailRunnerUnknownAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        var recon = SessionReconciliationServiceTests.BuildService(
            db,
            new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [] },
            new MockEventBus(),
            time: new FakeTimeProvider(new DateTimeOffset(DateTime.SpecifyKind(session.StartedAt, DateTimeKind.Utc)).AddSeconds(91)));
        await recon.ScanAsync(CancellationToken.None);
        var closed = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        closed.Status.ShouldBe(SessionStatus.Failed);
        closed.FailureReason.ShouldBe(SessionReconciliationService.RunnerUnknownSessionReason);
    }

    /// <summary>V-18 warm-reuse: an idle warm pool delegate takes the queued task; no prepare.</summary>
    private static async Task WarmReuseSendsNoPrepareAsync(string connection, string label)
    {
        var runner = new CountingRunner();
        var sink = new OrderSink([]);
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            Delegation = new DelegationSettings { MaxConcurrentTasks = 16 },
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(new FakeAgentProtocolAdapter { ReadyResult = true }));
                services.AddSingleton<IAgentTaskLaunchSink>(sink);
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "warm")).FullName;
        var warmSession = Guid.NewGuid();
        var warmAgent = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = warmSession, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                Cwd = directory, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = warmAgent, Name = $"task-{warmAgent:N}"[..13], Slug = $"task-{warmAgent:N}"[..13],
                WorkingDirectory = directory, Details = "Warm pool delegate.", Status = AgentStatus.Idle,
                Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, IsPoolDelegate = true,
                PoolIdleSince = now.AddMinutes(-3), PersistentSessionId = warmSession.ToString("D"), LaunchEnvJson = "{}",
                CreatedAt = now, UpdatedAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Warm reuse", Goal = "Reuse the warm delegate.",
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, Role = AgentTaskRole.Custom,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = directory, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now.AddMinutes(-1), ExecutionDeadlineAt = now.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claimed = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            (await dispatcher.TryReuseWarmAgentAsync(claimed, DateTime.UtcNow, CancellationToken.None))
                .ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused, label);
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        (await verify.AgentTasks.SingleAsync(t => t.Id == taskId)).AgentSessionId.ShouldBe(warmSession, label);
        runner.Prepares.ShouldBe(0, label);
        sink.SessionIds.ShouldBeEmpty(label);
    }

    /// <summary>
    /// V-18 recovery: after the cold dispatch prepared its id, the boot-wedge relaunch allocates a
    /// fresh session through the same dispatcher and sink; the prepare count does not move.
    /// </summary>
    private static async Task BootWedgeRelaunchSendsNoPrepareAsync(string connection, string label)
    {
        var runner = new CountingRunner();
        var sink = new OrderSink([]);
        var dispatched = await DispatchColdAsync(connection, runner, sink, parent: false, afterDispatch: async (harness, cold) =>
        {
            runner.Prepares.ShouldBe(1, label + ": the cold dispatch itself prepared");
            await using var scope = harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .RelaunchWedgedAsync(cold.TaskId, cold.SessionId, CancellationToken.None);
        });

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == dispatched.TaskId);
        task.AgentSessionId.ShouldNotBe(dispatched.SessionId, label + ": the recovery allocated a fresh session");
        sink.SessionIds.ShouldBe([dispatched.SessionId, task.AgentSessionId!.Value], label);
        runner.Prepares.ShouldBe(1, label);
    }

    /// <summary>V-18 resume: interrupted-launch resume of an existing session id; no prepare.</summary>
    private static async Task InterruptedLaunchResumeSendsNoPrepareAsync(string connection, string label)
    {
        var runner = new CountingRunner();
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter));
                services.AddSingleton<ISessionRunnerClient>(runner);
            },
        });
        var agentId = harness.AgentId;
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "resume")).FullName;
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Starting, Cwd = directory, Cols = 120, Rows = 30,
                CreatedAt = now.AddMinutes(-2), StartedAt = now.AddMinutes(-2), LastSeenAt = now.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AgentStatus.Running)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Interrupted launch resume",
                Goal = "Resume the interrupted launch.", Role = AgentTaskRole.Plan, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Shared, WorkingDirectory = directory,
                AgentSessionId = sessionId, AgentId = agentId, Status = AgentTaskStatus.Dispatched,
                CreatedAt = now.AddMinutes(-2), DispatchedAt = now.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
        }

        adapter.RegisterOnStart = harness.Runtime;
        await using (var scope = harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(sessionId, agentId, CancellationToken.None);

        adapter.Started.ShouldBeTrue(label + ": the resume relaunched the session");
        adapter.StartedSessionId.ShouldBe(sessionId, label + ": the existing id, not a fresh one");
        runner.Prepares.ShouldBe(0, label);
        runner.Certifies.ShouldBe(0, label);
    }

    /// <summary>Records the launch sink's Enqueue order and session ids; it accepts.</summary>
    private sealed class OrderSink(List<string> order) : IAgentTaskLaunchSink
    {
        public List<Guid> SessionIds { get; } = [];

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            order.Add("enqueue");
            SessionIds.Add(sessionId);
        }
    }

    /// <summary>V-20: throws once when the victim's Blocked hold is saved; no other change.</summary>
    private sealed class VictimHoldSaveFault : SaveChangesInterceptor
    {
        public Guid VictimId { get; init; }
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Id == VictimId
                    && e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Blocked))
            {
                Fired = true;
                throw new IOException("injected certified-hold save fault");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// V-16: when armed, the hold's task-row FOR UPDATE changes one condition the first-read
    /// certificate was bound to: the session generation, its bound store, or the clock past the
    /// certificate's five-second life.
    /// </summary>
    private sealed class LockedReadChange(string connection, Guid sessionId, string condition, FakeTimeProvider clock)
        : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Fired == 0 && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("AgentTasks", StringComparison.Ordinal))
            {
                Fired++;
                await using var side = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
                switch (condition)
                {
                    case "changed-generation":
                        await side.AgentSessions.Where(s => s.Id == sessionId)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, x => x.StartedAt.AddSeconds(1)), cancellationToken);
                        break;
                    case "store-or-epoch-change":
                        var rebound = Guid.NewGuid();
                        await side.AgentSessions.Where(s => s.Id == sessionId)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunnerStoreId, rebound), cancellationToken);
                        break;
                    case "expired-proof":
                        clock.Advance(RunnerAbsenceEvidenceValidator.Lifetime + TimeSpan.FromSeconds(1));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
                }
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// A real runner runtime over a temp root (adoption complete, past the epoch's replay window)
    /// behind a random-port Kestrel that maps the production capabilities, session GET,
    /// transcript and absence-evidence routes with a synthetic key file. <see cref="Client"/> is
    /// the production HTTP client with the same key path and the shared clock.
    /// </summary>
    private sealed class EvidenceHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _root;
        private readonly HttpClient _raw;
        private int _prepares;
        private int _certifies;

        public SessionRunnerRuntime Runtime { get; }
        public SessionRunnerHttpClient Client { get; }
        public int Prepares => Volatile.Read(ref _prepares);
        public int Certifies => Volatile.Read(ref _certifies);
        public int LastCertifyStatus { get; private set; }

        private EvidenceHost(WebApplication app, string root, HttpClient raw, SessionRunnerRuntime runtime, SessionRunnerHttpClient client)
        {
            _app = app;
            _root = root;
            _raw = raw;
            Runtime = runtime;
            Client = client;
        }

        public static async Task<EvidenceHost> StartAsync(FakeTimeProvider clock)
        {
            var root = Path.Combine(Path.GetTempPath(), "c1153-s4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var keyPath = Path.Combine(root, "absence.key");
            await File.WriteAllTextAsync(keyPath, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            var settings = new RunnerSettings { SessionLogPath = Path.Combine(root, "runner") };
            settings.AbsenceEvidence.KeyPath = keyPath;
            var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
                timeProvider: clock);
            await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            clock.Advance(RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(runtime);
            builder.Services.AddSingleton(Options.Create(settings));
            builder.Services.AddSingleton(new AbsenceEvidenceKeyProvider(
                Options.Create(settings), NullLogger<AbsenceEvidenceKeyProvider>.Instance));
            builder.Services.Configure<HerdrSettings>(_ => { });
            builder.Services.Configure<Antiphon.SessionRunner.HostStatsSettings>(_ => { });
            var app = builder.Build();
            EvidenceHost? self = null;
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                var certify = context.Request.Method == "POST" && path.EndsWith("/absence-evidence", StringComparison.Ordinal);
                if (context.Request.Method == "POST" && path.EndsWith("/absence-evidence/prepare", StringComparison.Ordinal))
                    Interlocked.Increment(ref self!._prepares);
                if (certify)
                    Interlocked.Increment(ref self!._certifies);
                await next(context);
                if (certify)
                    self!.LastCertifyStatus = context.Response.StatusCode;
            });
            app.UseRunnerExceptionMapping();
            app.MapRunnerCapabilitiesRoute(new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
            app.MapSessionGetRoute();
            app.MapSessionTranscriptRoute();
            app.MapAbsenceEvidenceRoutes();
            await app.StartAsync();
            var uri = new Uri(app.Urls.Single());
            uri.IsLoopback.ShouldBeTrue();
            uri.Port.ShouldNotBe(17204);
            var serverSettings = new SessionRunnerSettings { BaseUrl = uri.ToString() };
            serverSettings.AbsenceEvidence.KeyPath = keyPath;
            var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) };
            var client = new SessionRunnerHttpClient(http, new FixedFactory(uri), Options.Create(serverSettings), time: clock);
            self = new EvidenceHost(app, root, new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) }, runtime, client);
            return self;
        }

        public RunnerAbsenceRecordState? State(Guid id) => Runtime.AbsenceEvidence!.Store.Read(id).Record?.State;

        public async Task<HttpStatusCode> RawGetAsync(string path)
        {
            using var response = await _raw.GetAsync(path);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            _raw.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await Runtime.DisposeAsync();
            try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }

    private sealed class FixedFactory(Uri uri) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// The production HTTP client over a handler that signs every answer with the client's own
    /// key, so each V-15 shape is authenticated and is refused only for the shape itself.
    /// </summary>
    private sealed class SigningStub : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1153-stub-" + Guid.NewGuid().ToString("N"));
        private readonly AbsenceEvidenceKey _key;
        private readonly FakeTimeProvider _clock;
        private readonly string _shape;
        private readonly Guid _store = Guid.NewGuid();

        public SessionRunnerHttpClient Client { get; }
        public int CapabilityReads { get; private set; }
        public int Posts { get; private set; }

        public SigningStub(FakeTimeProvider clock, string shape)
        {
            _clock = clock;
            _shape = shape;
            Directory.CreateDirectory(_root);
            var material = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var keyPath = Path.Combine(_root, "absence.key");
            File.WriteAllText(keyPath, Convert.ToBase64String(material));
            _key = AbsenceEvidenceKey.FromMaterial(material);
            var settings = new SessionRunnerSettings { BaseUrl = "http://runner.test" };
            settings.AbsenceEvidence.KeyPath = keyPath;
            Client = new SessionRunnerHttpClient(new HttpClient(new Handler(this)) { BaseAddress = new Uri("http://runner.test/") },
                new FixedFactory(new Uri("http://runner.test/")), Options.Create(settings), time: clock);
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
            {
                CapabilityReads++;
                IReadOnlyList<string> features = _shape == "old-runner" ? [] : [RunnerAbsenceEvidence.Feature];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(
                        new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false, Features: features, RunnerStoreId: _store),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
                };
            }

            Posts++;
            if (_shape == "evidence-unreachable")
                throw new HttpRequestException("evidence endpoint unreachable");
            var body = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var posted = AbsenceEvidenceRoutes.ParseRequest(body).ShouldNotBeNull();
            var prepare = request.RequestUri.AbsolutePath.EndsWith("/prepare", StringComparison.Ordinal);
            if (_shape == "timeout")
                _clock.Advance(TimeSpan.FromSeconds(6));
            var status = _shape == "prepare-faulted" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            string text;
            if (_shape == "prepare-faulted")
                text = "{\"type\":\"absence_evidence_unavailable\",\"status\":503}";
            else if (_shape == "empty-transcript")
                text = JsonSerializer.Serialize(new RunnerTranscriptDto(posted.SessionId, [], 0),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            else
            {
                var shape = AbsenceCertificateShape.Pristine(posted.SessionId, posted.AcceptedStartedAt, posted.RunnerStoreId,
                    Guid.NewGuid(), posted.RequestNonce, _clock.GetUtcNow().UtcDateTime);
                if (_shape == "stale-nonce")
                    shape["requestNonce"] = RunnerAbsenceEvidence.NewNonce();
                if (_shape == "mismatched-generation")
                    shape = AbsenceCertificateShape.Flip(shape, "generation-one-microsecond");
                if (_shape == "incomplete")
                    shape["complete"] = false;
                text = shape.ToJsonString();
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            response.Headers.Add(AbsenceEvidenceAuthentication.KeyIdHeader, _key.KeyId);
            response.Headers.Add(AbsenceEvidenceAuthentication.MacHeader, AbsenceEvidenceAuthentication.Sign(_key,
                AbsenceEvidenceAuthentication.ResponseCanonical(
                    prepare ? AbsenceEvidenceAuthentication.PrepareOperation : AbsenceEvidenceAuthentication.CertifyOperation,
                    posted.RequestNonce, (int)status, bytes)));
            return response;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }

        private sealed class Handler(SigningStub owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = owner.Respond(request);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(response);
            }
        }
    }

    /// <summary>
    /// The remote owner: a real runtime behind <c>PhoneHomeRuntimeAdapter</c> and a real
    /// <c>PhoneHomeCommandDispatcher</c>, answering the server's authenticated phone-home
    /// connection as the scripted peer. The runtime's clock follows the system clock (the
    /// phone-home client's), started one replay window in the past so requests are admissible.
    /// <paramref name="oldRunner"/> registers capabilities without the absence feature.
    /// </summary>
    private sealed class RemoteEvidence : IAsyncDisposable
    {
        private readonly string _root;
        private int _certifyFrames;
        private int _prepareFrames;

        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public SessionRunnerRuntime Runtime { get; private set; } = null!;
        public int CertifyFrames => Volatile.Read(ref _certifyFrames);
        public int PrepareFrames => Volatile.Read(ref _prepareFrames);

        private RemoteEvidence(string root) => _root = root;

        public static async Task<RemoteEvidence> StartAsync(string connection, bool oldRunner = false)
        {
            var remote = new RemoteEvidence(Path.Combine(Path.GetTempPath(), "c1153-remote-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(remote._root);
            var clock = new ShiftedSystemClock { Offset = -(RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1)) };
            var settings = new RunnerSettings { SessionLogPath = Path.Combine(remote._root, "runner") };
            remote.Runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance,
                timeProvider: clock);
            await remote.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            clock.Offset = TimeSpan.Zero;
            var adapter = new PhoneHomeRuntimeAdapter(remote.Runtime, new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
            var dispatcher = new PhoneHomeCommandDispatcher(adapter, new PhoneHomeSettings
            {
                Enabled = true, AllowedCwd = remote._root, Capacity = 2,
                CapacityStatePath = Path.Combine(remote._root, "capacity.json"),
                LaunchGenerationsPath = Path.Combine(remote._root, "generations"),
            });
            remote.Host = await PhoneHomeTestHost.StartAsync(connectionString: connection);
            var capabilities = adapter.Capabilities();
            if (oldRunner)
                capabilities = capabilities with
                {
                    Features = capabilities.Features!.Where(f => f != RunnerAbsenceEvidence.Feature).ToList(),
                };
            remote.Peer = await remote.Host.ConnectPeerAsync(storeId: remote.Runtime.RunnerStoreId, capabilities: capabilities);
            remote.Peer.Reply = request =>
            {
                if (request.Operation == PhoneHomeOperation.CertifyAbsence)
                    Interlocked.Increment(ref remote._certifyFrames);
                if (request.Operation == PhoneHomeOperation.PrepareAbsenceEvidence)
                    Interlocked.Increment(ref remote._prepareFrames);
                return dispatcher.DispatchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            };
            remote.Host.Directory.MarkRecovered(await remote.Host.WaitLiveAsync());
            return remote;
        }

        public async ValueTask DisposeAsync()
        {
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            await Runtime.DisposeAsync();
            try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>The system clock moved by <see cref="Offset"/>; wall and monotonic move together.</summary>
    private sealed class ShiftedSystemClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + Offset;

        public override long GetTimestamp() =>
            TimeProvider.System.GetTimestamp()
            + (long)(Offset.Ticks * (double)TimeProvider.System.TimestampFrequency / TimeSpan.TicksPerSecond);

        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
    }
}
