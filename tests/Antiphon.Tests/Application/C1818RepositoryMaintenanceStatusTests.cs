using System.Text.Json;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1818. A phone-home heartbeat that carries repository maintenance is the sample
/// GET /api/session-runners/{id}/status returns.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1818RepositoryMaintenanceStatusTests
{
    [Test]
    [Timeout(30_000)]
    public async Task C1818_heartbeat_maintenance_is_on_runner_status()
    {
        var at = DateTimeOffset.Parse("2026-11-10T03:04:05Z");
        var sample = new RunnerRepositoryMaintenanceStatus(at,
        [
            new RunnerRepositoryMaintenanceReport("/work/repos/antiphon", "ran", null, 3, 1, 0, 1),
        ]);
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        await peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, peer.Epoch, Guid.NewGuid(),
            Payload: JsonSerializer.SerializeToElement(
                new PhoneHomeCapacityHeartbeat(1, RepositoryMaintenance: sample), PhoneHomeFraming.Json)));
        await new PhoneHomeRunnerClient(host.Directory.SnapshotLive(host.AllowedRunnerId)!)
            .GetHealthAsync(CancellationToken.None);

        using var response = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/status");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("repositoryMaintenance");
        body.ShouldContain("\"outcome\":\"ran\"");
        body.ShouldContain("\"looseBefore\":3");
        var copied = host.Directory.Status(host.AllowedRunnerId).RepositoryMaintenance;
        copied.ShouldNotBeNull();
        var report = copied!.Repositories.ShouldHaveSingleItem();
        report.Outcome.ShouldBe("ran");
        report.LooseBefore.ShouldBe(3);
        report.Repository.ShouldBe("/work/repos/antiphon");
    }
}
