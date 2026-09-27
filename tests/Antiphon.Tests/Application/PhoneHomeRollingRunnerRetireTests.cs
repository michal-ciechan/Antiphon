using System.Net;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Security;
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

public sealed partial class PhoneHomeRollingRunnerTests
{
    [Test]
    public async Task Forced_retire_requires_the_operator_token_and_the_runner_id_confirmation()
    {
        await using var world = await RollingWorld.StartAsync();
        var id = RollingRunnerSettings.Server2Temp;
        (await PostRetireAsync(world, id, "upgrade", id, token: false, proxied: true))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostRetireAsync(world, id, "upgrade", RollingRunnerSettings.Server2, token: true))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostRetireAsync(world, id, "upgrade", id, token: true))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        world.PeerB.Retires.ShouldBeEmpty();
    }

    [Test]
    public async Task Forced_retire_fails_bound_sessions_writes_an_incident_and_sends_a_forced_retire()
    {
        await using var world = await RollingWorld.StartAsync();
        await world.SeedSessionAsync(SessionStatus.Running, RollingRunnerSettings.Server2Temp);
        (await PostDrainAsync(world, RollingRunnerSettings.Server2Temp,
            new DrainBody("retire temp", RollingRunnerSettings.Server2, true), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostRetireAsync(world, RollingRunnerSettings.Server2Temp, "operator upgrade",
            RollingRunnerSettings.Server2Temp, token: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        world.PeerB.Retires.Single().Force.ShouldBeTrue();
        await using (var db = world.NewDb())
        {
            var row = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2Temp);
            row.Status.ShouldBe(SessionStatus.Failed);
            row.FailureReason.ShouldContain("server2-temp");
            row.FailureReason.ShouldContain("operator upgrade");
            row.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
            (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.RunnerForceRetired)).ShouldBe(1);
            var state = await db.SessionRunnerStates.SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2Temp);
            state.RetireReason.ShouldStartWith("forced:");
            state.RetiredAt.ShouldNotBeNull();
        }

        await using var disconnected = await RollingWorld.StartAsync();
        await disconnected.SeedSessionAsync(SessionStatus.Running, RollingRunnerSettings.Server2Temp);
        (await PostDrainAsync(disconnected, RollingRunnerSettings.Server2Temp,
            new DrainBody("retire temp", RollingRunnerSettings.Server2, true), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        disconnected.PeerB.Socket.Abort();
        (await PostRetireAsync(disconnected, RollingRunnerSettings.Server2Temp, "lost runner",
            RollingRunnerSettings.Server2Temp, token: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        disconnected.PeerB.Retires.ShouldBeEmpty();
        await using var disconnectedDb = disconnected.NewDb();
        (await disconnectedDb.AgentSessions.SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2Temp))
            .Status.ShouldBe(SessionStatus.Failed);
    }

    [Test]
    public async Task Forced_retire_of_a_runner_that_is_not_draining_is_refused()
    {
        await using var world = await RollingWorld.StartAsync();
        var id = RollingRunnerSettings.Server2Temp;
        var response = await PostRetireAsync(world, id, "must drain", id, token: true);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        world.PeerB.Retires.ShouldBeEmpty();
        await using var db = world.NewDb();
        (await db.SessionRunnerStates.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Rolling_upgrade_moves_new_launches_to_server2_temp_keeps_server2_reachable_and_retires_server2_temp_when_idle()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var world = await RollingWorld.StartAsync(new RollingOptions
        {
            Clock = clock,
            ConfigureSettings = settings => settings.LeaseSeconds = 3600,
        });
        (await PostDrainAsync(world, RollingRunnerSettings.Server2,
            new DrainBody("upgrade old", RollingRunnerSettings.Server2Temp), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await world.CreateDefaultTaskAsync();
        await world.StampDesktopWorktreeAsync(created.Id);
        for (var tick = 0; tick < 5 && world.PeerB.Launches.Count == 0; tick++)
        {
            await world.TickAsync();
            await world.WaitForLaunchesAsync(TimeSpan.FromSeconds(3));
        }
        world.PeerB.Launches.Count.ShouldBe(1);
        world.PeerA.Launches.ShouldBeEmpty();
        await world.Queue.EnqueueAsync(world.SessionA, "still on old", MessageSendMode.WhenIdle, CancellationToken.None);
        world.PeerA.Inputs.ShouldContain(frame => InputText(frame) == "still on old");
        world.PeerB.Inputs.ShouldNotContain(frame => InputText(frame) == "still on old");

        (await PostClearAsync(world, RollingRunnerSettings.Server2, token: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostDrainAsync(world, RollingRunnerSettings.Server2Temp,
            new DrainBody("retire temp", RollingRunnerSettings.Server2, true), token: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var db = world.NewDb())
            await db.AgentSessions.Where(s => s.RunnerId == RollingRunnerSettings.Server2Temp)
                .ExecuteUpdateAsync(s => s.SetProperty(row => row.Status, SessionStatus.Stopped));

        var job = new RunnerRetireJob(world.Host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            world.RunnerDirectory, Options.Create(world.Configured), clock, NullLogger<RunnerRetireJob>.Instance);
        clock.Advance(TimeSpan.FromSeconds(60));
        await job.RunAsync(CancellationToken.None);
        world.PeerB.Retires.ShouldBeEmpty();
        clock.Advance(TimeSpan.FromSeconds(120));
        await job.RunAsync(CancellationToken.None);
        world.PeerB.Retires.Single().Force.ShouldBeFalse();
        var retired = await ReadStatusAsync(world, RollingRunnerSettings.Server2Temp);
        retired.GetProperty("retiredAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
        var old = await ReadStatusAsync(world, RollingRunnerSettings.Server2);
        old.GetProperty("acceptingNewWork").GetBoolean().ShouldBeTrue();
    }

    private static Task<HttpResponseMessage> PostRetireAsync(
        RollingWorld world, string id, string reason, string confirmRunnerId, bool token, bool proxied = false) =>
        world.Host.PostOperatorAsync($"/api/session-runners/{id}/retire",
            new { reason, confirmRunnerId },
            token ? OperatorTokenFile.ReadOrCreate(world.Host.OperatorTokenPath) : null, proxied);
}
