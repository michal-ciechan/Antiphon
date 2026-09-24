using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0644 V-4. Retained callers after the fresh Worktree default: Commit and Merge stay on the
/// checkout they already own, specialists stay on their seat, a Worktree task does not take the
/// Shared commit-on-settle contract, a Shared settlement commits only its footprint, card launches
/// use the card worktree, tracker import does not launch, card files stay on the board root, and
/// nightly still refuses a shared tree or a linked worktree.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeDefaultCallerTests
{
    [Before(Class)]
    public static Task WarmSharedStoreAsync() => TestDbFixture.Lifecycle.EnsureReadyAsync();

    [Test]
    [Timeout(120_000)]
    public async Task CommitAndMergeStayOnOwnedCheckout()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var owned = Directory.CreateTempSubdirectory("c644-owned").FullName;
        var settled = Directory.CreateTempSubdirectory("c644-settled").FullName;
        try
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "conflicted owner",
                Goal = "finish the slice",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = settled,
                WorktreePath = owned,
                WorktreeBranch = "feat/card-task-owned",
                MergeTargetRef = "master",
                RepoPath = settled,
                Status = AgentTaskStatus.Succeeded,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var parent = await db.AgentTasks.SingleAsync(t => t.Id == id);
            var service = CreateTaskService(db);

            var merge = await service.CreateMergeTaskAsync(parent, ["conflicted.cs"], CancellationToken.None);
            merge.ShouldNotBeNull();
            merge!.Workspace.ShouldBe(WorkspaceMode.Shared);
            merge.Role.ShouldBe(AgentTaskRole.Merge);
            merge.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
            merge.WorktreePath.ShouldBeNull();
            SamePath(merge.WorkingDirectory, owned).ShouldBeTrue("Merge works in the conflicted task's worktree");

            const string head = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
            var commit = await service.CreateCommitTaskAsync(
                parent, "unattributable", ["x.md"], head, null, CancellationToken.None);
            commit.ShouldNotBeNull();
            commit!.Workspace.ShouldBe(WorkspaceMode.Shared);
            commit.Role.ShouldBe(AgentTaskRole.Commit);
            commit.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
            commit.WorktreePath.ShouldBeNull();
            commit.CommitBaselineSha.ShouldBe(head);
            SamePath(commit.WorkingDirectory, settled).ShouldBeTrue("Commit works in the settled task's dirty checkout");
        }
        finally
        {
            DeleteDirectory(owned);
            DeleteDirectory(settled);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task SpecialistFactoriesStayOnSeat()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var seatDir = CheckInterpreterProvisioner.ResolveWorkingDirectory(new DelegationSettings
        {
            CheckInterpreterWorkingDirectory = Path.Combine(Path.GetTempPath(), "c644-seat-" + Guid.NewGuid().ToString("N")),
        });
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                AgentKind = AgentKind.ClaudeCode,
                DefinitionName = "claude",
                Status = SessionStatus.Running,
                Cwd = seatDir,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = seatId,
                Name = "c644-seat",
                Slug = "c644-seat-" + Guid.NewGuid().ToString("N")[..8],
                Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Low,
                WorkingDirectory = seatDir,
                PersistentSessionId = sessionId.ToString("D"),
                AlwaysOn = true,
                Status = AgentStatus.Running,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            await db.Entry(db.AgentSessions.Local.Single()).ReloadAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var seat = await db.Agents.SingleAsync(a => a.Id == seatId);
            var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance);
            var spec = CheckInterpreterProvisioner.Spec(new DelegationSettings()) with { Slug = seat.Slug };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var pending = runner.RunAsync(
                spec, "seat check", "synthetic facts", TimeSpan.FromSeconds(60), 3,
                _ => Task.FromResult<Agent?>(seat), stop.Token);
            try
            {
                AgentTask? task = null;
                await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
                {
                    await using var read = new AppDbContext(options);
                    task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seatId);
                    return task is not null;
                });
                task!.Workspace.ShouldBe(WorkspaceMode.Shared);
                task.WorkingDirectory.ShouldBe(seatDir);
                task.WorktreePath.ShouldBeNull();
                task.AgentId.ShouldBe(seatId);
            }
            finally
            {
                await stop.CancelAsync();
                try { await pending; } catch (OperationCanceledException) { }
            }
        }

        await using (var db = new AppDbContext(options))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            var generation = SessionGeneration.Normalize(session.StartedAt);
            var subjectId = Guid.NewGuid();
            var dispatched = SessionGeneration.Normalize(now.AddMinutes(-1));
            db.AgentTasks.Add(new AgentTask
            {
                Id = subjectId,
                RootTaskId = subjectId,
                Title = "subject",
                Goal = "watch",
                Role = AgentTaskRole.Code,
                Kind = AgentTaskKind.Worker,
                Status = AgentTaskStatus.Working,
                AgentId = seatId,
                AgentSessionId = sessionId,
                WorkingDirectory = seatDir,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            var recovery = new CheckCompactionRecovery
            {
                Id = Guid.NewGuid(),
                PhysicalAgentId = seatId,
                SessionId = sessionId,
                AcceptedStartedAt = generation,
                BoundaryIdentity = "c644-boundary",
                BoundaryCreatedAt = dispatched,
                ContinuationCreatedAt = dispatched,
                ConfiguredThresholdMinutes = 10,
                DetectedAt = dispatched,
                State = CheckCompactionRecoveryState.AwaitingCheck,
                ResumeSessionId = sessionId,
                ResumeAcceptedStartedAt = generation,
            };
            db.CheckCompactionRecoveries.Add(recovery);
            db.LegacyCheckNotePublications.Add(new LegacyCheckNotePublication
            {
                Id = Guid.NewGuid(),
                CheckedTaskId = subjectId,
                CheckedTaskAttempt = 1,
                CheckedTaskDispatchedAt = dispatched,
                CheckNumber = 1,
                RecoveryId = recovery.Id,
                PhysicalAgentId = seatId,
                InterpreterSessionId = sessionId,
                InterpreterAcceptedStartedAt = generation,
                ParentSessionId = sessionId,
                CapturedAt = now,
                FactsSnapshotJson = "{}",
                RenderContextJson = "{}",
                InterpretationDeadlineAt = now.AddMinutes(5),
                State = LegacyCheckNoteState.Captured,
                SourceEventId = Guid.NewGuid(),
                NotificationId = Guid.NewGuid(),
                NextAttemptAt = now,
            });
            await db.SaveChangesAsync();
            await new LegacyCheckNotePublicationService(db, TimeProvider.System)
                .BindInterpretationAsync(
                    db.LegacyCheckNotePublications.Local.Single().Id, CancellationToken.None);
        }

        await using (var verify = new AppDbContext(options))
        {
            var captured = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Title == "captured check");
            captured.Workspace.ShouldBe(WorkspaceMode.Shared);
            captured.AgentId.ShouldBe(seatId);
            captured.AgentSessionId.ShouldBe(sessionId);
            captured.WorktreePath.ShouldBeNull();
            captured.Role.ShouldBe(AgentTaskRole.Check);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task DefaultTaskUsesWorktreeSettlement()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var settings = new DelegationSettings();
        var fresh = await world.CreateTaskAsync(new CreateAgentTaskRequest("ordinary fresh work", Role: AgentTaskRole.Code));
        var shared = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "explicitly shared", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared));

        await using var db = world.CreateContext();
        var worktree = await db.AgentTasks.SingleAsync(t => t.Id == fresh.Id);
        var sharedRow = await db.AgentTasks.SingleAsync(t => t.Id == shared.Id);
        worktree.RepoPath.ShouldNotBeNull();
        sharedRow.RepoPath.ShouldNotBeNull();
        worktree.Status = AgentTaskStatus.Succeeded;
        sharedRow.Status = AgentTaskStatus.Succeeded;

        worktree.Workspace.ShouldBe(WorkspaceMode.Worktree);
        CommitOnSettleEligibility.IsEligible(worktree).ShouldBeFalse();
        DelegationReportFormatter.BuildBrief(worktree, settings)
            .ShouldNotContain(DelegationReportFormatter.SharedWriteCommitLine);

        sharedRow.Workspace.ShouldBe(WorkspaceMode.Shared);
        CommitOnSettleEligibility.IsEligible(sharedRow).ShouldBeTrue();
        DelegationReportFormatter.BuildBrief(sharedRow, settings)
            .ShouldContain(DelegationReportFormatter.SharedWriteCommitLine);
    }

    [Test]
    [Timeout(120_000)]
    public async Task SharedStillCommitsOnlyItsFootprint()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        using var repo = new ScratchGitRepo("c644-foot");
        await repo.CommitFileAsync(".gitignore", "*.secret\n");
        var parent = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-1);
        await using (var db = new AppDbContext(options))
        {
            db.AgentSessions.Add(RunningSession(parent, repo.Path, dispatched));
            db.AgentSessions.Add(RunningSession(sessionId, repo.Path, dispatched));
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                ParentSessionId = parent,
                ReplyTo = AgentTaskReplyTo.Session,
                Title = "shared writer",
                Goal = "edit a.md only",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                AgentKind = AgentKind.ClaudeCode,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = repo.Path,
                RepoPath = repo.Path,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();
        }

        var edited = Path.Combine(repo.Path, "a.md");
        var foreign = Path.Combine(repo.Path, "other.md");
        await File.WriteAllTextAsync(edited, "a\n");
        await File.WriteAllTextAsync(foreign, "other\n");
        await SeedFileEditAsync(options, sessionId, edited, DateTime.UtcNow);
        await TurnSeeding.SeedTurnAsync(
            () => new AppDbContext(options),
            sessionId,
            DelegationReportFormatter.TaskMarker(taskId),
            "Wrote a.md.");

        var factory = new SettleScope(schema.ConnectionString, repo.WorktreeRoot);
        try
        {
            await new AgentTaskReplyService(
                    factory,
                    Options.Create(new DelegationSettings { PtySingleChunkBytes = 43_200 }),
                    new MockEventBus(),
                    TimeProvider.System,
                    NullLogger<AgentTaskReplyService>.Instance)
                .OnTurnEndAsync(sessionId, CancellationToken.None);
        }
        finally
        {
            factory.DisposeProvider();
        }

        var names = (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.ShouldBe(["a.md"]);
        (await repo.GitReadAsync("status", "--porcelain")).Trim().ShouldContain("?? other.md");
    }

    [Test]
    [Timeout(120_000)]
    public async Task ScheduledAndChannelCardsLaunchInCardWorktree()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "c644-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var repo = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repo);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scheduled = new FakeAgentProtocolAdapter { PromptOutput = "SCHEDULED", TurnCompleted = true };
        var channel = new FakeAgentProtocolAdapter { PromptOutput = "CHANNEL", TurnCompleted = true };
        try
        {
            await using var harness = AgentControlServiceIntegrationTests.BuildHarness(
                tempRoot, [scheduled, channel], connectionString: schema.ConnectionString);
            var now = DateTime.UtcNow;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                db.Projects.Add(new Project
                {
                    Id = Guid.NewGuid(),
                    Name = "c644-" + Guid.NewGuid().ToString("N")[..8],
                    GitRepositoryUrl = "https://example.test/c644.git",
                    LocalRepositoryPath = repo,
                    BaseBranch = "master",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            Guid projectId;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                projectId = await db.Projects.Select(p => p.Id).SingleAsync();
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(projectId, "C644 callers"), CancellationToken.None);
            var scheduledCard = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Scheduled launch"), CancellationToken.None);
            var channelCard = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Channel launch"), CancellationToken.None);

            // ScheduleService.Spawn calls this same production method with an empty spawn request.
            var spawned = await harness.CardService.SpawnAsync(
                scheduledCard.Id, new SpawnCardRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(20), CancellationToken.None);

            var scope = harness.Scope.ServiceProvider;
            var delegated = await new AgentChannelService(
                    scope.GetRequiredService<AppDbContext>(),
                    scope.GetRequiredService<AgentSessionRuntime>(),
                    harness.CardService,
                    harness.EventBus,
                    NullLogger<AgentChannelService>.Instance)
                .DelegateCardAsync(
                    new ChannelDelegateCardRequest(
                        channelCard.Id, channelCard.ConcurrencyToken, "take the channel card", DefinitionName: "fake"),
                    CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(20), CancellationToken.None);

            AssertCardWorktree(scheduled, scheduledCard.Identifier, tempRoot, repo);
            AssertCardWorktree(channel, channelCard.Identifier, tempRoot, repo);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var scheduledSession = await verify.AgentSessions.SingleAsync(s => s.Id == spawned.SessionId);
            var channelSession = await verify.AgentSessions.SingleAsync(s => s.Id == delegated.SessionId);
            SamePath(scheduledSession.Cwd, scheduled.StartedCwd!).ShouldBeTrue();
            SamePath(channelSession.Cwd, channel.StartedCwd!).ShouldBeTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task TrackerImportDoesNotLaunch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var tempRoot = ExternalTrackerSyncLandingColumnTests.NewTempRoot();
        Directory.CreateDirectory(tempRoot);
        try
        {
            Guid boardId;
            await using (var db = new AppDbContext(options))
            {
                var graph = await ExternalTrackerSyncLandingColumnTests.SeedTrackedBoardAsync(db, tempRoot);
                boardId = graph.Board.Id;
                var tracker = new ExternalTrackerSyncLandingColumnTests.FakeIssueTracker(TrackerKind.GitHubIssues, [
                    ExternalTrackerSyncLandingColumnTests.Issue("acme/app#3", "#3", "Imported", "body"),
                ]);
                await ExternalTrackerSyncLandingColumnTests.NewSut(db, tracker)
                    .SyncAsync(DateTime.UtcNow, boardId, CancellationToken.None);
            }

            await using var verify = new AppDbContext(options);
            (await verify.Cards.CountAsync(c => c.BoardId == boardId)).ShouldBe(1);
            var cardIds = verify.Cards.Where(c => c.BoardId == boardId).Select(c => c.Id);
            (await verify.AgentSessions.CountAsync(s => s.CardId != null && cardIds.Contains(s.CardId.Value))).ShouldBe(0);
            (await verify.RunAttempts.CountAsync(a => cardIds.Contains(a.CardId))).ShouldBe(0);
            (await verify.AgentTasks.CountAsync(t => t.CardId != null && cardIds.Contains(t.CardId.Value))).ShouldBe(0);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task CardExportsKeepBoardRoot()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        using var repo = new ScratchGitRepo("c644-cards");
        await repo.CommitFileAsync(".keep", "seed\n");
        var delegateCwd = Directory.CreateTempSubdirectory("c644-delegate-cwd").FullName;
        try
        {
            var now = DateTime.UtcNow;
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            await using (var db = new AppDbContext(options))
            {
                db.Projects.Add(new Project
                {
                    Id = projectId,
                    Name = "c644-export",
                    GitRepositoryUrl = "https://example.invalid/c644.git",
                    LocalRepositoryPath = repo.Path,
                    BaseBranch = "master",
                    RepositoryVisibility = RepositoryVisibility.Private,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                var board = new Board
                {
                    Id = boardId,
                    ProjectId = projectId,
                    Name = "Antiphon",
                    SyncCardFiles = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Boards.Add(board);
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = columnId,
                    BoardId = boardId,
                    StateKey = "backlog",
                    Name = "Backlog",
                    ColumnOrder = 0,
                    CardStatus = CardStatus.Backlog,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                db.Cards.Add(new Card
                {
                    Id = Guid.NewGuid(),
                    BoardId = boardId,
                    BoardColumnId = columnId,
                    Identifier = "CARD-0644",
                    Title = "Worktree default",
                    Description = "board publication",
                    Status = CardStatus.Backlog,
                    Importance = CardImportance.Normal,
                    LabelsJson = "[]",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            CardFileSyncBoardResult result;
            await using (var db = new AppDbContext(options))
            {
                var status = await CardFiles(db).GetStatusAsync(boardId, CancellationToken.None);
                status.Directory.ShouldNotBeNull();
                var ignore = Path.Combine(repo.Path, ".gitignore");
                await File.WriteAllTextAsync(ignore, "/docs/cards/*\n!/" + status.Directory + "/\n");
                result = await CardFiles(db).SyncBoardAsync(boardId, dryRun: false, CancellationToken.None);
            }

            result.WriteSkipReason.ShouldBeNull();
            result.Written.ShouldBeGreaterThan(0);
            var boardDir = Path.Combine(repo.Path, "docs", "cards");
            Directory.Exists(boardDir).ShouldBeTrue();
            Directory.GetFiles(boardDir, "*.md", SearchOption.AllDirectories).ShouldNotBeEmpty();
            Directory.Exists(Path.Combine(delegateCwd, "docs")).ShouldBeFalse();
            await using var verify = new AppDbContext(options);
            var stored = await verify.Boards.SingleAsync(b => b.Id == boardId);
            SamePath(stored.CardFilesRepositoryPath.ShouldNotBeNull(), repo.Path).ShouldBeTrue();
        }
        finally
        {
            DeleteDirectory(delegateCwd);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task NightlyKeepsDedicatedCloneGuard()
    {
        var root = Directory.CreateTempSubdirectory("c644-nightly").FullName;
        using var main = new ScratchGitRepo("c644-nightly-main");
        using var clone = new ScratchGitRepo("c644-nightly-clone");
        var linked = Path.Combine(root, "linked");
        try
        {
            await main.CommitFileAsync("README.md", "base\n");
            (await ScratchGitRepo.GitInAsync(main.Path, "worktree", "add", "-b", "c644-linked", linked, "HEAD"))
                .Ok.ShouldBeTrue();
            await clone.CommitFileAsync("README.md", "clone\n");
            await clone.GitAsync("remote", "add", "origin", "https://github.com/michal-ciechan/Antiphon");
            await File.WriteAllTextAsync(Path.Combine(clone.Path, ".antiphon-nightly-owned"), "{}\n");
            var before = (await clone.GitReadAsync("rev-parse", "HEAD")).Trim();

            var shared = await RunNightlyAsync(@"C:\src\Antiphon", Path.Combine(root, "state-shared"), whatIf: true);
            shared.Exit.ShouldBe(3, shared.Output);
            shared.Output.ShouldContain("shared tree");
            shared.Output.ShouldContain("REFUSED");

            var worktree = await RunNightlyAsync(linked, Path.Combine(root, "state-linked"), whatIf: true);
            worktree.Exit.ShouldBe(3, worktree.Output);
            worktree.Output.ShouldContain("linked Git worktree");
            worktree.Output.ShouldContain("REFUSED");

            var state = Path.Combine(root, "state-clone");
            var admitted = await RunNightlyAsync(clone.Path, state, whatIf: true);
            admitted.Exit.ShouldBe(0, admitted.Output);
            admitted.Output.ShouldNotContain("REFUSED");
            admitted.Output.ShouldContain("\"testsPassed\":false");
            File.Exists(Path.Combine(state, "last-run.json")).ShouldBeTrue("admission passes the guard and records the run");
            (await clone.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(before);
        }
        finally
        {
            try { await ScratchGitRepo.GitInAsync(main.Path, "worktree", "remove", "--force", linked); } catch { /* best effort */ }
            DeleteDirectory(root);
        }
    }

    private static void AssertCardWorktree(FakeAgentProtocolAdapter adapter, string identifier, string tempRoot, string repo)
    {
        adapter.Started.ShouldBeTrue();
        var cwd = adapter.StartedCwd.ShouldNotBeNull();
        var expected = Path.Combine(tempRoot, "worktrees", "card-" + identifier);
        SamePath(cwd, expected).ShouldBeTrue($"launch cwd {cwd} is the card worktree");
        SamePath(cwd, repo).ShouldBeFalse("a card launch must not run in the project root");
    }

    private static AgentSession RunningSession(Guid id, string cwd, DateTime at) => new()
    {
        Id = id,
        DefinitionName = "fake",
        AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Running,
        Cwd = cwd,
        Cols = 120,
        Rows = 30,
        CreatedAt = at,
        StartedAt = at,
        LastSeenAt = at,
    };

    private static async Task SeedFileEditAsync(DbContextOptions<AppDbContext> options, Guid sessionId, string absolutePath, DateTime at)
    {
        await using var db = new AppDbContext(options);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.ToolCall,
            ToolName = "Write",
            ToolUseId = "toolu_" + Guid.NewGuid().ToString("N"),
            ToolInput = JsonSerializer.Serialize(new Dictionary<string, string> { ["file_path"] = absolutePath }),
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static AgentTaskService CreateTaskService(AppDbContext db) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40, MaxCostUsdPerRoot = 5.00m }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    private static CardTaskFileService CardFiles(AppDbContext db) =>
        new(
            db,
            new CardTaskFileSyncGate(),
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            NullLogger<CardTaskFileService>.Instance,
            new CardFileRepository(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance));

    private static async Task<(int Exit, string Output)> RunNightlyAsync(string checkout, string state, bool whatIf)
    {
        Directory.CreateDirectory(state);
        var coordination = Path.Combine(state, "coord");
        var logs = Path.Combine(state, "logs");
        Directory.CreateDirectory(coordination);
        Directory.CreateDirectory(logs);
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1"));
        start.ArgumentList.Add("-NoSync");
        start.ArgumentList.Add("-NoReport");
        start.ArgumentList.Add("-Trigger");
        start.ArgumentList.Add("manual");
        start.ArgumentList.Add("-CheckoutRoot");
        start.ArgumentList.Add(checkout);
        start.ArgumentList.Add("-StateRoot");
        start.ArgumentList.Add(state);
        start.ArgumentList.Add("-LogRoot");
        start.ArgumentList.Add(logs);
        start.ArgumentList.Add("-CoordinationRoot");
        start.ArgumentList.Add(coordination);
        start.ArgumentList.Add("-RunId");
        start.ArgumentList.Add(Guid.NewGuid().ToString("N"));
        if (whatIf)
            start.ArgumentList.Add("-WhatIf");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The settle path's scope: real database, real queue, real gated commit.</summary>
    private sealed class SettleScope : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public SettleScope(string connectionString, string worktreeRoot)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1,
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    TranscriptConfirmTimeoutSeconds = 3,
                    ReEnterIntervalSeconds = 1,
                },
            }));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings { PtySingleChunkBytes = 43_200 }));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<ApiErrorRecoveryService>();
            services.AddSingleton<CapacityRecoveryService>();
            services.AddScoped<ModelAvailability>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentReviewCheckpointService>();
            services.AddScoped<AgentFilesService>();
            services.AddScoped<IWorkspaceProgressProbe>(sp => sp.GetRequiredService<AgentFilesService>());
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = worktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            });
            services.AddSingleton(Options.Create(new DeliverablesSettings
            {
                BrowserPath = Path.Combine(Path.GetTempPath(), "antiphon-missing-browser", "msedge.exe"),
            }));
            services.AddSingleton<MarkdownPdfRenderer>();
            services.AddSingleton<DeliverableBundleService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }
        public void DisposeProvider() => _provider.Dispose();
    }
}
