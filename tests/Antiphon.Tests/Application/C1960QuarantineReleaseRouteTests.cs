using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1960 W-A1..W-A3. <c>POST /api/session-runners/{runnerId}/workspace-slots/quarantine/{dirId}/release</c>
/// is the operator's way to send phone-home op 44 (<c>WorkspaceSlotQuarantineRelease</c>): the operator token
/// is required, every input the runner would refuse is refused before any frame is sent, and the runner's
/// answer (removed, refused, removal_failed) comes back as 200 with the runner's body. W-A3 drives a real
/// runner pool through the socket, so the evidence is the runner ledger and the disk, not the request alone.
/// </summary>
[Category("Integration")]
// CARD-1960 plan, performance budget (provisional): 2x the 17.2 s loaded p95 of the nearest measured relative,
// C0824_ReleaseRoundTripRealRunner (C0824WorkspaceSlotServerTests, CARD-1660 header). Re-derive from n>=5.
[Timeout(35_000)]
public sealed class C1960QuarantineReleaseRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);
    private const string DirId = "0123456789ab";
    private const string InvalidCode = "workspace_slot_quarantine_release_invalid";

    private static RunnerCapabilitiesDto SlotRunner() =>
        new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null);

    private static string Route(string runnerId, string dirId) =>
        "/api/session-runners/" + runnerId + "/workspace-slots/quarantine/" + dirId + "/release";

    private static void MapDirectory(IServiceCollection services) =>
        services.AddSingleton<ISessionRunnerDirectory>(provider => provider.GetRequiredService<PhoneHomeRunnerDirectory>());

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Route_forwards_release_and_returns_runner_answer()
    {
        // W-A1. No token: refused and nothing sent. A desktop runner id: 409 not phone-home, nothing sent. With the
        // token, one op-44 frame carries the dirId, the disposition and the reason with the requester folded in,
        // and the runner's refused or removed answer comes back as 200 with the runner's body.
        await using var host = await PhoneHomeTestHost.StartAsync(configureServices: MapDirectory);
        await using var peer = await host.ConnectPeerAsync(capabilities: SlotRunner());
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.SilentFor(PhoneHomeOperation.WorkspaceSlotQuarantineRelease);
        var route = Route(host.AllowedRunnerId, DirId);
        using (var refused = await host.PostOperatorAsync(route, new { disposition = "Pushed", reason = "no token" }, token: null))
            ((int)refused.StatusCode).ShouldBeInRange(400, 403);
        peer.RequestCount(PhoneHomeOperation.WorkspaceSlotQuarantineRelease).ShouldBe(0);
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        using (var local = await host.PostOperatorAsync(Route(PhoneHomeRunnerDirectory.LocalRunnerId, DirId),
                   new { disposition = "Pushed", reason = "desktop" }, token))
        {
            local.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await local.Content.ReadAsStringAsync()).ShouldContain("workspace_slot_runner_not_phone_home");
        }

        peer.RequestCount(PhoneHomeOperation.WorkspaceSlotQuarantineRelease).ShouldBe(0);
        var cases = new (object Body, string Disposition, string Reason, PhoneHomeWorkspaceSlotQuarantineReleaseResponse Answer)[]
        {
            (new { disposition = "Pushed", reason = "published", requester = "orchestrator" }, "Pushed",
                "published (by orchestrator)", new(DirId, "refused", "dirty: ?? work.txt")),
            (new { disposition = "AcceptLoss", reason = "diff kept" }, "AcceptLoss",
                "diff kept (by operator)", new(DirId, "removed")),
        };
        for (var i = 0; i < cases.Length; i++)
        {
            var post = host.PostOperatorAsync(route, cases[i].Body, token);
            var frame = await WaitForAsync(peer, PhoneHomeOperation.WorkspaceSlotQuarantineRelease, i + 1, post);
            var sent = frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceSlotQuarantineReleaseRequest>(PhoneHomeFraming.Json)!;
            sent.DirId.ShouldBe(DirId);
            sent.Disposition.ShouldBe(cases[i].Disposition);
            sent.Reason.ShouldBe(cases[i].Reason, "the requester rides in the reason the runner records");
            await peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(cases[i].Answer, PhoneHomeFraming.Json)));
            using var response = await post.WaitAsync(Bound);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, "a runner answer is an answer, not a transport failure");
            var body = (await response.Content.ReadFromJsonAsync<PhoneHomeWorkspaceSlotQuarantineReleaseResponse>(PhoneHomeFraming.Json))!;
            body.ShouldBe(cases[i].Answer);
        }

        peer.RequestCount(PhoneHomeOperation.WorkspaceSlotQuarantineRelease).ShouldBe(2);
        var logged = host.Logs.Entries.Where(entry => entry.Category == "WorkspaceSlotQuarantineRelease").ToList();
        logged.Count.ShouldBe(2);
        logged.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        host.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [Arguments("disposition-lowercase")]
    [Arguments("disposition-unknown")]
    [Arguments("disposition-empty")]
    [Arguments("dirid-traversal")]
    [Arguments("dirid-13-hex")]
    [Arguments("reason-201")]
    [Arguments("requester-token")]
    [Arguments("reason-token")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Route_refuses_invalid_input_before_sending(string shape)
    {
        // W-A2. Each shape is refused 400 before any frame is sent. The peer auto-replies, so a route that sent
        // first would answer 200 here rather than hang.
        await using var host = await PhoneHomeTestHost.StartAsync(configureServices: MapDirectory);
        await using var peer = await host.ConnectPeerAsync(capabilities: SlotRunner());
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        var dirId = shape switch
        {
            "dirid-traversal" => Uri.EscapeDataString("../x"),
            "dirid-13-hex" => DirId + "c",
            _ => DirId,
        };
        var disposition = shape switch
        {
            "disposition-lowercase" => "pushed",
            "disposition-unknown" => "Delete",
            "disposition-empty" => "",
            _ => "Pushed",
        };
        var reason = shape switch
        {
            "reason-201" => new string('r', 201),
            "reason-token" => "released with " + token,
            _ => "published",
        };
        var requester = shape == "requester-token" ? token : "orchestrator";
        using var response = await host.PostOperatorAsync(Route(host.AllowedRunnerId, dirId),
            new { disposition, reason, requester }, token);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        text.ShouldContain(InvalidCode);
        text.ShouldNotContain(token, Case.Insensitive);
        peer.RequestCount(PhoneHomeOperation.WorkspaceSlotQuarantineRelease).ShouldBe(0, "nothing reaches the runner");
        host.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task Route_releases_real_quarantine_end_to_end()
    {
        // W-A3. Server tick -> real runner lease; an untracked file makes the reconciler's release quarantine the
        // directory custody/dirty. Through the route, Pushed is refused by the runner (dirty) and the entry stays
        // open; AcceptLoss retains the file, removes the directory and closes the ledger entry.
        await using var world = await C0824SlotTransportWorld.StartAsync(configureHost: MapDirectory);
        var taskId = await world.SeedAsync();
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var leased = world.Pool.Ledger().Slots.Where(item => item.TaskId == taskId).ShouldHaveSingleItem("one ledger lease");
        var planted = "work " + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(leased.Path, "work.txt"), planted);
        await using (var db = world.Rig.NewDb())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, AgentTaskStatus.Succeeded).SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        using (var scope = world.Rig.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>().ReconcileAsync(CancellationToken.None).WaitAsync(Bound);
        var entry = world.Pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem("the dirty slot is quarantined");
        (entry.Class, entry.Reason, entry.IsOpen).ShouldBe(("custody", "dirty", true));
        Directory.Exists(entry.Path).ShouldBeTrue();

        var token = OperatorTokenFile.ReadOrCreate(world.Rig.Host.OperatorTokenPath);
        var route = Route(world.Rig.RunnerId, entry.DirId);
        using (var pushed = await world.Rig.Host.PostOperatorAsync(route,
                   new { disposition = "Pushed", reason = "published", requester = "orchestrator" }, token).WaitAsync(Bound))
        {
            pushed.StatusCode.ShouldBe(HttpStatusCode.OK);
            var answer = (await pushed.Content.ReadFromJsonAsync<PhoneHomeWorkspaceSlotQuarantineReleaseResponse>(PhoneHomeFraming.Json))!;
            answer.DirId.ShouldBe(entry.DirId);
            answer.Outcome.ShouldBe("refused");
            answer.Detail.ShouldNotBeNull().ShouldStartWith("dirty:");
        }

        world.Pool.Ledger().QuarantinedDirectories.Single(item => item.DirId == entry.DirId).IsOpen.ShouldBeTrue("a refusal closes nothing");
        Directory.Exists(entry.Path).ShouldBeTrue();
        using (var accepted = await world.Rig.Host.PostOperatorAsync(route,
                   new { disposition = "AcceptLoss", reason = "diff retained", requester = "orchestrator" }, token).WaitAsync(Bound))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
            var answer = (await accepted.Content.ReadFromJsonAsync<PhoneHomeWorkspaceSlotQuarantineReleaseResponse>(PhoneHomeFraming.Json))!;
            (answer.DirId, answer.Outcome).ShouldBe((entry.DirId, "removed"));
        }

        var closed = world.Pool.Ledger().QuarantinedDirectories.Single(item => item.DirId == entry.DirId);
        closed.ClosedAt.ShouldNotBeNull();
        closed.Disposition.ShouldBe("AcceptLoss");
        closed.Outcome.ShouldBe("removed: diff retained (by orchestrator)");
        Directory.Exists(entry.Path).ShouldBeFalse();
        File.ReadAllText(world.Pool.RetainedRoot + "/quarantine/" + entry.DirId + "/untracked/work.txt").ShouldBe(planted);
        world.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotQuarantineRelease).ShouldBe(2);
    }

    private static async Task<PhoneHomeFrame> WaitForAsync(PhoneHomeScriptedPeer peer, PhoneHomeOperation operation, int count, Task<HttpResponseMessage> post)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (peer.RequestCount(operation) >= count)
                return peer.Incoming.ToArray().Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == operation).ElementAt(count - 1);
            if (post.IsCompleted)
            {
                using var early = await post;
                throw new InvalidOperationException(
                    $"The route answered {(int)early.StatusCode} before sending {operation}: {await early.Content.ReadAsStringAsync()}");
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Expected {count} {operation} request(s); saw {peer.RequestCount(operation)}.");
    }
}
