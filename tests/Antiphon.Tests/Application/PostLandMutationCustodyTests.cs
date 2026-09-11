using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
public sealed class PostLandMutationCustodyTests
{
    [Test]
    public async Task C478_FinalBranchBoundaryReloadsCommittedAuthority()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        var branch = "refs/heads/feat/card-task-" + DelegationReportFormatter.Short(world.TaskId);
        var changed = false;
        world.Host.Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.SequenceEqual(new[] { "symbolic-ref", "-q", branch })) return;
            await using var observer = world.Host.CreateContext();
            await observer.AgentTasks.Where(t => t.Id == world.TaskId).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.VerificationExecutionRevision, t => t.VerificationExecutionRevision + 1));
            changed = true;
        };
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        changed.ShouldBeTrue();
        result.DirectoryGone.ShouldBeTrue();
        result.BranchDeleted.ShouldBeFalse();
        result.Residue.ShouldBe("verification_authority_changed");
        world.Host.Fixture.Git.Trace.Any(arguments => arguments.Contains("update-ref")).ShouldBeFalse();
        (await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "rev-parse", branch)).Trim().ShouldBe(world.Host.Fixture.SeedSha);
    }

    [Test]
    public async Task C478_SnapshotSubdirectoryCannotLaunchAnUnboundSession()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var nested = Path.Combine(await world.PathAsync(), "nested");
        Directory.CreateDirectory(nested);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = new AgentSession { Id = Guid.NewGuid(), Cwd = nested, StartedAt = DateTime.UtcNow, Status = SessionStatus.Starting };
        db.AgentSessions.Add(session); await db.SaveChangesAsync();
        var spec = new AgentLaunchSpec("fixture", AgentKind.Raw, "cmd.exe", [], new Dictionary<string, string>(), nested, 80, 24);
        await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<VerificationExecutionService>()
            .PrepareLaunchAsync(session, spec, default));
        (await db.VerificationExecutions.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C478_CanonicalAuthorizationRejectsAnEscapingJunction()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var link = Path.Combine(world.Host.Fixture.Repository, "alias-outside");
        // The fixture owns both target and link; only the link is removed before fixture disposal.
        using var command = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", link, world.Host.Fixture.Observer },
        })!;
        await command.WaitForExitAsync(); command.ExitCode.ShouldBe(0);
        try
        {
            await using var scope = world.Host.Services.CreateAsyncScope();
            var admission = scope.ServiceProvider.GetRequiredService<SourceLandingAdmission>();
            await admission.RequireAuthorizedDirectoryAsync(world.Host.Fixture.Repository, world.Host.Fixture.Repository, [], default);
            await Should.ThrowAsync<ForbiddenException>(() => admission.RequireAuthorizedDirectoryAsync(link, world.Host.Fixture.Repository, [], default));
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded, 0u)]
    [Arguments(AgentTaskStatus.Failed, 8u)]
    [Arguments(AgentTaskStatus.Canceled, 16u)]
    public async Task C478_V17_RealReceiptToGuardedRemoval(AgentTaskStatus terminal, uint flags)
    {
        await using var world = await PostLandMutationWorld.CreateAsync(native: true);
        var binding = await world.ReserveAsync();
        var prefix = "Local\\c478-app-" + Guid.NewGuid().ToString("N");
        var roles = new[] { "root", "middle", "leaf" };
        var ready = roles.Select(r => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-" + r)).ToArray();
        var release = roles.Select(r => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-release-" + r)).ToArray();
        try
        {
            SessionRunnerSessionDto started;
            await using (var scope = world.Host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == binding.Generation.SessionId);
                var spec = await scope.ServiceProvider.GetRequiredService<VerificationExecutionService>().PrepareLaunchAsync(session,
                    world.Spec(binding) with { Exe = Path.Combine(AppContext.BaseDirectory, "custody-child", "Antiphon.CustodyTestChild.exe"),
                        Args = ["root", prefix, flags.ToString()] }, default);
                started = await world.Runner.StartAsync(session.Id, spec, default);
            }
            using var root = System.Diagnostics.Process.GetProcessById(started.Pid!.Value);
            foreach (var signal in ready) signal.WaitOne(TimeSpan.FromSeconds(15)).ShouldBeTrue();
            release[0].Set(); release[1].Set();
            await root.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await world.TerminalAsync(binding.Generation.SessionId);
            await using (var db = world.Host.CreateContext())
            {
                (await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status = terminal;
                await db.SaveChangesAsync();
            }
            await world.WriteRestorationAsync([]);
            var path = await world.PathAsync();
            world.Host.Fixture.Git.Trace.Clear();
            await using (var scope = world.Host.Services.CreateAsyncScope())
            {
                var refused = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
                refused.IsClean.ShouldBeFalse();
                world.Host.Fixture.Git.Trace.Any(args => args.Contains("remove") || args.Contains("update-ref")).ShouldBeFalse();
                Directory.Exists(path).ShouldBeTrue();
            }
            release[2].Set();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            VerificationCustodyStatus status;
            do
            {
                status = await world.Runner.ReadVerificationCustodyAsync(binding, true, deadline.Token);
                if (status.Receipt is null) await Task.Delay(25, deadline.Token);
            } while (status.Receipt is null);
            status.State.ShouldBe(VerificationCustodyState.Exited);
            await world.Host.RestartServicesAsync();
            await using var final = world.Host.Services.CreateAsyncScope();
            (await final.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default)).Residue.ShouldBeNull();
            Directory.Exists(path).ShouldBeFalse();
            await using var observer = world.Host.CreateContext();
            var execution = await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
            execution.ReceiptBytes.ShouldBe(status.Receipt);
            new VerificationReceiptPolicy().ValidateImported(execution, binding);
            (await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status.ShouldBe(terminal);
            File.Exists(await world.EvidencePathAsync()).ShouldBeTrue();
        }
        finally
        {
            foreach (var signal in release) signal.Set();
            foreach (var signal in ready.Concat(release)) signal.Dispose();
        }
    }

    [Test]
    public async Task C478_ConfirmedSourceCreatesExactSnapshotAndRetainsIdentity()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        task.WorktreeBaseSha.ShouldBe(task.SourceLandingSha);
        task.MergeTargetRef.ShouldBeNull();
        task.VerificationCreationJson.ShouldNotBeNull();
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(task.SourceLandingSha);
        await File.WriteAllTextAsync(Path.Combine(world.Host.Fixture.Repository, "later.txt"), "later target\n");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "add", ".");
        await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "commit", "-m", "later target");
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ValidateVerificationAsync(task, lease!, default);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "rev-parse", "HEAD")).Trim().ShouldBe(task.SourceLandingSha);
        task.SourceLandingSha = new string('f', 40);
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task C478_OpenSourceAdmissionSerializesEvenWithDifferentCompanions(AgentTaskStatus status)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = status;
            await db.SaveChangesAsync();
        }
        await using var first = world.Host.Services.CreateAsyncScope();
        await using var second = world.Host.Services.CreateAsyncScope();
        async Task Check(IServiceProvider services, Guid card)
        {
            var service = world.TaskService(services);
            var ex = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(world.Request(card), world.Caller, default));
            ex.Message.ShouldContain(world.TaskId.ToString("D"));
        }
        await Task.WhenAll(Check(first.ServiceProvider, world.Companion), Check(second.ServiceProvider, world.OtherCompanion));
    }

    [Test]
    public async Task C478_ConcurrentFirstSourceAdmissionAcceptsOne()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }
        async Task<bool> Attempt(Guid card)
        {
            await using var scope = world.Host.Services.CreateAsyncScope();
            try { await world.TaskService(scope.ServiceProvider).CreateAsync(world.Request(card), world.Caller, default); return true; }
            catch (ConflictException ex) when (ex.Message.Contains("already has open task")) { return false; }
        }
        (await Task.WhenAll(Attempt(world.Companion), Attempt(world.OtherCompanion))).Count(ok => ok).ShouldBe(1);
    }

    [Test]
    [Arguments("mode")]
    [Arguments("project")]
    [Arguments("original-card")]
    [Arguments("publication")]
    [Arguments("source-sha")]
    [Arguments("repository")]
    public async Task C478_SourceIdentityRefusesIndependently(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var source = scope.ServiceProvider.GetRequiredService<SourceLandingAdmission>();
        (await source.RequireSourceAsync(task, default)).Id.ShouldBe(world.Operation);
        switch (variant)
        {
            case "mode": task.Workspace = WorkspaceMode.Shared; break;
            case "project": task.ProjectId = Guid.NewGuid(); break;
            case "original-card": task.CardId = world.Original; break;
            case "source-sha": task.SourceLandingSha = new string('f', 40); break;
            case "repository": task.RepoPath = world.Host.Fixture.Observer; break;
            case "publication":
                var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation);
                op.RemoteConfirmedAt = null;
                await db.SaveChangesAsync();
                break;
        }
        await Should.ThrowAsync<ConflictException>(() => source.RequireSourceAsync(task, default));
    }

    [Test]
    public async Task C478_ReservationPersistsBeforeRunnerAndSealFencesDelayedLaunch()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await using (var db = world.Host.CreateContext())
        {
            var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
            execution.RunnerCallIntentAt.ShouldBeNull();
            (execution.AcceptedStartedAt.Ticks % 10).ShouldBe(0);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        var session = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentSessions.SingleAsync(s => s.Id == binding.Generation.SessionId);
        await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<VerificationExecutionService>()
            .PrepareLaunchAsync(session, world.Spec(binding), default));
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).VerificationCleanupSealJson.ShouldNotBeNull();
        (await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId)).RunnerCallIntentAt.ShouldBeNull();
    }

    [Test]
    public async Task C478_RecoveryConsumesSameBindingAndRetainsUncertainAttempt()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == binding.Generation.SessionId);
        var service = scope.ServiceProvider.GetRequiredService<VerificationExecutionService>();
        (await service.PrepareLaunchAsync(session, world.Spec(binding) with { VerificationBinding = null }, default)).VerificationBinding.ShouldBe(binding);
        await using var observer = world.Host.CreateContext();
        (await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId)).RunnerCallIntentAt.ShouldNotBeNull();
        await Should.ThrowAsync<ConflictException>(() => service.PrepareLaunchAsync(session,
            world.Spec(binding) with { VerificationBinding = binding with { ExecutionId = Guid.NewGuid() } }, default));
        session.StartedAt = session.StartedAt.AddSeconds(1);
        await Should.ThrowAsync<ConflictException>(() => service.PrepareLaunchAsync(session, world.Spec(binding), default));
        (await observer.VerificationExecutions.CountAsync(e => e.TaskId == world.TaskId)).ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C478_SnapshotNeverAutosavesOrLands(bool sourced)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        if (!sourced) task.SourceLandingOperationId = null; // Direct lower-service protection also covers legacy Mutation.
        await File.AppendAllTextAsync(Path.Combine(task.WorktreePath!, "keep.txt"), "mutant\n");
        var before = await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff");
        var outcome = await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().TryMergeBackAsync(task, default);
        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        (await world.Host.Fixture.RequiredAsync(task.WorktreePath!, "diff")).ShouldBe(before);
        await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
        await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>().RunAsync(task, lease!, default));
    }

    [Test]
    [Arguments("unknown-file")]
    [Arguments("empty-directory")]
    [Arguments("ignored-file")]
    [Arguments("output-escape")]
    [Arguments("dirty-source")]
    [Arguments("missing-evidence")]
    public async Task C478_NeverReservedCleanupRetainsUnknownFiles(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var path = await world.PathAsync();
        await world.WriteRestorationAsync(variant == "output-escape" ? [new("../outside.txt", new string('A', 64))] : []);
        switch (variant)
        {
            case "unknown-file": await File.WriteAllTextAsync(Path.Combine(path, "unknown.txt"), "keep"); break;
            case "empty-directory": Directory.CreateDirectory(Path.Combine(path, "unknown-empty")); break;
            case "ignored-file": Directory.CreateDirectory(Path.Combine(path, "bin-private")); await File.WriteAllTextAsync(Path.Combine(path, "bin-private", "secret.txt"), "keep"); break;
            case "dirty-source": await File.AppendAllTextAsync(Path.Combine(path, "keep.txt"), "mutant"); break;
            case "missing-evidence": File.Delete(await world.EvidencePathAsync()); break;
        }
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default)).IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
        (await world.Host.Fixture.RequiredAsync(path, "rev-parse", "HEAD")).Trim().ShouldBe(world.Host.Fixture.SeedSha);
    }

    [Test]
    public async Task C478_NeverReservedCleanupRemovesExactOwnedOutputsAndIsIdempotent()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        var path = await world.PathAsync();
        Directory.CreateDirectory(Path.Combine(path, "bin-verification"));
        var bytes = Encoding.UTF8.GetBytes("owned result\n");
        await File.WriteAllBytesAsync(Path.Combine(path, "bin-verification", "result.txt"), bytes);
        await world.WriteRestorationAsync([new("bin-verification/result.txt", Convert.ToHexString(SHA256.HashData(bytes)))]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<VerificationCleanupService>();
        var result = await service.CleanupAsync(world.TaskId, default);
        result.Residue.ShouldBeNull(); result.DirectoryGone.ShouldBeTrue(); result.BranchDeleted.ShouldBeTrue();
        (await service.CleanupAsync(world.TaskId, default)).IsClean.ShouldBeTrue();
        File.Exists(await world.EvidencePathAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task C478_V17_RealModernHostReceiptImportsAndRemovesSnapshot()
    {
        await using var world = await PostLandMutationWorld.CreateAsync(native: true);
        var binding = await world.ReserveAsync();
        var path = await world.PathAsync();
        await using (var scope = world.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == binding.Generation.SessionId);
            var spec = await scope.ServiceProvider.GetRequiredService<VerificationExecutionService>().PrepareLaunchAsync(session, world.Spec(binding), default);
            await world.Runner.StartAsync(session.Id, spec, default);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        VerificationCustodyStatus status;
        do
        {
            status = await world.Runner.ReadVerificationCustodyAsync(binding, true, timeout.Token);
            if (status.Receipt is null) await Task.Delay(25, timeout.Token);
        } while (status.Receipt is null);
        status.State.ShouldBe(VerificationCustodyState.Exited);
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await using var cleanup = world.Host.Services.CreateAsyncScope();
        (await cleanup.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default)).Residue.ShouldBeNull();
        Directory.Exists(path).ShouldBeFalse();
        await using var observer = world.Host.CreateContext();
        var execution = await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
        execution.ReceiptBytes.ShouldBe(status.Receipt);
        new VerificationReceiptPolicy().ValidateImported(execution, binding);
    }

    [Test]
    public async Task C478_V13_AcceptedExecutionHistory()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await using var scope = world.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
        execution.RunnerCallIntentAt.ShouldBeNull();
        execution.CustodyReason.ShouldBe(VerificationCustodyState.Starting.ToString());
        (execution.AcceptedStartedAt.Ticks % 10).ShouldBe(0);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == binding.Generation.SessionId);
        var prepared = await scope.ServiceProvider.GetRequiredService<VerificationExecutionService>()
            .PrepareLaunchAsync(session, world.Spec(binding), default);
        prepared.VerificationBinding.ShouldBe(binding);
        var detail = await world.TaskService(scope.ServiceProvider).GetAsync(world.TaskId, default);
        detail.SourceLandingOperationId.ShouldBe(world.Operation);
        detail.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        var listed = detail.VerificationExecutions.ShouldHaveSingleItem();
        listed.ExecutionId.ShouldBe(binding.ExecutionId);
        listed.RunnerStoreId.ShouldBe(binding.RunnerStoreId);
        listed.HasReceipt.ShouldBeFalse();
        listed.CustodyReason.ShouldBe(VerificationCustodyState.Starting.ToString());
        await using var observer = world.Host.CreateContext();
        (await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId)).RunnerCallIntentAt.ShouldNotBeNull();
    }

    [Test]
    public async Task C478_G183_PersistBinding()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await using var observer = world.Host.CreateContext();
        var stored = await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
        stored.BindingJson.ShouldContain(binding.ExecutionId.ToString("D"));
        stored.RunnerCallIntentAt.ShouldBeNull();
        JsonSerializer.Deserialize<VerificationExecutionBinding>(stored.BindingJson).ShouldBe(binding);
    }

    [Test]
    public async Task C478_G184_AcceptedGeneration()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        (binding.Generation.AcceptedStartedAt.Ticks % 10).ShouldBe(0);
        await using var observer = world.Host.CreateContext();
        var stored = await observer.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
        stored.AcceptedStartedAt.ShouldBe(binding.Generation.AcceptedStartedAt);
        stored.SessionId.ShouldBe(binding.Generation.SessionId);
    }

    [Test]
    public async Task C478_G186_AttemptHistory()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var first = await world.ReserveAsync();
        await using (var db = world.Host.CreateContext())
        {
            var execution = await db.VerificationExecutions.SingleAsync(e => e.Id == first.ExecutionId);
            execution.ReceiptBytes = [1, 2, 3];
            execution.CustodyReason = VerificationCustodyState.Unknown.ToString();
            await db.SaveChangesAsync();
        }
        var second = await world.ReserveAsync();
        second.ExecutionId.ShouldNotBe(first.ExecutionId);
        second.Generation.SessionId.ShouldNotBe(first.Generation.SessionId);
        await using var observer = world.Host.CreateContext();
        (await observer.VerificationExecutions.CountAsync(e => e.TaskId == world.TaskId)).ShouldBe(2);
        (await observer.VerificationExecutions.SingleAsync(e => e.Id == first.ExecutionId)).CustodyReason
            .ShouldBe(VerificationCustodyState.Unknown.ToString());
    }

    [Test]
    public async Task C478_G189_NoPool()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = world.Host.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, Status = SessionStatus.Running, Cwd = row.WorktreePath!, AgentKind = AgentKind.Raw,
                StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = "sourced-pool", Slug = "sourced-pool", WorkingDirectory = row.WorktreePath!,
                Status = AgentStatus.Running, Kind = AgentKind.Raw, ModelLevel = AgentModelLevel.Medium,
                IsPoolDelegate = true, PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            row.AgentId = agentId;
            row.AgentSessionId = sessionId;
            row.Status = AgentTaskStatus.Dispatched;
            await db.SaveChangesAsync();
        }

        var replies = new AgentTaskReplyService(
            world.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings { PoolEnabled = true }),
            new MockEventBus(), TimeProvider.System, NullLogger<AgentTaskReplyService>.Instance);
        await replies.RecoverFromBindRefusalAsync(world.TaskId, new DelegateBindRefusalEvidence(["deadbeef"], null), default);

        await using var observer = world.Host.CreateContext();
        var agent = await observer.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status.ShouldBe(AgentStatus.Running);
        agent.PoolIdleSince.ShouldBeNull();
        agent.PoolReservedForRootTaskId.ShouldBeNull();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        task.AgentId.ShouldBe(agentId);
        task.VerificationCleanupResidue.ShouldBe("verification_release_unresolved");
        (await observer.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task C478_G224_EmptyHistory()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.Residue.ShouldBeNull();
        await using var observer = world.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        var seal = JsonSerializer.Deserialize<VerificationCleanupSeal>(task.VerificationCleanupSealJson!);
        seal!.NeverReserved.ShouldBeTrue();
        (await observer.VerificationExecutions.CountAsync(e => e.TaskId == world.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C478_G187_SupportAdmission()
    {
        await using var world = await PostLandMutationWorld.CreateAsync(provision: false, custodySupport: false);
        await using var scope = world.Host.Services.CreateAsyncScope();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.TaskService(scope.ServiceProvider)
            .CreateAsync(world.Request(world.Companion), world.Caller, default));
        ex.Code.ShouldBe("verification_custody_unsupported_backend");
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.CountAsync(t => t.SourceLandingOperationId == world.Operation)).ShouldBe(0);
    }

    [Test]
    [Arguments("schema")]
    [Arguments("sealedAt")]
    [Arguments("observation")]
    public async Task C478_G215_ImportSchema(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        var valid = world.ValidReceipt(binding);
        var bad = variant switch
        {
            "schema" => valid with { SchemaVersion = 2 },
            "sealedAt" => valid with { SealedAtUtc = default },
            _ => valid with { ObservationMethod = "worker-restoration", StateRevision = 1, OutputDrained = false },
        };
        await world.SeedImportedReceiptAsync(binding, bad);
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    public async Task C478_G216_RunnerProvenance()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    [Arguments("task")]
    [Arguments("operation")]
    public async Task C478_G217_TaskOperation(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        var valid = world.ValidReceipt(binding);
        var source = variant == "task"
            ? valid.Binding.Source with { TaskId = Guid.NewGuid() }
            : valid.Binding.Source with { SourceOperationId = Guid.NewGuid() };
        await world.SeedImportedReceiptAsync(binding, valid with { Binding = valid.Binding with { Source = source } });
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    public async Task C478_G218_GenerationBinding()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        var valid = world.ValidReceipt(binding);
        var generation = valid.Binding.Generation with
        {
            AcceptedStartedAt = DateTime.SpecifyKind(valid.Binding.Generation.AcceptedStartedAt.AddSeconds(1), DateTimeKind.Utc),
        };
        await world.SeedImportedReceiptAsync(binding, valid with { Binding = valid.Binding with { Generation = generation } });
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    [Arguments("path")]
    [Arguments("common")]
    [Arguments("git")]
    [Arguments("branch")]
    [Arguments("creation")]
    [Arguments("sha")]
    public async Task C478_G219_Coordinates(string variant)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        var valid = world.ValidReceipt(binding);
        var creation = valid.Binding.Creation;
        var receipt = variant switch
        {
            "path" => valid with { Binding = valid.Binding with { Creation = creation with { WorktreePath = Path.Combine(world.Host.Fixture.Root, "stranger") } } },
            "common" => valid with { Binding = valid.Binding with { Creation = creation with { CommonGitDirectory = world.Host.Fixture.Observer } } },
            "git" => valid with { Binding = valid.Binding with { Creation = creation with { WorktreeGitDirectory = Path.Combine(world.Host.Fixture.Root, "stranger.git") } } },
            "branch" => valid with { Binding = valid.Binding with { Creation = creation with { Branch = "feat/card-task-other" } } },
            "creation" => valid with { Binding = valid.Binding with { Creation = creation with { CreationId = Guid.NewGuid() } } },
            _ => valid with { Binding = valid.Binding with { Source = valid.Binding.Source with { LandedSha = new string('f', 40) } } },
        };
        await world.SeedImportedReceiptAsync(binding, receipt);
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    public async Task C478_G220_ContainerBinding()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var binding = await world.ReserveAsync();
        var valid = world.ValidReceipt(binding);
        var stranger = valid.Host with { ContainerId = Guid.NewGuid(), HostInstanceId = Guid.NewGuid() };
        await world.SeedImportedReceiptAsync(binding, valid with { Host = stranger }, storedHost: valid.Host);
        await world.TerminalAsync(binding.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
    }

    [Test]
    public async Task C478_G221_TaskSealAtomic()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .Residue.ShouldBeNull();
        var ex = await Should.ThrowAsync<ConflictException>(() => world.ReserveAsync());
        ex.Message.ShouldContain("verification_reservation_refused");
    }

    [Test]
    public async Task C478_G223_AllAttempts()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var first = await world.ReserveAsync();
        await world.SeedImportedReceiptAsync(first, world.ValidReceipt(first) with { SchemaVersion = 2 });
        var second = await world.ReserveAsync();
        await world.SeedImportedReceiptAsync(second, world.ValidReceipt(second));
        await world.TerminalAsync(second.Generation.SessionId);
        await world.WriteRestorationAsync([]);
        await AssertCleanupRetainsAsync(world);
        await using var observer = world.Host.CreateContext();
        (await observer.VerificationExecutions.CountAsync(e => e.TaskId == world.TaskId)).ShouldBe(2);
    }

    private static async Task AssertCleanupRetainsAsync(PostLandMutationWorld world)
    {
        var path = await world.PathAsync();
        world.Host.Fixture.Git.Trace.Clear();
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .IsClean.ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
        world.Host.Fixture.Git.Trace.Any(PostLandMutationWorld.IsDestructive).ShouldBeFalse();
    }

    [Test]
    public async Task C478_G225_ReadBeforeDelete() => await C478_FinalBranchBoundaryReloadsCommittedAuthority();

    [Test]
    public async Task C478_G226_Retention()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await using var scope = world.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default))
            .Residue.ShouldBeNull();
        await using var observer = world.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.VerificationCleanupSealJson.ShouldNotBeNull();
        task.SourceLandingOperationId.ShouldBe(world.Operation);
        (await observer.AgentTaskLandings.CountAsync(o => o.Id == world.Operation)).ShouldBe(1);
    }

    [Test]
    public async Task C478_G229_SealPersistsRefusal()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await world.TerminalAsync();
        await world.WriteRestorationAsync([]);
        await File.WriteAllTextAsync(Path.Combine(await world.PathAsync(), "unknown.txt"), "keep");
        await using var scope = world.Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerificationCleanupService>().CleanupAsync(world.TaskId, default);
        result.IsClean.ShouldBeFalse();
        await using var observer = world.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.VerificationCleanupSealJson.ShouldNotBeNull();
        task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        var ex = await Should.ThrowAsync<ConflictException>(() => world.ReserveAsync());
        ex.Message.ShouldContain("verification_reservation_refused");
    }
}

