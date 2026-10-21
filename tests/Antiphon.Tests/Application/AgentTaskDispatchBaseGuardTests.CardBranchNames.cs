using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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

public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    public async Task C1213_MintUsesCardRoleAndRunnerCapability()
    {
        // treat missing/wrong-case capability as supported -> old-runner-branch red.
        foreach (var vector in new[] { "local", "capable", "empty", "null-features", "null-capabilities", "unknown", "near", "case", "fault", "disabled", "cached-feature" })
        {
            using var repo = new ScratchGitRepo("c1213-mint");
            await repo.CommitFileAsync("seed.txt", "seed");
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = CreateContext(schema);
            var card = await SeedCardAsync(db, "CARD-1213");
            var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, null, AgentTaskRole.TestDesign);
            task.RunnerId = vector == "local" ? null : "fixture";
            await db.SaveChangesAsync();
            var directory = new C1213Directory(vector == "disabled" ? "capable" : vector);
            await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, runners: directory, cardBranchNames: vector != "disabled");
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, default);
            var expected = (vector is "local" or "capable" ? "feat/card-1213-testdesign-" : "feat/card-task-") + task.Id.ToString("N")[..8];
            task.WorktreeBranch.ShouldBe(expected, "old-runner-branch " + vector);
            db.Update(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).WorktreeBranch.ShouldBe(expected, "persisted-branch");
            (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "symbolic-ref", "HEAD")).StdOut.Trim().ShouldBe("refs/heads/" + expected);
            Path.GetFileName(task.WorktreePath!).ShouldBe("card-task-" + task.Id.ToString("N")[..8]);
        }

        using var rolesRepo = new ScratchGitRepo("c1213-roles"); await rolesRepo.CommitFileAsync("seed.txt", "seed");
        await using var rolesSchema = await TestDbFixture.CreateIsolatedSchemaAsync(); await using var rolesDb = CreateContext(rolesSchema);
        var rolesCard = await SeedCardAsync(rolesDb, "CARD-1213");
        await using var rolesProvider = CreateProvider(rolesSchema.ConnectionString, rolesRepo.WorktreeRoot, cardBranchNames: true);
        await using var rolesScope = rolesProvider.CreateAsyncScope();
        var roleTokens = new (AgentTaskRole Role, string Stage)[] {
            (AgentTaskRole.Custom, "custom"), (AgentTaskRole.Plan, "plan"), (AgentTaskRole.Code, "code"), (AgentTaskRole.Review, "review"), (AgentTaskRole.Debug, "debug"), (AgentTaskRole.Coverage, "coverage"), (AgentTaskRole.Docs, "docs"), (AgentTaskRole.Commit, "commit"), (AgentTaskRole.Test, "test"), (AgentTaskRole.Deploy, "deploy"), (AgentTaskRole.Merge, "merge"), (AgentTaskRole.Check, "check"), (AgentTaskRole.Distill, "distill"), (AgentTaskRole.Diagnose, "diagnose"), (AgentTaskRole.Investigate, "investigate"), (AgentTaskRole.TestDesign, "testdesign"), (AgentTaskRole.Mutation, "mutation") };
        foreach (var (role, token) in roleTokens)
        {
            var task = new AgentTask { Id = Guid.NewGuid(), CardId = rolesCard.Id, RepoPath = rolesRepo.Path, Role = role };
            await rolesScope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, default);
            task.WorktreeBranch.ShouldBe("feat/card-1213-" + token + "-" + task.Id.ToString("N")[..8], "role-token");
        }
        var orch = new AgentTask { Id = Guid.NewGuid(), CardId = rolesCard.Id, RepoPath = rolesRepo.Path, Kind = AgentTaskKind.Orchestrator, Role = AgentTaskRole.Plan };
        await rolesScope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(orch, default);
        orch.WorktreeBranch.ShouldBe("feat/card-1213-orch-" + orch.Id.ToString("N")[..8], "orchestrator-token");
    }

    [Test]
    public async Task C1213_UnboundAndUnknownMintLegacy()
    {
        // force a new name for missing metadata/role -> fallback-branch red.
        foreach (var vector in new[] { "valid", "unbound", "missing-row", "foreign-card", "long-card", "unicode-card", "unknown-role", "missing-db" })
        {
            using var repo = new ScratchGitRepo("c1213-unbound"); await repo.CommitFileAsync("seed.txt", "seed");
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = CreateContext(schema);
            var card = await SeedCardAsync(db, vector switch { "foreign-card" => "E03-003", "long-card" => "CARD-123456789", "unicode-card" => "CARD-１２１３", _ => "CARD-1213" });
            var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, null);
            if (vector == "unbound") task.CardId = null;
            if (vector == "missing-row") task.CardId = Guid.NewGuid();
            if (vector == "unknown-role") task.Role = (AgentTaskRole)999;
            await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, cardBranchNames: true);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
            if (vector == "missing-db")
            {
                var ctor = typeof(DelegationWorktreeService).GetConstructors().Single();
                service = (DelegationWorktreeService)ctor.Invoke(ctor.GetParameters().Select(p => p.Name == "db" ? null : scope.ServiceProvider.GetService(p.ParameterType) ?? p.DefaultValue).ToArray());
            }
            await service.CreateForTaskAsync(task, default);
            task.WorktreeBranch.ShouldBe((vector == "valid" ? "feat/card-1213-code-" : "feat/card-task-") + task.Id.ToString("N")[..8], "fallback-branch " + vector);
        }
    }

    [Test]
    public async Task C1213_RetryAdoptsRecordedBranch()
    {
        // replace adopted coordinates with newly minted branch -> adopted-branch or HEAD/file red.
        foreach (var modern in new[] { false, true })
        foreach (var race in new[] { false, true })
        {
            using var repo = new ScratchGitRepo("c1213-adopt"); await repo.CommitFileAsync("seed.txt", "seed");
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync(); await using var db = CreateContext(schema);
            var card = await SeedCardAsync(db, "CARD-1213"); var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, null);
            var branch = (modern ? "feat/card-1213-code-" : "feat/card-task-") + task.Id.ToString("N")[..8];
            var path = Path.Combine(repo.WorktreeRoot, "card-task-" + task.Id.ToString("N")[..8]);
            async Task CreateExistingAsync()
            {
                await repo.GitAsync("worktree", "add", "-b", branch, path, "HEAD");
                await File.WriteAllTextAsync(Path.Combine(path, "sentinel.txt"), "retained retry bytes");
            }
            if (!race) await CreateExistingAsync();
            var before = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
            task.WorktreeBaseRef = "refs/heads/expired";
            await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, runners: new C1213Directory("empty"), cardBranchNames: !modern, worktreeFactory: race ? sp => new C1213RaceWorktrees(
                new WorktreeManager(sp.GetRequiredService<IOptions<GitSettings>>(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorktreeManager>.Instance, leases: sp.GetRequiredService<IRepositoryMutationLease>(), landingGit: sp.GetRequiredService<ILandingGit>()), CreateExistingAsync) : null);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, default);
            task.WorktreeBranch.ShouldBe(branch, "adopted-branch"); task.WorktreePath.ShouldBe(path);
            (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(before);
            File.ReadAllText(Path.Combine(path, "sentinel.txt")).ShouldBe("retained retry bytes");
        }
    }

    [Test]
    public async Task C1213_NullBranchFindsEitherKeptCandidate(CancellationToken ct)
    {
        // probe only one form -> dispatcher reselects an expired source instead of adoption.
        foreach (var modern in new[] { false, true })
        {
            using var repo = new ScratchGitRepo("c1213-null"); await repo.CommitFileAsync("seed.txt", "seed");
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync(); await using var db = CreateContext(schema);
            var card = await SeedCardAsync(db, "CARD-1213");
            var source = await SeedKeptSiblingAsync(db, repo, card.Id, "source"); await db.SaveChangesAsync(ct);
            AgentTaskCreatedDto created;
            await using (var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, cardBranchNames: true))
            await using (var scope = provider.CreateAsyncScope())
                created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                    new CreateAgentTaskRequest("adopt retry", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, Card: card.Id.ToString("D"), WorktreeBaseTask: source.Id.ToString("D")), new(null, null, repo.Path), ct);
            var branch = (modern ? "feat/card-1213-code-" : "feat/card-task-") + created.Id.ToString("N")[..8];
            var path = Path.Combine(repo.WorktreeRoot, "card-task-" + created.Id.ToString("N")[..8]);
            var sha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
            await repo.GitAsync("worktree", "add", "-b", branch, path, source.WorktreeBranch!);
            await repo.GitAsync("branch", "-D", source.WorktreeBranch!);
            await using (var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, cardBranchNames: true))
            await using (var scope = provider.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(ct);
            db.ChangeTracker.Clear(); var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, "kept-adopted: " + task.FailureReason);
            task.WorktreeBranch.ShouldBe(branch); task.WorktreePath.ShouldBe(path);
            (await ScratchGitRepo.GitInAsync(path, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(sha);
        }
    }

    [Test]
    public async Task C1213_SourceLandingKeepsLegacyIdentity()
    {
        // routing snapshot through ordinary mint -> snapshot creation/identity validation refuses.
        using var repo = new ScratchGitRepo("c1213-snapshot"); await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync(); await using var db = CreateContext(schema);
        var sourceCard = await SeedCardAsync(db, "CARD-1213");
        var companion = new Card { Id = Guid.NewGuid(), BoardId = sourceCard.BoardId, BoardColumnId = sourceCard.BoardColumnId, Identifier = "CARD-1214", Title = "companion", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Cards.Add(companion);
        var source = await SeedQueuedWorktreeTaskAsync(db, repo.Path, sourceCard.Id, null);
        source.Status = AgentTaskStatus.Succeeded;
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var opId = Guid.NewGuid(); var common = Path.Combine(repo.Path, ".git");
        var publication = new AgentTaskLanding { Id = opId, TaskId = source.Id, SchemaVersion = 1, Publication = LandPublicationOutcome.Landed,
            RepositoryPath = repo.Path, CommonDirectory = common, WorktreePath = repo.Path, GitDirectory = common, SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master", OriginalSourceSha = sha, TargetBeforeSha = sha, VerifiedSourceSha = sha, ObservedRemoteTargetSha = sha,
            RemoteFingerprint = new string('a', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow, SourcePinned = true, TargetPinned = true, VerificationSkipReason = "exact_remote_containment", ConfirmationMethod = "push-endpoint-read-fetch-ancestry", RecoveryRefPrefix = $"refs/antiphon/land/{source.Id:N}/{opId:N}" };
        new AgentTaskLandingState().HasPublication(publication).ShouldBeTrue(); db.AgentTaskLandings.Add(publication); await db.SaveChangesAsync();
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, runners: new C1213Directory("capable"), cardBranchNames: true);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        var ordinary = new AgentTask { Id = Guid.NewGuid(), CardId = sourceCard.Id, RepoPath = repo.Path, Role = AgentTaskRole.Code };
        await service.CreateForTaskAsync(ordinary, default);
        ordinary.WorktreeBranch.ShouldBe("feat/card-1213-code-" + ordinary.Id.ToString("N")[..8], "ordinary-new-control");
        var task = new AgentTask { Id = Guid.NewGuid(), CardId = companion.Id, RepoPath = repo.Path, Role = AgentTaskRole.Mutation, Workspace = WorkspaceMode.Worktree, SourceLandingOperationId = opId, SourceLandingSha = sha };
        await service.CreateForTaskAsync(task, default);
        task.WorktreeBranch.ShouldBe("feat/card-task-" + task.Id.ToString("N")[..8], "snapshot-created");
        await using (var lease = (await scope.ServiceProvider.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(repo.Path, default)).ShouldNotBeNull())
        {
        await service.ValidateVerificationAsync(task, lease, default);
        task.WorktreeBranch = "feat/card-1214-mutation-" + task.Id.ToString("N")[..8];
        await Should.ThrowAsync<ConflictException>(() => service.ValidateVerificationAsync(task, lease, default));
        }
        // The remote arm drives RemoteVerificationWorkspace, which admits only POSIX runner paths
        // (the remote runner is Linux-only), against real temp directories (CARD-1415).
        if (OperatingSystem.IsWindows())
            Skip.Test("CARD-1415: the remote snapshot arm needs POSIX runner paths; the local arm above passed.");
        await repo.AddBareOriginAsync();
        var origin = (await repo.GitReadAsync("remote", "get-url", "origin")).Trim();
        var remoteRoot = Directory.CreateTempSubdirectory("c1213-snapshot-recipient").FullName;
        try
        {
            var remoteRepo = Path.Combine(remoteRoot, "repo"); var allowed = Path.Combine(remoteRoot, "work");
            (await ScratchGitRepo.GitInAsync(repo.Path, "clone", "-b", "master", origin, remoteRepo)).Ok.ShouldBeTrue();
            Directory.CreateDirectory(allowed);
            var runner = new RunnerWorkspaceService(remoteRepo, allowed, origin, System.Diagnostics.Process.Start);
            var workspace = new Antiphon.Server.Infrastructure.Agents.SessionRunner.RemoteVerificationWorkspace(new C1213SnapshotTransport(runner), remoteRepo, allowed);
            await using var remoteProvider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, runners: new C1213Directory("capable"), cardBranchNames: true, verificationWorkspaces: new C1213SnapshotDirectory(workspace));
            await using var remoteScope = remoteProvider.CreateAsyncScope();
            var remoteService = remoteScope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
            var remoteTask = new AgentTask { Id = Guid.NewGuid(), CardId = companion.Id, RepoPath = repo.Path, Role = AgentTaskRole.Mutation, Workspace = WorkspaceMode.Worktree, RunnerId = "fixture", SourceLandingOperationId = opId, SourceLandingSha = sha };
            await remoteService.CreateForTaskAsync(remoteTask, default);
            remoteTask.WorktreeBranch.ShouldBe("feat/card-task-" + remoteTask.Id.ToString("N")[..8], "remote-snapshot-created");
            (await ScratchGitRepo.GitInAsync(remoteTask.WorktreePath!, "symbolic-ref", "HEAD")).StdOut.Trim().ShouldBe("refs/heads/" + remoteTask.WorktreeBranch);
            await using var remoteLease = (await remoteScope.ServiceProvider.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(repo.Path, default)).ShouldNotBeNull();
            await remoteService.ValidateVerificationAsync(remoteTask, remoteLease, default);
            remoteTask.WorktreeBranch = "feat/card-1214-mutation-" + remoteTask.Id.ToString("N")[..8];
            await Should.ThrowAsync<ConflictException>(() => remoteService.ValidateVerificationAsync(remoteTask, remoteLease, default));
        }
        finally { ReadOnlyDirectory.Delete(remoteRoot); }

    }

    private sealed class C1213RaceWorktrees(IWorktreeManager inner, Func<Task> create) : IWorktreeManager
    {
        private int _lists;
        public async Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repo, CancellationToken ct)
        {
            var observed = await inner.ListAsync(repo, ct);
            if (_lists++ == 0) await create();
            return observed;
        }
        public Task<WorktreeInfo> CreateAsync(string repo, string id, string @base, CancellationToken ct) => inner.CreateAsync(repo, id, @base, ct);
        public Task<WorktreeInfo> CreateAsync(string repo, string id, string @base, RepositoryLease lease, CancellationToken ct) => inner.CreateAsync(repo, id, @base, lease, ct);
        public Task<WorktreeInfo> CreateAsync(string repo, string id, string @base, RepositoryLease lease, string? branch, CancellationToken ct) => inner.CreateAsync(repo, id, @base, lease, branch, ct);
        public Task RemoveAsync(string repo, string path, CancellationToken ct) => throw new NotSupportedException();
        public Task TouchAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class C1213SnapshotDirectory(IVerificationWorkspace workspace) : IVerificationWorkspaceDirectory
    { public IVerificationWorkspace Resolve(string? runnerId) => workspace; }
    private sealed class C1213SnapshotTransport(RunnerWorkspaceService runner) : IVerificationWorkspaceTransport
    {
        public Task<PhoneHomeVerificationCreateResponse> CreateAsync(PhoneHomeVerificationCreateRequest r, CancellationToken ct) => runner.CreateVerificationAsync(r, ct);
        public Task<PhoneHomeVerificationValidateResponse> ValidateAsync(PhoneHomeVerificationValidateRequest r, CancellationToken ct) => runner.ValidateVerificationAsync(r, ct);
        public Task<PhoneHomeVerificationInspectResponse> InspectAsync(PhoneHomeVerificationInspectRequest r, CancellationToken ct) => runner.InspectVerificationAsync(r, ct);
        public Task<PhoneHomeVerificationReadRestorationResponse> ReadRestorationAsync(PhoneHomeVerificationReadRestorationRequest r, CancellationToken ct) => throw new NotSupportedException("ordinary creation proof only");
        public Task<PhoneHomeVerificationRemoveResponse> RemoveAsync(PhoneHomeVerificationRemoveRequest r, CancellationToken ct) => throw new NotSupportedException("ordinary creation proof only");
    }

    private sealed class C1213Directory(string vector) : ISessionRunnerDirectory
    {
        private readonly FakeSessionRunnerClient _client = new() { VerificationStoreId = Guid.NewGuid(), ExtraFeatures = vector == "capable" ? ["workspaceCardBranchV1"] : [] };
        public ISessionRunnerClient Local => _client;
        public ISessionRunnerClient Resolve(string? runnerId) => _client;
        public IReadOnlyList<string> KnownRunnerIds => ["fixture"];
        public Guid? GetLiveStoreId(string? runnerId) => _client.VerificationStoreId;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            if (vector == "fault") throw new IOException("capability unavailable");
            if (vector == "unknown") return Task.FromResult<RunnerDescriptor?>(null);
            RunnerCapabilitiesDto? caps = vector == "null-capabilities" ? null : new("test", "test", "test", false, Features: vector switch { "capable" or "cached-feature" => ["workspaceCardBranchV1"], "near" => ["workspaceCardBranchV10"], "case" => ["WorkspaceCardBranchV1"], "null-features" => null, _ => [] });
            return Task.FromResult<RunnerDescriptor?>(new("fixture", "fixture", "linux", null, true, true, false, 2, caps));
        }
    }
}
