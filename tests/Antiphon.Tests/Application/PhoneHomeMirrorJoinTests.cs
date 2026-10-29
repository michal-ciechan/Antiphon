using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1032 S2 D1. Preparation goes through a real phone-home socket, the runner
/// mutation lock, and a scratch git repository, then a fresh server row read.
/// CP-21 requires Linux: the busy and crash fixtures use Unix FIFOs
/// (<c>/usr/bin/mkfifo</c>, <c>/bin/sh</c>) and <c>File.SetUnixFileMode</c>.
/// Windows skips these methods. That skip is not acceptance, and this class
/// does not supply a portable barrier.
/// Each task branch is <c>DelegateBranchName.Legacy</c> for that task id.
/// CARD-1213 refuses any other name before the runner is asked.
/// </summary>
[NativeLinuxOnly("CP-21 phone-home join requires Linux FIFOs and Unix file modes.")]
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PhoneHomeMirrorJoinTests
{
    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinIdleRecipient(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: an idle registered runner accepts the live store and finishes the mirror.
        // mutation: skip StampOwnedCreationAsync's owner-stamp write and still return success.
        // red: stampMatches.ShouldBeTrue() — the worktree git dir has no antiphon-mirror-owner.json for this task.
        await using var world = await JoinWorld.StartAsync(cancellationToken);
        var taskId = await world.SeedAsync();
        world.Begin(taskId);
        await world.Idle(cancellationToken);

        var warnings = await world.WarningsAsync(taskId);
        var rows = await world.BindingsAsync(taskId);
        rows.Count.ShouldBe(1, string.Join(" | ", warnings));
        var row = rows[0];
        row.BindingId.ShouldNotBe(Guid.Empty, string.Join(" | ", warnings));
        row.RunnerStoreId.ShouldBe(world.StoreId.ToString("D"));
        row.TaskId.ShouldBe(taskId);
        var stamp = JoinWorld.ReadStamp(row.GitDirectory);
        var stampMatches = stamp is not null
            && stamp.TaskId == taskId
            && stamp.CreationGeneration == row.BindingId
            && stamp.PreparationId == row.PreparationId
            && File.Exists(RunnerWorkspaceBindingStore.OwnerStampPath(row.GitDirectory));
        stampMatches.ShouldBeTrue();
        world.Ledger(row.PreparationId).Kind.ShouldBe(BindingReadKind.Complete);
        warnings.ShouldBeEmpty();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinBusyRecipient(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: a second mirror waits while an earlier git mutation holds the runner lock.
        // mutation: skip MutationLock.WaitAsync so the second request writes a ledger during the first git.
        // red: partialBindingLeft.ShouldBeFalse() — the waiting task gains a runner record or a completed server binding.
        await using var world = await JoinWorld.StartAsync(cancellationToken, hook: true);
        var first = await world.SeedAsync();
        var second = await world.SeedAsync();
        world.ArmHold();
        world.Begin(first);
        (await world.Entered(cancellationToken)).ShouldBe("x");

        var occupied = world.InFlight;
        world.Begin(second);
        var warnings = new List<string>();
        using (var gate = LoadTimeoutWait.Open(
            clock: null, budget: null, wallCap: LoadWallCap, cancellationToken: cancellationToken,
            label: "second mirror admission"))
        {
            try
            {
                while (world.InFlight <= occupied && warnings.Count == 0)
                {
                    gate.Token.ThrowIfCancellationRequested();
                    warnings = await world.WarningsAsync(second);
                    if (warnings.Count == 0)
                        await Task.Delay(20, gate.Token);
                }
            }
            catch (OperationCanceledException) when (gate.WallElapsed)
            {
                gate.ThrowIfTimedOut();
            }
        }

        var secondRows = await world.BindingsAsync(second);
        secondRows.Count.ShouldBe(1);
        var secondPath = world.Worktree(second);
        var preparationId = secondRows[0].PreparationId;
        // A dispatch that is not held by the lock writes its intent on this turn. The lock keeps
        // the ledger missing for the whole hold, so the bound poll is the observation window.
        for (var turn = 0; turn < 50
            && world.Ledger(preparationId).Kind == BindingReadKind.Missing
            && !Directory.Exists(secondPath)
            && secondRows[0].BindingId == Guid.Empty
            && warnings.Count == 0; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken);
            warnings = await world.WarningsAsync(second);
            secondRows = await world.BindingsAsync(second);
        }

        var partialBindingLeft = world.Ledger(preparationId).Kind != BindingReadKind.Missing
            || Directory.Exists(secondPath)
            || secondRows[0].BindingId != Guid.Empty
            || warnings.Count > 0;
        partialBindingLeft.ShouldBeFalse();

        world.ReleaseHold();
        await world.Idle(cancellationToken);
        var settledWarnings = await world.WarningsAsync(second);
        var settled = await world.BindingsAsync(second);
        settled.Count.ShouldBe(1);
        settled[0].BindingId.ShouldNotBe(Guid.Empty, string.Join(" | ", settledWarnings));
        settled[0].PreparationId.ShouldBe(preparationId);
        world.Ledger(preparationId).Kind.ShouldBe(BindingReadKind.Complete);
        var firstRows = await world.BindingsAsync(first);
        firstRows.Count.ShouldBe(1);
        firstRows[0].BindingId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinCrashRetry(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: the server drops its waiter after enqueue and the retry reuses the same preparation.
        // mutation: replace the stored PreparationId on reuse. A second insert collides with the
        // task/attempt/store/empty-binding unique index, so the guarded row would stay the original id.
        // red: recoveredPreparationId.ShouldBe(firstPreparationId) — the retry stores a second identity.
        await using var world = await JoinWorld.StartAsync(cancellationToken, hook: true);
        var taskId = await world.SeedAsync();
        world.ArmHold();
        world.Begin(taskId);
        (await world.Entered(cancellationToken)).ShouldBe("x");

        var armed = await world.BindingsAsync(taskId);
        armed.Count.ShouldBe(1);
        armed[0].BindingId.ShouldBe(Guid.Empty);
        var firstPreparationId = armed[0].PreparationId;

        await world.DropPreparerAsync();
        var interrupted = await world.BindingsAsync(taskId);
        interrupted.Count.ShouldBe(1);
        interrupted[0].BindingId.ShouldBe(Guid.Empty);
        interrupted[0].PreparationId.ShouldBe(firstPreparationId);

        world.ReleaseHold();
        await JoinWorld.UntilAsync(
            () => world.Ledger(firstPreparationId).Kind == BindingReadKind.Complete, cancellationToken);
        world.Rebuild();
        world.Begin(taskId);
        await world.Idle(cancellationToken);

        var rows = await world.BindingsAsync(taskId);
        var recoveredPreparationId = rows[^1].PreparationId;
        recoveredPreparationId.ShouldBe(firstPreparationId);
        rows.Count.ShouldBe(1);
        var oneCompleted = rows[0].BindingId != Guid.Empty
            && world.Ledger(firstPreparationId).Kind == BindingReadKind.Complete;
        oneCompleted.ShouldBeTrue();
        (await world.WarningsAsync(taskId)).ShouldBeEmpty();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinForeignOwner(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: a second full task id collides with an owned short worktree through the live queue.
        // mutation: skip the owner-stamp comparison and adopt the existing directory.
        // red: foreignAdopted.ShouldBeFalse() — the foreign task's server row becomes a completed binding.
        await using var world = await JoinWorld.StartAsync(cancellationToken);
        var ownerId = Guid.Parse("deadbeef-0000-4000-8000-0000000000a1");
        var foreignId = Guid.Parse("deadbeef-0000-4000-8000-0000000000b2");
        var ownerPreparation = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
        var foreignPreparation = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b2");
        var workspace = world.Workspace();
        await world.EnsureBranchAsync(ownerId, cancellationToken);
        var created = await workspace.MirrorAsync(world.MirrorRequest(ownerId, ownerPreparation), cancellationToken);
        var ownerStamp = File.ReadAllBytes(RunnerWorkspaceBindingStore.OwnerStampPath(
            workspace.OpenBindingStore().Read(ownerPreparation).Record!.GitDirectory));
        var ownerLedger = File.ReadAllBytes(workspace.OpenBindingStore().RecordPath(ownerPreparation));

        var generation = Guid.Parse("cccccccc-0000-4000-8000-0000000000c3");
        workspace.OpenBindingStore().WriteIntent(new RunnerWorkspaceBindingRecord
        {
            TaskId = foreignId,
            Attempt = 1,
            PreparationId = foreignPreparation,
            RunnerId = world.RunnerId,
            RunnerStoreId = world.StoreId.ToString("D"),
            BindingId = generation,
            RepositoryIdentity = world.OriginUrl,
            Branch = JoinWorld.BranchFor(foreignId),
            Name = "task-" + foreignId.ToString("N")[..8],
            SourceFullRef = "refs/heads/" + JoinWorld.BranchFor(foreignId),
            CreationBaselineSha = world.Sha,
            WorktreePath = created.Path,
            CommonDirectory = world.Clone,
            GitDirectory = created.Path,
            TreeExistedAtIntent = false,
            CreationGeneration = generation,
        });
        var foreignLedger = File.ReadAllBytes(workspace.OpenBindingStore().RecordPath(foreignPreparation));
        await world.SeedAsync(foreignId);
        await world.InsertIncompleteAsync(foreignId, foreignPreparation);

        world.Begin(foreignId);
        await world.Idle(cancellationToken);

        var rows = await world.BindingsAsync(foreignId);
        rows.Count.ShouldBe(1);
        var foreignAdopted = rows[0].BindingId != Guid.Empty;
        foreignAdopted.ShouldBeFalse();
        var warnings = await world.WarningsAsync(foreignId);
        warnings.ShouldContain(detail => detail.Contains("foreign_mirror_owner", StringComparison.Ordinal));
        File.ReadAllBytes(RunnerWorkspaceBindingStore.OwnerStampPath(
            workspace.OpenBindingStore().Read(ownerPreparation).Record!.GitDirectory)).ShouldBe(ownerStamp);
        File.ReadAllBytes(workspace.OpenBindingStore().RecordPath(ownerPreparation)).ShouldBe(ownerLedger);
        File.ReadAllBytes(workspace.OpenBindingStore().RecordPath(foreignPreparation)).ShouldBe(foreignLedger);
        world.Ledger(foreignPreparation).Kind.ShouldBe(BindingReadKind.Intent);
        world.Ledger(ownerPreparation).Kind.ShouldBe(BindingReadKind.Complete);
        Directory.Exists(created.Path).ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinStaleBinding(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: an exact preparation replay arrives after the runner removed that generation.
        // mutation: return the completed binding from RequireCurrentGenerationAsync without checking the directory.
        // red: staleCompleted.ShouldBeFalse() — the server row is completed while the worktree is gone.
        await using var world = await JoinWorld.StartAsync(cancellationToken);
        var taskId = Guid.Parse("deadbeef-0000-4000-8000-000000000021");
        var preparationId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
        var workspace = world.Workspace();
        await world.EnsureBranchAsync(taskId, cancellationToken);
        var created = await workspace.MirrorAsync(world.MirrorRequest(taskId, preparationId), cancellationToken);
        var originalBindingId = created.Binding!.BindingId;
        var removed = await workspace.RemoveAsync(
            new PhoneHomeWorkspaceRemoveRequest(created.Path), cancellationToken);
        removed.Removed.ShouldBeTrue();
        Directory.Exists(created.Path).ShouldBeFalse();
        var stored = workspace.OpenBindingStore().Read(preparationId);
        stored.Kind.ShouldBe(BindingReadKind.Complete);
        await world.SeedAsync(taskId);
        await world.InsertIncompleteAsync(taskId, preparationId, stored.Record);

        world.Begin(taskId);
        await world.Idle(cancellationToken);

        var rows = await world.BindingsAsync(taskId);
        rows.Count.ShouldBe(1);
        var staleCompleted = rows[0].BindingId != Guid.Empty;
        staleCompleted.ShouldBeFalse();
        rows[0].PreparationId.ShouldBe(preparationId);
        var warnings = await world.WarningsAsync(taskId);
        warnings.ShouldContain(detail => detail.Contains("stale_mirror_binding", StringComparison.Ordinal));
        Directory.Exists(created.Path).ShouldBeFalse();
        var after = world.Ledger(preparationId);
        after.Kind.ShouldBe(BindingReadKind.Complete);
        after.Record!.BindingId.ShouldBe(originalBindingId);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1032_JoinStoreMismatch(CancellationToken cancellationToken)
    {
        RequireLinuxJoin();
        // case: the registered store file changes before the mirror, so the request names the old live store.
        // mutation: bind the requested store and skip the runner_store_mismatch throw.
        // red: wrongStoreCompleted.ShouldBeFalse() — the recipient creates the tree for the store it no longer is.
        await using var world = await JoinWorld.StartAsync(cancellationToken);
        await world.RewriteStoreAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var taskId = await world.SeedAsync();
        world.Begin(taskId);
        await world.Idle(cancellationToken);

        var rows = await world.BindingsAsync(taskId);
        rows.Count.ShouldBe(1);
        var path = world.Worktree(taskId);
        var wrongStoreCompleted = rows[0].BindingId != Guid.Empty
            || Directory.Exists(path)
            || world.Ledger(rows[0].PreparationId).Kind != BindingReadKind.Missing;
        wrongStoreCompleted.ShouldBeFalse();
        rows[0].RunnerStoreId.ShouldBe(world.StoreId.ToString("D"));
        var warnings = await world.WarningsAsync(taskId);
        warnings.ShouldContain(detail => detail.Contains("runner_store_mismatch", StringComparison.Ordinal));
    }

    /// <summary>A hook that never runs fails here, not at the two-minute test ceiling.</summary>
    private static readonly TimeSpan LoadWallCap = TimeSpan.FromSeconds(45);

    /// <summary>CP-21 executes on Linux. A Windows skip is not a passing result.</summary>
    private static void RequireLinuxJoin()
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException(
                "CP-21 phone-home join requires Linux FIFOs and Unix file modes.");
    }

    private sealed class JoinWorld : IAsyncDisposable
    {
        private readonly CancellationTokenSource _runnerStop = new();
        private ServiceProvider? _provider;
        private Task<string>? _entered;
        private string? _root;
        private string? _hold;
        private bool _hook;

        public FakeTimeProvider Clock { get; } =
            new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public IsolatedTestSchema Schema { get; private set; } = null!;
        public PhoneHomeConnectionService Connection { get; private set; } = null!;
        public PhoneHomeSettings Settings { get; private set; } = null!;
        public Guid StoreId { get; private set; }
        public string OriginUrl { get; private set; } = "";
        public string Sha { get; private set; } = "";
        public string Clone { get; private set; } = "";
        private string? _origin;
        public string RunnerId => Host.AllowedRunnerId;
        public int InFlight => Connection.InFlight;
        private RemoteWorkspacePreparer Preparer =>
            _provider!.GetRequiredService<RemoteWorkspacePreparer>();

        public static async Task<JoinWorld> StartAsync(CancellationToken ct, bool hook = false)
        {
            var world = new JoinWorld();
            try
            {
                await world.StartCoreAsync(ct, hook);
                return world;
            }
            catch
            {
                await world.DisposeAsync();
                throw;
            }
        }

        public static string BranchFor(Guid taskId) => DelegateBranchName.Legacy(taskId);

        public async Task EnsureBranchAsync(Guid taskId, CancellationToken ct)
        {
            var name = BranchFor(taskId);
            var (code, _, _) = await RunCaptureAsync(
                "git", _origin!, ct, "show-ref", "--verify", "--quiet", "refs/heads/" + name);
            if (code == 0)
                return;
            await GitAsync(_origin!, ct, "branch", name);
        }

        public async Task<Guid> SeedAsync(Guid? id = null)
        {
            var taskId = id ?? Guid.NewGuid();
            await EnsureBranchAsync(taskId, CancellationToken.None);
            await using var db = NewDb();
            var now = Clock.GetUtcNow().UtcDateTime;
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "mirror join", Goal = "join",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = Clone, RepoPath = Clone, WorktreePath = Clone,
                WorktreeBranch = BranchFor(taskId), RunnerId = RunnerId, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
            return taskId;
        }

        public async Task InsertIncompleteAsync(
            Guid taskId, Guid preparationId, RunnerWorkspaceBindingRecord? copy = null)
        {
            await using var db = NewDb();
            db.TaskWorktreeMirrorBindings.Add(new TaskWorktreeMirrorBinding
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                Attempt = copy?.Attempt ?? 1,
                PreparationId = preparationId,
                RunnerId = RunnerId,
                RunnerStoreId = StoreId.ToString("D"),
                BindingId = Guid.Empty,
                RepositoryIdentity = copy?.RepositoryIdentity ?? OriginUrl,
                CommonDirectory = "",
                GitDirectory = "",
                WorktreePath = "",
                SourceFullRef = copy?.SourceFullRef ?? "refs/heads/" + BranchFor(taskId),
                CreationBaselineSha = copy?.CreationBaselineSha ?? Sha,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public void Begin(Guid taskId) => Preparer.TryBegin(taskId, RunnerId).ShouldBeTrue();

        public Task Idle(CancellationToken ct) =>
            LoadTimeoutWait.UntilSignaledAsync(
                token => Preparer.WhenIdleAsync(token),
                clock: null, budget: null, wallCap: LoadWallCap, cancellationToken: ct,
                label: "mirror join idle");

        public RunnerWorkspaceService Workspace()
        {
            var service = new RunnerWorkspaceService(Settings.RepositoryPolicy(), Settings.AllowedCwd);
            service.BindRecipient(Settings.RunnerId, StoreId);
            return service;
        }

        public PhoneHomeWorkspaceMirrorRequest MirrorRequest(Guid taskId, Guid preparationId)
        {
            var branch = BranchFor(taskId);
            return new(branch, Sha, "task-" + taskId.ToString("N")[..8], OriginUrl,
                new PhoneHomeWorkspaceMirrorBinding(
                    taskId, 1, preparationId, RunnerId, StoreId.ToString("D"), "refs/heads/" + branch));
        }

        public BindingRead Ledger(Guid preparationId) => Workspace().OpenBindingStore().Read(preparationId);

        public string Worktree(Guid taskId) =>
            Settings.AllowedCwd.TrimEnd('/') + "/worktrees/task-" + taskId.ToString("N")[..8];

        public async Task<List<TaskWorktreeMirrorBinding>> BindingsAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.TaskWorktreeMirrorBindings.AsNoTracking()
                .Where(row => row.TaskId == taskId)
                .OrderBy(row => row.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<string>> WarningsAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(row => row.AgentTaskId == taskId && row.Type == AgentTaskEventType.Warning)
                .Select(row => row.Detail)
                .ToListAsync();
        }

        public async Task RewriteStoreAsync(Guid store) =>
            await File.WriteAllTextAsync(Settings.StoreIdPath, store.ToString("D"));

        public void ArmHold()
        {
            File.WriteAllText(Path.Combine(_hold!, "hold"), "1");
            _ = Entered(CancellationToken.None);
        }

        public Task<string> Entered(CancellationToken ct)
        {
            _entered ??= Task.Run(() =>
            {
                using var stream = new FileStream(Path.Combine(_hold!, "entered"), FileMode.Open, FileAccess.Read);
                var buffer = new byte[8];
                var read = stream.Read(buffer, 0, buffer.Length);
                return System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            }, ct);
            return LoadTimeoutWait.UntilSignaledAsync(
                token => _entered.WaitAsync(token),
                clock: null, budget: null, wallCap: LoadWallCap, cancellationToken: ct,
                label: "checkout hook");
        }

        public void ReleaseHold()
        {
            var marker = Path.Combine(_hold!, "hold");
            if (File.Exists(marker))
                File.Delete(marker);
            Unblock(Path.Combine(_hold!, "release"));
        }

        public async Task DropPreparerAsync()
        {
            if (_provider is null)
                return;
            await _provider.DisposeAsync();
            _provider = null;
        }

        public void Rebuild() => _provider = BuildProvider();

        public static MirrorCreationOwnerStamp? ReadStamp(string gitDirectory)
        {
            var path = RunnerWorkspaceBindingStore.OwnerStampPath(gitDirectory);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<MirrorCreationOwnerStamp>(
                File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public static Task UntilAsync(Func<bool> ready, CancellationToken ct) =>
            LoadTimeoutWait.UntilSignaledAsync(async token =>
            {
                while (!ready())
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(20, token);
                }
            }, clock: null, budget: null, wallCap: LoadWallCap, cancellationToken: ct, label: "binding ledger");

        public async ValueTask DisposeAsync()
        {
            if (_hook && _hold is not null)
            {
                Unblock(Path.Combine(_hold, "release"));
                Unblock(Path.Combine(_hold, "entered"));
            }

            _runnerStop.Cancel();
            if (Connection is not null)
            {
                try { await Connection.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }

            if (_provider is not null)
                await _provider.DisposeAsync();
            if (Host is not null)
                await Host.DisposeAsync();
            if (Schema is not null)
                await Schema.DisposeAsync();
            _runnerStop.Dispose();
            if (_root is not null)
            {
                try { Directory.Delete(_root, recursive: true); }
                catch (IOException) { }
            }
        }

        private async Task StartCoreAsync(CancellationToken ct, bool hook)
        {
            RequireLinuxJoin();
            _hook = hook;
            _root = Directory.CreateTempSubdirectory("c1032-join").FullName;
            var origin = Path.Combine(_root, "origin");
            _origin = origin;
            Clone = Path.Combine(_root, "clone");
            Directory.CreateDirectory(origin);
            await GitAsync(origin, ct, "init", "-b", "main");
            await GitAsync(origin, ct, "config", "user.email", "t@localhost");
            await GitAsync(origin, ct, "config", "user.name", "t");
            await GitAsync(origin, ct, "config", "commit.gpgsign", "false");
            await File.WriteAllTextAsync(Path.Combine(origin, "README"), "c1032\n", ct);
            await GitAsync(origin, ct, "add", "README");
            await GitAsync(origin, ct, "commit", "-m", "init");
            await GitAsync(_root, ct, "clone", origin, Clone);
            Sha = (await GitOutAsync(origin, ct, "rev-parse", "HEAD")).Trim();
            OriginUrl = (await GitOutAsync(Clone, ct, "remote", "get-url", "origin")).Trim();
            if (hook)
                await InstallHookAsync(ct);

            Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            Host = await PhoneHomeTestHost.StartAsync(Clock);
            StoreId = Guid.NewGuid();
            var state = Path.Combine(_root, "state");
            Directory.CreateDirectory(state);
            var allowed = Path.Combine(_root, "cwd");
            Directory.CreateDirectory(allowed);
            var storePath = Path.Combine(state, "runner-store-id");
            var secretPath = Path.Combine(state, "secret");
            await File.WriteAllTextAsync(storePath, StoreId.ToString("D"), ct);
            await File.WriteAllTextAsync(secretPath, Host.Secret, ct);
            Settings = new PhoneHomeSettings
            {
                Enabled = true,
                RunnerId = Host.AllowedRunnerId,
                ServerOrigin = Host.Http.BaseAddress!.ToString(),
                SecretPath = secretPath,
                StoreIdPath = storePath,
                CapacityStatePath = Path.Combine(state, "capacity"),
                LaunchGenerationsPath = Path.Combine(state, "launch-generations"),
                AllowedCwd = allowed,
                RunnerRepository = Clone,
                RunnerCloneSource = OriginUrl,
                ProbeSecondaryRepositoryPushAccess = false,
                Capacity = 4,
                HeartbeatSeconds = 30,
            };
            var runtime = new SessionRunnerRuntime(
                Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
                {
                    SessionLogPath = Path.Combine(_root, "sessions"),
                }),
                NullLogger<SessionRunnerRuntime>.Instance);
            var gate = new PhoneHomeAdoptionGate();
            Connection = new PhoneHomeConnectionService(
                Options.Create(Settings),
                gate,
                new PhoneHomeCommandDispatcher(
                    new PhoneHomeRuntimeAdapter(
                        runtime,
                        new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)),
                    Settings),
                runtime,
                new DefaultHttpClientFactory(),
                TimeProvider.System,
                NullLogger<PhoneHomeConnectionService>.Instance);
            _ = Connection.StartAsync(_runnerStop.Token);
            gate.SignalReady();
            var live = await Host.WaitLiveAsync(TimeSpan.FromSeconds(45));
            Host.Directory.MarkRecovered(live);
            _provider = BuildProvider();
        }

        private async Task InstallHookAsync(CancellationToken ct)
        {
            _hold = Path.Combine(_root!, "hold");
            var hooks = Path.Combine(_root!, "hooks");
            Directory.CreateDirectory(_hold);
            Directory.CreateDirectory(hooks);
            await RunAsync("/usr/bin/mkfifo", _root!, ct, Path.Combine(_hold, "entered"));
            await RunAsync("/usr/bin/mkfifo", _root!, ct, Path.Combine(_hold, "release"));
            var hook = Path.Combine(hooks, "post-checkout");
            await File.WriteAllTextAsync(hook, """
                #!/bin/sh
                hold='__HOLD__'
                if [ -f "$hold/hold" ]; then
                  printf x > "$hold/entered"
                  read dummy < "$hold/release" || true
                fi
                exit 0
                """.Replace("__HOLD__", _hold, StringComparison.Ordinal), ct);
            // CP-21 requires Linux. This is not a portable barrier; Windows skips the join.
            if (OperatingSystem.IsLinux())
                MarkHookExecutable(hook);
            await GitAsync(Clone, ct, "config", "core.hooksPath", hooks);
        }

        private ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(Schema.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                AllowedRoots = ["C:\\", "/"],
            }));
            services.AddSingleton<ISessionRunnerDirectory>(Host.Directory);
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<ILandingGit>(new FixedGit(Sha, OriginUrl));
            services.AddSingleton<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
            return services.BuildServiceProvider();
        }

        private AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        [UnsupportedOSPlatform("windows")]
        private static void MarkHookExecutable(string hook) =>
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        private static void Unblock(string fifo)
        {
            if (!File.Exists(fifo))
                return;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList = { "-c", "exec 3<>\"$1\"; printf x >&3", "unblock", fifo },
            });
            process?.WaitForExit();
        }

        private static Task GitAsync(string cwd, CancellationToken ct, params string[] args) =>
            RunAsync("git", cwd, ct, args);

        private static async Task<string> GitOutAsync(string cwd, CancellationToken ct, params string[] args)
        {
            var (code, stdout, stderr) = await RunCaptureAsync("git", cwd, ct, args);
            if (code != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({code}): {stderr}");
            return stdout;
        }

        private static async Task RunAsync(string file, string cwd, CancellationToken ct, params string[] args)
        {
            var (code, stdout, stderr) = await RunCaptureAsync(file, cwd, ct, args);
            if (code != 0)
                throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed ({code}): {stderr} {stdout}");
        }

        private static async Task<(int Code, string Stdout, string Stderr)> RunCaptureAsync(
            string file, string cwd, CancellationToken ct, params string[] args)
        {
            var start = new ProcessStartInfo
            {
                FileName = file,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(start) ?? throw new InvalidOperationException(file + " did not start");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, await stdout, await stderr);
        }

        private sealed class FixedGit(string sha, string origin) : ILandingGit
        {
            public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
                Task.FromResult(arguments[0] switch
                {
                    "rev-parse" => new LandingGitResult(0, sha, ""),
                    "remote" => new LandingGitResult(0, origin, ""),
                    "push" => new LandingGitResult(0, "", ""),
                    _ => throw new NotSupportedException(arguments[0]),
                });

            public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) =>
                throw new NotSupportedException();
        }

        private sealed class DefaultHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}
