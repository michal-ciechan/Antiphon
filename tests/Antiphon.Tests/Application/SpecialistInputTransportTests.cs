using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SpecialistInputTransportTests
{
    private static async Task<BridgeQueueHarness> HarnessAsync(bool observable = true)
    {
        var settings = new DelegationSettings { ModernPtyBriefInlineMaxBytes = 128, ModernPtySingleWriteMaxBytes = 128 };
        var h = await BridgeQueueHarness.CreateAsync(new()
        {
            Delegation = settings,
            ConfigureDeliveryVerification = v =>
            {
                v.TranscriptConfirmTimeoutSeconds = 1;
                v.PostFailureConfirmGraceSeconds = 0;
                v.PostEvidenceSettleMs = 0;
            },
            ConfigureServices = services => services.AddSingleton(sp => new PtyDeliveryProfile(
                sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<PtyDeliveryProfile>.Instance,
                Options.Create(settings), backendOverride: "modern")),
        });
        if (observable) await h.InsertTurnAsync("prior turn", "prior answer");
        return h;
    }

    private static async Task<AgentTask> TaskAsync(BridgeQueueHarness h, string facts, int? maxBytes = null)
    {
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        var id = Guid.NewGuid();
        var task = new AgentTask { Id = id, RootTaskId = id, Title = "synthetic capability fixture",
            Goal = facts, Role = AgentTaskRole.Check, ReplyTo = AgentTaskReplyTo.None,
            AgentKind = session.AgentKind, AgentId = h.AgentId, AgentSessionId = h.SessionId,
            WorkingDirectory = session.Cwd, CreatedAt = DateTime.UtcNow,
            ExecutionDeadlineAt = DateTime.UtcNow.AddMinutes(2) };
        var body = DelegationReportFormatter.BuildBrief(task, new()).ReplaceLineEndings("\n").Trim();
        task.SpecialistInputPolicyJson = new SpecialistInputPolicy(1, id, session.Id,
            session.StartedAt!.Value, session.AgentKind, DeliveryBackend.ModernConPty,
            maxBytes ?? Encoding.UTF8.GetByteCount(body), "synthetic-transport-fixture-only").Serialize();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static string Fit(AgentTask task) => AgentTaskDispatcher.FitBriefForTyping(task, new(),
        new(DeliveryBackend.ModernConPty, 128, 20000, 128, "synthetic envelope"), agentKind: task.AgentKind);

    private static Task<SessionQueueDto> EnqueueAsync(BridgeQueueHarness h, AgentTask task, string body, bool deliver = true) =>
        h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, conversationKey: "same-conversation", deliverIfIdle: deliver,
            executionDeadlineAt: task.ExecutionDeadlineAt, executionTaskId: task.Id);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Card0415_V04_complete_utf8_envelope_survives_both_spill_layers(int spareByte)
    {
        await using var h = await HarnessAsync();
        var task = await TaskAsync(h, "head café\r\nmiddle 日本語 😀 e\u0301\r\ntail evidence " + new string('x', 2000));
        var policy = SpecialistInputPolicy.Read(task.SpecialistInputPolicyJson)!;
        task.SpecialistInputPolicyJson = (policy with { MaxUtf8Bytes = policy.MaxUtf8Bytes + spareByte }).Serialize();
        await using (var db = BridgeQueueHarness.CreateContext())
            await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.SpecialistInputPolicyJson, task.SpecialistInputPolicyJson));
        var expected = DelegationReportFormatter.BuildBrief(task, new()).ReplaceLineEndings("\n").Trim();
        var body = Fit(task);
        body.ShouldBe(expected);
        await EnqueueAsync(h, task, body);
        h.Adapter.SubmittedBodies.ShouldBe([expected]);
        h.Adapter.Inputs.ShouldBe(["\u001b[200~" + expected + "\u001b[201~", "\r"]);
        await using var verify = BridgeQueueHarness.CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == task.Id);
        row.Body.ShouldBe(expected);
        row.SpecialistInputPolicyJson.ShouldBe(task.SpecialistInputPolicyJson);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        Directory.Exists(Path.Combine(task.WorkingDirectory, ".antiphon")).ShouldBeFalse();
    }

    [Test]
    public async Task Card0415_V04_oversize_is_refused_before_file_or_submit_even_when_characters_fit()
    {
        await using var h = await HarnessAsync();
        var task = await TaskAsync(h, "日本語 " + new string('é', 1000));
        var policy = SpecialistInputPolicy.Read(task.SpecialistInputPolicyJson)!;
        task.SpecialistInputPolicyJson = (policy with { MaxUtf8Bytes = policy.MaxUtf8Bytes - 1 }).Serialize();
        var raw = DelegationReportFormatter.BuildBrief(task, new()).ReplaceLineEndings("\n").Trim();
        raw.Length.ShouldBeLessThan(policy.MaxUtf8Bytes - 1);
        Should.Throw<SpecialistInputUnsupportedException>(() => Fit(task)).Outcome.ShouldBe(SpecialistAttemptOutcome.InputUnsupported);
        await using (var db = BridgeQueueHarness.CreateContext())
            await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.SpecialistInputPolicyJson, task.SpecialistInputPolicyJson));
        await Should.ThrowAsync<SpecialistInputUnsupportedException>(() => EnqueueAsync(h, task, raw));
        h.Adapter.Inputs.ShouldBeEmpty();
        await using var verify = BridgeQueueHarness.CreateContext();
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == task.Id)).ShouldBe(0);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Goal.ShouldBe(task.Goal);
        Directory.Exists(Path.Combine(task.WorkingDirectory, ".antiphon")).ShouldBeFalse();
    }

    [Test]
    public async Task Card0415_V04_reload_retains_policy_and_does_not_batch_Checks()
    {
        await using var h = await HarnessAsync();
        var first = await TaskAsync(h, "first complete fact bundle " + new string('a', 1000));
        var second = await TaskAsync(h, "second complete fact bundle " + new string('b', 1000));
        await EnqueueAsync(h, first, Fit(first), false);
        await EnqueueAsync(h, second, Fit(second), false);
        var reloaded = ActivatorUtilities.CreateInstance<SessionMessageQueueService>(h.Provider);
        await reloaded.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBe([Fit(first)]);
        await reloaded.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBe([Fit(first), Fit(second)]);
    }

    [Test]
    [Arguments("middle")]
    [Arguments("tail")]
    [Arguments("extra")]
    [Arguments("queued")]
    [Arguments("screen")]
    [Arguments("wrong-token")]
    public async Task Card0415_V04_only_the_complete_current_UserPrompt_confirms(string damage)
    {
        await using var h = await HarnessAsync(observable: damage != "screen");
        var task = await TaskAsync(h, "HEAD\nMIDDLE café 日本語\nTAIL");
        var body = Fit(task);
        h.Adapter.OnSubmitted = async submitted =>
        {
            if (damage == "screen") return;
            var record = damage switch
            {
                "middle" => submitted.Replace("MIDDLE", "BROKEN"),
                "tail" => submitted.Replace("TAIL", ""),
                "extra" => "foreign prefix\n" + submitted,
                "wrong-token" => submitted.Replace(DelegationReportFormatter.Short(task.Id), "deadbeef"),
                _ => submitted,
            };
            await h.InsertTranscriptEntryAsync(damage == "queued" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt,
                record, timestamp: DateTime.UtcNow);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };
        await EnqueueAsync(h, task, body);
        await using var verify = BridgeQueueHarness.CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == task.Id);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        row.Body.ShouldBe(body);
        row.SpecialistInputPolicyJson.ShouldBe(task.SpecialistInputPolicyJson);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1, "a partial/missing transcript must never cause a second body submit");
    }

    [Test]
    public async Task Card0415_V04_generation_change_at_reload_refuses_before_submit()
    {
        await using var h = await HarnessAsync();
        var task = await TaskAsync(h, "full facts");
        await EnqueueAsync(h, task, Fit(task), false);
        await using (var db = BridgeQueueHarness.CreateContext())
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, DateTime.UtcNow.AddMinutes(1)));
        await Should.ThrowAsync<SpecialistInputUnsupportedException>(() => h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None));
        h.Adapter.Inputs.ShouldBeEmpty();
    }
}
