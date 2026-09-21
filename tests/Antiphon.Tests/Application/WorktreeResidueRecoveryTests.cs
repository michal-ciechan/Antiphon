using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeResidueRecoveryTests
{
    [Test]
    public async Task C459_SpentSlotSurvivesRestart()
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        var body = ReleaseBody(task, h.SourceSha);
        var released = await service.ReleaseAsync(task.Id, body, Operator, CancellationToken.None);
        var heldPath = Path.Combine(h.NamedWorktree, "held.bin");
        await using (var held = new FileStream(heldPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
            var first = await service.TryRetireAsync(row, null, CancellationToken.None);
            first.IsClean.ShouldBeFalse();
            await db.Entry(row).ReloadAsync();
            row.CommandIntentId.ShouldNotBeNull();
            Directory.Exists(h.NamedWorktree).ShouldBeTrue();
        }

        h.Host.Fixture.Git.Trace.Clear();
        await using var restarted = h.Host.CreateContext();
        var restartedService = RestartRetirement(h, restarted);
        var again = await restarted.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        var second = await restartedService.TryRetireAsync(again, null, CancellationToken.None);
        var removeCallsForAttempt = h.RemoveCalls;
        removeCallsForAttempt.ShouldBeLessThanOrEqualTo(1);
        second.IsClean.ShouldBeFalse();
        (await restarted.TaskWorktreeRetirementAttempts.SingleAsync(a => a.RetirementId == released.Id))
            .CommandIntentId.ShouldNotBeNull();
    }

    [Test]
    public async Task C459_ClaimCommittedBeforeIo()
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        var mutationSeen = false;
        Guid retirementId = Guid.Empty;
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
            {
                await using var observer = h.Host.CreateContext();
                mutationSeen = await observer.WorkspaceUseReservations.AsNoTracking()
                    .AnyAsync(r => r.RetirementId == retirementId && r.Active && r.Kind == WorkspaceReservationKind.Retirement);
            }

            return null;
        };
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        var released = await service.ReleaseAsync(task.Id, ReleaseBody(task, h.SourceSha), Operator, CancellationToken.None);
        retirementId = released.Id;
        var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        await service.TryRetireAsync(row, null, CancellationToken.None);
        var committedClaimAtMutation = mutationSeen;
        committedClaimAtMutation.ShouldBeTrue();
    }

    [Test]
    public async Task C459_IntentCommittedBeforeGit()
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        Guid? committedCommandIdAtRemove = null;
        Guid retirementId = Guid.Empty;
        h.Host.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
            {
                await using var observer = h.Host.CreateContext();
                committedCommandIdAtRemove = await observer.TaskWorktreeRetirementAttempts.AsNoTracking()
                    .Where(a => a.RetirementId == retirementId)
                    .Select(a => a.CommandIntentId)
                    .SingleOrDefaultAsync();
            }

            return null;
        };
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        var released = await service.ReleaseAsync(task.Id, ReleaseBody(task, h.SourceSha), Operator, CancellationToken.None);
        retirementId = released.Id;
        var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        await service.TryRetireAsync(row, null, CancellationToken.None);
        committedCommandIdAtRemove.ShouldNotBeNull();
    }

    [Test]
    public async Task C459_ComponentsAreIndependent()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
            Task.FromResult(args[0] == "update-ref" && args.Contains("-d")
                ? new LandingGitResult(128, "", "branch_cas_failed")
                : null);
        var result = await h.RemoveAsync();
        var complete = result.IsClean;
        complete.ShouldBeFalse();
        result.DirectoryGone.ShouldBeTrue();
        result.BranchDeleted.ShouldBeFalse();
        (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", h.Host.Fixture.SourceRef)).Trim()
            .ShouldBe(h.SourceSha);
    }

    [Test]
    public async Task C459_OutageKeepsFence()
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        using var cts = new CancellationTokenSource();
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("remove"))
                cts.Cancel();
            return Task.FromResult<LandingGitResult?>(null);
        };
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        var released = await service.ReleaseAsync(task.Id, ReleaseBody(task, h.SourceSha), Operator, CancellationToken.None);
        var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        try { await service.TryRetireAsync(row, null, cts.Token); }
        catch (OperationCanceledException) { }

        await using var observer = h.Host.CreateContext();
        var fencePresent = await observer.WorkspaceUseReservations.AnyAsync(r =>
            r.RetirementId == released.Id && r.Active);
        fencePresent.ShouldBeTrue();
        Directory.Exists(h.NamedWorktree).ShouldBeTrue();
    }

    [Test]
    public async Task C459_RunIntentPrecedesEnqueue()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RunAsync();
        Guid runId;
        Guid operationId;
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Fixture.TaskId && o.Active);
            op.Cleanup = LandCleanupStatus.Pending;
            var run = new WorktreeResidueRun
            {
                Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Execute = true, Preview = false, ActionBudget = 25,
            };
            db.WorktreeResidueRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
            operationId = op.Id;
        }

        Guid? committedRunLinkAtEnqueue = null;
        h.Fault.AfterSaveAcknowledged = async ctx =>
        {
            if (!ctx.ChangeTracker.Entries<AgentTaskLandRequest>().Any(e => e.Entity.CleanupOnly))
                return;
            await using var observer = h.CreateContext();
            committedRunLinkAtEnqueue = await observer.WorktreeResidueRuns.AsNoTracking()
                .Where(r => r.Id == runId).Select(r => (Guid?)r.Id).SingleOrDefaultAsync();
        };
        await h.RequestCleanupRetryAsync(operationId, runId);
        committedRunLinkAtEnqueue.ShouldNotBeNull();
    }

    [Test]
    public async Task C459_IncompletePinsRetained()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await h.Host.Fixture.Git.PinRetirementAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
            Task.FromResult(args[0] == "update-ref" && args.Contains("-d") && args.Any(a => a.Contains("feat/card-task"))
                ? new LandingGitResult(128, "", "branch_failed")
                : null);
        await h.RemoveAsync();
        var pin = await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "show-ref", "--verify", "--hash",
            LandingGit.RetirementPrefix(h.RetirementId) + "source");
        var allRequiredPinsPresent = pin.Trim() == h.SourceSha;
        allRequiredPinsPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C459_PinRetirementUsesCas()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await h.Host.Fixture.Git.PinRetirementAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "moved-pin");
        var concurrentSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        var pinRef = LandingGit.RetirementPrefix(h.RetirementId) + "source";
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "update-ref", pinRef, concurrentSha);
        var deleted = await h.Host.Fixture.Git.DeleteRetirementPinAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        deleted.Succeeded.ShouldBeFalse();
        var changedPinSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", pinRef)).Trim();
        changedPinSha.ShouldBe(concurrentSha);
    }

    [Test]
    public async Task C459_TerminalProjectionRecovers()
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        await using var scope = h.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Host.Fixture.TaskId);
        var released = await service.ReleaseAsync(task.Id, ReleaseBody(task, h.SourceSha), Operator, CancellationToken.None);
        var row = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
        (await service.TryRetireAsync(row, null, CancellationToken.None)).IsClean.ShouldBeTrue();

        var runId = Guid.NewGuid();
        db.WorktreeResidueRuns.Add(new WorktreeResidueRun
        {
            Id = runId, StartedAt = DateTime.UtcNow, Execute = true, Preview = false, ActionBudget = 25,
        });
        await db.SaveChangesAsync();

        await using var restarted = h.Host.CreateContext();
        var sweep = new WorktreeResidueSweepService(
            restarted,
            h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IWorktreeManager>(),
            Options.Create(new WorktreeResidueSettings { Execute = false, MaxActionsPerRun = 25 }),
            Options.Create(new GitSettings { WorktreeBasePath = Path.Combine(h.Host.Fixture.Root, "trees") }),
            TimeProvider.System,
            NullLogger<WorktreeResidueSweepService>.Instance,
            RestartRetirement(h, restarted));
        var dto = await sweep.PreviewAsync(null, null, CancellationToken.None);
        var fetchedCandidateTerminalOutcome = dto.Rows.Single(c => c.TaskId == h.Host.Fixture.TaskId).Outcome;
        fetchedCandidateTerminalOutcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("release-before")]
    [Arguments("release-after")]
    [Arguments("claim-before")]
    [Arguments("claim-after")]
    [Arguments("intent-before")]
    [Arguments("intent-after")]
    [Arguments("git-exit")]
    [Arguments("directory-result")]
    [Arguments("registration-result")]
    [Arguments("branch-cas")]
    [Arguments("terminal-before")]
    [Arguments("terminal-after")]
    [Arguments("run-projection")]
    public async Task C459_WorkerDeathAtEveryRetirementHandoff(string cut)
    {
        await using var h = await SettledRemovalHarness.CreateAsync(namedLeaf: true);
        var ready = Path.Combine(h.Host.Fixture.Root, "worker-ready.json");
        using var worker = StartRetirementWorker(h, cut, ready);
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(100, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await stderr : "required crash cut not reached");
            await using var observer = h.Host.CreateContext();
            var retirements = await observer.TaskWorktreeRetirements.AsNoTracking()
                .Where(r => r.TaskId == h.Host.Fixture.TaskId).ToListAsync();
            if (cut.EndsWith("-before", StringComparison.Ordinal) && cut.StartsWith("release-", StringComparison.Ordinal))
                retirements.ShouldBeEmpty();
            else if (cut != "release-before")
                retirements.ShouldHaveSingleItem();

            if (cut is "claim-after" or "intent-after" or "git-exit" or "directory-result" or "registration-result"
                or "branch-cas" or "terminal-after" or "run-projection")
            {
                (await observer.WorkspaceUseReservations.AsNoTracking()
                    .AnyAsync(r => r.TaskId == h.Host.Fixture.TaskId && r.Active)).ShouldBeTrue();
            }

            if (cut is "intent-after" or "git-exit" or "directory-result" or "registration-result" or "branch-cas"
                or "terminal-after")
            {
                (await observer.TaskWorktreeRetirementAttempts.AsNoTracking()
                    .AnyAsync(a => a.CommandIntentId != null)).ShouldBeTrue();
            }

            if (cut is "directory-result" or "registration-result" or "branch-cas" or "terminal-after")
            {
                var attempt = await observer.TaskWorktreeRetirementAttempts.AsNoTracking()
                    .SingleAsync(a => a.RetirementId == retirements[0].Id);
                if (cut is "directory-result" or "registration-result" or "branch-cas" or "terminal-after")
                    attempt.DirectoryRemoved.ShouldBe(true);
                if (cut is "registration-result" or "branch-cas" or "terminal-after")
                    attempt.RegistrationRemoved.ShouldBe(true);
            }

            if (cut == "run-projection")
            {
                (await observer.WorktreeResidueRunCandidates.CountAsync(c => c.TaskId == h.Host.Fixture.TaskId))
                    .ShouldBeGreaterThan(0);
            }

            worker.Kill(entireProcessTree: false);
            await worker.WaitForExitAsync();

            var resumeReady = Path.Combine(h.Host.Fixture.Root, "worker-ready-resume.json");
            using var resumed = StartRetirementWorker(h, "resume", resumeReady);
            var resumedOut = resumed.StandardOutput.ReadToEndAsync();
            var resumedErr = resumed.StandardError.ReadToEndAsync();
            try
            {
                using var resumeBudget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await resumed.WaitForExitAsync(resumeBudget.Token);
                resumed.ExitCode.ShouldBe(0, await resumedErr);
            }
            finally
            {
                if (!resumed.HasExited) resumed.Kill(true);
                await resumed.WaitForExitAsync();
                await Task.WhenAll(resumedOut, resumedErr);
            }

            await using var recovered = h.Host.CreateContext();
            var final = await recovered.TaskWorktreeRetirements.AsNoTracking()
                .SingleAsync(r => r.TaskId == h.Host.Fixture.TaskId && r.Active);
            if (cut is "intent-after" or "git-exit")
            {
                final.CommandIntentId.ShouldNotBeNull();
                Directory.Exists(h.NamedWorktree).ShouldBeTrue("spent unknown command is not replayed");
            }
            else if (cut is not "release-before")
            {
                Directory.Exists(h.NamedWorktree).ShouldBeFalse();
                final.State.ShouldBe(WorktreeRetirementState.Complete);
            }
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static Process StartRetirementWorker(SettledRemovalHarness h, string cut, string ready)
    {
        var script = Path.Combine(h.Host.Fixture.Root, "retirement-worker.ps1");
        File.WriteAllText(script, """
            $ErrorActionPreference = 'Stop'
            $assembly = [Reflection.Assembly]::LoadFrom($args[0])
            $type = $assembly.GetType('Antiphon.Tests.TestHelpers.LandingSafetyHarness', $true)
            $method = $type.GetMethod('RunRetirementCrashWorkerAsync', [Reflection.BindingFlags]'Public,Static')
            $task = $method.Invoke($null, [object[]]@($args[1], $args[2], $args[3], $args[4]))
            $task.GetAwaiter().GetResult()
            """);
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-File", script, typeof(LandingSafetyHarness).Assembly.Location,
                     h.Host.Fixture.Root, h.Host.Fixture.TaskId.ToString(), cut, ready,
                 })
            start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_C459_TEST_CONNECTION"] = h.Host.Schema.ConnectionString;
        return Process.Start(start)!;
    }

    private static ReleaseWorktreeRetirementRequest ReleaseBody(AgentTask task, string sha) =>
        new(task.ConcurrencyToken, sha,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result ?? ""))),
            true, "reviewed no further use");

    private static AgentTaskService.Caller Operator => new(null, null, "");

    private static TaskWorktreeRetirementService RestartRetirement(SettledRemovalHarness h, AppDbContext db) =>
        new(db, TimeProvider.System,
            Options.Create(new WorktreeResidueSettings { MinSettledMinutes = 120, MaxActionsPerRun = 25 }),
            Options.Create(new GitSettings { WorktreeBasePath = Path.Combine(h.Host.Fixture.Root, "trees") }),
            NullLogger<TaskWorktreeRetirementService>.Instance,
            h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.ILandingGit>(),
            reservations: h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal>(),
            commands: h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRetirementCommandJournal>(),
            worktrees: h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IWorktreeManager>(),
            leases: h.Host.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>(),
            admission: h.Host.Services.GetRequiredService<WorkspaceUseAdmission>());
}
