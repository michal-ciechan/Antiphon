using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class VerificationCleanupServiceTests
{
    [Test]
    public Task C2083_Late_host_exit_is_recovered_without_another_cleanup() => VerifyCustodyRetryAsync(hostExits: true);

    [Test]
    public Task C2083_Live_host_keeps_one_intent_until_operator_close() => VerifyCustodyRetryAsync(hostExits: false);

    private static async Task VerifyCustodyRetryAsync(bool hostExits)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var db = world.Host.CreateContext();
        await using var runner = await PhoneHomeTestHost.StartAsync(connectionString: db.Database.GetConnectionString());
        var reservationRunner = (FakeSessionRunnerClient)world.Runner;
        reservationRunner.VerificationStoreId = runner.StoreId;
        reservationRunner.VerificationCustodyBackend = VerificationCustodyBackends.LinuxCgroup;
        var binding = await world.ReserveAsync();
        await world.TerminalAsync(binding.Generation.SessionId);
        await BindToRemoteRunnerAsync(world, runner.AllowedRunnerId);
        if (hostExits)
        {
            await using var oldPeer = await runner.ConnectPeerAsync(platform: "linux");
            runner.Directory.MarkRecovered(await runner.WaitLiveAsync());
            var unsupported = await runner.Directory.Resolve(runner.AllowedRunnerId)
                .RecoverDeadVerificationCustodyAsync(binding, default);
            unsupported.State.ShouldBe(VerificationCustodyState.UnsupportedBackend);
            oldPeer.RequestCount(PhoneHomeOperation.ReadCustody).ShouldBe(0);
        }

        var capabilities = (await reservationRunner.GetCapabilitiesAsync(default)).ShouldNotBeNull();
        capabilities = capabilities with { Platform = "linux",
            Features = [.. capabilities.Features ?? [], RunnerCapabilityFeatures.VerificationDeadHostRecoveryV1] };
        await using var peer = await runner.ConnectPeerAsync(capabilities: capabilities);
        runner.Directory.MarkRecovered(await runner.WaitLiveAsync());
        var custodyHost = new VerificationHostIdentity(runner.StoreId, Guid.NewGuid(), Guid.NewGuid(), 123, DateTime.UtcNow);
        const string hostPresent = "custody_host_still_present";
        var currentReason = hostPresent;
        var responseBinding = binding;
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.ReadCustody
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new VerificationCustodyStatus(responseBinding,
                    VerificationCustodyState.Unknown, currentReason, custodyHost), PhoneHomeFraming.Json))
            : null;

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var workspace = new RecordingWorkspaceDirectory();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var cleanup = new VerificationCleanupService(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            runner.Directory, world.Host.Services.GetRequiredService<IRepositoryMutationLease>(),
            world.Host.Services.GetRequiredService<IWorktreeManager>(), clock, workspace);
        (await cleanup.CleanupAsync(world.TaskId, default)).Residue.ShouldContain(hostPresent);
        (await ReconcileAsync()).ShouldBeEmpty();
        AssertCustodyCalls(1);

        var intent = (await IntentsAsync()).ShouldHaveSingleItem();
        intent.FailureReason.ShouldStartWith("pending:custody-release:");
        clock.Advance(TimeSpan.FromMinutes(2));
        // A startup, cron or reconnect pass may race another pass. They share one claim.
        foreach (var outcome in await Task.WhenAll(ReconcileAsync(), ReconcileAsync()))
            outcome.ShouldBeEmpty();
        (await ReconcileAsync()).ShouldBeEmpty();
        AssertCustodyCalls(2);

        if (hostExits)
        {
            currentReason = "descendants_remain";
            clock.Advance(TimeSpan.FromMinutes(2));
            (await ReconcileAsync()).ShouldBeEmpty();

            currentReason = "released_unknown";
            responseBinding = binding with { ExecutionId = Guid.NewGuid() };
            clock.Advance(TimeSpan.FromMinutes(2));
            (await ReconcileAsync()).ShouldBeEmpty();

            responseBinding = binding;
            clock.Advance(TimeSpan.FromMinutes(2));
            (await ReconcileAsync())
                .ShouldBe([binding.Generation.SessionId], "reconciliation must finish the withheld release after the host exits");
            (await RunnerSlotService.IntentOutcomesAsync(db, [intent.Id], default)).ShouldHaveSingleItem()
                .Outcome.ShouldBe("released");
            clock.Advance(TimeSpan.FromMinutes(2));
            (await ReconcileAsync()).ShouldBeEmpty();
            AssertCustodyCalls(5);
            var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
            execution.ReceiptBytes.ShouldBeNull();
            workspace.RemoveCalls.ShouldBeEmpty();
            return;
        }

        (await cleanup.CleanupAsync(world.TaskId, default)).Residue.ShouldContain(hostPresent);
        clock.Advance(TimeSpan.FromMinutes(2));
        (await ReconcileAsync()).ShouldBeEmpty();
        var attention = AttentionServiceTests.BuildService(runner.Local, timeProvider: clock, db: db);
        (await StopAttentionAsync()).ShouldBeEmpty();
        clock.Advance(TimeSpan.FromMinutes(2));
        (await ReconcileAsync()).ShouldBeEmpty();
        AssertCustodyCalls(5, 2);

        var pending = (await IntentsAsync()).ShouldHaveSingleItem();
        pending.Id.ShouldBe(intent.Id);
        pending.CreatedAt.ShouldBe(intent.CreatedAt);
        pending.FailureReason.ShouldStartWith("pending:custody-release:");
        var item = (await StopAttentionAsync()).ShouldHaveSingleItem();
        item.Evidence.ShouldContain(hostPresent);
        const string intentsPath = "/api/session-runners/slots/release-intents";
        var closePath = $"{intentsPath}/{intent.Id:D}/close";
        item.Evidence.ShouldContain(closePath);
        var operatorToken = OperatorTokenFile.ReadOrCreate(runner.OperatorTokenPath);
        using var list = new HttpRequestMessage(HttpMethod.Get,
            $"{intentsPath}?runnerId={runner.AllowedRunnerId}&sessionId={binding.Generation.SessionId:D}");
        list.Headers.Add(OperatorTokenFile.Header, operatorToken);
        using var response = await runner.Http.SendAsync(list);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<RunnerSlotPendingIntentsPageDto>();
        page.ShouldNotBeNull().Intents.ShouldHaveSingleItem().IntentId.ShouldBe(intent.Id);

        using var closed = await runner.PostOperatorAsync(closePath,
            new { Requester = "fixture operator", Evidence = "Ownership transferred for manual recovery." }, operatorToken);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        clock.Advance(TimeSpan.FromMinutes(2));
        (await ReconcileAsync()).ShouldBeEmpty();
        AssertCustodyCalls(5, 2);
        (await StopAttentionAsync()).ShouldBeEmpty();
        (await cleanup.CleanupAsync(world.TaskId, default)).Residue.ShouldContain(hostPresent);
        (await IntentsAsync()).ShouldHaveSingleItem().FailureReason.ShouldStartWith("closed:");
        AssertCustodyCalls(6, 2, 5);

        async Task<IReadOnlyList<Guid>> ReconcileAsync()
        {
            await using var pass = world.Host.CreateContext();
            return await RunnerSlotService.ReconcilePendingReleasesAsync(runner.Directory, pass, default, clock);
        }

        Task<List<AgentIncident>> IntentsAsync() => db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == binding.Generation.SessionId && i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent)
            .ToListAsync();

        async Task<AttentionItemDto[]> StopAttentionAsync() => (await attention.GetAsync(default)).Items
            .Where(i => i.Kind == AttentionKind.SessionStopStuck && i.SessionId == binding.Generation.SessionId).ToArray();

        void AssertCustodyCalls(int count, params int[] additionalSeals)
        {
            var reads = peer.Incoming.Where(f => f.Kind == PhoneHomeFrameKind.Request
                && f.Operation == PhoneHomeOperation.ReadCustody)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeReadCustodyRequest>(PhoneHomeFraming.Json)!)
                .ToArray();
            reads.Length.ShouldBe(count);
            for (var index = 0; index < reads.Length; index++)
            {
                var read = reads[index];
                var explicitSeal = index == 0 || additionalSeals.Contains(index);
                read.Binding.ShouldBe(binding);
                read.Seal.ShouldBe(explicitSeal);
                read.DeadHostOnly.ShouldBe(!explicitSeal);
            }
            peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
            peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
        }
    }
}
