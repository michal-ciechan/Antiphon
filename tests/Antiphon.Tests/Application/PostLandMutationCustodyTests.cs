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
    public async Task C478_ConfirmedSourceCreatesExactSnapshotAndRetainsIdentity()
    {
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync();
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
        await using var world = await World.CreateAsync(native: true);
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

    private sealed class World : IAsyncDisposable
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
        public AgentTaskService TaskService(IServiceProvider services) => new(services.GetRequiredService<AppDbContext>(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance), Options.Create(new DelegationSettings()),
            new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance,
            sourceLanding: services.GetRequiredService<SourceLandingAdmission>());

        public static async Task<World> CreateAsync(bool native = false)
        {
            var world = new World();
            world.Runner = native
                ? new DirectSessionRunnerClient(Path.Combine(world.Host.Fixture.Root, "runner"), "modern") { AdvertiseVerificationCustody = true }
                : new FakeSessionRunnerClient { VerificationStoreId = Guid.NewGuid() };
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
        public async ValueTask DisposeAsync()
        {
            if (Runner is IAsyncDisposable disposable) await disposable.DisposeAsync();
            await Host.DisposeAsync();
        }
    }
}
