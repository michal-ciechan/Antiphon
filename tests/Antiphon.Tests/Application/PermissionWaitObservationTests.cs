using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class PermissionWaitTests
{
    [Test]
    public async Task C1518_OptionalObservationInputs()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        var valid = (await f.Harness.Runtime.ObserveDialogAsync(f.SessionId, ct))!;
        var reader = f.Reader;
        var cases = new (string Label, ProviderDialogObservation? Value, bool Wait)[]
        {
            ("absent", null, false), ("empty JSON", JsonSerializer.Deserialize<ProviderDialogObservation>("{}"), false),
            ("wrong session", valid with { SessionId = Guid.NewGuid() }, false),
            ("empty session", valid with { SessionId = Guid.Empty }, false),
            ("empty epoch", valid with { RuntimeEpoch = Guid.Empty }, false),
            ("missing generation", valid with { AcceptedStartedAt = null }, false),
            ("default generation", valid with { AcceptedStartedAt = default(DateTime) }, false),
            ("stale generation", valid with { AcceptedStartedAt = f.Generation.AddSeconds(-1) }, false),
            ("future generation", valid with { AcceptedStartedAt = f.Generation.AddSeconds(1) }, false),
            ("history unknown", valid with { InputHistoryKnown = false }, false),
            ("negative output", valid with { OutputSequence = -1 }, false),
            ("negative input", valid with { InputRevision = -1 }, false),
            ("unknown", valid with { State = ProviderDialogState.Unknown }, false),
            ("invalid state", valid with { State = (ProviderDialogState)77 }, false),
            ("clear", valid with { State = ProviderDialogState.Clear }, false),
            ("zero counters", valid with { OutputSequence = 0, InputRevision = 0 }, true),
            ("missing kind", valid with { Kind = null, CanCancel = null, SubmittedInput = null }, true),
            ("empty kind", valid with { Kind = "", CanCancel = false }, true),
            ("unknown kind", valid with { Kind = "secret command", CanCancel = true }, true),
            ("question", valid with { Kind = "Question" }, true),
            ("proof is metadata", valid with { SubmittedInput = new SubmittedInputProof(Guid.NewGuid(), "binding", 0, 1, "prompt", 1), CanCancel = true }, true),
            ("permission", JsonSerializer.Deserialize<ProviderDialogObservation>(JsonSerializer.Serialize(valid)), true),
        };
        foreach (var row in cases)
        {
            reader.ObserveOverride = (_, _) => Task.FromResult(row.Value);
            var facts = await reader.ReadAsync(f.TaskId, true, ct);
            (facts?.IsWait == true).ShouldBe(row.Wait, row.Label);
            if (row.Wait)
            {
                facts!.DialogLabel.ShouldBe(row.Value!.Kind switch
                { "ToolPermission" => "permission dialog", "Question" => "question dialog", _ => "provider dialog" }, row.Label);
            }
        }
        await using var db = f.Db();
        foreach (var id in new Guid?[] { null, Guid.Empty, Guid.NewGuid() })
        {
            await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.AgentSessionId, id), ct);
            (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("missing session binding");
        }
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.AgentSessionId, f.SessionId).SetProperty(t => t.DispatchedAt, (DateTime?)null), ct);
        (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("missing dispatch");
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.DispatchedAt, f.DispatchedAt), ct);
        foreach (var runner in new[] { "", "desktop", "fixture-runner" })
        {
            await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RunnerId, runner).SetProperty(s => s.RunnerStoreId, (Guid?)null), ct);
            var value = await reader.ReadAsync(f.TaskId, true, ct);
            (value?.IsWait == true).ShouldBe(runner != "fixture-runner", "remote store absence is unknown");
        }
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RunnerId, (string?)null), ct);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId).ExecuteDeleteAsync(ct);
        await db.SessionQueuedMessages.Where(m => m.AgentSessionId == f.SessionId).ExecuteDeleteAsync(ct);
        await f.Writer.RecordAsync(f.TaskId, await reader.ReadAsync(f.TaskId, true, ct), ct);
        var note = await f.NoteAsync(ct);
        note.Body.ShouldContain("Tool execution attribution is unknown.");
        note.Body.ShouldContain("Pending refinements: 0; receipt: none queued.");
    }

    [Test]
    public async Task C1518_ObservationBindingRacesAndExpiry()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        var reader = f.Reader;
        var valid = (await f.Harness.Runtime.ObserveDialogAsync(f.SessionId, ct))!;
        var facts = await reader.ReadAsync(f.TaskId, true, ct);
        facts.ShouldNotBeNull();
        f.Clock.Advance(TimeSpan.FromSeconds(90));
        (await reader.CachedAsync(f.TaskId, ct))!.IsWait.ShouldBeTrue("90 seconds is inclusive");
        f.Clock.Advance(TimeSpan.FromTicks(1));
        (await reader.CachedAsync(f.TaskId, ct)).ShouldBeNull("access cannot renew completed-read time");
        reader.ObserveOverride = (_, _) => Task.FromResult<ProviderDialogObservation?>(null);
        (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("failed refresh invalidates");
        (await reader.CachedAsync(f.TaskId, ct)).ShouldBeNull();
        await using var db = f.Db();
        reader.ObserveOverride = async (_, token) =>
        {
            await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, 2), token);
            return valid;
        };
        (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("attempt changed across await");
        reader.ObserveOverride = async (_, token) =>
        {
            await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, f.Generation.AddSeconds(1)), token);
            return valid;
        };
        (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("generation changed across await");
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, f.Generation), ct);
        reader.ObserveOverride = async (_, token) =>
        {
            await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RunnerId, "fixture-runner").SetProperty(s => s.RunnerStoreId, Guid.NewGuid()), token);
            return valid;
        };
        (await reader.ReadAsync(f.TaskId, true, ct)).ShouldBeNull("runner binding changed across await");
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.RunnerId, (string?)null).SetProperty(s => s.RunnerStoreId, (Guid?)null), ct);
        facts.Fresh(facts.CompletedAt.AddTicks(-1)).ShouldBeFalse("negative-age evidence is not current");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        reader.ObserveOverride = async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task.WaitAsync(token); return valid; }
            return valid with { RuntimeEpoch = Guid.NewGuid(), State = ProviderDialogState.Clear };
        };
        var old = reader.ReadAsync(f.TaskId, true, ct);
        await entered.Task.WaitAsync(ct);
        var newer = reader.ReadAsync(f.TaskId, true, ct);
        release.SetResult();
        await old;
        (await newer)!.IsWait.ShouldBeFalse();
        (await reader.CachedAsync(f.TaskId, ct))!.IsWait.ShouldBeFalse("serialized reads cannot revive old blocked evidence");
        reader.ObserveOverride = (_, token) => Task.FromCanceled<ProviderDialogObservation?>(token);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => reader.ReadAsync(f.TaskId, true, canceled.Token));
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1518_RealChildAndOtherClocksRemainBounded()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await using var child = await PermissionWaitChild.StartAsync(ct);
        child.Exited.ShouldBeFalse("real owned child reached its readiness barrier");
        await using var db = f.Db();
        var task = await f.TaskAsync(ct);
        var settings = new DelegationSettings { BootModelWaitDeadlineMinutes = 0 };
        var now = f.DispatchedAt.AddMinutes(90);
        var absent = await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct);
        absent!.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        absent.Breached.ShouldBeTrue();
        child.Exited.ShouldBeFalse("classification does not stop a real child");
        f.Clock.AdvanceTo(new DateTimeOffset(now));
        var wait = await f.Reader.ReadAsync(f.TaskId, true, ct);
        var blocked = await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct, wait);
        blocked!.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ProviderInputWait);
        blocked.Limit.ShouldBe(absent.Limit);
        settings.RolePolicy["Code"] = new() { TimeoutMinutes = 30 };
        (await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct, wait))!.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.Ceiling);
        settings.RolePolicy["Code"] = new() { TimeoutMinutes = 240 };
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind, TranscriptKinds.ToolResult), ct);
        var model = await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct, wait);
        model!.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait);
        model.Limit.ShouldBe(TimeSpan.FromMinutes(20));
        settings.RolePolicy["Code"] = new() { TimeoutMinutes = 0 };
        settings.LocalExecutionDeadlineMinutes = settings.ModelWaitDeadlineMinutes = 0;
        (await TaskDeadlinePolicy.EvaluateAsync(db, task, now, settings, ct, wait)).ShouldBeNull("all clocks disarmed");
        f.Clock.Advance(TimeSpan.FromSeconds(91));
        settings.LocalExecutionDeadlineMinutes = 90;
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind, TranscriptKinds.ToolCall), ct);
        (await TaskDeadlinePolicy.EvaluateAsync(db, task, f.Clock.GetUtcNow().UtcDateTime, settings, ct, wait))!.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution, "stale supplied facts cannot claim a current modal");
        await child.ReleaseAsync();
        child.Exited.ShouldBeTrue();
    }
}
