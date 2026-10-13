using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-1032 S8 / CP-15: durable outcome and retry over the real phone-home socket,
// runner dispatcher MutationLock and scratch Git recipient.
public sealed partial class CardWorktreeMirrorCleanupTests
{
    // Hand-written expectation for the coordinator's persisted retry delay.
    private static readonly TimeSpan MirrorRetryDelay = TimeSpan.FromMinutes(5);

    // case: local endpoint Complete, mirror endpoint Pending, fresh coordinator;
    // mutation (PC-71): skip a target whose local endpoint is Complete;
    // red: mirrorDirectoryGone.ShouldBeTrue() fails.
    [Test] public async Task C1017_MirrorCompletesIndependently()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
        await using (var db = f.Card.Host.CreateContext())
            (await db.CardWorktreeCleanupEndpoints.Where(e => e.Target.TaskId == f.Card.TaskId && e.EndpointIdentity == "local")
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, CardWorktreeCleanupEndpointState.Complete))).ShouldBe(1);
        (await f.EndpointAsync()).State.ShouldBe(CardWorktreeCleanupEndpointState.Pending);
        var result = await f.TryAsync();
        var mirrorDirectoryGone = !Directory.Exists(f.Mirror) && !Directory.Exists(f.Binding.GitDirectory);
        mirrorDirectoryGone.ShouldBeTrue(result.Reason);
        result.Complete.ShouldBeTrue();
        await using var fresh = f.Card.Host.CreateContext();
        var rows = await fresh.CardWorktreeCleanupEndpoints.AsNoTracking().Where(e => e.Target.TaskId == f.Card.TaskId).ToListAsync();
        var local = rows.Single(e => e.EndpointIdentity == "local");
        local.State.ShouldBe(CardWorktreeCleanupEndpointState.Complete); local.OperationId.ShouldBeNull();
        var mirror = rows.Single(e => e.Id == f.EndpointId);
        mirror.State.ShouldBe(CardWorktreeCleanupEndpointState.Complete); mirror.ImportedReceipt.ShouldNotBeNull();
    }

    // case: runner connection ended, local tree eligible; mutation (PC-72): refuse local execution
    // while a mirror endpoint of the target is not Complete; red: localDirectoryGone.ShouldBeTrue() fails.
    // The offline mirror keeps retryable debt and completes after reconnect once due.
    [Test] public async Task C1017_LocalProgressSurvivesOffline()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
        await f.DisconnectAsync();
        WorktreeRemoval local;
        await using (var scope = f.Card.Host.Services.CreateAsyncScope())
        {
            var targetId = (await f.EndpointAsync()).TargetId;
            local = await scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupService>()
                .TryCleanupLocalAsync(targetId, null, CancellationToken.None);
        }
        var localDirectoryGone = !Directory.Exists(f.Card.Tree);
        localDirectoryGone.ShouldBeTrue(local.Residue);
        var held = await f.TryAsync();
        held.Complete.ShouldBeFalse(); held.Reason.ShouldBe(PhoneHomeProblemTypes.Unavailable);
        await RetryDebtAsync(f, PhoneHomeProblemTypes.Unavailable, intent: false);
        await f.ReconnectAsync(); f.Clock.Advance(MirrorRetryDelay);
        await f.RemovedAsync();
    }

    // case: real recipient removes and journals its receipt, the reply is lost at the server client,
    // then a fresh coordinator after runner reconnect; mutation (PC-82): send a new OperationId on
    // retry; red: distinctRemovalOperationIds.ShouldBe(1) fails.
    [Test] public async Task C1017_LostAckReusesOperation()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync();
        CompletedCardWorkspaceRemovalResponse? recipientReply = null;
        f.AfterRemoval = r =>
        {
            recipientReply = r; f.AfterRemoval = null;
            return new PhoneHomeTransportException(PhoneHomeProblemTypes.ConnectionClosedInFlight, "fixture: reply lost");
        };
        var lost = await f.TryAsync();
        lost.Complete.ShouldBeFalse(); lost.Reason.ShouldBe(PhoneHomeProblemTypes.ConnectionClosedInFlight);
        recipientReply.ShouldNotBeNull().Complete.ShouldBeTrue();
        Directory.Exists(f.Mirror).ShouldBeFalse("the recipient removed before the reply was lost");
        await RetryDebtAsync(f, PhoneHomeProblemTypes.ConnectionClosedInFlight, intent: true);
        await f.DisconnectAsync(); await f.ReconnectAsync(); f.Clock.Advance(MirrorRetryDelay);
        var retry = await f.TryAsync();
        var distinctRemovalOperationIds = f.SentOperationIds.Distinct().Count();
        distinctRemovalOperationIds.ShouldBe(1);
        retry.Complete.ShouldBeTrue(retry.Reason); retry.Receipt.ShouldBe(recipientReply!.Receipt);
        f.Journal.Read()!.Single().Command.OperationId.ShouldBe(f.SentOperationIds[0]);
        var row = await f.EndpointAsync();
        row.State.ShouldBe(CardWorktreeCleanupEndpointState.Complete); row.Retryable.ShouldBeFalse(); row.NextAttemptAt.ShouldBeNull();
    }

    // Six vectors: the tested preparation, inspection or removal crosses the socket while an earlier
    // actual WorkspaceMirror holds the runner MutationLock (busy), or with no holder (ready).
    // mutation (PC-285): drop guarded work queued while the turn is busy;
    // red: busyRecipientDirectoryGone.ShouldBeTrue() fails.
    [Test] public async Task C1032_RecoveryQueuesBusyAndReady()
    {
        foreach (var vector in new[] { "preparation-busy", "preparation-ready", "inspection-busy", "inspection-ready", "removal-busy", "removal-ready" })
        {
            var stage = vector[..vector.IndexOf('-')]; var busy = vector.EndsWith("-busy", StringComparison.Ordinal);
            await using var f = await CompletedCardMirrorFixture.CreateAsync(stage != "preparation" ? MirrorPreparation.Direct
                : busy ? MirrorPreparation.Busy : MirrorPreparation.Ready);
            await f.DiscoverAsync();
            Task<CompletedCardWorkspaceRemovalResponse> pending;
            Task? holder = null;
            if (busy && stage == "inspection")
            {
                holder = await f.HoldMutationTurnAsync();
                pending = f.TryAsync();
                await f.ReleaseWhenQueuedAsync(holder, pending);
            }
            else if (busy && stage == "removal")
            {
                var held = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                f.AfterIntent = async _ => held.SetResult(await f.HoldMutationTurnAsync());
                pending = f.TryAsync();
                holder = await held.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await f.ReleaseWhenQueuedAsync(holder, pending);
            }
            else pending = f.TryAsync();
            var result = await pending;
            var busyRecipientDirectoryGone = !Directory.Exists(f.Mirror) && !Directory.Exists(f.Binding.GitDirectory);
            busyRecipientDirectoryGone.ShouldBeTrue(vector + ": " + result.Reason);
            result.Complete.ShouldBeTrue(vector);
            f.InspectionRequests.ShouldBe(1, vector); f.SentOperationIds.Count.ShouldBe(1, vector);
            var row = await f.EndpointAsync();
            row.State.ShouldBe(CardWorktreeCleanupEndpointState.Complete, vector);
            JsonSerializer.Deserialize<CompletedCardWorkspaceReceipt>(row.ImportedReceipt!, PhoneHomeFraming.Json)
                .ShouldBe(f.Journal.Read()!.Single().Receipt, vector);
        }
    }

    // Server-side cuts, each with a hand-written expected outcome; table in the S8 evidence doc.
    // I0 inspection loss and D1 pre-send loss keep retryable debt; a fresh coordinator before
    // NextAttemptAt sends nothing (PC-289 callsBeforeDue), then the due retry reaches the recipient
    // with the original operation (PC-286). D9 server save failure after a real receipt keeps the
    // operation (PC-287). D11-frozen replays original bytes after the task changes (PC-511).
    // D11-store: a replacement store on reconnect never completes the old endpoint (PC-288).
    [Test] public async Task C1032_RecoveryCrashHandoffs()
    {
        foreach (var cut in new[] { "I0", "D1", "D9", "D11-frozen", "D11-store" })
        {
            await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
            if (cut == "I0")
            {
                f.BeforeInspection = _ => { f.BeforeInspection = null;
                    throw new PhoneHomeTransportException(PhoneHomeProblemTypes.RequestTimeout, "fixture: inspection reply lost"); };
                (await f.TryAsync()).Reason.ShouldBe(PhoneHomeProblemTypes.RequestTimeout, cut);
                await RetryDebtAsync(f, PhoneHomeProblemTypes.RequestTimeout, intent: false);
            }
            else if (cut == "D9")
            {
                f.FailReceiptImport = true;
                await Should.ThrowAsync<IOException>(() => f.TryAsync());
                f.FailReceiptImport = false;
                var cutRow = await f.EndpointAsync();
                cutRow.State.ShouldBe(CardWorktreeCleanupEndpointState.IntentRecorded, cut); cutRow.ImportedReceipt.ShouldBeNull(cut);
                Directory.Exists(f.Mirror).ShouldBeFalse(cut);
                var originalOperationId = cutRow.OperationId.ShouldNotBeNull();
                await f.RemovedAsync();
                var postRestartOperationId = (await f.EndpointAsync()).OperationId;
                postRestartOperationId.ShouldBe(originalOperationId);
                f.SentRequests.Distinct().Count().ShouldBe(1, cut);
                continue;
            }
            else
            {
                f.BeforeRemoval = (_, _) => { f.BeforeRemoval = null;
                    throw new PhoneHomeTransportException(PhoneHomeProblemTypes.RequestLimit, "fixture: not sent"); };
                (await f.TryAsync()).Reason.ShouldBe(PhoneHomeProblemTypes.RequestLimit, cut);
                await RetryDebtAsync(f, PhoneHomeProblemTypes.RequestLimit, intent: true);
                f.Journal.Read()!.Count.ShouldBe(0, cut);
            }
            var inspected = f.InspectionRequests; var removals = f.RemovalRequests;
            f.Clock.Advance(MirrorRetryDelay - TimeSpan.FromSeconds(1));
            var early = await f.TryAsync();
            var callsBeforeDue = f.InspectionRequests - inspected + f.RemovalRequests - removals;
            callsBeforeDue.ShouldBe(0, cut);
            early.Complete.ShouldBeFalse(cut); early.Reason.ShouldBe("retry_not_due", cut);
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            if (cut == "D11-frozen")
                await f.Card.ChangeTaskAsync(t => { t.WorktreePath = f.Card.Tree + "-moved"; t.Result = "changed report\n"; });
            if (cut == "D11-store")
            {
                var original = (await f.EndpointAsync()).OperationId;
                await f.DisconnectAsync(); await f.ReconnectAsync(Guid.NewGuid());
                var refused = await f.TryAsync();
                refused.Complete.ShouldBeFalse(cut); refused.Reason.ShouldBe("runner_store_mismatch", cut);
                var oldEndpointComplete = (await f.EndpointAsync()).State == CardWorktreeCleanupEndpointState.Complete;
                oldEndpointComplete.ShouldBeFalse();
                (await f.EndpointAsync()).OperationId.ShouldBe(original, cut);
                f.RemovalRequests.ShouldBe(removals, cut);
                Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(cut);
                (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", cut);
                f.Journal.Read()!.Count.ShouldBe(0, cut);
                continue;
            }
            await f.RemovedAsync();
            var recoveredRecipientReceiptExists = f.Journal.Read()!.Single().Receipt is not null;
            recoveredRecipientReceiptExists.ShouldBeTrue(cut);
            if (cut != "I0")
            {
                f.SentOperationIds.Distinct().Count().ShouldBe(1, cut);
                var replayedRequestBytes = f.SentRequests[^1];
                replayedRequestBytes.ShouldBe(f.SentRequests[0], cut);
            }
        }
    }

    // case: real Program with WorktreeResidue:Execute=true, then the existing residue sweep over a
    // populated eligible mirror fixture with Execute=true; mutations (PC-290/291/292): register the
    // coordinator, call it from the sweep, or map a cleanup route; red: the named oracle below.
    // The explicit invocation that follows proves the fixture was eligible.
    [Test] public async Task C1032_RecoveryInertServerComposition()
    {
        await using (var factory = new AntiphonWebAppFactory())
        await using (var program = factory.WithWebHostBuilder(b => b.UseSetting("WorktreeResidue:Execute", "true")))
        {
            await using var scope = program.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IOptions<WorktreeResidueSettings>>().Value.Execute.ShouldBeTrue();
            var productionCoordinatorResolved = scope.ServiceProvider.GetService<CardWorktreeMirrorCleanupService>() is not null;
            productionCoordinatorResolved.ShouldBeFalse();
            scope.ServiceProvider.GetService<ICompletedCardWorkspaceClient>().ShouldBeNull();
            var routes = program.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
                .Select(e => e.RoutePattern.RawText ?? "").ToList();
            routes.ShouldNotBeEmpty();
            var publicGuardedCleanupRoutes = routes.Where(r => r.Contains("completed-card", StringComparison.OrdinalIgnoreCase)
                || r.Contains("mirror", StringComparison.OrdinalIgnoreCase) && r.Contains("cleanup", StringComparison.OrdinalIgnoreCase)).ToList();
            publicGuardedCleanupRoutes.ShouldBeEmpty();
        }
        await using var f = await CompletedCardMirrorFixture.CreateAsync(); await f.DiscoverAsync();
        await using (var scope = f.Card.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new WorktreeResidueSweepService(db, scope.ServiceProvider.GetRequiredService<IWorktreeManager>(),
                Options.Create(f.Card.Residue), Options.Create(new GitSettings { WorktreeBasePath = Path.Combine(f.Card.Host.Fixture.Root, "trees") }),
                f.Card.Host.Clock, NullLogger<WorktreeResidueSweepService>.Instance,
                scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()).RunAsync(CancellationToken.None);
        }
        f.Card.Residue.Execute.ShouldBeTrue();
        var row = await f.EndpointAsync();
        var automaticMirrorCommands = f.Journal.Read()!.Count + (row.OperationId is null ? 0 : 1) + f.RemovalRequests;
        automaticMirrorCommands.ShouldBe(0);
        Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(); (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n");
        await f.RemovedAsync();
    }

    private static async Task RetryDebtAsync(CompletedCardMirrorFixture f, string reason, bool intent)
    {
        var row = await f.EndpointAsync();
        row.State.ShouldNotBe(CardWorktreeCleanupEndpointState.Complete, reason); row.ImportedReceipt.ShouldBeNull(reason);
        row.Reason.ShouldBe(reason); row.Retryable.ShouldBeTrue(reason);
        row.NextAttemptAt.ShouldBe(f.Clock.GetUtcNow().UtcDateTime + MirrorRetryDelay, reason);
        (row.OperationId is not null).ShouldBe(intent, reason); (row.ImmutableRequest is not null).ShouldBe(intent, reason);
        if (!intent) { Directory.Exists(f.Binding.GitDirectory).ShouldBeTrue(reason); (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", reason); }
    }
}
