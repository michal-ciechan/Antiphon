using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PhoneHomeTaskDispatchProjectionTests
{
    [Test]
    [Arguments(AgentKind.Codex, AgentTaskRole.Code, "missing")]
    [Arguments(AgentKind.ClaudeCode, AgentTaskRole.Review, "missing")]
    [Arguments(AgentKind.Grok, AgentTaskRole.Code, "missing")]
    [Arguments(AgentKind.Codex, AgentTaskRole.Code, "cached")]
    [Arguments(AgentKind.Codex, AgentTaskRole.Review, "stale")]
    [Arguments(AgentKind.ClaudeCode, AgentTaskRole.Code, "relaunch")]
    public async Task C1285_worker_launch_has_durable_task_token(AgentKind kind, AgentTaskRole role, string scenario)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse("/work/worktrees/c1285-token")),
            PhoneHomeOperation.ProviderAuth => Result(frame,
                new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, kind);
        var (originalToken, originalHash) = AgentTaskService.NewToken();
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var task = await seed.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Role = role;
            task.TokenHash = originalHash;
            task.RemoteWorktreePath = "/work/worktrees/c1285-token";
            task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new ProgressBaselineSnapshot(
                1, DateTime.UtcNow, DateTime.UtcNow,
                new ProgressSourceBaseline(workspace.Path, workspace.Path, taskId, workspace.Path,
                    "refs/heads/" + task.WorktreeBranch, new string('1', 40),
                    new ProgressRemoteBaseline(ProgressRemoteState.Missing, null, "c1285")), null));
            await seed.SaveChangesAsync();
        }
        AgentTaskService.RawTokens[taskId] = scenario == "stale"
            ? AgentTaskService.NewToken().Token : originalToken;

        var committedAtEnqueue = new List<bool>();
        var sink = new RecordingLaunchSink
        {
            OnEnqueue = (sessionId, spec) =>
            {
                // Independent context at the external-effect boundary: a later save cannot pass.
                using var observe = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var stored = observe.AgentTasks.AsNoTracking().Single(t => t.Id == taskId);
                committedAtEnqueue.Add(spec.Env.TryGetValue("ANTIPHON_TASK_TOKEN", out var token)
                    && !string.IsNullOrWhiteSpace(token)
                    && stored.TokenHash == AgentTaskService.HashToken(token)
                    && stored.AgentSessionId == sessionId);
            },
        };
        // Seed the durable state after remote preparation. Only this task's raw cache is lost.
        if (scenario == "missing") AgentTaskService.RawTokens.TryRemove(taskId, out _);
        var graph = CreateDispatcher(schema, host, sink);
        await using var services = graph.Services;
        try
        {
            await graph.Dispatcher.TickAsync(CancellationToken.None);
            await using (var observe = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var stored = await observe.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                var events = await observe.AgentTaskEvents.Where(e => e.AgentTaskId == taskId)
                    .Select(e => e.Detail).ToListAsync();
                stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason + string.Join("; ", events));
                sink.Specs.Count.ShouldBe(1, string.Join("; ", events));
            }
            if (scenario == "relaunch")
            {
                var firstSession = Guid.Parse(sink.Specs[0].Env["ANTIPHON_SESSION_ID"]);
                await graph.Dispatcher.RelaunchWedgedAsync(taskId, firstSession, CancellationToken.None);
                sink.Specs.Count.ShouldBe(2);
            }
            committedAtEnqueue.Count.ShouldBe(sink.Specs.Count);
            committedAtEnqueue.All(value => value).ShouldBeTrue("every launch must carry a token whose hash is already committed");
            var launch = sink.Specs[^1];
            var token = launch.Env["ANTIPHON_TASK_TOKEN"];
            (token == originalToken).ShouldBe(scenario == "cached");
            launch.Env["ANTIPHON_TASK_ID"].ShouldBe(taskId.ToString("D"));

            await using var authScope = services.CreateAsyncScope();
            var service = authScope.ServiceProvider.GetRequiredService<AgentTaskService>();
            var caller = await service.AuthenticateAsync(token, CancellationToken.None);
            caller.Task.ShouldNotBeNull().Id.ShouldBe(taskId);
            caller.SessionId.ShouldBe(Guid.Parse(launch.Env["ANTIPHON_SESSION_ID"]));
            caller.MayDelegate.ShouldBeFalse("a replacement Worker token must not grant orchestrator authority");
            if (scenario != "cached")
                await Should.ThrowAsync<ForbiddenException>(() => service.AuthenticateAsync(originalToken, CancellationToken.None));

            // Exercise the real phone-home serialization as well as the server's launch sink.
            var sessionId = Guid.Parse(launch.Env["ANTIPHON_SESSION_ID"]);
            await new Antiphon.Server.Infrastructure.Agents.SessionRunner.PhoneHomeRunnerClient(live)
                .StartAsync(sessionId, launch, CancellationToken.None);
            var wire = peer.Launches.ShouldHaveSingleItem().Payload!.Value;
            (wire.GetProperty("env").GetProperty("ANTIPHON_TASK_TOKEN").GetString() == token).ShouldBeTrue();
            launch.Args.Any(arg => arg.Contains(token, StringComparison.Ordinal)).ShouldBeFalse();
        }
        finally
        {
            AgentTaskService.RawTokens.TryRemove(taskId, out _);
        }
    }
}
