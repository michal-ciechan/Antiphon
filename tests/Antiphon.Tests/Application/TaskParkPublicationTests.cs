using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TaskParkPublicationTests
{
    [Test]
    public async Task C1065_WorkspaceModesPreservePublicationAuthority()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Workspace = WorkspaceMode.Shared,
            Role = AgentTaskRole.Code, Goal = "Inspect landed source", SourceLandingOperationId = Guid.NewGuid() };
        DelegationReportFormatter.BuildBrief(task, new DelegationSettings())
            .Contains(DelegationReportFormatter.SharedWriteCommitLine, StringComparison.Ordinal)
            .ShouldBeFalse("G-73: SourceLanding custody overrides generic writable instructions");
        await using (var w = await PublicationWorld.CreateAsync())
        {
            var tip = await w.CommitAsync("local.txt");
            var published = await w.PrepareAsync();
            published.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, published.Reason);
            published.Evidence!.SourceSha.ShouldBe(tip);
            (await w.RemoteShaAsync()).ShouldBe(tip);
            (await w.VerifyAsync()).Evidence.ShouldBe(published.Evidence);
            (await w.RowAsync()).PublicationReceiptId.ShouldBe(published.Evidence.ReceiptId);
            await w.EpisodeAsync();
            await File.WriteAllTextAsync(Path.Combine(w.Path, "untracked.cs"), "uncommitted source");
            (await w.PrepareAsync()).Reason.ShouldBe("park_dirty");
            (await w.RemoteShaAsync()).ShouldBe(tip);
            File.Delete(Path.Combine(w.Path, "untracked.cs"));
            await w.GitTextAsync(w.Path, "remote", "remove", "origin");
            (await w.PrepareAsync()).Outcome.ShouldBe(TaskParkPublicationOutcome.Unknown, "unavailable endpoint read");
        }
        await using (var w = await PublicationWorld.CreateAsync(remote: true))
        {
            var tip = await w.CommitAsync("remote.txt");
            var published = await w.PrepareAsync();
            published.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, published.Reason);
            published.Evidence!.SourceSha.ShouldBe(tip);
            (await w.RemoteShaAsync()).ShouldBe(tip);
            (await w.GitTextAsync(w.LocalPath, "rev-parse", "HEAD")).ShouldBe(w.Baseline);
            w.Directory.Client.Calls.ShouldBeGreaterThanOrEqualTo(2);
            (await w.VerifyAsync()).Evidence.ShouldBe(published.Evidence);
            await File.WriteAllTextAsync(System.IO.Path.Combine(w.Path, "dirty.txt"), "source");
            (await w.VerifyAsync()).Outcome.ShouldBe(TaskParkPublicationOutcome.Held);
        }
        await using (var w = await PublicationWorld.CreateAsync(WorkspaceMode.Shared))
        {
            await w.PushAsync();
            w.Git.Commands.Clear();
            (await w.PrepareAsync()).Outcome.ShouldBe(TaskParkPublicationOutcome.Published, "G-68: published Shared");
            w.Git.Commands.Any(c => c[0] == "push").ShouldBeFalse("G-68: Shared never pushes");
            await w.EpisodeAsync();
            await w.CommitAsync("shared-unpublished.txt");
            w.Git.Commands.Clear();
            (await w.PrepareAsync()).Reason.ShouldBe("park_publish_unconfirmed");
            w.Git.Commands.Any(c => c[0] == "push").ShouldBeFalse("G-68: unpublished Shared stays held");
            await w.PushAsync();
            var reservation = await w.Reservations.TryAdmitConsumerAsync(new(w.Key, WorkspaceReservationKind.Launch,
                Guid.NewGuid(), Guid.NewGuid()), default);
            (await w.PrepareAsync()).Reason.ShouldBe("park_other_writer", "G-70");
            await w.Reservations.ReleaseConsumerAsync(reservation.Snapshot!.Id, reservation.Snapshot.Generation, default);
            var otherId = Guid.NewGuid();
            await using (var db = w.Fixture.Db())
            {
                db.AgentTasks.Add(new AgentTask { Id = otherId, RootTaskId = otherId, Goal = "Other writer",
                    Status = AgentTaskStatus.Working, Workspace = WorkspaceMode.Shared, AgentSessionId = w.Fixture.SessionId,
                    WorkingDirectory = w.LocalPath, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            (await w.PrepareAsync()).Reason.ShouldBe("park_other_writer", "G-70 current task owner");
            await using (var db = w.Fixture.Db()) await db.AgentTasks.Where(t => t.Id == otherId).ExecuteDeleteAsync();
            await w.ChangeTaskAsync(t => t.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(
                TaskProgressJson.TryReadBaseline(t.ProgressBaselineJson)! with { Primary = w.Source with { FullRef = "refs/heads/master" } }));
            (await w.PrepareAsync()).Reason.ShouldBe("park_ownership_unknown", "G-69");
        }
        await using (var w = await PublicationWorld.CreateAsync(WorkspaceMode.ReadOnly))
        {
            var clean = await w.PrepareAsync();
            clean.Outcome.ShouldBe(TaskParkPublicationOutcome.NoSourceChanges, clean.Reason);
            clean.Evidence!.SourceSha.ShouldBe(w.Baseline);
            clean.Evidence.RemoteSha.ShouldBeNull();
            (await w.VerifyAsync()).Evidence.ShouldBe(clean.Evidence);
            await w.EpisodeAsync();
            await File.WriteAllTextAsync(System.IO.Path.Combine(w.Path, "write.txt"), "unauthorized");
            (await w.PrepareAsync()).Reason.ShouldBe("park_dirty", "G-71");
            await w.GitTextAsync(w.Path, "add", "write.txt");
            await w.GitTextAsync(w.Path, "commit", "-m", "discovered readonly mutation");
            (await w.PrepareAsync()).Reason.ShouldBe("park_readonly_changed", "G-72");
            w.Git.Commands.Any(c => c[0] == "push").ShouldBeFalse();
        }
    }

    [Test]
    public async Task C1065_CommitInstructionsAndRefusalsRespectOverrides()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Workspace = WorkspaceMode.Worktree,
            Role = AgentTaskRole.Code, Goal = "Implement the assigned change" };
        var brief = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
        brief.Contains("Before reporting blocked", StringComparison.Ordinal).ShouldBeTrue("G-76: ordinary writable brief must require publication before block");
        brief.Contains("truthful WIP commit", StringComparison.Ordinal).ShouldBeTrue("G-76");
        foreach (var mode in new[] { WorkspaceMode.Worktree, WorkspaceMode.Shared })
        {
            task.Workspace = mode;
            task.CommitOnSettle = CommitOnSettlePolicy.Never;
            task.RunnerId = "fixture";
            task.RemoteWorktreePath = "/unreachable/owned-mirror";
            task.WorktreeBranch = RemoteWorkspaceService.OwnedBranch(task.Id);
            var excluded = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
            excluded.Contains("truthful WIP commit", StringComparison.Ordinal).ShouldBeFalse("G-77 NoCommit");
            excluded.ShouldContain(DelegationReportFormatter.DoNotCommitLine);
            excluded.Contains("push after every slice", StringComparison.Ordinal).ShouldBeFalse();
        }
        task.CommitOnSettle = null;
        task.Workspace = WorkspaceMode.ReadOnly;
        DelegationReportFormatter.BuildBrief(task, new DelegationSettings()).Contains("truthful WIP commit", StringComparison.Ordinal).ShouldBeFalse("G-77 ReadOnly");
        task.Workspace = WorkspaceMode.Shared;
        task.SourceLandingOperationId = Guid.NewGuid();
        DelegationReportFormatter.BuildBrief(task, new DelegationSettings()).Contains("truthful WIP commit", StringComparison.Ordinal).ShouldBeFalse("G-77 SourceLanding");

        await using var w = await PublicationWorld.CreateAsync();
        var originalHead = await w.GitTextAsync(w.Path, "rev-parse", "HEAD");
        await File.WriteAllTextAsync(Path.Combine(w.Path, "do-not-autosave.txt"), "retained uncommitted source");
        await w.ChangeTaskAsync(t => t.CommitOnSettle = CommitOnSettlePolicy.Never);
        (await w.PrepareAsync()).Reason.ShouldBe("park_no_commit", "G-74");
        await w.ChangeTaskAsync(t => t.CommitOnSettle = null);
        await using (var sourceLanding = await PublicationWorld.CreateAsync(sourceLanding: true))
        {
            var sourceHead = await sourceLanding.GitTextAsync(sourceLanding.Path, "rev-parse", "HEAD");
            await File.WriteAllTextAsync(Path.Combine(sourceLanding.Path, "retained-source.txt"), "do not autosave");
            (await sourceLanding.PrepareAsync()).Reason.ShouldBe("park_source_landing", "G-73");
            (await sourceLanding.GitTextAsync(sourceLanding.Path, "rev-parse", "HEAD")).ShouldBe(sourceHead);
            (await sourceLanding.GitTextAsync(sourceLanding.Path, "status", "--porcelain")).ShouldContain("retained-source.txt");
            sourceLanding.Directory.Client.Calls.ShouldBe(0);
        }
        await using (var db = w.Fixture.Db())
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = w.Fixture.TaskId,
                Type = AgentTaskEventType.CommitRecoveryStarted, Detail = "unresolved", At = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        (await w.PrepareAsync()).Reason.ShouldBe("park_commit_recovery", "G-75");
        await using (var db = w.Fixture.Db())
        {
            (await db.AgentTasks.CountAsync(t => t.ParentTaskId == w.Fixture.TaskId && t.Role == AgentTaskRole.Commit))
                .ShouldBe(0, "no automatic Commit child");
            await db.AgentTaskEvents.Where(e => e.AgentTaskId == w.Fixture.TaskId && e.Type == AgentTaskEventType.CommitRecoveryStarted).ExecuteDeleteAsync();
        }
        await w.ChangeTaskAsync(t => t.ProgressBaselineJson = null);
        (await w.PrepareAsync()).Outcome.ShouldBe(TaskParkPublicationOutcome.Held, "uncertain owner");
        (await w.GitTextAsync(w.Path, "rev-parse", "HEAD")).ShouldBe(originalHead);
        (await w.GitTextAsync(w.Path, "status", "--porcelain")).ShouldContain("do-not-autosave.txt");
        w.Directory.Client.Calls.ShouldBe(0);
    }

    [Test]
    public async Task C1065_PublicationReceiptCannotAuthorizeChangedAttempt()
    {
        // Original observed G-92 regression remains independently executable.
        await using (var f = await BlockedTaskParkFixture.CreateAsync())
        {
            f.Options.Enabled = true;
            var id = (await f.RegisterAsync()).ShouldNotBeNull();
            await using (var db = f.Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                task.Result = "A different report after the captured block";
                await db.SaveChangesAsync();
            }
            (await f.AdvanceAsync(id, AgentTaskParkState.Published)).ShouldBeFalse("G-92: report changed");
        }
        await using var w = await PublicationWorld.CreateAsync();
        await w.CommitAsync("receipt-source.txt");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiptReady = new TaskCompletionSource<TaskParkPublicationEvidence>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveReceipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.BeforeReceiptSave = async proof =>
        {
            receiptReady.TrySetResult(proof);
            await saveReceipt.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };
        w.Git.Before = async args =>
        {
            if (args[0] != "status") return;
            entered.TrySetResult();
            await proceed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        var prepare = w.PrepareAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = w.Fixture.Db();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "AgentTasks" WHERE "Id" = {w.Fixture.TaskId} FOR UPDATE NOWAIT""");
            var intent = await db.AgentTaskParks.SingleAsync(p => p.Id == w.ParkId);
            intent.RepositoryIdentity.ShouldNotBeNull("G-93: committed intent visible while Git is paused");
            intent.PublicationReceiptId.ShouldBeNull();
            await tx.CommitAsync();
        }
        catch
        {
            proceed.TrySetResult(); saveReceipt.TrySetResult();
            await prepare;
            throw;
        }
        finally { proceed.TrySetResult(); }
        w.Git.Before = null;
        var proof = await receiptReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
        var binding = proof.Request.Binding;
        var changes = new (string Label, WorkspaceParkBinding Value)[]
        {
            ("G-78 park", binding with { ParkId = Guid.NewGuid() }),
            ("G-78 action", binding with { ActionId = Guid.NewGuid() }),
            ("G-79 task", binding with { TaskId = Guid.NewGuid() }),
            ("G-80 attempt", binding with { Attempt = binding.Attempt + 1 }),
            ("G-81 block", binding with { BlockEventId = Guid.NewGuid() }),
            ("G-82 token", binding with { TaskConcurrencyToken = Guid.NewGuid() }),
            ("G-83 agent", binding with { AgentId = Guid.NewGuid() }),
            ("G-84 runner", binding with { RunnerId = "replacement" }),
            ("G-85 store", binding with { RunnerStoreId = Guid.NewGuid() }),
            ("G-86 session", binding with { SessionId = Guid.NewGuid() }),
            ("G-87 generation", binding with { AcceptedStartedAt = binding.AcceptedStartedAt.AddSeconds(1) }),
            ("G-88 repository", binding with { RepositoryIdentity = new string('B', 64) }),
            ("G-89 endpoint", binding with { EndpointFingerprint = new string('C', 64) }),
            ("G-90 ref", binding with { FullRef = "refs/heads/master" }),
            ("G-92 report", binding with { ReportDigest = new string('D', 64) }),
            ("baseline", binding with { BaselineSha = new string('e', 40) }),
        };
        foreach (var (label, changed) in changes)
            (await w.AcceptAsync(proof with { Request = proof.Request with { Binding = changed } })).ShouldBeFalse(label);
        foreach (var altered in new[] { proof with { SourceSha = w.Baseline }, proof with { RemoteSha = w.Baseline },
            proof with { Clean = false }, proof with { DescendsFromBaseline = false }, proof with { ReceiptId = Guid.Empty },
            proof with { Request = proof.Request with { Path = w.Repository } } })
            (await w.AcceptAsync(altered)).ShouldBeFalse("G-91 source/receipt alteration");
        (await w.RowAsync()).PublicationReceiptId.ShouldBeNull("wrong receipts never overwrite source intent");
        }
        finally { saveReceipt.TrySetResult(); await prepare; }
        var result = await prepare;
        result.Outcome.ShouldBe(TaskParkPublicationOutcome.Published, result.Reason);
        (await w.AcceptAsync(proof)).ShouldBeTrue("positive control: same source accepted by fresh service");
        (await w.AcceptAsync(proof with { ReceiptId = Guid.NewGuid() })).ShouldBeFalse("immutable receipt id");
        (await w.RowAsync()).SourceSha.ShouldBe(proof.SourceSha);
        (await w.RowAsync()).RunnerSeatReleaseId.ShouldBeNull("publication never reserves release");
        var changedAtIo = false;
        w.Git.Before = async args =>
        {
            if (args[0] != "status" || changedAtIo) return;
            changedAtIo = true;
            await using var db = w.Fixture.Db();
            await db.AgentTaskParks.Where(p => p.Id == w.ParkId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndpointFingerprint, new string('F', 64)));
        };
        (await w.AcceptAsync(proof)).ShouldBeFalse("intent changed during the fresh source read");
        w.Git.Before = null;
        await using (var db = w.Fixture.Db())
            await db.AgentTaskParks.Where(p => p.Id == w.ParkId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndpointFingerprint, proof.Request.Binding.EndpointFingerprint));
        await w.ChangeTaskAsync(t => t.Result += " changed");
        (await w.AcceptAsync(proof)).ShouldBeFalse("G-92 fresh DB report changed");
        await using var verify = w.Fixture.Db();
        (await verify.RunnerSeatReleases.CountAsync()).ShouldBe(0);
    }

    internal sealed class PublicationWorld : IAsyncDisposable
    {
        public BlockedTaskParkFixture Fixture { get; private set; } = null!;
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c1065-publication-" + Guid.NewGuid().ToString("N"));
        public string Repository => System.IO.Path.Combine(Root, "repo");
        public string Origin => System.IO.Path.Combine(Root, "origin.git");
        public string LocalPath => System.IO.Path.Combine(Root, "worktrees", "task-" + Fixture.TaskId.ToString("N")[..8]);
        public string Path { get; private set; } = "";
        public string Baseline { get; private set; } = "";
        public string FullRef => "refs/heads/" + RemoteWorkspaceService.OwnedBranch(Fixture.TaskId);
        public Guid ParkId { get; private set; }
        public ProgressSourceBaseline Source { get; private set; } = null!;
        public ParkGit Git { get; } = new();
        public RepositoryMutationLease Leases { get; private set; } = null!;
        public ParkDirectory Directory { get; private set; } = null!;
        public Func<TaskParkPublicationEvidence, Task>? BeforeReceiptSave { get; set; }
        public IWorkspaceReservationJournal Reservations { get; private set; } = null!;
        public WorkspaceReservationKey Key => WorkspaceReservationKey.For(LocalPath, FullRef, Repository);
        private string? _remoteIdentity;

        public static async Task<PublicationWorld> CreateAsync(WorkspaceMode mode = WorkspaceMode.Worktree, bool remote = false,
            bool sourceLanding = false)
        {
            var w = new PublicationWorld();
            try
            {
                System.IO.Directory.CreateDirectory(w.Root);
                w.Fixture = await BlockedTaskParkFixture.CreateAsync(sourceLanding: sourceLanding);
                w.Fixture.Options.Enabled = true;
                w.Leases = new(w.Git);
                w.Reservations = new WorkspaceReservationJournal(w.Fixture.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
                await w.GitTextAsync(w.Root, "init", "--bare", "-b", "master", w.Origin);
                await w.GitTextAsync(w.Root, "init", "-b", "master", w.Repository);
                await w.ConfigureAsync(w.Repository);
                await File.WriteAllTextAsync(System.IO.Path.Combine(w.Repository, "base.txt"), "base source");
                await w.GitTextAsync(w.Repository, "add", "base.txt");
                await w.GitTextAsync(w.Repository, "commit", "-m", "base");
                await w.GitTextAsync(w.Repository, "remote", "add", "origin", w.Origin);
                await w.GitTextAsync(w.Repository, "push", "origin", "master");
                w.Baseline = await w.GitTextAsync(w.Repository, "rev-parse", "HEAD");
                await w.GitTextAsync(w.Repository, "worktree", "add", "-b", RemoteWorkspaceService.OwnedBranch(w.Fixture.TaskId), w.LocalPath, w.Baseline);
                w.Path = w.LocalPath;
                var common = await w.Git.CommonDirectoryAsync(w.Repository, default);
                w.Source = new(w.Repository, common, w.Fixture.TaskId, w.LocalPath, w.FullRef, w.Baseline,
                    new(ProgressRemoteState.Missing, EndpointFingerprint: BlockedTaskParkingService.Digest(w.Origin)));
                RunnerWorkspaceParkService? runtime = null;
                if (remote)
                {
                    var repo = System.IO.Path.Combine(w.Root, "runner-repo");
                    var allowed = System.IO.Path.Combine(w.Root, "remote");
                    w.Path = System.IO.Path.Combine(allowed, "worktrees", "task-" + w.Fixture.TaskId.ToString("N")[..8]);
                    await w.GitTextAsync(w.Root, "clone", w.Origin, repo);
                    await w.ConfigureAsync(repo);
                    await w.GitTextAsync(repo, "worktree", "add", "-b", RemoteWorkspaceService.OwnedBranch(w.Fixture.TaskId), w.Path, w.Baseline);
                    runtime = new(new RunnerWorkspaceService(repo, allowed, w.Origin, start =>
                    {
                        ParkGit.Isolate(start);
                        return Process.Start(start);
                    }));
                    w._remoteIdentity = RunnerWorkspaceParkService.RepositoryIdentity(await w.Git.CommonDirectoryAsync(repo, default));
                }
                await using (var db = w.Fixture.Db())
                {
                    var task = await db.AgentTasks.SingleAsync(t => t.Id == w.Fixture.TaskId);
                    task.Workspace = mode;
                    task.Role = AgentTaskRole.Code;
                    task.WorktreePath = w.LocalPath;
                    task.WorkingDirectory = w.Repository;
                    task.RepoPath = w.Repository;
                    task.WorktreeBaseSha = w.Baseline;
                    task.WorktreeBranch = RemoteWorkspaceService.OwnedBranch(task.Id);
                    task.RemoteWorktreePath = remote ? w.Path : null;
                    task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(1, DateTime.UtcNow, DateTime.UtcNow, w.Source, null));
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == w.Fixture.SessionId);
                    session.Cwd = w.Path;
                    session.RunnerCwd = w.Path;
                    w.Directory = new(new ParkClient(runtime), remote
                        ? new SessionRunnerOwner(task.RunnerId!, session.RunnerStoreId!.Value, w.Path) : null);
                    await db.SaveChangesAsync();
                }
                w.ParkId = (await w.Fixture.RegisterAsync()).ShouldNotBeNull();
                w.Git.Commands.Clear();
                return w;
            }
            catch { await w.DisposeAsync(); throw; }
        }

        internal TaskParkPublicationService Service(AppDbContext db) => new(db, new(Git, Leases), Directory,
            Reservations, TimeProvider.System, Options.Create(Fixture.Options))
            { BeforeReceiptSaveAsync = BeforeReceiptSave is null ? null : (proof, _) => BeforeReceiptSave(proof) };
        public async Task<TaskParkPublicationResult> PrepareAsync()
        { await using var db = Fixture.Db(); return await Service(db).PrepareAsync(ParkId, _remoteIdentity, default); }
        public async Task<TaskParkPublicationResult> VerifyAsync()
        { await using var db = Fixture.Db(); return await Service(db).VerifyAsync(ParkId, default); }
        public async Task<bool> AcceptAsync(TaskParkPublicationEvidence proof)
        { await using var db = Fixture.Db(); return await Service(db).AcceptAsync(ParkId, proof, default); }
        public async Task<AgentTaskPark> RowAsync()
        { await using var db = Fixture.Db(); return await db.AgentTaskParks.SingleAsync(p => p.Id == ParkId); }
        public async Task EpisodeAsync()
        { await Fixture.NextEpisodeAsync(true); ParkId = (await Fixture.RegisterAsync()).ShouldNotBeNull(); }
        public async Task ChangeTaskAsync(Action<AgentTask> change)
        { await using var db = Fixture.Db(); change(await db.AgentTasks.SingleAsync(t => t.Id == Fixture.TaskId)); await db.SaveChangesAsync(); }
        public async Task<string> CommitAsync(string file)
        {
            await File.WriteAllTextAsync(System.IO.Path.Combine(Path, file), file);
            await GitTextAsync(Path, "add", file);
            await GitTextAsync(Path, "commit", "-m", "truthful WIP " + file);
            return await GitTextAsync(Path, "rev-parse", "HEAD");
        }
        public Task<string> PushAsync() => GitTextAsync(Path, "push", "origin", "HEAD:" + FullRef);
        public async Task<string> RemoteShaAsync() => (await GitTextAsync(Path, "ls-remote", "--refs", Origin, FullRef)).Split('\t')[0];
        public async Task<string> GitTextAsync(string path, params string[] args)
        {
            var result = await Git.RunAsync(path, args, default);
            result.ExitCode.ShouldBe(0, result.Diagnostic);
            return result.Output.Trim();
        }
        private async Task ConfigureAsync(string path)
        {
            await GitTextAsync(path, "config", "user.name", "CARD-1065 test");
            await GitTextAsync(path, "config", "user.email", "test@example.invalid");
            await GitTextAsync(path, "config", "commit.gpgsign", "false");
        }
        public async ValueTask DisposeAsync()
        {
            if (Fixture is not null) await Fixture.DisposeAsync();
            RunnerSettlementSyncTests.SyncWorld.DeleteTree(Root);
        }
    }

    internal sealed class ParkGit : TaskProgressGit
    {
        public List<string[]> Commands { get; } = [];
        public Func<IReadOnlyList<string>, Task>? Before { get; set; }
        protected override void ConfigureProcess(ProcessStartInfo start) => Isolate(start);
        public static void Isolate(ProcessStartInfo start)
        {
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add(arguments.ToArray());
            if (Before is not null) await Before(arguments);
            return await base.RunAsync(repository, arguments, ct);
        }
    }

    internal sealed class ParkDirectory(ParkClient client, SessionRunnerOwner? owner) : ISessionRunnerDirectory
    {
        public SessionRunnerOwner? Owner { get; set; } = owner;
        public bool IdentityCapability { get; set; } = true;
        public List<string?> Resolved { get; } = [];
        public ParkClient Client => client;
        public ISessionRunnerClient Local => client;
        public IReadOnlyList<string> KnownRunnerIds => owner is null ? [] : [owner.RunnerId];
        public ISessionRunnerClient Resolve(string? runnerId) { Resolved.Add(runnerId); return client; }
        public Guid? GetLiveStoreId(string? runnerId) => owner?.RunnerStoreId;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(owner);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(
            Owner is null ? SessionRunnerBinding.Local.Instance : new SessionRunnerBinding.Remote(Owner));
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerDescriptor?>(new(
            runnerId!, "fixture", null, null, true, true, false, null,
            new("fixture", "fixture", "fixture", false, Features: IdentityCapability
                ? [RunnerCapabilityFeatures.WorkspaceParkV1, RunnerCapabilityFeatures.TerminalSeatReleaseV1, RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1]
                : [RunnerCapabilityFeatures.WorkspaceParkV1, RunnerCapabilityFeatures.TerminalSeatReleaseV1])));
    }

    internal sealed class ParkClient(RunnerWorkspaceParkService? runtime) : ISessionRunnerClient
    {
        public int Calls { get; private set; }
        public List<WorkspaceRepositoryIdentityRequest> IdentityCalls { get; } = [];
        public Func<WorkspaceRepositoryIdentityResult, Task<WorkspaceRepositoryIdentityResult>>? AfterIdentity { get; set; }
        public Func<WorkspaceParkCommand, Task>? BeforePublication { get; set; }
        public async Task<WorkspaceRepositoryIdentityResult> ReadWorkspaceRepositoryIdentityAsync(WorkspaceRepositoryIdentityRequest request, CancellationToken ct)
        {
            IdentityCalls.Add(request);
            var result = await runtime!.ReadIdentityAsync(request, request.Path, ct);
            return AfterIdentity is null ? result : await AfterIdentity(result);
        }
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
        public async Task<WorkspaceParkResult> ParkWorkspaceAsync(WorkspaceParkCommand request, CancellationToken ct)
        {
            Calls++;
            if (BeforePublication is not null) await BeforePublication(request);
            return request.Prepare is { } prepare ? await runtime!.PrepareAsync(prepare, ct) : await runtime!.VerifyAsync(request.Verify!, ct);
        }
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException("S4 must never stop a seat");
    }
}
