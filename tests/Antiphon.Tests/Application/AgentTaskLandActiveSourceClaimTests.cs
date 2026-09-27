using System.Diagnostics;
using System.Security.Cryptography;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandActiveSourceClaimTests
{
    [Test]
    [Arguments("Fresh", "canonical", "follow-up")]
    [Arguments("Fresh", "canonical", "merge-helper")]
    [Arguments("Fresh", "alias", "follow-up")]
    [Arguments("Fresh", "alias", "merge-helper")]
    [Arguments("AlreadyPresent", "canonical", "follow-up")]
    [Arguments("AlreadyPresent", "canonical", "merge-helper")]
    [Arguments("AlreadyPresent", "alias", "follow-up")]
    [Arguments("AlreadyPresent", "alias", "merge-helper")]
    [Arguments("ResumePublication", "canonical", "follow-up")]
    [Arguments("ResumePublication", "canonical", "merge-helper")]
    [Arguments("ResumePublication", "alias", "follow-up")]
    [Arguments("ResumePublication", "alias", "merge-helper")]
    [Arguments("CleanupRetry", "canonical", "follow-up")]
    [Arguments("CleanupRetry", "canonical", "merge-helper")]
    [Arguments("CleanupRetry", "alias", "follow-up")]
    [Arguments("CleanupRetry", "alias", "merge-helper")]
    public async Task C448_V29_ActiveSourceClaimHoldsEveryMode(string mode, string spelling, string claimant)
    {
        await using var h = new LandingSafetyHarness();
        ConfigureDispatcher(h);
        await h.InitializeAsync();
        h.Services.GetServices<IHostedService>().ShouldBeEmpty();
        var source = mode == "AlreadyPresent" ? h.Fixture.SeedSha : await h.AddSourceAsync();
        await h.RequestAsync(filter: "/*/*/Fixture/Pass", expectedSourceSha: source);
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.PushStarted;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
            h.Fault.AfterCommit = false;
        }
        if (mode == "CleanupRetry")
        {
            var residue = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(residue)!);
            await File.WriteAllTextAsync(residue, "owned receipt residue\n");
            await h.RunAsync();
            (await h.OperationAsync()).ShouldNotBeNull().Cleanup.ShouldBe(LandCleanupStatus.Refused);
            await h.RepostAsync();
            File.Delete(residue);
        }

        string? alias = null;
        var claimPath = h.Fixture.Source;
        if (spelling == "alias")
        {
            alias = Path.Combine(h.Fixture.Root, "alias trees");
            if (OperatingSystem.IsWindows())
                await MklinkJunctionAsync(alias, Path.Combine(h.Fixture.Root, "trees"));
            else Directory.CreateSymbolicLink(alias, Path.Combine(h.Fixture.Root, "trees"));
            claimPath = Path.Combine(alias, "source");
            (await h.Fixture.Git.CanonicalDirectoryAsync(claimPath, CancellationToken.None))
                .ShouldBe(await h.Fixture.Git.CanonicalDirectoryAsync(h.Fixture.Source, CancellationToken.None));
        }

        try
        {
            var before = await ReadSourceAsync(h);
            var canonicalSource = await h.Fixture.Git.CanonicalDirectoryAsync(h.Fixture.Source, CancellationToken.None);
            var common = await h.Fixture.Git.CommonDirectoryAsync(h.Fixture.Repository, CancellationToken.None);
            var opBefore = await h.OperationAsync();
            var verifierBefore = h.Verifier.Calls;
            var remoteBefore = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim();
            Guid requestId;
            int attempt;
            await using (var db = h.CreateContext())
            {
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
                task.Status.ShouldBe(AgentTaskStatus.Succeeded);
                requestId = task.CurrentLandRequestId.ShouldNotBeNull();
                attempt = task.LandAttempt;
            }
            var claimId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var reservationId = Guid.NewGuid();
            await using (var db = h.CreateContext())
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = claimId, RootTaskId = h.Fixture.TaskId, ParentTaskId = h.Fixture.TaskId,
                    FollowUpOfTaskId = claimant == "follow-up" ? h.Fixture.TaskId : null,
                    Title = "exact source claimant", Goal = "hold the fixture source", Kind = AgentTaskKind.Worker,
                    Role = claimant == "merge-helper" ? AgentTaskRole.Merge : AgentTaskRole.Code,
                    Workspace = claimant == "merge-helper" ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
                    RepoPath = h.Fixture.Repository, WorkingDirectory = claimPath,
                    WorktreePath = claimant == "merge-helper" ? null : claimPath,
                    WorktreeBranch = claimant == "merge-helper" ? null : h.Fixture.SourceRef[11..],
                    AgentSessionId = claimant == "merge-helper" ? sessionId : null,
                    ReplyTo = AgentTaskReplyTo.None, Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow,
                });
                if (claimant == "merge-helper")
                {
                    db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "merge", Status = SessionStatus.Running,
                        Cwd = claimPath, CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
                    db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId,
                        Sequence = 1, Kind = TranscriptKinds.UserPrompt, Text = "real merge helper prompt",
                        Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
                }
                db.WorkspaceUseReservations.Add(new WorkspaceUseReservation { Id = reservationId,
                    CanonicalPath = canonicalSource, CommonDirectory = common, SourceFullRef = h.Fixture.SourceRef,
                    TaskId = claimId, SessionId = claimant == "merge-helper" ? sessionId : null,
                    Kind = WorkspaceReservationKind.Launch, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            await using (var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(h.Fixture.Source, CancellationToken.None))
                lease.ShouldNotBeNull("a live DB source claim must be the hold, not a test-held native lease");
            h.Fixture.Git.Trace.Clear();
            (await h.RunAsync()).ShouldBe(LandRunResult.Held);
            ((await h.OperationAsync())?.Id).ShouldBe(opBefore?.Id);
            ((await h.OperationAsync())?.Phase).ShouldBe(opBefore?.Phase);
            h.Verifier.Calls.ShouldBe(verifierBefore);
            await using (var db = h.CreateContext())
            {
                var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId);
                task.LandAttempt.ShouldBe(attempt);
                task.CurrentLandRequestId.ShouldBe(requestId);
                var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
                request.State.ShouldBe(LandRequestState.Held);
                request.HoldReasonCode.ShouldBe("repository_or_source_writer");
                request.HoldingTaskId.ShouldBe(claimId);
            }
            (await ReadSourceAsync(h)).ShouldBe(before);
            Directory.Exists(h.Fixture.Source).ShouldBeTrue();
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(remoteBefore);
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase") || a.Contains("remove"));
            await h.Fixture.AssertRemoteSourceAsync();

            await using (var other = new LandingSafetyHarness())
            {
                await other.InitializeAsync();
                await other.AddSourceAsync();
                (await other.RunAsync()).ShouldBe(LandRunResult.Complete);
                (await other.OperationAsync()).ShouldNotBeNull().RemoteConfirmedAt.ShouldNotBeNull();
            }

            await using (var db = h.CreateContext())
            {
                var claim = await db.AgentTasks.SingleAsync(t => t.Id == claimId);
                claim.Status = AgentTaskStatus.Canceled;
                claim.CompletedAt = DateTime.UtcNow;
                if (claimant == "merge-helper")
                {
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
                    session.Status = SessionStatus.Stopped;
                    session.EndedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
            }
            await using (var scope = h.Services.CreateAsyncScope())
            {
                var reservations = scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>();
                await reservations.ReleaseTaskConsumersAsync(claimId, CancellationToken.None);
                if (claimant == "merge-helper")
                    await reservations.ReleaseSessionConsumersAsync(sessionId, CancellationToken.None);
            }
            await using (var db = h.CreateContext())
                (await db.WorkspaceUseReservations.AsNoTracking().SingleAsync(r => r.Id == reservationId))
                    .Active.ShouldBeFalse();
            await h.RestartServicesAsync();
            (await ReadSourceAsync(h)).ShouldBe(before);
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(remoteBefore);
            h.Fixture.Git.Trace.Clear();
            (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
            var after = (await h.OperationAsync()).ShouldNotBeNull();
            after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            if (mode == "ResumePublication") h.Verifier.Calls.ShouldBe(1);
            if (mode == "CleanupRetry")
            {
                after.RemoteConfirmedAt.ShouldBe(opBefore!.RemoteConfirmedAt);
                h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
            }
            if (mode == "Fresh") h.Verifier.Calls.ShouldBe(1);
            await using (var db = h.CreateContext())
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId)).LandAttempt.ShouldBe(attempt + 1);
            await h.Fixture.AssertRemoteSourceAsync();
        }
        finally
        {
            if (alias is not null) Directory.Delete(alias, recursive: false);
        }

        await AssertLandFirstAdmissionAsync(claimant);
    }

    private static void ConfigureDispatcher(LandingSafetyHarness h)
    {
        h.ConfigureServices = services =>
        {
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
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            services.AddScoped<DispatchBaseWarningIntentService>();
        };
    }

    private static async Task AssertLandFirstAdmissionAsync(string claimant)
    {
        await using var h = new LandingSafetyHarness();
        ConfigureDispatcher(h);
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var queuedId = Guid.NewGuid();
        await using (var db = h.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask { Id = queuedId, RootTaskId = queuedId,
                FollowUpOfTaskId = claimant == "follow-up" ? h.Fixture.TaskId : null,
                Title = "queued exact source", Goal = "fixture admission", Kind = AgentTaskKind.Worker,
                Role = claimant == "merge-helper" ? AgentTaskRole.Merge : AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                Workspace = claimant == "merge-helper" ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
                WorkingDirectory = h.Fixture.Source, RepoPath = h.Fixture.Repository,
                WorktreePath = claimant == "merge-helper" ? null : h.Fixture.Source,
                WorktreeBranch = claimant == "merge-helper" ? null : h.Fixture.SourceRef[11..],
                ReplyTo = AgentTaskReplyTo.None, Status = AgentTaskStatus.Queued, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
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
            await using var scope = h.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await using var db = h.CreateContext();
            var queued = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queuedId);
            queued.Status.ShouldBe(AgentTaskStatus.Queued);
            queued.AgentId.ShouldBeNull();
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == queuedId && e.Type == AgentTaskEventType.Held)).ShouldBeTrue();
            queued.AgentSessionId.ShouldBeNull();
        }
        finally
        {
            h.Fixture.Git.BeforeCommand = null;
            release.TrySetResult();
            await running;
        }
    }

    private static async Task<(string Head, string Status, string Registration, string IndexHash)> ReadSourceAsync(LandingSafetyHarness h)
    {
        var head = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var status = await h.Fixture.RequiredAsync(h.Fixture.Source, "status", "--porcelain=v1", "--untracked-files=all");
        var registration = await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "list", "--porcelain");
        var admin = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "--absolute-git-dir")).Trim();
        var index = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(admin, "index"))));
        return (head, status, registration, index);
    }

    private static async Task MklinkJunctionAsync(string alias, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", alias, target }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await child.WaitForExitAsync(budget.Token);
        child.ExitCode.ShouldBe(0, await error + await output);
    }
}
