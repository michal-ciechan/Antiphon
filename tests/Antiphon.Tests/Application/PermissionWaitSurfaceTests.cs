using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class PermissionWaitTests
{
    [Test]
    public async Task C1518_CheckAttentionAndStallAgree()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        var observed = (await f.Harness.Runtime.ObserveDialogAsync(f.SessionId, ct))!;
        await f.ScanAsync(ct);
        await using var db = f.Db();
        (await db.AgentIncidents.CountAsync(i => i.SessionId == f.SessionId, ct)).ShouldBe(0, "below the generic stall threshold");
        var original = await db.TranscriptEntries.AsNoTracking().SingleAsync(t => t.AgentSessionId == f.SessionId, ct);
        for (var i = 2; i <= 7; i++) db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(),
            AgentSessionId = f.SessionId, Sequence = i, Uuid = "loop-" + i, Kind = original.Kind,
            ToolName = original.ToolName, ToolInput = original.ToolInput, Timestamp = original.Timestamp, CreatedAt = original.CreatedAt });
        await db.SaveChangesAsync(ct);
        f.Harness.Delegation.StallDetection.Enabled = true;
        f.Clock.AdvanceTo(new DateTimeOffset(f.DispatchedAt).AddMinutes(30));
        foreach (var (kind, label) in new[] { ("ToolPermission", "permission dialog"), ("Question", "question dialog"), ("Choice", "provider dialog") })
        {
            f.Reader.ObserveOverride = (_, _) => Task.FromResult<ProviderDialogObservation?>(observed with { Kind = kind });
            await f.Reader.ReadAsync(f.TaskId, true, ct);
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            var check = scope.ServiceProvider.GetRequiredService<DelegateCheckProbe>();
            var facts = await check.GatherAsync(await f.TaskAsync(ct), ct);
            DelegateCheckProbe.RenderDigest(facts).ShouldContain("current Claude " + label);
            var attention = await scope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(ct, includeProgressProbe: false);
            var row = attention.Items.Single(i => i.TaskId == f.TaskId && i.Kind.ToString() == "ProviderInputWait");
            row.Severity.ShouldBe(AlertSeverity.Warning);
            row.Headline.ShouldContain(label);
            row.Evidence.ShouldContain("90-minute");
            row.Actions.ShouldBe(new[] { AttentionAction.OpenDrawer, AttentionAction.OpenAgent, AttentionAction.OpenCard });
            row.Evidence.ShouldNotContain(PermissionWaitFixture.CapturedRefusal);
            await db.AgentIncidents.Where(i => i.SessionId == f.SessionId).ExecuteDeleteAsync(ct);
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            dispatcher.WorkspaceProbeOverride = new NoWorkspaceProgress();
            (await dispatcher.DetectStalledProgressAsync(ct)).ShouldBe(1);
            (await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.SessionId == f.SessionId && i.Kind == AgentIncidentKind.TaskProgressStalled, ct)).Message.ShouldContain("current Claude " + label);
        }
        f.Clock.Advance(TimeSpan.FromSeconds(91));
        await using var finalScope = f.Harness.Provider.CreateAsyncScope();
        var stale = await finalScope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(ct, includeProgressProbe: false);
        stale.Items.ShouldNotContain(i => i.Kind.ToString() == "ProviderInputWait");
    }

    [Test]
    public async Task C1518_FailedSeatRecoveryDoesNotResurrect()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await f.ScanAsync(ct);
        f.Clock.AdvanceTo(new DateTimeOffset(f.DispatchedAt).AddMinutes(90));
        (await f.DeadlineAsync(ct)).ShouldBe(1);
        await f.AssertCustodyAsync(AgentTaskStatus.Failed, ct);
        var replies = f.Harness.Provider.GetRequiredService<AgentTaskReplyService>();
        await Should.ThrowAsync<ConflictException>(() => replies.RefineAsync(f.TaskId, "new input", ct));
        (await f.TaskAsync(ct)).FailureReason!.ShouldContain("New Refine calls on this task are refused.");
        await using var scope = f.Harness.Provider.CreateAsyncScope();
        var attention = await scope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(ct, includeProgressProbe: false);
        attention.Items.ShouldNotContain(i => i.TaskId == f.TaskId && i.Kind.ToString() == "ProviderInputWait");
        await using var db = f.Db();
        // The absent server-row route is a runner-only seat. Deleting this fixture's real
        // session would itself cascade its queue, which is not recovery behavior.
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: f.Harness.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var releaseCalls = 0;
        var releasing = f.SessionId;
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.ReleaseSlot) return null;
            releaseCalls++;
            return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new RunnerSessionDto(releasing, 4, f.Generation, "Exited", 0, "KilledByRequest", 0), PhoneHomeFraming.Json));
        };
        foreach (var id in new[] { f.SessionId, Guid.NewGuid() })
        {
            releasing = id;
            peer.Sessions.Add(new RunnerSessionDto(id, 4, f.Generation, "Running", null, "", 0));
            using var refused = await host.PostOperatorAsync($"/api/session-runners/{host.AllowedRunnerId}/slots/{id:D}/release",
                new RunnerSlotReleaseRequest("permission fixture"), null);
            ((int)refused.StatusCode).ShouldBe(403);
            using var released = await host.PostOperatorAsync($"/api/session-runners/{host.AllowedRunnerId}/slots/{id:D}/release",
                new RunnerSlotReleaseRequest("permission fixture"), OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath));
            released.EnsureSuccessStatusCode();
        }
        releaseCalls.ShouldBe(2);
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.RunnerSlotForceReleased, ct)).ShouldBe(2);
        await Should.ThrowAsync<ConflictException>(() => replies.RefineAsync(f.TaskId, "still refused", ct));
        (await f.TaskAsync(ct)).Status.ShouldBe(AgentTaskStatus.Failed);
        (await db.SessionQueuedMessages.CountAsync(m => m.Id == f.RefinementId, ct)).ShouldBe(1);
        File.ReadAllText(f.SourceMarker).ShouldBe(PermissionWaitFixture.SourceContents);
        f.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1518_CadenceAndReadFailuresAreBounded()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        var observed = (await f.Harness.Runtime.ObserveDialogAsync(f.SessionId, ct))!;
        var calls = 0;
        f.Reader.ObserveOverride = (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult<ProviderDialogObservation?>(observed); };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Reader.ReadAsync(f.TaskId, false, ct)));
        calls.ShouldBe(1, "concurrent Check/scan reads share the session cadence");
        f.Clock.Advance(TimeSpan.FromSeconds(59));
        await f.ScanAsync(ct);
        calls.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.ScanAsync(ct);
        calls.ShouldBe(2, "due at exactly 60s");
        await using var db = f.Db();
        var peers = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var session = Guid.NewGuid(); var task = Guid.NewGuid(); peers.Add(task);
            db.AgentSessions.Add(new AgentSession { Id = session, AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, StartedAt = f.Generation, CreatedAt = f.Generation, DefinitionName = "fake", Cwd = f.WorktreePath });
            db.AgentTasks.Add(new AgentTask { Id = task, RootTaskId = task, AgentSessionId = session,
                AgentKind = AgentKind.ClaudeCode, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Working,
                DispatchedAt = f.DispatchedAt, CreatedAt = f.DispatchedAt, Attempt = 1, Title = "sparse peer", Goal = "observe", WorkingDirectory = f.WorktreePath });
        }
        await db.SaveChangesAsync(ct);
        var active = 0; var peak = 0; var readCount = 0;
        var four = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Reader.ObserveOverride = async (id, token) =>
        {
            var n = Interlocked.Increment(ref active);
            int old; do { old = peak; } while (n > old && Interlocked.CompareExchange(ref peak, n, old) != old);
            if (Interlocked.Increment(ref readCount) == 4) four.TrySetResult();
            try { await four.Task.WaitAsync(token); if (n == 1) throw new IOException("peer unavailable"); return observed with { SessionId = id }; }
            finally { Interlocked.Decrement(ref active); }
        };
        await f.ScanAsync(ct);
        peak.ShouldBe(4, "exact bounded concurrency");
        (await db.AgentTaskLandNotifications.CountAsync(n => peers.Contains(n.TaskId), ct)).ShouldBeGreaterThan(0, "failed peer does not block sparse peers");
    }

    [Test]
    public async Task C1518_DetectionHasNoTerminalSideEffects()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        (await f.ScanAsync(ct)).ShouldBe(1, "positive diagnosis rules out a no-op");
        await using var scope = f.Harness.Provider.CreateAsyncScope();
        var attention = await scope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(ct, includeProgressProbe: false);
        attention.Items.ShouldContain(i => i.TaskId == f.TaskId && i.Kind.ToString() == "ProviderInputWait");
        await f.AssertCustodyAsync(AgentTaskStatus.Working, ct);
        await using var db = f.Db();
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Workspace, WorkspaceMode.Shared), ct);
        var check = await scope.ServiceProvider.GetRequiredService<DelegateCheckProbe>().GatherAsync(await f.TaskAsync(ct), ct);
        DelegateCheckProbe.RenderDigest(check).ShouldContain("current Claude permission dialog");
        (await f.DeadlineAsync(ct)).ShouldBe(0);
        await f.AssertCustodyAsync(AgentTaskStatus.Working, ct);
    }
    private sealed class NoWorkspaceProgress : IWorkspaceProgressProbe
    {
        public Task<WorkspaceProgressArm> ProbeProgressAsync(string? directory, DateTime since, bool sharedCheckout, CancellationToken ct) =>
            Task.FromResult(new WorkspaceProgressArm(false, null, null, sharedCheckout));
    }
}
