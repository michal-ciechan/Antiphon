using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class DelegateScriptWorktreeBaseTests
{
    [Test]
    [Arguments("continue")]
    [Arguments("wait")]
    [Arguments("fresh")]
    [Arguments("unknown_fallback")]
    public async Task T0442_V15_initial_post_prints_the_service_base_preview(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v15");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A");
        if (scenario == "wait")
        {
            source.LandRequestedAt = DateTime.UtcNow;
            await AgentTaskDispatchBaseGuardTests.SeedPendingSiblingLandAsync(db, repo, source);
        }
        await db.SaveChangesAsync();
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        WorktreeListingFailureGit? fault = scenario == "unknown_fallback"
            ? new(repo.Path) : null;
        await RunArmAsync(repo, schema, card, source, sourceSha, scenario, fault, null);
        if (scenario == "unknown_fallback")
        {
            fault!.Calls.ShouldBeGreaterThan(0, "the worktree-listing fault must be reached");
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await RunArmAsync(repo, schema, card, source, sourceSha, "inspection_timeout",
                new DeadlineGit(clock), clock);
        }
    }

    private static async Task RunArmAsync(ScratchGitRepo repo, IsolatedTestSchema schema,
        Card card, AgentTask source, string sourceSha, string scenario, LandingGit? git,
        TimeProvider? clock)
    {
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot, git: git, clock: clock);
        using var relay = new ServiceRelay(provider, repo.Path);
        var args = new List<string>
        {
            "-Role", "Code", "-Goal", "continue the card", "-Card", card.Id.ToString("D"),
            "-Worktree",
        };
        if (scenario == "fresh") args.Add("-FreshWorktree");
        var run = await DelegateScriptRunner.RunAsync(relay.BaseUrl, null, repo.Path, args);
        run.ExitCode.ShouldBe(0, run.Output);
        relay.LastFailure.ShouldBeNull();
        relay.RequestCount.ShouldBe(1, "the preview must be in the initial create response");
        relay.LastPath.ShouldBe("/api/agent-tasks");
        run.Output.ShouldContain("NO REPLY WILL BE ROUTED");
        run.Output.ShouldContain("base preview:");
        switch (scenario)
        {
            case "continue":
                run.Output.ShouldContain(DelegationReportFormatter.Short(source.Id));
                run.Output.ShouldContain(source.WorktreeBranch!);
                run.Output.ShouldContain(sourceSha);
                run.Output.ShouldContain("new isolated branch");
                run.Output.ShouldContain("landing target master");
                break;
            case "wait":
                run.Output.ShouldContain("waiting for same-card land");
                break;
            case "fresh":
                run.Output.ShouldContain("Target at master");
                run.Output.ShouldContain(source.WorktreeBranch!);
                run.Output.ShouldContain("Fresh worktree omits");
                break;
            case "unknown_fallback":
                run.Output.ShouldContain("Target at master");
                run.Output.ShouldContain("unknown");
                run.Output.ShouldContain(source.WorktreeBranch!);
                break;
            case "inspection_timeout":
                run.Output.ShouldContain("inspection_timeout");
                run.Output.ShouldContain("Retry or select a base explicitly");
                break;
        }
        await using var verify = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var created = await verify.AgentTasks.AsNoTracking().SingleAsync(t =>
            t.Id == relay.CreatedTaskId);
        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.WorktreePath.ShouldBeNull();
        created.AgentSessionId.ShouldBeNull();
        created.WorktreeBasePreviewJson.ShouldNotBeNull();
    }

    private sealed class ServiceRelay : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _pump;
        private readonly ServiceProvider _provider;
        private readonly string _callerDirectory;

        public ServiceRelay(ServiceProvider provider, string callerDirectory)
        {
            _provider = provider;
            _callerDirectory = callerDirectory;
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string? LastPath { get; private set; }
        public int RequestCount { get; private set; }
        public Guid CreatedTaskId { get; private set; }
        public Exception? LastFailure { get; private set; }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                RequestCount++;
                LastPath = context.Request.Url?.AbsolutePath;
                try
                {
                    if (context.Request.HttpMethod != "POST" || LastPath != "/api/agent-tasks")
                        throw new InvalidOperationException("unexpected detail/event/poll request");
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var request = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
                        await reader.ReadToEndAsync(), Json)!;
                    await using var scope = _provider.CreateAsyncScope();
                    var created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                        .CreateAsync(request, new AgentTaskService.Caller(null, null, _callerDirectory),
                            CancellationToken.None);
                    CreatedTaskId = created.Id;
                    var payload = JsonSerializer.SerializeToUtf8Bytes(created, Json);
                    context.Response.StatusCode = 201;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(payload);
                }
                catch (Exception ex)
                {
                    LastFailure = ex;
                    context.Response.StatusCode = 500;
                    var problem = JsonSerializer.SerializeToUtf8Bytes(new { detail = ex.Message }, Json);
                    await context.Response.OutputStream.WriteAsync(problem);
                }
                finally { context.Response.Close(); }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            _stop.Dispose();
        }
    }

    private sealed class WorktreeListingFailureGit(string repositoryPath) : LandingGit
    {
        public int Calls { get; private set; }

        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args,
            CancellationToken ct)
        {
            if (args is ["worktree", "list", "--porcelain"] && repository == repositoryPath)
            {
                Calls++;
                return Task.FromResult(new LandingGitResult(128, "", "injected worktree listing failure"));
            }

            return base.RunAsync(repository, args, ct);
        }
    }

    private sealed class DeadlineGit(FakeTimeProvider clock) : LandingGit
    {
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args,
            CancellationToken ct)
        {
            if (args is ["rev-parse", "--path-format=absolute", "--git-common-dir"])
            {
                // CARD-1134: the provider leaves WorktreeBaseInspectionTimeoutSeconds at its default;
                // the extra tick crosses that configured deadline instead of a copied literal.
                clock.Advance(TimeSpan.FromSeconds(new GitSettings().WorktreeBaseInspectionTimeoutSeconds)
                    + TimeSpan.FromTicks(1));
                return Task.FromResult(new LandingGitResult(0, repository, ""));
            }
            return base.RunAsync(repository, args, ct);
        }
    }
}
