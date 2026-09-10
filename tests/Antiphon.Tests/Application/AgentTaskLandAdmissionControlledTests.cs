using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandAdmissionControlledTests
{
    [Test]
    [Arguments("shared", "Fresh", "land-first")]
    [Arguments("shared", "AlreadyPresent", "land-first")]
    [Arguments("shared", "ResumePublication", "land-first")]
    [Arguments("shared", "CleanupRetry", "land-first")]
    [Arguments("follow-up", "Fresh", "land-first")]
    [Arguments("follow-up", "AlreadyPresent", "land-first")]
    [Arguments("follow-up", "ResumePublication", "land-first")]
    [Arguments("follow-up", "CleanupRetry", "land-first")]
    [Arguments("shared", "Fresh", "dispatch-first")]
    [Arguments("shared", "AlreadyPresent", "dispatch-first")]
    [Arguments("shared", "ResumePublication", "dispatch-first")]
    [Arguments("shared", "CleanupRetry", "dispatch-first")]
    [Arguments("follow-up", "Fresh", "dispatch-first")]
    [Arguments("follow-up", "AlreadyPresent", "dispatch-first")]
    [Arguments("follow-up", "ResumePublication", "dispatch-first")]
    [Arguments("follow-up", "CleanupRetry", "dispatch-first")]
    [Arguments("shared", "Fresh", "dispatch-before-acquire")]
    [Arguments("shared", "AlreadyPresent", "dispatch-before-acquire")]
    [Arguments("shared", "ResumePublication", "dispatch-before-acquire")]
    [Arguments("shared", "CleanupRetry", "dispatch-before-acquire")]
    [Arguments("follow-up", "Fresh", "dispatch-before-acquire")]
    [Arguments("follow-up", "AlreadyPresent", "dispatch-before-acquire")]
    [Arguments("follow-up", "ResumePublication", "dispatch-before-acquire")]
    [Arguments("follow-up", "CleanupRetry", "dispatch-before-acquire")]
    public async Task C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther(string writerKind, string mode, string order)
    {
        await using var h = new LandingProtocolHarness();
        var admissionLease = new InterceptedLease(new Antiphon.Server.Infrastructure.Git.RepositoryMutationLease(h.Fixture.Git));
        h.ConfigureServices = services =>
        {
            services.AddSingleton<IRepositoryMutationLease>(admissionLease);
            services.AddSingleton<IEventBus>(h.Events);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512, PoolEnabled = false }));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "claude";
                s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>(); // No worker consumes this fixture queue.
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
        };
        await h.InitializeAsync();
        h.Services.GetServices<IHostedService>().ShouldBeEmpty("this fixture never starts a launch worker or real Program");
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        }
        if (mode == "CleanupRetry")
        {
            var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "owned-admission-fixture.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "fixture-owned residue\n");
            await h.RunAsync();
            await h.RepostAsync();
            File.Delete(sentinel);
        }
        var writerId = Guid.NewGuid();
        int beforeAttempt;
        await using (var db = h.CreateContext())
        {
            beforeAttempt = await db.AgentTasks.Where(t => t.Id == h.Fixture.TaskId).Select(t => t.LandAttempt).SingleAsync();
            db.AgentTasks.Add(new AgentTask
            {
                Id = writerId, RootTaskId = writerId, Title = "C448 admission fixture", Goal = "Fixture claim only",
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                Workspace = writerKind == "shared" ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
                WorkingDirectory = writerKind == "shared" ? h.Fixture.Repository : h.Fixture.Source,
                RepoPath = h.Fixture.Repository, WorktreePath = writerKind == "shared" ? null : h.Fixture.Source,
                WorktreeBranch = writerKind == "shared" ? null : h.Fixture.SourceRef[11..],
                FollowUpOfTaskId = writerKind == "shared" ? null : h.Fixture.TaskId,
                ReplyTo = AgentTaskReplyTo.None, Status = AgentTaskStatus.Queued, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await using var scope = h.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        Task<LandRunResult>? waitingLand = null;
        var releaseAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (order == "dispatch-before-acquire")
            {
                var enteringLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                admissionLease.BeforeAcquire = async () =>
                {
                    admissionLease.BeforeAcquire = null;
                    enteringLease.TrySetResult();
                    await releaseAdmission.Task;
                };
                waitingLand = h.RunAsync();
                await enteringLease.Task.WaitAsync(TimeSpan.FromSeconds(60));
            }
            if (order == "land-first")
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var fired = false;
                h.Fixture.Git.BeforeCommand = async (_, args) =>
                {
                    if (!fired && args[0] == "show-ref") { fired = true; entered.TrySetResult(); await release.Task; }
                    return null;
                };
                var running = h.RunAsync();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
                    await dispatcher.TickAsync(CancellationToken.None);
                    await using var db = h.CreateContext();
                    var writer = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId);
                    writer.Status.ShouldBe(AgentTaskStatus.Queued, "no durable writer claim may appear while landing owns the repository lease");
                    writer.AgentId.ShouldBeNull();
                    (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == writerId && e.Type == AgentTaskEventType.Held)).ShouldBeTrue();
                    Directory.Exists(h.Fixture.Source).ShouldBeTrue();
                }
                finally { h.Fixture.Git.BeforeCommand = null; release.TrySetResult(); await running; }
            }
            else
            {
                await dispatcher.TickAsync(CancellationToken.None);
                await using (var db = h.CreateContext())
                    (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
                h.Fixture.Git.Trace.Clear();
                releaseAdmission.TrySetResult();
                (await (waitingLand ?? h.RunAsync())).ShouldBe(LandRunResult.Held, "landing must reload the real committed Shared/follow-up admission claim");
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
                await using (var db = h.CreateContext())
                {
                    var pending = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                    pending.LandAttempt.ShouldBe(beforeAttempt);
                    pending.LandRequestedAt.ShouldNotBeNull();
                    var writer = await db.AgentTasks.SingleAsync(t => t.Id == writerId);
                    writer.Status = AgentTaskStatus.Succeeded;
                    writer.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
                await h.RunAsync();
            }
        }
        finally
        {
            releaseAdmission.TrySetResult();
            if (waitingLand is not null) await waitingLand;
        }
        new AgentTaskLandingState().HasPublication((await h.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue();
        await using (var db = h.CreateContext())
            (await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).LandAttempt.ShouldBe(beforeAttempt + 1);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class InterceptedLease(IRepositoryMutationLease inner) : IRepositoryMutationLease
    {
        public Func<Task>? BeforeAcquire { get; set; }
        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            if (BeforeAcquire is not null) await BeforeAcquire();
            return await inner.TryAcquireAsync(repository, ct);
        }
        public bool Owns(RepositoryLease lease, string commonDirectory) => inner.Owns(lease, commonDirectory);
    }

}
