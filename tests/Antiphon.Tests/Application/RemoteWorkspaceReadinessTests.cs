using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.RemoteWorkspacePreparerTests;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RemoteWorkspaceReadinessTests
{
    [Test]
    [Arguments("binding")]
    [Arguments("legacy path")]
    [Arguments("recovered binding")]
    public async Task C1349_CaptureRefreshesReadyHold(string scenario)
    {
        var bus = new ReadinessBus();
        await using var rig = await Rig.StartAsync(configure: s => s.AddSingleton<IEventBus>(bus));
        var setup = await BeginAsync(rig);
        if (scenario == "recovered binding")
        {
            await rig.RestartAsync();
            await rig.TickAsync();
            var recovered = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 2);
            var sent = recovered.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
            sent.Binding!.PreparationId.ShouldBe(setup.Sent.Binding!.PreparationId);
            setup = setup with { Frame = recovered, Sent = sent };
        }
        bus.Observe = async () => await AssertReadModelsAsync(rig, setup.TaskId, ready: true);
        await rig.Peer.EmitAsync(scenario == "legacy path" ? MirrorResult(setup.Frame, setup.Path)
            : BoundResult(setup.Frame, setup.Binding, setup.Path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        bus.ReadyHints.ShouldBe(1, "a fresh reader inside the hint must see the committed path and ready event");
        await AssertReadyAsync(rig, setup.TaskId, setup.Path);
        rig.Sink.Specs.ShouldBeEmpty();
        var events = await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held);
        events.Count(e => RemoteWorkspaceReadiness.IsInFlight(e.Detail)).ShouldBe(1);
        events.Count(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold).ShouldBe(1);
    }

    [Test]
    [Arguments("canceled")]
    [Arguments("Working")]
    [Arguments("retry")]
    [Arguments("reassigned")]
    [Arguments("empty path")]
    [Arguments("mismatched path")]
    [Arguments("capacity hold")]
    [Arguments("lease hold")]
    [Arguments("land hold")]
    [Arguments("stale dispatcher writer")]
    public async Task C1349_LateCaptureCannotReplaceCurrentHold(string scenario)
    {
        var bus = new ReadinessBus();
        await using var rig = await Rig.StartAsync(configure: s => s.AddSingleton<IEventBus>(bus));
        var setup = await BeginAsync(rig);
        var cached = await rig.ReadTaskAsync(setup.TaskId);
        var originalHold = (await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held)).Last().Detail;
        string? stronger = scenario switch
        {
            "capacity hold" => DispatchHoldDetails.ConcurrencyCap(1),
            "lease hold" => DispatchHoldDetails.LeaseOccupiedUnknown,
            "land hold" => "held: CARD-1349's kept branch feat/other (task other) is landing and is not yet in master",
            _ => null,
        };
        await using (var edit = rig.NewDb())
        {
            var row = await edit.AgentTasks.SingleAsync(t => t.Id == setup.TaskId);
            switch (scenario)
            {
                case "canceled": row.Status = AgentTaskStatus.Canceled; break;
                case "Working": row.Status = AgentTaskStatus.Working; break;
                case "retry": row.Attempt++; break;
                case "reassigned": row.RunnerId = "fixture-other-runner"; break;
                case "mismatched path": row.RemoteWorktreePath = "/work/current-attempt"; break;
            }
            if (stronger is not null)
                edit.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = setup.TaskId,
                    Type = AgentTaskEventType.Held, Detail = stronger, At = DateTime.UtcNow.AddSeconds(1) });
            await edit.SaveChangesAsync();
        }
        var returned = scenario == "empty path" ? setup.Binding with { WorktreePath = "" } : setup.Binding;
        await rig.Peer.EmitAsync(BoundResult(setup.Frame, returned, scenario == "empty path" ? "" : setup.Path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (scenario == "stale dispatcher writer")
        {
            await using var scope = rig.Provider.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TraceHeldAsync(cached,
                originalHold, new Dictionary<Guid, string>(), default)).ShouldBeFalse();
        }
        await using var read = rig.NewDb();
        var saved = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == setup.TaskId);
        var history = await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held);
        if (scenario != "empty path")
            (await read.TaskWorktreeMirrorBindings.SingleAsync(b => b.PreparationId == setup.Binding.PreparationId))
                .BindingId.ShouldBe(setup.Binding.BindingId, "late valid resource evidence remains retained");
        else
            (await read.TaskWorktreeMirrorBindings.SingleAsync()).BindingId.ShouldBe(Guid.Empty);
        if (scenario == "stale dispatcher writer")
        {
            history.Last().Detail.ShouldBe(RemoteWorkspaceReadiness.ReadyHold);
            history.Count(e => RemoteWorkspaceReadiness.IsInFlight(e.Detail)).ShouldBe(1);
            history.Count(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold).ShouldBe(1);
        }
        else
        {
            history.ShouldNotContain(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold);
            if (stronger is not null) history.Last().Detail.ShouldBe(stronger);
            if (scenario is "Working" or "retry" or "reassigned" or "empty path") saved.RemoteWorktreePath.ShouldBeNull();
            else if (scenario == "mismatched path") saved.RemoteWorktreePath.ShouldBe("/work/current-attempt");
            else saved.RemoteWorktreePath.ShouldBe(setup.Path);
        }
        saved.AgentSessionId.ShouldBeNull(); rig.Sink.Specs.ShouldBeEmpty();
        if (scenario is "canceled" or "Working" or "retry" or "reassigned" or "empty path") bus.ReadyHints.ShouldBe(0);
        if (stronger is not null)
        {
            var items = await AttentionAsync(rig);
            items.Single(i => i.TaskId == setup.TaskId).Headline.ShouldContain(stronger);
        }
    }

    [Test]
    [Arguments("binding-only commit")]
    [Arguments("path-only commit")]
    [Arguments("ready rollback")]
    [Arguments("publish throws")]
    [Arguments("concurrent capture")]
    [Arguments("repeated recovery")]
    public async Task C1349_ReadyProjectionSurvivesLostNotification(string scenario)
    {
        var bus = new ReadinessBus();
        await using var rig = await Rig.StartAsync(configure: s => s.AddSingleton<IEventBus>(bus));
        var setup = await BeginAsync(rig);
        if (scenario == "binding-only commit")
        {
            await using var edit = rig.NewDb();
            var row = await edit.TaskWorktreeMirrorBindings.SingleAsync();
            row.BindingId = setup.Binding.BindingId; row.CommonDirectory = setup.Binding.CommonDirectory;
            row.GitDirectory = setup.Binding.GitDirectory; row.WorktreePath = setup.Path;
            await edit.SaveChangesAsync();
            (await rig.ReadTaskAsync(setup.TaskId)).RemoteWorktreePath.ShouldBeNull();
            await AssertReadModelsAsync(rig, setup.TaskId, ready: false);
        }
        if (scenario == "path-only commit")
        {
            await using var edit = rig.NewDb();
            await edit.AgentTasks.Where(t => t.Id == setup.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RemoteWorktreePath, setup.Path));
            (await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held))
                .ShouldNotContain(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold);
            await AssertReadModelsAsync(rig, setup.TaskId, ready: true);
        }
        if (scenario == "ready rollback")
        {
            await using var edit = rig.NewDb();
            await using (var transaction = await edit.Database.BeginTransactionAsync())
            {
                (await RemoteWorkspaceService.CaptureMirrorBindingAsync(edit, setup.Binding, default)).ShouldBeTrue();
                (await edit.AgentTaskEvents.CountAsync(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold)).ShouldBe(1);
                await transaction.RollbackAsync();
            }
            (await rig.ReadTaskAsync(setup.TaskId)).RemoteWorktreePath.ShouldBeNull();
            (await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held))
                .ShouldNotContain(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold);
            bus.ReadyHints.ShouldBe(0);
        }
        async Task CaptureAsync()
        {
            await using var db = rig.NewDb();
            (await RemoteWorkspaceService.CaptureMirrorBindingAsync(db, setup.Binding, default)).ShouldBeTrue();
        }
        if (scenario == "concurrent capture") await Task.WhenAll(CaptureAsync(), CaptureAsync());
        if (scenario == "repeated recovery") { await CaptureAsync(); await CaptureAsync(); await CaptureAsync(); }
        if (scenario == "publish throws")
        {
            bus.Observe = async () =>
            {
                await AssertReadyAsync(rig, setup.TaskId, setup.Path);
                throw new IOException("owned notification failure after committed readiness");
            };
        }
        await rig.Peer.EmitAsync(BoundResult(setup.Frame, setup.Binding, setup.Path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await AssertReadyAsync(rig, setup.TaskId, setup.Path);
        bus.ReadyHints.ShouldBe(1);
        (await rig.EventsAsync(setup.TaskId, AgentTaskEventType.Held))
            .Count(e => e.Detail == RemoteWorkspaceReadiness.ReadyHold).ShouldBe(1);
        var task = await rig.ReadTaskAsync(setup.TaskId);
        task.RemotePrepFailures.ShouldBe(0); task.DispatchNotBeforeAt.ShouldBeNull();
        rig.Sink.Specs.ShouldBeEmpty();
    }

    private sealed record PendingMirror(Guid TaskId, PhoneHomeFrame Frame, PhoneHomeWorkspaceMirrorRequest Sent,
        CompletedCardWorkspaceBinding Binding, string Path);
    private static async Task<PendingMirror> BeginAsync(Rig rig)
    {
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        // The rig workspace is not a Git checkout, and a Code task fails its claim-time progress
        // baseline capture before any mirror request; the rig's Custom worker role mirrors as
        // production does and is listed by the pipeline read model like every helper role.
        var id = await rig.SeedAsync();
        await rig.TickAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var frame = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var sent = frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var path = "/work/worktrees/" + sent.Name;
        return new(id, frame, sent, ValidBinding(sent, Guid.NewGuid(), path), path);
    }
    private static async Task AssertReadyAsync(Rig rig, Guid id, string path)
    {
        var task = await rig.ReadTaskAsync(id);
        task.Status.ShouldBe(AgentTaskStatus.Queued); task.RemoteWorktreePath.ShouldBe(path);
        task.AgentSessionId.ShouldBeNull();
        await AssertReadModelsAsync(rig, id, ready: true);
    }
    private static async Task AssertReadModelsAsync(Rig rig, Guid id, bool ready)
    {
        await using var db = rig.NewDb();
        var settings = Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 });
        var pipeline = await new AgentTaskPipelineStatusService(db, settings,
            new AreaMapLoader(settings, NullLogger<AreaMapLoader>.Instance), TimeProvider.System)
            .GetAsync(CancellationToken.None);
        var queued = pipeline.Stages.Single(s => s.Role == AgentTaskRole.Custom).Queued.Single(t => t.TaskId == id);
        queued.QueueReason.ShouldBe(ready ? AgentTaskPipelineStatusService.QueueReasonAwaitingDispatch
            : AgentTaskPipelineStatusService.QueueReasonRemotePrep);
        var item = (await AttentionAsync(rig)).Single(i => i.TaskId == id);
        if (ready)
        {
            item.Headline.ShouldContain(RemoteWorkspaceReadiness.ReadyHold);
            item.Headline.ShouldNotContain(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        }
        else item.Headline.ShouldContain(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
    }
    private static async Task<IReadOnlyList<AttentionItemDto>> AttentionAsync(Rig rig)
    {
        await using var db = rig.NewDb();
        return (await new AttentionService(db, new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            new FakeTimeProvider(DateTimeOffset.UtcNow.AddSeconds(302)), NullLogger<AttentionService>.Instance)
            .GetAsync(default, includeProgressProbe: false)).Items;
    }
    private sealed class ReadinessBus : IEventBus
    {
        public int ReadyHints { get; private set; }
        public Func<Task>? Observe { get; set; }
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
        public async Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            if (eventName != "AgentTaskChanged") return;
            ReadyHints++;
            if (Observe is not null) await Observe();
        }
    }
}
