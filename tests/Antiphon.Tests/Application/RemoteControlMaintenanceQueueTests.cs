using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("RemoteControlRecovery")]
[Category("Slow")]
public class RemoteControlMaintenanceQueueTests
{
    [Test]
    public async Task C514_Executor_rechecks_kind()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        await h.SetKindAsync(AgentKind.Grok);
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.WithheldCapability);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await h.SetKindAsync(AgentKind.ClaudeCode);
        var allowed = await h.ReserveAndExecuteAsync();
        allowed.ShouldBe(RemoteControlArmResult.ArmedObserved);
        h.Adapter.ConditionalInputs.ShouldContain("/remote-control");
    }

    [Test]
    public async Task C514_Bridge_arms_between_enqueue_and_delivery()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var id = await h.ReserveAsync();
        id.ShouldNotBeNull();
        h.Probe.Armed = true;
        h.Probe.Connections = 0;
        var result = await h.ExecuteAsync(id!.Value);
        result.ShouldBe(RemoteControlArmResult.SuppressedAlreadyArmed);
        h.Adapter.ConditionalInputs.Count(i => i.Contains("/remote-control", StringComparison.Ordinal))
            .ShouldBe(0);
    }

    [Test]
    public async Task C514_Unknown_never_authorizes_automatic_arm()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Probe.Throw = true;
        (await h.ReserveAndExecuteAsync()).ShouldBe(RemoteControlArmResult.WithheldUnknown);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();

        h.Probe.Throw = false;
        h.Probe.StateFileFound = false;
        (await h.ReserveAndExecuteAsync()).ShouldBe(RemoteControlArmResult.WithheldUnknown);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();

        h.Probe.StateFileFound = true;
        h.Runner.Pid = null;
        (await h.ReserveAndExecuteAsync()).ShouldBe(RemoteControlArmResult.WithheldUnknown);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();

        await using var noCap = await RemoteControlRecoveryHarness.CreateAsync(advertiseConditional: false);
        (await noCap.ReserveAndExecuteAsync()).ShouldBe(RemoteControlArmResult.WithheldTransport);
        noCap.Adapter.ConditionalInputs.ShouldBeEmpty();
        noCap.Runner.RawInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Current_menu_independently_suppresses_arm()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Probe.Armed = false;
        h.Probe.StateFileFound = true;
        h.Adapter.RemoteControlMenuOpen = true;
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.WithheldMenuPresent);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Stale_request_is_superseded_before_transport()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var id = await h.ReserveAsync();
        id.ShouldNotBeNull();
        await h.ReplaceGenerationAsync();
        var result = await h.ExecuteAsync(id!.Value);
        result.ShouldBe(RemoteControlArmResult.SupersededGeneration);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        h.Runner.ConditionalCalls.ShouldBeEmpty();
        await using var db = h.CreateDb();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.MaintenanceResult.ShouldBe(RemoteControlArmResult.SupersededGeneration);
    }

    [Test]
    public async Task C514_Busy_at_delivery_holds_until_fresh_TurnEnd()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        await h.MarkWorkingAsync();
        var id = await h.ReserveAsync();
        var held = await h.ExecuteAsync(id!.Value);
        held.ShouldBe(RemoteControlArmResult.WithheldNotIdle);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.MaintenanceSlotActive.ShouldBeTrue();
        }

        await h.MarkIdleAsync();
        var after = await h.ExecuteAsync(id.Value);
        after.ShouldBe(RemoteControlArmResult.ArmedObserved);
        h.Adapter.ConditionalInputs.ShouldContain("/remote-control");
    }

    [Test]
    public async Task C514_Running_launch_owner_excludes_health_delivery()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        h.LaunchQueue.TryRegister(h.SessionId).ShouldBeTrue();
        try
        {
            var result = await h.ReserveAndExecuteAsync(QueuedMessageOrigin.Supervision);
            result.ShouldBe(RemoteControlArmResult.WithheldLaunchOwner);
            h.Adapter.ConditionalInputs.ShouldBeEmpty();
        }
        finally
        {
            h.LaunchQueue.Unregister(h.SessionId);
        }

        var allowed = await h.ReserveAndExecuteAsync(QueuedMessageOrigin.Supervision);
        allowed.ShouldBe(RemoteControlArmResult.ArmedObserved);
    }

    [Test]
    public async Task C514_Stopping_without_launch_owner_excludes_health_delivery()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.SetStatusAsync(SessionStatus.Stopping);
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.WithheldStopping);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Producer_identity_survives_reload()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var boot = await h.ReserveAsync(QueuedMessageOrigin.System);
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == boot);
            row.Origin.ShouldBe(QueuedMessageOrigin.System);
            row.MaintenanceKind.ShouldBe(RemoteControlMaintenanceKind.AutomaticArm);
            row.MaintenanceAcceptedStartedAt.ShouldBe(h.Generation);
            row.Body.ShouldBe("/remote-control");
            row.MaintenanceSlotActive = false;
            await db.SaveChangesAsync();
        }

        var health = await h.ReserveAsync(QueuedMessageOrigin.Supervision);
        await using var verify = h.CreateDb();
        var healthRow = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == health);
        healthRow.Origin.ShouldBe(QueuedMessageOrigin.Supervision);
        healthRow.MaintenanceKind.ShouldBe(RemoteControlMaintenanceKind.AutomaticArm);
        healthRow.MaintenanceAcceptedStartedAt.ShouldBe(h.Generation);
    }

    [Test]
    public async Task C514_Arm_intent_commit_precedes_first_byte()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Recovery.BeforePersist = name => Task.FromResult(name == "submission-started");
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.Requested);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Terminal_ambiguous_arm_veto_survives_restart()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.ConditionalOutcomeOverride = ConditionalInputOutcomes.Unknown;
        var first = await h.ReserveAndExecuteAsync();
        first.ShouldBe(RemoteControlArmResult.ArmUnconfirmed);
        h.Adapter.ConditionalInputs.Count.ShouldBe(1);

        h.Adapter.ConditionalOutcomeOverride = null;
        h.Adapter.ConditionalInputs.Clear();
        var second = await h.ReserveAsync();
        second.ShouldBeNull();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_No_composer_evidence_withholds_arm_Enter()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.EchoTypedInputToScreen = false;
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.ArmUnconfirmed);
        h.Adapter.ConditionalInputs.ShouldBe(["/remote-control"]);
        h.Adapter.ConditionalInputs.ShouldNotContain("\r");
    }

    [Test]
    public async Task C514_Arm_without_UserPrompt_submits_one_Enter()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        var result = await h.ReserveAndExecuteAsync();
        result.ShouldBe(RemoteControlArmResult.ArmedObserved);
        h.Adapter.ConditionalInputs.ShouldBe(["/remote-control", "\r"]);
        await using var db = h.CreateDb();
        (await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
    }

    [Test]
    public async Task C514_SendNow_and_amend_preserve_maintenance_policy()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var id = await h.ReserveAsync();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => h.Queue.SendNowAsync(h.SessionId, id!.Value, CancellationToken.None));
        (await h.Queue.AmendPendingBodyAsync(
            h.SessionId, id!.Value, "prefix", 4000, CancellationToken.None)).ShouldBeFalse();
        await using var db = h.CreateDb();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.MaintenanceKind.ShouldBe(RemoteControlMaintenanceKind.AutomaticArm);
        row.Origin.ShouldBe(QueuedMessageOrigin.Supervision);
        row.Body.ShouldBe("/remote-control");
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Legacy_attempted_rows_are_never_retyped()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await using (var db = h.CreateDb())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = "/remote-control",
                Status = QueuedMessageStatus.Pending,
                Sequence = 9,
                Origin = QueuedMessageOrigin.Ui,
                CreatedAt = DateTime.UtcNow,
                DeliveryAttempts = 1,
                LastDeliveryBaselineSequence = 1,
                MaintenanceKind = RemoteControlMaintenanceKind.LegacyUnclassified,
            });
            await db.SaveChangesAsync();
        }

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Housekeeping_preserves_operator_and_attempted_rows()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        Guid operatorId;
        Guid attemptedId;
        await using (var db = h.CreateDb())
        {
            operatorId = Guid.NewGuid();
            attemptedId = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = operatorId,
                AgentSessionId = h.SessionId,
                Body = "/remote-control",
                Status = QueuedMessageStatus.Pending,
                Sequence = 2,
                Origin = QueuedMessageOrigin.Ui,
                CreatedAt = DateTime.UtcNow,
            });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = attemptedId,
                AgentSessionId = h.SessionId,
                Body = "/remote-control",
                Status = QueuedMessageStatus.Pending,
                Sequence = 3,
                Origin = QueuedMessageOrigin.Supervision,
                CreatedAt = DateTime.UtcNow,
                DeliveryAttempts = 1,
                LastDeliveryBaselineSequence = 4,
            });
            await db.SaveChangesAsync();
        }

        await h.Queue.CancelPendingRemoteControlAsync(h.SessionId, CancellationToken.None);
        await using var verify = h.CreateDb();
        var op = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == operatorId);
        op.Status.ShouldBe(QueuedMessageStatus.Pending);
        var attempted = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == attemptedId);
        attempted.Status.ShouldBe(QueuedMessageStatus.Pending);
        attempted.DeliveryAttempts.ShouldBe(1);
        attempted.LastDeliveryBaselineSequence.ShouldBe(4);
    }

    [Test]
    public async Task C514_Boot_and_health_keep_their_explicit_origins()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var boot = await h.ReserveAsync(QueuedMessageOrigin.System);
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == boot);
            row.Origin.ShouldBe(QueuedMessageOrigin.System);
            row.MaintenanceSlotActive = false;
            await db.SaveChangesAsync();
        }

        var health = await h.ReserveAsync(QueuedMessageOrigin.Supervision);
        await using var verify = h.CreateDb();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == health))
            .Origin.ShouldBe(QueuedMessageOrigin.Supervision);
    }

    [Test]
    public async Task C514_Arm_receipt_requires_observed_execution()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.MarkWorkingAsync();
        var id = await h.ReserveAsync();
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.MaintenanceResult.ShouldBe(RemoteControlArmResult.Requested);
            row.MaintenanceResult.ShouldNotBe(RemoteControlArmResult.ArmedObserved);
        }

        (await dbIncidents(h)).ShouldNotContain(AgentIncidentKind.RcReArmed);

        h.Probe.Armed = true;
        var suppressed = await h.ExecuteAsync(id!.Value);
        suppressed.ShouldBe(RemoteControlArmResult.SuppressedAlreadyArmed);
        (await dbIncidents(h)).ShouldNotContain(AgentIncidentKind.RcReArmed);
    }

    [Test]
    public async Task C514_Postflight_window_begins_at_execution()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        var id = await h.ReserveAsync();
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        await h.ExecuteAsync(id!.Value);
        await using var verify = h.CreateDb();
        var executed = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        executed.SubmissionStartedAt.ShouldNotBeNull();
        executed.SubmissionStartedAt!.Value.ShouldBeGreaterThan(before.AddSeconds(-2));
        executed.CreatedAt.ShouldBeLessThan(executed.SubmissionStartedAt.Value.AddMinutes(-1));
    }

    [Test]
    public async Task C514_Maintenance_faults_reconcile_without_retyping()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.ConditionalOutcomeOverride = ConditionalInputOutcomes.Unknown;
        await h.ReserveAndExecuteAsync();
        var bodies = h.Adapter.ConditionalInputs.Count(i => i == "/remote-control");
        bodies.ShouldBe(1);
        h.Adapter.ConditionalOutcomeOverride = null;
        h.Adapter.ConditionalInputs.Clear();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    private static async Task<List<AgentIncidentKind>> dbIncidents(RemoteControlRecoveryHarness h)
    {
        await using var db = h.CreateDb();
        return await db.AgentIncidents
            .Where(i => i.SessionId == h.SessionId)
            .Select(i => i.Kind)
            .ToListAsync();
    }

    /// <summary>
    /// CARD-0514 R-13: a recording HTTP handler FORWARDS the conditional operation to the child and
    /// then loses its reply. The bytes landed; the server cannot know that, so the outcome is
    /// Unknown at the transport and ArmUnconfirmed on the request, and it stays there. Neither the
    /// missing acknowledgement, nor a snapshot read failure, nor a probe that later reports armed
    /// authorizes a repeat or a verification in that generation — only a new generation reopens the
    /// slot.
    /// </summary>
    [Test]
    public async Task C514_Lost_write_reply_is_unknown_until_observed()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var child = new List<string>();
        var handler = new RecordingLostReplyHandler(request =>
        {
            // The child really takes the bytes; only the reply is lost on the way back.
            child.Add(request.Input);
            return new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Written, generation, request.ExpectedLastSequence + 1);
        });
        var transport = new SessionRunnerHttpClient(
            new HttpClient(handler),
            new SingleClientFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var write = await transport.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, 7, "/remote-control"),
            CancellationToken.None);

        write.Outcome.ShouldBe(ConditionalInputOutcomes.Unknown, string.Join(" | ", handler.Faults));
        child.ShouldBe(["/remote-control"], "a lost reply does not un-write the bytes");
        handler.Forwarded.Count.ShouldBe(1, "a lost reply is not a retry signal");

        // What that Unknown means for the request, on the production recovery graph.
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.ConditionalOutcomeOverride = ConditionalInputOutcomes.Unknown;
        (await h.ReserveAndExecuteAsync()).ShouldBe(RemoteControlArmResult.ArmUnconfirmed);
        h.Adapter.ConditionalInputs.Count(i => i == "/remote-control").ShouldBe(1);

        Guid rowId;
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            row.MaintenanceResult.ShouldBe(RemoteControlArmResult.ArmUnconfirmed);
            row.SubmissionStartedAt.ShouldNotBeNull("intent is committed before the possible write");
            rowId = row.Id;
        }

        h.Adapter.ConditionalOutcomeOverride = null;

        // A screen that cannot be read is Unknown, not an empty screen, and not verification.
        h.Runner.HangSnapshot = true;
        h.Runner.HangDelay = TimeSpan.FromSeconds(5);
        (await h.ReserveAsync()).ShouldBeNull("an unresolved possible write blocks the next arm");
        h.Runner.HangSnapshot = false;

        // Absence of bridge data is not authority either way.
        h.Probe.StateFileFound = false;
        (await h.ReserveAsync()).ShouldBeNull();

        // Nor is a probe that now reports armed: it cannot say WHICH write armed it.
        h.Probe.StateFileFound = true;
        h.Probe.Armed = true;
        (await h.ReserveAsync()).ShouldBeNull();
        h.Adapter.ConditionalInputs.Count(i => i == "/remote-control")
            .ShouldBe(1, "no second automatic arm in this generation");
        await using (var db = h.CreateDb())
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == rowId))
                .MaintenanceResult.ShouldBe(RemoteControlArmResult.ArmUnconfirmed);

        // A proven replacement generation ends the old authority, so a fresh slot may open.
        await h.ReplaceGenerationAsync();
        (await h.ReserveAsync()).ShouldNotBeNull("the veto is generation-scoped, not permanent");
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>
    /// Records the conditional operation and FORWARDS it (so the child really receives the bytes),
    /// then drops the reply — the shape where an acknowledgement never comes back but the write is
    /// not undone.
    /// </summary>
    private sealed class RecordingLostReplyHandler(
        Func<RunnerConditionalInputRequest, RunnerConditionalInputResult> forward) : HttpMessageHandler
    {
        public List<RunnerConditionalInputRequest> Forwarded { get; } = [];
        public List<string> Faults { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RunnerConditionalInputRequest? body;
            try
            {
                request.RequestUri!.AbsolutePath.ShouldEndWith("/conditional-input");
                body = await request.Content!.ReadFromJsonAsync<RunnerConditionalInputRequest>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                forward(body!);
            }
            catch (Exception ex)
            {
                Faults.Add(ex.ToString());
                throw;
            }

            Forwarded.Add(body!);
            throw new HttpRequestException("the reply was lost after the write");
        }
    }
}
