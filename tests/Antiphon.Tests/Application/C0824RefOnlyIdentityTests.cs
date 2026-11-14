using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional multi-step schema/identity budget.
public sealed class C0824RefOnlyIdentityTests
{
    [Test]
    public async Task Mode_round_trips_with_legacy_default()
    {
        // Assert representation before touching new columns: absent mapping must be assertion-red, never SQL setup red.
        await using (var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseNpgsql("Host=localhost;Database=metadata_only").Options))
        {
            var task = model.Model.FindEntityType(typeof(AgentTask))!;
            task.FindProperty(nameof(AgentTask.DesktopWorkspaceKind)).ShouldNotBeNull("persisted-mode=RefOnly");
            Convert.ToInt32(task.FindProperty(nameof(AgentTask.DesktopWorkspaceKind))!.GetDefaultValue())
                .ShouldBe((int)DesktopWorkspaceKind.Checkout, "legacy-default=Checkout");
            task.FindProperty(nameof(AgentTask.TaskRefWorkspaceBindingId)).ShouldNotBeNull("persisted-binding=expected");
            var bindingModel = model.Model.FindEntityType(typeof(TaskRefWorkspaceBinding)).ShouldNotBeNull("durable-binding-schema");
            bindingModel.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(TaskRefWorkspaceBinding.TaskId)]))
                .ShouldBeTrue("duplicate-task-rejected");
            bindingModel.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(TaskRefWorkspaceBinding.RepositoryPath), nameof(TaskRefWorkspaceBinding.FullRef)]))
                .ShouldBeTrue("duplicate-ref-rejected");
        }
        var migrations = MigrationTestDatabase.Names();
        var migration = Array.FindIndex(migrations, m => m.EndsWith("_Card0824RefOnlyDesktopCoordinates", StringComparison.Ordinal));
        migration.ShouldBeGreaterThan(0, "generated-upgrade-present");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var id = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            // The class budget is 60s. A full walk from an empty database does not fit it.
            // Drop only the throwing host-load suffix, then rewind the reversible predecessor.
            await SessionTranscriptSummaryMigrationFixture.DetachFromOwnedCloneAsync(schema);
            await MigrationTestDatabase.DetachHostLoadAsync(db);
            await db.Database.GetService<IMigrator>().MigrateAsync(migrations[migration - 1]);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel",
                    "Attempt", "MaxAttempts", "Workspace", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                    "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd")
                VALUES ({id}, {id}, {0}, {"legacy"}, {"preserve"}, {0}, {0}, {0}, {1}, {2}, {1},
                    {"/tmp/c0824-legacy"}, {false}, {0}, {0}, {Guid.NewGuid()}, {DateTime.UtcNow}, {0L}, {0L}, {0m})
                """);
            await db.Database.GetService<IMigrator>().MigrateAsync();
        }
        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var legacy = await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        legacy.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.Checkout, "legacy-default=Checkout");
        legacy.TaskRefWorkspaceBindingId.ShouldBeNull();

        var (taskRow, binding) = await SeedAsync(fresh, Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "c0824-roundtrip-" + id.ToString("N")));
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = taskRow.Id, SourceDesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly,
            SourceTaskRefWorkspaceBindingId = binding.Id, GitDirectory = null,
        };
        fresh.AgentTaskLandings.Add(op);
        await fresh.SaveChangesAsync();
        await using (var reader = NewDb(schema))
        {
            var loaded = await reader.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskRow.Id);
            loaded.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly, "persisted-mode=RefOnly");
            loaded.TaskRefWorkspaceBindingId.ShouldBe(binding.Id, "persisted-binding=expected");
            var durable = await reader.TaskRefWorkspaceBindings.AsNoTracking().SingleAsync(b => b.Id == binding.Id);
            durable.SelectedBaseSha.ShouldBe(BaseSha);
            durable.SelectedBaseRef.ShouldBe("refs/heads/master");
            durable.SelectedBaseSource.ShouldBe(WorktreeBaseSource.Explicit);
            durable.PreparationState.ShouldBe(TaskRefWorkspacePreparationState.Published);
            durable.Revision.ShouldBe(7);
            var landed = await reader.AgentTaskLandings.AsNoTracking().SingleAsync(l => l.Id == op.Id);
            landed.SourceDesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            landed.SourceTaskRefWorkspaceBindingId.ShouldBe(binding.Id);
            landed.GitDirectory.ShouldBeNull();
        }
        await using (var duplicateTask = NewDb(schema))
        {
            duplicateTask.TaskRefWorkspaceBindings.Add(Copy(binding, Guid.NewGuid(), binding.TaskId, "refs/heads/other"));
            (await Should.ThrowAsync<DbUpdateException>(() => duplicateTask.SaveChangesAsync(), "duplicate-task-rejected"))
                .InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }
        await using (var duplicateRef = NewDb(schema))
        {
            var otherId = Guid.NewGuid();
            duplicateRef.AgentTasks.Add(new AgentTask { Id = otherId, RootTaskId = otherId, WorkingDirectory = binding.RepositoryPath });
            await duplicateRef.SaveChangesAsync();
            duplicateRef.TaskRefWorkspaceBindings.Add(Copy(binding, Guid.NewGuid(), otherId, binding.FullRef));
            (await Should.ThrowAsync<DbUpdateException>(() => duplicateRef.SaveChangesAsync(), "duplicate-ref-rejected"))
                .InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }
        (await fresh.TaskRefWorkspaceBindings.AsNoTracking().CountAsync()).ShouldBe(1, "original-binding-retained");
        var legacyBranch = "feat/card-task-legacy-collision";
        var oldId = Guid.NewGuid();
        fresh.AgentTasks.Add(new AgentTask { Id = oldId, RootTaskId = oldId, WorkingDirectory = binding.RepositoryPath,
            RepoPath = binding.RepositoryPath, WorktreeBranch = legacyBranch, WorktreePath = Path.Combine(binding.RepositoryPath, "old-checkout") });
        var proposedId = Guid.NewGuid();
        var proposed = new AgentTask { Id = proposedId, RootTaskId = proposedId, WorkingDirectory = binding.RepositoryPath,
            RepoPath = binding.RepositoryPath, WorktreeBranch = legacyBranch, WorktreeBaseSha = BaseSha,
            WorktreeBaseRef = "refs/heads/master", WorktreeBaseSource = WorktreeBaseSource.Explicit,
            Workspace = WorkspaceMode.Worktree, RunnerId = "fixture-runner", Role = AgentTaskRole.Plan };
        fresh.AgentTasks.Add(proposed);
        await fresh.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => new TaskWorkspaceContextResolver(fresh)
            .RecordPlannedAsync(proposed, binding.CommonDirectory, ScratchRoot(), CancellationToken.None), "legacy-collision-refused"))
            .Code.ShouldBe("ref_only_legacy_ref_owner_conflict");

        // A binding-only owner must be found using the host's canonical path comparer,
        // including trailing separators. The legacy task-branch fence is deliberately absent.
        await using var owned = await RefOnlySyncFixture.CreateAsync();
        await owned.Db.AgentTasks.Where(t => t.Id == owned.Task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.WorktreeBranch, (string?)null));
        await owned.Db.TaskRefWorkspaceBindings.Where(b => b.Id == owned.Binding.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.RepositoryPath, owned.Git.Desktop + Path.DirectorySeparatorChar));
        var collisionId = Guid.ParseExact(owned.Task.Id.ToString("N")[..8] + "111111111111111111111111", "N");
        var collision = new AgentTask
        {
            Id = collisionId, RootTaskId = collisionId, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = owned.Git.Desktop, RepoPath = owned.Git.Desktop,
            RunnerId = owned.Task.RunnerId, WorktreeBranch = owned.Git.Branch, WorktreeBaseSha = owned.Git.Baseline,
            WorktreeBaseRef = "refs/heads/master", WorktreeBaseSource = WorktreeBaseSource.Explicit,
        };
        owned.Db.AgentTasks.Add(collision); await owned.Db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => new TaskWorkspaceContextResolver(owned.Db)
            .RecordPlannedAsync(collision, owned.Binding.CommonDirectory, owned.Git.Root, default), "binding-only-canonical-owner"))
            .Code.ShouldBe("ref_only_binding_owner_conflict");
        (await owned.Db.TaskRefWorkspaceBindings.AsNoTracking().CountAsync(b => b.FullRef == owned.Git.FullRef)).ShouldBe(1, "one-canonical-ref-owner");
        Directory.Exists(Path.Combine(owned.Git.Root, ".ref-only", collisionId.ToString("N"))).ShouldBeFalse();
    }

    [Test]
    public async Task Identity_is_unique_stable_and_never_created()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var resolver = new TaskWorkspaceContextResolver(db);
        var root = Path.Combine(Path.GetTempPath(), "c0824-identity-" + Guid.NewGuid().ToString("N"));
        var leftId = Guid.Parse("12345678-0000-0000-0000-000000000001");
        var rightId = Guid.Parse("12345678-0000-0000-0000-000000000002");
        var leftIdentity = resolver.IdentityForNewBinding(root, leftId);
        var rightIdentity = resolver.IdentityForNewBinding(root, rightId);
        leftIdentity.ShouldNotBe(rightIdentity, "identity-distinct");
        leftIdentity.ShouldBe(Path.Combine(root, ".ref-only", leftId.ToString("N")), "full-guid-identity");
        Path.IsPathFullyQualified(leftIdentity).ShouldBeTrue();
        var (left, _) = await SeedAsync(db, leftId, root, branch: "feat/card-824-code-12345678");
        var (right, _) = await SeedAsync(db, rightId, root, branch: "feat/card-824-plan-12345678");
        (await resolver.ResolveAsync(left, CancellationToken.None)).IdentityPath.ShouldBe(leftIdentity);
        (await resolver.ResolveAsync(right, CancellationToken.None)).IdentityPath.ShouldBe(rightIdentity);
        await using var restarted = NewDb(schema);
        var changedRoot = root + "-changed";
        var again = await new TaskWorkspaceContextResolver(restarted).ResolveAsync(
            await restarted.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == leftId), CancellationToken.None);
        again.IdentityPath.ShouldBe(leftIdentity, "identity-stable");
        again.IdentityPath.ShouldNotBe(resolver.IdentityForNewBinding(changedRoot, leftId));
        Path.Exists(root).ShouldBeFalse("identity-never-created");
        Path.Exists(Path.GetDirectoryName(leftIdentity)!).ShouldBeFalse("identity-parent-never-created");
        Path.Exists(changedRoot).ShouldBeFalse();
    }

    [Test]
    public async Task Missing_repository_never_uses_identity_as_git_cwd()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var (task, binding) = await SeedAsync(db, Guid.NewGuid(), ScratchRoot());
        var resolver = new TaskWorkspaceContextResolver(db);
        var valid = await resolver.ResolveAsync(task, CancellationToken.None);
        valid.GitWorkingDirectory.ShouldBe(task.RepoPath, "no-identity-git");
        valid.ExecutionDirectory.ShouldBe("/runner/isolated/task", "runner-execution-coordinate");
        valid.IdentityPath.ShouldNotBe(valid.GitWorkingDirectory);
        foreach (var path in new string?[] { null, "" })
        {
            task.RepoPath = path;
            (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
                .Code.ShouldBe("ref_only_repository_required", "no-identity-git");
        }
        task.RepoPath = binding.RepositoryPath;
        task.WorktreePath = null;
        (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
            .Code.ShouldBe("source_coordinates_missing", "null-identity-refused");
        task.WorktreePath = binding.IdentityPath;
        await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.CommonDirectory, ""));
        (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
            .Code.ShouldBe("ref_only_common_directory_required", "common-required");
        Path.Exists(binding.IdentityPath).ShouldBeFalse("no-main-file-io");
        await SourceCallerAsync("valid", "null-repository", "empty-repository", "null-identity", "missing-common");
    }

    [Test]
    public async Task Mode_does_not_follow_directory_presence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var (task, binding) = await SeedAsync(db, Guid.NewGuid(), ScratchRoot());
        var resolver = new TaskWorkspaceContextResolver(db);
        (await resolver.ResolveAsync(task, CancellationToken.None)).Kind.ShouldBe(DesktopWorkspaceKind.RefOnly, "kind-from-row");
        Directory.CreateDirectory(binding.IdentityPath);
        var sentinel = Path.Combine(binding.IdentityPath, "foreign.txt");
        try
        {
            await File.WriteAllTextAsync(sentinel, "foreign bytes");
            (await resolver.ResolveAsync(task, CancellationToken.None)).Kind.ShouldBe(DesktopWorkspaceKind.RefOnly, "kind-from-row:decoy");
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("foreign bytes");
        }
        finally
        {
            File.Delete(sentinel); Directory.Delete(binding.IdentityPath);
            Directory.Delete(Path.GetDirectoryName(binding.IdentityPath)!); Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(binding.IdentityPath)!)!);
        }
        task.DesktopWorkspaceKind = (DesktopWorkspaceKind)91;
        (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
            .Code.ShouldBe("desktop_workspace_kind_invalid", "unknown-mode-refused");
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout;
        (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
            .Code.ShouldBe("desktop_workspace_binding_contradictory", "contradictory-binding-refused");
        task.TaskRefWorkspaceBindingId = null;
        (await resolver.ResolveAsync(task, CancellationToken.None)).Kind.ShouldBe(DesktopWorkspaceKind.Checkout, "legacy-kind=Checkout");
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly;
        (await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None)))
            .Code.ShouldBe("ref_only_binding_required", "binding-required");
        await SourceCallerAsync("valid", "valid-decoy");
    }

    [Test]
    public async Task Binding_survives_attempt_and_settings_changes()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h);
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        var initial = await new TaskWorkspaceContextResolver(db).ResolveAsync(task, default);
        initial.Kind.ShouldBe(DesktopWorkspaceKind.RefOnly, "existing-mode=RefOnly");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "move main after base selection");
        var currentMain = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        currentMain.ShouldNotBe(h.Fixture.SeedSha, "discriminating-main-base");
        task.Attempt++; await db.SaveChangesAsync();
        await using var fresh = h.CreateContext();
        var durable = await fresh.AgentTasks.SingleAsync(t => t.Id == task.Id);
        var settings = new MutableMonitor<RefOnlyDesktopSettings>(new() { Enabled = false, RunnerIds = [], Roles = "none" });
        var gate = new RefOnlyDesktopGate(settings, NullLogger<RefOnlyDesktopGate>.Instance);
        gate.Admits(durable).ShouldBeFalse();
        settings.Fault = new Microsoft.Extensions.Options.OptionsValidationException("",
            typeof(RefOnlyDesktopSettings), [RefOnlyDesktopSettings.SectionName + ":Roles invalid"]);
        gate.Admits(durable).ShouldBeFalse("invalid-admission-does-not-change-recorded-mode");
        var resolver = new TaskWorkspaceContextResolver(fresh);
        resolver.IdentityForNewBinding(h.Fixture.Root + "-new", task.Id).ShouldNotBe(binding.Identity);
        var resolved = await resolver.ResolveAsync(durable, default);
        resolved.ShouldBe(initial, "binding-unchanged");
        resolved.BaseSha.ShouldBe(h.Fixture.SeedSha, "base-unchanged");
        durable.TaskRefWorkspaceBindingId.ShouldBe(binding.Id);
        durable.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Explicit);
        await fresh.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.SelectedBaseSha, new string('b', 40)));
        await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(durable, default), "failed-recorded-preparation-holds");
        (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        Path.Exists(binding.Identity).ShouldBeFalse("no-checkout-fallback");
    }

    [Test]
    public async Task Foreign_binding_refuses_before_git_or_file_io()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var resolver = new TaskWorkspaceContextResolver(db);
        foreach (var variant in new[] { "task", "binding-id", "repository", "ref", "identity", "base" })
        {
            var (task, binding) = await SeedAsync(db, Guid.NewGuid(), ScratchRoot());
            if (variant == "task")
            {
                var other = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask { Id = other, RootTaskId = other, WorkingDirectory = binding.RepositoryPath });
                await db.SaveChangesAsync();
                await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.TaskId, other));
            }
            if (variant == "binding-id") task.TaskRefWorkspaceBindingId = Guid.NewGuid();
            if (variant == "repository")
                await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.RepositoryPath, binding.RepositoryPath + "-other"));
            if (variant == "ref")
                await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.FullRef, "refs/heads/foreign"));
            if (variant == "identity")
                await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.IdentityPath, binding.IdentityPath + "-other"));
            if (variant == "base")
                await db.TaskRefWorkspaceBindings.Where(b => b.Id == binding.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.SelectedBaseSha, new string('b', 40)));
            await Should.ThrowAsync<ConflictException>(() => resolver.ResolveAsync(task, CancellationToken.None), "foreign-" + variant + "-refused");
            Path.Exists(binding.IdentityPath).ShouldBeFalse("no-identity-io:" + variant);
            (await db.TaskRefWorkspaceBindings.AsNoTracking().CountAsync(b => b.Id == binding.Id)).ShouldBe(1, "binding-retained");
            db.ChangeTracker.Clear();
        }
        await SourceCallerAsync("task", "binding-id", "repository", "common", "ref", "identity", "base");
    }

    [Test]
    public async Task Path_equality_consumers_keep_task_and_runner_ownership_distinct()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var root = ScratchRoot();
        var (left, _) = await SeedAsync(db, Guid.NewGuid(), root);
        var (right, _) = await SeedAsync(db, Guid.NewGuid(), root);
        left.Status = right.Status = AgentTaskStatus.Working;
        left.RemoteWorktreePath = "/runner/isolated/left"; right.RemoteWorktreePath = "/runner/isolated/right";
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddScoped(_ => NewDb(schema)); services.AddDelegationWorktreeGraph();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>();
        var admission = new WorkspaceUseAdmission(journal, db);
        var lc = await new TaskWorkspaceContextResolver(db).ResolveAsync(left, CancellationToken.None);
        var rc = await new TaskWorkspaceContextResolver(db).ResolveAsync(right, CancellationToken.None);
        var lk = WorkspaceReservationKey.For(lc.IdentityPath, lc.FullRef, left.RepoPath!);
        var rk = WorkspaceReservationKey.For(rc.IdentityPath, rc.FullRef, right.RepoPath!);
        lk.CanonicalPath.ShouldBe(WorkspaceReservationKey.NormalizePath(left.WorktreePath), "reservation-distinct");
        rk.CanonicalPath.ShouldBe(WorkspaceReservationKey.NormalizePath(right.WorktreePath), "reservation-distinct");
        lk.CanonicalPath.ShouldNotBe(rk.CanonicalPath, "reservation-distinct");
        WorkspaceReservationKey.Same(lk, rk).ShouldBeFalse("reservation-distinct");
        (await admission.HasLiveTaskConsumerAsync(lc.IdentityPath, lc.FullRef, left.Id, CancellationToken.None)).ShouldBeFalse();
        await admission.RequireConsumerAsync(new(lk, WorkspaceReservationKind.Launch, left.Id, null, null), CancellationToken.None);
        await admission.RequireConsumerAsync(new(rk, WorkspaceReservationKind.Launch, right.Id, null, null), CancellationToken.None);
        right.WorktreeBranch = left.WorktreeBranch;
        await db.SaveChangesAsync();
        (await admission.HasLiveTaskConsumerAsync(lc.IdentityPath, lc.FullRef, left.Id, CancellationToken.None)).ShouldBeTrue("same-ref-fenced");
        right.WorktreeBranch = rc.FullRef[11..];
        left.RemoteWorkspaceSlotId = right.RemoteWorkspaceSlotId = "ws-fixture";
        left.RemoteWorkspaceSlotGeneration = right.RemoteWorkspaceSlotGeneration = 4;
        await db.SaveChangesAsync();
        (await TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync(db, left, CancellationToken.None)).ShouldBeTrue("same-generation-ambiguous");
        right.RemoteWorkspaceSlotGeneration = 5;
        await db.SaveChangesAsync();
        (await TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync(db, left, CancellationToken.None)).ShouldBeFalse("next-generation-distinct");
        Path.Exists(left.WorktreePath!).ShouldBeFalse("desktop-absence-no-seat-authority");
        left.Role = AgentTaskRole.Code; right.Role = AgentTaskRole.Docs; right.Status = AgentTaskStatus.Succeeded;
        var review = new StageOutcome
        {
            Stage = OrchestrationStage.Review, Source = StageOutcomeSource.Delegate, Outcome = StageOutcomeKind.Clean,
            ReviewedSourceClean = true, CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full,
            SubjectTaskId = left.Id, ReviewedSourceRef = lc.FullRef, ReviewedRepositoryPath = left.RepoPath,
            ReviewedSourceSha = BaseSha,
        };
        var followUp = new AdvisoryFollowUp(left, review, right, BaseSha, BaseSha, new string('b', 40));
        var narrative = new AdvisoryDelta("docs/notes/c0824-followup.md", "Earlier observation.", "Corrected observation.", AdvisoryContent.Narrative);
        var policy = new AdvisoryChangePolicy();
        policy.CanPrepareSeparateDocs(followUp, [narrative]).ShouldBeTrue("advisory-distinct-allowed");
        right.WorktreePath = left.WorktreePath;
        policy.CanPrepareSeparateDocs(followUp, [narrative]).ShouldBeFalse("advisory-identical-identity-refused");
    }

    private static async Task SourceCallerAsync(params string[] variants)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        foreach (var variant in variants)
        {
            if (variant != variants[0]) await h.ResetDatabaseAsync();
            var bound = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: variant == variants[0]);
            if (variant == "valid-decoy")
            {
                Directory.CreateDirectory(bound.Identity);
                await File.WriteAllTextAsync(Path.Combine(bound.Identity, "foreign.txt"), "caller decoy");
            }
            var queued = await h.RequestAsync("c0824-controlled-verifier", source);
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            var expected = variant switch
            {
                "null-repository" or "null-identity" => "source_coordinates_missing",
                "empty-repository" => "ref_only_repository_required",
                "missing-common" => "ref_only_common_directory_required",
                "task" => "ref_only_binding_task_mismatch", "binding-id" => "ref_only_binding_identity_mismatch",
                "repository" or "common" => "ref_only_binding_repository_mismatch",
                "ref" => "ref_only_binding_ref_mismatch", "identity" => "ref_only_binding_identity_path_mismatch",
                "base" => "ref_only_binding_base_mismatch", _ => null
            };
            if (variant == "null-repository") task.RepoPath = null;
            if (variant == "empty-repository") task.RepoPath = "";
            if (variant == "null-identity") task.WorktreePath = null;
            if (variant == "binding-id") task.TaskRefWorkspaceBindingId = Guid.NewGuid();
            await db.SaveChangesAsync();
            var query = db.TaskRefWorkspaceBindings.Where(b => b.Id == bound.Id);
            if (variant == "task")
            {
                var other = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask { Id = other, RootTaskId = other, WorkingDirectory = h.Fixture.Repository });
                await db.SaveChangesAsync();
                await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.TaskId, other));
            }
            if (variant == "repository") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.RepositoryPath, h.Fixture.Remote));
            if (variant == "missing-common") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.CommonDirectory, ""));
            if (variant == "common") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.CommonDirectory, h.Fixture.Root));
            if (variant == "ref") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.FullRef, "refs/heads/foreign"));
            if (variant == "identity") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.IdentityPath, bound.Identity + "-foreign"));
            if (variant == "base") await query.ExecuteUpdateAsync(s => s.SetProperty(b => b.SelectedBaseSha, new string('b', 40)));
            var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
            await using var lease = (await leases.TryAcquireAsync(h.Fixture.Repository, default)).ShouldNotBeNull();
            var workspace = h.Services.GetRequiredService<ILandWorkspace>();
            h.Fixture.Git.Trace.Clear(); h.Fixture.Git.Commands.Clear();
            var result = await new AgentTaskLandSourceResolver(db, h.Fixture.Git, leases, h.Clock, landWorkspace: workspace)
                .ResolveAsync(task, request, lease, default);
            if (expected is null)
            {
                result.Reason.ShouldBeNull("valid-land-caller:" + variant);
                result.Operation.ShouldNotBeNull().GitDirectory.ShouldBeNull("source-admin=null:" + variant);
                result.Operation.SourceDesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly, "kind-from-row:" + variant);
                result.Operation.SourceTaskRefWorkspaceBindingId.ShouldBe(bound.Id);
            }
            else
            {
                result.Reason.ShouldBe(expected, "foreign-" + variant + "-refused");
                result.Operation.ShouldBeNull("no-operation-before-binding-validation");
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("fetch") || a.Contains("update-ref") || a.Contains("push")
                    || a.Contains("reset") || a.Contains("remove"), "no-main-file-io:" + variant);
            }
            h.Fixture.Git.Commands.ShouldNotContain(c => Same(c.Directory, bound.Identity), "no-identity-git:" + variant);
            if (variant == "valid-decoy")
            {
                (await File.ReadAllTextAsync(Path.Combine(bound.Identity, "foreign.txt"))).ShouldBe("caller decoy");
                File.Delete(Path.Combine(bound.Identity, "foreign.txt")); Directory.Delete(bound.Identity);
            }
            (await db.TaskRefWorkspaceBindings.AsNoTracking().CountAsync(b => b.Id == bound.Id)).ShouldBe(1, "binding-retained");
        }
    }

    private static bool Same(string left, string right) => WorkspaceReservationKey.PathsEqual(left, right);

    private const string BaseSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string ScratchRoot() => Path.Combine(Path.GetTempPath(), "c0824-context-" + Guid.NewGuid().ToString("N"));
    private static AppDbContext NewDb(IsolatedTestSchema schema) => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
    private static async Task<(AgentTask Task, TaskRefWorkspaceBinding Binding)> SeedAsync(AppDbContext db, Guid id, string root,
        string? branch = null)
    {
        branch ??= "feat/card-task-" + id.ToString("N");
        var repo = Path.Combine(root, "repo");
        var identity = Path.Combine(root, ".ref-only", id.ToString("N"));
        var binding = new TaskRefWorkspaceBinding
        {
            Id = Guid.NewGuid(), TaskId = id, RepositoryPath = repo, CommonDirectory = Path.Combine(repo, ".git"),
            IdentityPath = identity, Branch = branch, FullRef = "refs/heads/" + branch, SelectedBaseSha = BaseSha,
            SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
            PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 7,
        };
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Workspace = WorkspaceMode.Worktree, RunnerId = "fixture-runner", Role = AgentTaskRole.Plan,
            DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly, TaskRefWorkspaceBindingId = binding.Id,
            RepoPath = repo, WorkingDirectory = repo, WorktreePath = identity, WorktreeBranch = branch,
            WorktreeBaseSha = BaseSha, WorktreeBaseRef = "refs/heads/master", WorktreeBaseSource = WorktreeBaseSource.Explicit,
            RemoteWorktreePath = "/runner/isolated/task", CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task); db.TaskRefWorkspaceBindings.Add(binding);
        await db.SaveChangesAsync();
        return (task, binding);
    }

    private static TaskRefWorkspaceBinding Copy(TaskRefWorkspaceBinding source, Guid id, Guid taskId, string fullRef) => new()
    {
        Id = id, TaskId = taskId, RepositoryPath = source.RepositoryPath, CommonDirectory = source.CommonDirectory,
        IdentityPath = source.IdentityPath, Branch = fullRef[11..], FullRef = fullRef, SelectedBaseSha = source.SelectedBaseSha,
        SelectedBaseRef = source.SelectedBaseRef, SelectedBaseSource = source.SelectedBaseSource,
        PreparationState = source.PreparationState, Revision = source.Revision,
    };
}
