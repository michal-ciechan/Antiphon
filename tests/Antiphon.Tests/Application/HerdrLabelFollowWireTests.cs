using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelFollowWireTests
{
    [Test]
    public async Task Launch_and_get_round_trip_follow_metadata()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(false);
        var launch = f.Launches.Single(); var intent = launch.Herdr!.LabelFollowIntent!;
        intent.Version.ShouldBe(1); intent.StandingAgentId.ShouldBe(f.AgentId); intent.TabLabel.ShouldBe("Old"); intent.WorkspaceLabel.ShouldBeNull();
        intent.PlacementEditToken.ShouldBe((await f.ReadAsync()).HerdrPlacementEditToken);
        f.Tab.Label = "Renamed";
        var dto = await f.Client.GetAsync(f.SessionId, CancellationToken.None);
        dto.AcceptedStartedAt.ShouldBe(launch.AcceptedStartedAt);
        var observation = dto.LabelObservation.ShouldNotBeNull(); observation.Intent.ShouldBe(intent);
        observation.Sequence.ShouldBe(2); observation.SessionId.ShouldBe(f.SessionId); observation.AcceptedStartedAt.ShouldBe(launch.AcceptedStartedAt!.Value);
        observation.TabId.ShouldBe(f.Saved.TabId); observation.PaneId.ShouldBe(f.Saved.PaneId); observation.WorkspaceId.ShouldBe(f.Saved.WorkspaceId);
        observation.TabLabel.ShouldBe("Renamed"); observation.WorkspaceLabel.ShouldBeNull(); observation.PositivelyVerified.ShouldBeTrue();
    }

    [Test]
    public async Task Direct_and_http_get_refresh_and_map_the_same_follow_observation()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        var directLogs = Path.Combine(f.Root, "direct"); f.Saved.SaveAtomic(HerdrPaneSidecar.PathFor(directLogs, f.SessionId));
        await using var direct = new DirectSessionRunnerClient(directLogs, herdrClient: f.Herdr, labelClock: f.Clock) { KillOnDispose = false };
        await direct.AdoptOrphanedHostsAsync();
        var a = await f.Client.GetAsync(f.SessionId, CancellationToken.None);
        var b = await direct.GetAsync(f.SessionId, CancellationToken.None);
        a.LabelObservation.ShouldNotBeNull(); b.LabelObservation.ShouldBe(a.LabelObservation);
        a.LabelObservation!.TabLabel.ShouldBe("Renamed");
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(directLogs, f.SessionId))!.TabLabel.ShouldBe("Renamed");
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Old_peers_and_sidecars_remain_compatible_without_follow(bool unknownVersion)
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync();
        var dto = await f.Runtime.GetAsync(f.SessionId, CancellationToken.None);
        var json = JsonSerializer.SerializeToNode(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (unknownVersion) json["labelObservation"]!["version"] = 99; else json.AsObject().Remove("labelObservation");
        f.OverrideGetJson = json.ToJsonString();
        var old = await f.Client.GetAsync(f.SessionId, CancellationToken.None); old.Status.ShouldBe("Running"); old.LabelObservation.ShouldBeNull();
        var logs = Path.Combine(f.Root, "legacy"); (f.Saved with { LabelFollow = null }).SaveAtomic(HerdrPaneSidecar.PathFor(logs, f.SessionId));
        await using var direct = new DirectSessionRunnerClient(logs, herdrClient: f.Herdr) { KillOnDispose = false };
        await direct.AdoptOrphanedHostsAsync(); var reads = f.Methods.Count(m => m == "tab.get");
        var legacy = await direct.GetAsync(f.SessionId, CancellationToken.None); legacy.Status.ShouldBe("Running"); legacy.LabelObservation.ShouldBeNull();
        f.Methods.Count(m => m == "tab.get").ShouldBe(reads);
        var launch = JsonSerializer.SerializeToNode(f.Launches.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        launch["herdr"]!.AsObject().Remove("labelFollowIntent");
        launch.Deserialize<RunnerLaunchRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Herdr!.LabelFollowIntent.ShouldBeNull();
    }
}