internal sealed class PostLandMutationWorld : IAsyncDisposable
    {
        public LandingSafetyHarness Host { get; } = new();
        public ISessionRunnerClient Runner { get; private set; } = null!;
        public Guid TaskId { get; private set; }
        public Guid Operation { get; private set; }
        public Guid Original { get; private set; }
        public Guid Companion { get; private set; }
        public Guid OtherCompanion { get; private set; }
        public AgentTaskService.Caller Caller => new(null, null, Host.Fixture.Repository);
        public CreateAgentTaskRequest Request(Guid card) => new("post-land battery", Role: AgentTaskRole.Mutation,
            Workspace: WorkspaceMode.Worktree, Card: card.ToString("D"), SourceLandingOperationId: Operation);
        public AgentTaskService TaskService(IServiceProvider services, DelegationSettings? settings = null)
        {
            var options = Options.Create(settings ?? new DelegationSettings());
            var db = services.GetRequiredService<AppDbContext>();
            return new(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance), options,
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance,
                openGate: new DelegationOpenGate(db, options),
                sourceLanding: services.GetRequiredService<SourceLandingAdmission>());
        }

        public static async Task<PostLandMutationWorld> CreateAsync(bool native = false, bool provision = true,
            bool custodySupport = true)
        {
            var world = new PostLandMutationWorld();
            world.Runner = native
                ? new DirectSessionRunnerClient(Path.Combine(world.Host.Fixture.Root, "runner"), "modern")
                    { AdvertiseVerificationCustody = custodySupport }
                : new FakeSessionRunnerClient { VerificationStoreId = custodySupport ? Guid.NewGuid() : null };
            world.Host.ConfigureServices = services =>
            {
                services.AddSingleton(world.Runner);
                services.AddScoped<SourceLandingAdmission>();
                services.AddScoped<VerificationExecutionService>();
                services.AddScoped<VerificationCleanupService>();
            };
            await world.Host.InitializeAsync();
            try
            {
            // WorktreeManager deliberately uses production Git I/O. Pin the fixture's local
            // checkout policy too, so it agrees with FixtureGit's isolated global config.
            await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "config", "core.autocrlf", "false");
            await world.Host.RunAsync();
            await using (var db = world.Host.CreateContext())
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "custody fixture", GitRepositoryUrl = "https://example.test/custody.git" };
                var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "custody fixture" };
                var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Backlog", StateKey = "backlog", CardStatus = CardStatus.Backlog };
                db.Projects.Add(project); db.Boards.Add(board); db.BoardColumns.Add(column);
                var cards = Enumerable.Range(1, 3).Select(i => new Card { Id = Guid.NewGuid(), BoardId = board.Id,
                    BoardColumnId = column.Id, Identifier = $"CARD-{i:0000}", Title = "fixture " + i }).ToArray();
                db.Cards.AddRange(cards);
                world.Original = cards[0].Id; world.Companion = cards[1].Id; world.OtherCompanion = cards[2].Id;
                (await db.AgentTasks.SingleAsync(t => t.Id == world.Host.Fixture.TaskId)).CardId = world.Original;
                world.Operation = (await db.AgentTaskLandings.SingleAsync(o => o.TaskId == world.Host.Fixture.TaskId)).Id;
                await db.SaveChangesAsync();
            }
            if (!provision) return world;
            await using var scope = world.Host.Services.CreateAsyncScope();
            world.TaskId = (await world.TaskService(scope.ServiceProvider).CreateAsync(world.Request(world.Companion), world.Caller, default)).Id;
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await context.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            await using var lease = await world.Host.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(task.RepoPath!, default);
            await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, lease!, default);
            await context.SaveChangesAsync();
            return world;
            }
            catch { await world.DisposeAsync(); throw; }
        }

        public async Task<VerificationExecutionBinding> ReserveAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var task = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {TaskId} FOR UPDATE").SingleAsync();
            var session = new AgentSession { Id = Guid.NewGuid(), Status = SessionStatus.Starting,
                Cwd = task.WorktreePath!, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, AgentKind = AgentKind.Raw };
            db.AgentSessions.Add(session);
            task.AgentSessionId = session.Id; task.Status = AgentTaskStatus.Dispatched;
            var binding = await scope.ServiceProvider.GetRequiredService<VerificationExecutionService>().ReserveAsync(task, session, default);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return binding;
        }
        public AgentLaunchSpec Spec(VerificationExecutionBinding binding) => new("custody fixture", AgentKind.Raw,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/c", "exit", "0"],
            new Dictionary<string, string>(), binding.Creation.WorktreePath, 80, 24, SessionId: binding.Generation.SessionId, VerificationBinding: binding);
        public async Task<string> PathAsync()
        {
            await using var db = Host.CreateContext(); return (await db.AgentTasks.SingleAsync(t => t.Id == TaskId)).WorktreePath!;
        }
        public async Task<string> EvidencePathAsync()
        {
            await using var db = Host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
            var creation = JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson!)!;
            return Path.Combine(creation.CommonGitDirectory, "antiphon", "verification", Operation.ToString("N"), TaskId.ToString("N"), "restoration.json");
        }
        public async Task WriteRestorationAsync(VerificationOutput[] outputs)
        {
            await using var db = Host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
            var creation = JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson!)!;
            var restoration = new VerificationRestoration(1, new(TaskId, Operation, task.SourceLandingSha!), creation.CreationId,
                true, "ordinary fixture completed; no PCs executed", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result!))), outputs);
            var path = await EvidencePathAsync(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(restoration, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        public async Task TerminalAsync(Guid? sessionId = null)
        {
            await using var db = Host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId); task.Status = AgentTaskStatus.Succeeded; task.Result = "fixture result";
            if (sessionId is Guid id) (await db.AgentSessions.SingleAsync(s => s.Id == id)).Status = SessionStatus.Stopped;
            await db.SaveChangesAsync();
        }

        public async Task CancelOpenAsync()
        {
            await using var db = Host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
            task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }

        public static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

        public VerificationCustodyReceipt ValidReceipt(VerificationExecutionBinding binding)
        {
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            var host = new VerificationHostIdentity(binding.RunnerStoreId, Guid.NewGuid(), Guid.NewGuid(),
                Math.Max(1, Environment.ProcessId), now);
            return new(1, binding, host, 3, now, now, "JobObjectBasicAccountingInformation", 0, true,
                VerificationCustodyState.Exited, 124, now);
        }

        public async Task SeedImportedReceiptAsync(VerificationExecutionBinding binding, VerificationCustodyReceipt receipt,
            VerificationHostIdentity? storedHost = null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, WebJson);
            await using var db = Host.CreateContext();
            var row = await db.VerificationExecutions.SingleAsync(e => e.Id == binding.ExecutionId);
            row.ReceiptBytes = bytes;
            row.HostIdentityJson = JsonSerializer.Serialize(storedHost ?? receipt.Host);
            row.ReceiptDigest = Convert.ToHexString(SHA256.HashData(bytes));
            row.ReceiptImportedAt = DateTime.UtcNow;
            row.CustodyReason = receipt.Disposition.ToString();
            await db.SaveChangesAsync();
        }

        public static bool IsDestructive(IReadOnlyList<string> arguments) =>
            arguments.Contains("remove") || arguments.Contains("--force")
            || (arguments.Contains("update-ref") && arguments.Contains("-d"));

        public async ValueTask DisposeAsync()
        {
            if (Runner is IAsyncDisposable disposable) await disposable.DisposeAsync();
            await Host.DisposeAsync();
        }
    }
