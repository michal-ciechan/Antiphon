using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
        await using var peer = await runner.ConnectPeerAsync(platform: "linux");
        runner.Directory.MarkRecovered(await runner.WaitLiveAsync());
        var custodyHost = new VerificationHostIdentity(runner.StoreId, Guid.NewGuid(), Guid.NewGuid(), 123, DateTime.UtcNow);
        const string hostPresent = "custody_host_still_present";
        var currentReason = hostPresent;
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.ReadCustody
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new VerificationCustodyStatus(binding,
                    VerificationCustodyState.Unknown, currentReason, custodyHost), PhoneHomeFraming.Json))
            : null;

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10));
        var workspace = new RecordingWorkspaceDirectory();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var cleanup = new VerificationCleanupService(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            runner.Directory, world.Host.Services.GetRequiredService<IRepositoryMutationLease>(),
            world.Host.Services.GetRequiredService<IWorktreeManager>(), clock, workspace);
        (await cleanup.CleanupAsync(world.TaskId, default)).Residue.ShouldContain(hostPresent);
        peer.RequestCount(PhoneHomeOperation.ReadCustody).ShouldBe(1);

        if (hostExits)
        {
            currentReason = "released_unknown";
            (await RunnerSlotService.ReconcilePendingReleasesAsync(runner.Directory, db, default))
                .ShouldBe([binding.Generation.SessionId], "reconciliation must finish the withheld release after the host exits");
            AssertCustodyCalls(2);
            var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
            execution.ReceiptBytes.ShouldBeNull();
            workspace.RemoveCalls.ShouldBeEmpty();
            return;
        }

        await RunnerSlotService.ReconcilePendingReleasesAsync(runner.Directory, db, default);
        var intent = (await db.AgentIncidents.AsNoTracking().Where(i => i.SessionId == binding.Generation.SessionId
            && i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent).ToListAsync()).ShouldHaveSingleItem();
        intent.FailureReason.ShouldStartWith("pending:");
        clock.Advance(TimeSpan.FromMinutes(16));
        var attention = new AttentionService(db, runner.Local, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), clock, NullLogger<AttentionService>.Instance);
        var item = (await attention.GetAsync(default)).Items
            .Where(i => i.Kind == AttentionKind.SessionStopStuck && i.SessionId == binding.Generation.SessionId)
            .ShouldHaveSingleItem();
        item.Evidence.ShouldContain(hostPresent);
        item.Evidence.ShouldContain($"/api/session-runners/slots/release-intents/{intent.Id:D}/close");

        void AssertCustodyCalls(int count)
        {
            var reads = peer.Incoming.Where(f => f.Kind == PhoneHomeFrameKind.Request
                && f.Operation == PhoneHomeOperation.ReadCustody)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeReadCustodyRequest>(PhoneHomeFraming.Json)!)
                .ToArray();
            reads.Length.ShouldBe(count);
            foreach (var read in reads)
            {
                read.Binding.ShouldBe(binding);
                read.Seal.ShouldBeTrue();
            }
            peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
            peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
        }
    }
}
