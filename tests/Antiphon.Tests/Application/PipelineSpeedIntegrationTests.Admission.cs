using System.Data.Common;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PipelineSpeedIntegrationTests
{
    [Test]
    public async Task C1303_CardAdmissionAllowsClaimSessionInsert()
    {
        await using var w = await C544World.CreateAsync();
        var admission = new HoldAdmissionCommit();
        await w.RestartAsync(admission);
        var create = CreateAsync(w, ChildRequest(w));
        var sessionInsert = new HoldClaimSessionInsert();
        await using var claimDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(w.Schema.ConnectionString).AddInterceptors(sessionInsert).Options);
        await claimDb.Database.OpenConnectionAsync();
        // Fail on a conflicting FK lock, before either barrier's existing 30-second guard.
        await claimDb.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
        var dispatcher = new OrchestratorService(claimDb, null!, null!, null!, null!, null!, null!, null!,
            new MockEventBus(), Options.Create(new OrchestratorSettings()), Options.Create(w.Delegation),
            w.Clock, NullLogger<OrchestratorService>.Instance);
        Task<Exception?>? claim = null;
        Guid? sessionId = null;
        try
        {
            await Task.WhenAny(admission.Entered.Task, create).WaitAsync(Guard);
            admission.Entered.Task.IsCompleted.ShouldBeTrue("create holds its admission transaction");
            await ProbeCardLockAsync(w);
            claim = ClaimAsync();
            await Task.WhenAny(sessionInsert.Inserted.Task, claim).WaitAsync(Guard);
            sessionInsert.Inserted.Task.IsCompleted.ShouldBeTrue(
                "claim session insert must complete while create holds the card admission lock; "
                + (claim.IsCompleted ? (await claim)?.GetBaseException().Message : "claim still running"));
            create.IsCompleted.ShouldBeFalse("session insert did not release admission");
            // This independent NOWAIT probe also rejects simply removing the admission lock.
            await ProbeCardLockAsync(w);
        }
        finally
        {
            admission.Release.TrySetResult();
            try { await create.WaitAsync(Guard); }
            finally
            {
                // The claim's subsequent card UPDATE still requires the admission fence.
                sessionInsert.Release.TrySetResult();
                if (claim is not null) await claim.WaitAsync(Guard);
            }
        }
        (await claim!).ShouldBeNull("claim commits after admission releases the card");
        sessionId.ShouldNotBeNull();
        sessionId.ShouldBe(sessionInsert.SessionId);
        await using var verify = w.CreateContext();
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).CardId.ShouldBe(w.Card.Id);
        (await verify.Cards.SingleAsync(c => c.Id == w.Card.Id)).OwnerSessionId.ShouldBe(sessionId);
        (await verify.AgentTasks.SingleAsync(t => t.Id == create.Result.Id)).CardId.ShouldBe(w.Card.Id);

        async Task<Exception?> ClaimAsync()
        {
            try
            {
                sessionId = await dispatcher.TryClaimCardAsync(w.Card.Id, w.Card.ConcurrencyToken,
                    "fixture", AgentKind.Raw, 120, 30, w.Clock.GetUtcNow().UtcDateTime, CancellationToken.None);
                return null;
            }
            catch (Exception error) { return error; }
        }
    }

    private sealed class HoldClaimSessionInsert : SaveChangesInterceptor
    {
        public TaskCompletionSource Inserted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid SessionId { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,
            int result, CancellationToken ct = default)
        {
            if (Inserted.Task.IsCompleted) return result;
            var session = data.Context!.ChangeTracker.Entries<AgentSession>()
                .Single(e => e.Entity.CardId is not null).Entity;
            SessionId = session.Id;
            (await data.Context.Set<AgentSession>().AsNoTracking().SingleAsync(s => s.Id == SessionId, ct))
                .CardId.ShouldBe(session.CardId, "real claim inserted its session before the card UPDATE");
            Inserted.TrySetResult();
            await Release.Task.WaitAsync(Guard, ct);
            return result;
        }
    }

    [Test]
    [Arguments("title")]
    [Arguments("description")]
    [Arguments("owner")]
    [Arguments("token")]
    [Arguments("state")]
    [Arguments("archived")]
    [Arguments("platform")]
    [Arguments("policy")]
    public async Task C1289_ConcurrentCardEdit(string change)
    {
        var locks = new CardLockObserver();
        await using var w = await C544World.CreateAsync(locks);
        await using var edit = w.CreateContext();
        await using var tx = await edit.Database.BeginTransactionAsync();
        await edit.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {w.Card.Id} FOR UPDATE");
        var create = CreateAsync(w, ChildRequest(w));
        try
        {
            await Task.WhenAny(locks.Attempt.Task, create).WaitAsync(Guard);
            locks.Attempt.Task.IsCompleted.ShouldBeTrue("edit lands after admission pre-read");
            create.IsCompleted.ShouldBeFalse();
            var card = await edit.Cards.SingleAsync(c => c.Id == w.Card.Id);
            switch (change)
            {
                case "title": card.Title = "Human edited title"; break;
                case "description": card.Description = "Human edited description"; break;
                case "owner": card.OwnerSessionId = w.CallerSessionId; break;
                case "state": card.Status = CardStatus.NeedsDecision; break;
                case "archived": card.ArchivedAt = DateTime.UtcNow; break;
                case "platform": card.RequiredPlatform = RequiredPlatform.Windows; break;
                case "policy": card.CodeVerificationPolicy = CardVerificationPolicy.FullOnly; break;
            }
            card.ConcurrencyToken = Guid.NewGuid();
            await edit.SaveChangesAsync();
        }
        finally { await tx.CommitAsync(); }
        if (change is "state" or "archived" or "platform" or "policy")
        {
            var error = await Should.ThrowAsync<ConflictException>(() => create.WaitAsync(Guard));
            error.Code.ShouldBe(change == "archived" ? "card_implementation_closed" : "card_admission_changed");
            (await w.TaskCountAsync()).ShouldBe(0);
        }
        else
        {
            var child = await create.WaitAsync(Guard);
            child.CardId.ShouldBe(w.Card.Id, "unrelated edit must not refuse ordinary Code admission");
            (await w.TaskCountAsync()).ShouldBe(1);
            await using var verify = w.CreateContext();
            if (change == "title") (await verify.Cards.SingleAsync(c => c.Id == w.Card.Id)).Title.ShouldBe("Human edited title");
        }
    }

    /// <summary>
    /// CARD-1288. The sweep sentence stays the sweep. CreateAsync still returns
    /// card_implementation_closed for Code on a card that is already Done, by explicit id,
    /// by title, and by the caller parent's card. CARD-1321 also refuses an explicit id on a
    /// Canceled card, and a Shared follow-up whose prior task's card is Done.
    /// </summary>
    [Test]
    public async Task C1288_ClosedCardBindingForms()
    {
        const string sentence =
            "The sweep still leaves a task created after the close uncanceled, including a post-Done `Mutation`. "
            + "`CreateAsync` (ordinary create and a new handoff child) returns 409 `card_implementation_closed` for "
            + "`Code`, `Custom`, `Debug`, `Docs`, `Merge`, `Commit`, and `Deploy` when the resolved card is Done, "
            + "Canceled, or archived. That resolution includes an explicit card, the first `CARD-nnnn` in the title, "
            + "the caller parent's card, and a follow-up's card. Unbound creates stay admitted. "
            + "`CreateMergeTaskAsync` and `CreateCommitTaskAsync` copy the parent card and do not apply this refusal.";
        var lifecycle = CollapseWhitespace(ReadRepoFile("docs/agent-card-lifecycle.md"));
        var expected = CollapseWhitespace(sentence);
        lifecycle.ShouldContain(expected, Case.Sensitive, "c1288-closed-card-sentence");
        var sweepEnd = lifecycle.IndexOf("is left alone.", StringComparison.Ordinal);
        sweepEnd.ShouldBeGreaterThanOrEqualTo(0, "c1288-sweep-sentence");
        lifecycle[(sweepEnd + "is left alone.".Length)..].TrimStart()
            .StartsWith("The sweep still leaves a task created after the close uncanceled", StringComparison.Ordinal)
            .ShouldBeTrue("c1288-sentence-follows-sweep");

        await using var w = await C544World.CreateAsync();
        await using (var db = w.CreateContext())
        {
            var card = await db.Cards.SingleAsync(c => c.Id == w.Card.Id);
            card.Status = CardStatus.Done;
            card.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        await RefuseClosedCodeAsync(w, ChildRequest(w), w.Caller());
        await RefuseClosedCodeAsync(w, ChildRequest(w) with
        {
            Card = null,
            Title = w.Card.Identifier + " closed title",
        }, w.Caller());
        // A worker cannot delegate. The in-memory parent is the bound orchestrator; its card is the Done card.
        var parent = new AgentTask
        {
            Id = w.Owner.Id,
            RootTaskId = w.Owner.RootTaskId,
            Depth = w.Owner.Depth,
            Kind = AgentTaskKind.Orchestrator,
            CardId = w.Card.Id,
        };
        await RefuseClosedCodeAsync(w, ChildRequest(w) with
        {
            Card = null,
            Title = "closed parent bind",
        }, new AgentTaskService.Caller(parent, w.CallerSessionId, w.RepositoryPath, ProjectId: w.Project.Id));

        await using (var db = w.CreateContext())
        {
            var card = await db.Cards.SingleAsync(c => c.Id == w.Card.Id);
            card.Status = CardStatus.Canceled;
            card.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        await RefuseClosedCodeAsync(w, ChildRequest(w), w.Caller());

        Guid priorId;
        await using (var db = w.CreateContext())
        {
            var card = await db.Cards.SingleAsync(c => c.Id == w.Card.Id);
            card.Status = CardStatus.Done;
            card.ConcurrencyToken = Guid.NewGuid();
            priorId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.AgentTasks.Add(new AgentTask
            {
                Id = priorId,
                RootTaskId = priorId,
                Title = "shared prior",
                Goal = "Earlier shared work.",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = w.RepositoryPath,
                Status = AgentTaskStatus.Succeeded,
                CardId = w.Card.Id,
                ProjectId = w.Project.Id,
                CreatedAt = now.AddHours(-1),
                CompletedAt = now,
            });
            await db.SaveChangesAsync();
        }
        // Caller() has a null task, so a parent card cannot mask the follow-up.
        // Shared skips the frozen worktree tip. The title has no CARD- token.
        // TaskCountAsync excludes only the owner, so the seeded prior is the one counted row.
        await RefuseClosedCodeAsync(w, ChildRequest(w) with
        {
            Card = null,
            Title = "closed follow-up bind",
            Workspace = WorkspaceMode.Shared,
            FollowUpOnTask = priorId.ToString(),
        }, w.Caller(), expectedTasks: 1);
    }

    private static async Task RefuseClosedCodeAsync(
        C544World w, CreateAgentTaskRequest request, AgentTaskService.Caller caller, int expectedTasks = 0)
    {
        await using var scope = w.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var error = await Should.ThrowAsync<ConflictException>(() =>
            service.CreateAsync(request, caller, CancellationToken.None));
        error.Code.ShouldBe("card_implementation_closed");
        (await w.TaskCountAsync()).ShouldBe(expectedTasks);
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        return File.ReadAllText(Path.Combine(directory.FullName, relative));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1289_GitLockDuration(bool handoff)
    {
        var locks = new AdmissionLocks();
        await using var w = await C544World.CreateAsync(locks);
        var git = new AdmissionGit(w);
        w.ConfigureServices = services =>
        {
            services.AddSingleton<ILandingGit>(git);
            services.AddScoped<DelegationOpenGate>();
        };
        await w.RestartAsync(locks);
        var action = handoff ? await SeedActionAsync(w) : null;
        var started = Stopwatch.GetTimestamp();
        var child = await CreateAsync(w, ChildRequest(w), action?.Id);
        Console.WriteLine($"C1289 measurement handoff={handoff} createMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} cardLockMs={locks.HeldMilliseconds:F1} gitMs={git.ElapsedMilliseconds:F1} gitCalls={git.Calls} lockedDuringGit={git.CardLocked}");
        git.Calls.ShouldBeGreaterThan(0, "real Git preview was reached");
        git.CardLocked.ShouldBe(handoff, "ordinary Git preview precedes the card lock; handoff keeps idempotent recovery first");
        (await w.TaskAsync(child.Id)).WorktreeBasePreviewJson.ShouldNotBeNullOrWhiteSpace();
        if (action is not null)
        {
            await using var db = w.CreateContext();
            (await db.PipelineHandoffActions.SingleAsync(a => a.Id == action.Id)).ChildTaskId.ShouldBe(child.Id);
            var calls = git.Calls;
            (await CreateAsync(w, ChildRequest(w), action.Id)).Id.ShouldBe(child.Id);
            git.Calls.ShouldBe(calls, "linked-child retry must bypass Git inspection");
        }
    }

    [Test]
    public async Task C1289_CardLockOrder()
    {
        var locks = new AdmissionLocks();
        await using var w = await C544World.CreateAsync(locks);
        var action = await SeedActionAsync(w);
        await using (var scope = w.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var writer = new PipelineHandoffActionService(db, w.Clock);
            await writer.RecordAsync(w.Owner.Id, new PipelineHandoffActionRequest(action.Attempt,
                action.SettlementEventId, AgentTaskRole.Code, 0, "eligible", "lock order"), "fixture", CancellationToken.None);
        }
        string.Join(",", locks.Order).ShouldBe("card,task,action", "observation writer takes card before task/action");
        locks.Order.Clear();
        await using (var scope = w.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var writer = new PipelineHandoffActionService(db, w.Clock);
            await Should.ThrowAsync<ConflictException>(() => writer.RequestChildAsync(w.Owner.Id, action.Id,
                new PipelineHandoffChildRequest(ChildRequest(w)), w.Caller(), "fixture", CancellationToken.None));
        }
        string.Join(",", locks.Order).ShouldBe("card,action", "refusal writer takes card before action");
        locks.Order.Clear();
        await CreateAsync(w, ChildRequest(w), action.Id);
        string.Join(",", locks.Order).ShouldBe("card,action,host,concurrency", "admission lock order");
    }

    [Test]
    public async Task C1303_R4_ConcurrentCodeCreatesSurviveTransitionSweep()
    {
        var pause = new AdmissionPreReadPause();
        await using var w = await C544World.CreateAsync(pause, delegation: settings =>
        {
            settings.MaxOpenTasks = 20;
            settings.RolePolicy["Code"].RecommendedInFlight = 8;
        });
        var evidenceAt = await SeedSweepEvidenceAsync(w);
        var first = ObserveAsync(CreateAsync(w, ChildRequest(w)));
        var second = ObserveAsync(CreateAsync(w, ChildRequest(w)));
        Exception? setup = null;
        try
        {
            try { await pause.BothPreReads.Task.WaitAsync(Guard); }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    "admission pre-read not reached; card SQL: " + pause.Describe(), ex);
            }
            (await SweepAsync(w)).ShouldBe(1, "the transition sweep moves the card while both creates hold the pre-read");
            await using var moved = w.CreateContext();
            var card = await moved.Cards.SingleAsync(c => c.Id == w.Card.Id);
            card.Status.ShouldBe(CardStatus.InProgress);
            (await moved.CardRevisions.Where(r => r.CardId == w.Card.Id && r.Kind == CardRevisionKind.Move)
                .OrderByDescending(r => r.RevisionNumber).FirstAsync()).EditedBy.ShouldBe(CardService.TransitionActor);
            evidenceAt.ShouldBeGreaterThan(card.CreatedAt);
        }
        catch (Exception error) { setup = error; }
        finally { pause.Release.TrySetResult(); }
        var results = await AwaitPairAsync(first, second);
        if (setup is not null) throw setup;

        pause.BothPreReads.Task.IsCompleted.ShouldBeTrue(
            "both creates reach the admission pre-read; card SQL: " + pause.Describe());
        foreach (var result in new[] { results.Item1, results.Item2 })
        {
            result.Error.ShouldBeNull(
                "an open-column sweep move is not card_admission_changed: "
                + (result.Error is ConflictException conflict
                    ? conflict.Code + " " + conflict.Message
                    : result.Error?.GetBaseException().Message));
            result.Created!.CardId.ShouldBe(w.Card.Id);
        }
        results.Item1.Created!.Id.ShouldNotBe(results.Item2.Created!.Id);
        (await w.TaskCountAsync()).ShouldBe(3, "sweep evidence plus both Code creates");
    }

    private static async Task<DateTime> SeedSweepEvidenceAsync(C544World w)
    {
        await using var db = w.CreateContext();
        var card = await db.Cards.SingleAsync(c => c.Id == w.Card.Id);
        var now = card.UpdatedAt;
        db.BoardColumns.Add(new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = w.Board.Id, StateKey = "in-progress", Name = "In Progress",
            ColumnOrder = 1, CardStatus = CardStatus.InProgress, IsActive = true,
            CreatedAt = now, UpdatedAt = now,
        });
        var id = Guid.NewGuid();
        var dispatchedAt = now.AddSeconds(1);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "Sibling dispatch", Goal = "Move the card.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High, Status = AgentTaskStatus.Dispatched,
            CardId = w.Card.Id, ProjectId = w.Project.Id, WorkingDirectory = w.RepositoryPath,
            DispatchedAt = dispatchedAt, CreatedAt = dispatchedAt,
        });
        await db.SaveChangesAsync();
        return dispatchedAt;
    }

    private static async Task<int> SweepAsync(C544World w)
    {
        await using var db = w.CreateContext();
        await using var scope = w.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var cards = new CardService(
            db,
            services.GetRequiredService<AgentRegistry>(),
            null!,
            services.GetRequiredService<AgentSessionLaunchQueue>(),
            services.GetRequiredService<IEventBus>(),
            w.Clock,
            services.GetRequiredService<AgentReviewCheckpointService>());
        var sweep = new CardWorkTransitionService(
            db, cards, Options.Create(new CardWorkTransitionSettings { Enabled = true }),
            NullLogger<CardWorkTransitionService>.Instance);
        return await sweep.ScanAsync(CancellationToken.None);
    }

    private static async Task<CreateResult> ObserveAsync(Task<AgentTaskCreatedDto> create)
    {
        try { return new CreateResult(await create, null); }
        catch (Exception error) { return new CreateResult(null, error); }
    }

    private static async Task<(CreateResult, CreateResult)> AwaitPairAsync(Task<CreateResult> first, Task<CreateResult> second)
    {
        var pair = await Task.WhenAll(first, second).WaitAsync(Guard);
        return (pair[0], pair[1]);
    }

    private sealed record CreateResult(AgentTaskCreatedDto? Created, Exception? Error);

    private sealed class AdmissionPreReadPause : DbCommandInterceptor
    {
        private int _preReads;
        public TaskCompletionSource BothPreReads { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _seen = [];

        public string Describe()
        {
            lock (_seen) return _seen.Count == 0 ? "(none)" : string.Join(" || ", _seen);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("\"Cards\"", StringComparison.Ordinal))
            {
                lock (_seen)
                {
                    if (_seen.Count < 6) _seen.Add(sql.ReplaceLineEndings(" "));
                }
            }
            if (!IsAdmissionProjection(sql)) return result;
            var seen = Interlocked.Increment(ref _preReads);
            if (seen <= 2)
            {
                if (seen == 2) BothPreReads.TrySetResult();
                await Release.Task.WaitAsync(Guard, ct);
            }
            return result;
        }

        private static bool IsAdmissionProjection(string sql) =>
            sql.Contains("\"CodeVerificationPolicy\"", StringComparison.Ordinal)
            && sql.Contains("\"ReviewVerificationPolicy\"", StringComparison.Ordinal)
            && sql.Contains("\"ActiveWorkflowRunId\"", StringComparison.Ordinal)
            && !sql.Contains("FOR UPDATE", StringComparison.Ordinal)
            && !sql.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal);
    }

    private sealed class AdmissionLocks : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public List<string> Order { get; } = [];
        private long _locked;
        public double HeldMilliseconds { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("FOR UPDATE") || sql.Contains("FOR NO KEY UPDATE"))
            {
                if (sql.Contains("\"Cards\"")) { Order.Add("card"); _locked = Stopwatch.GetTimestamp(); }
                else if (sql.Contains("\"PipelineHandoffActions\"")) Order.Add("action");
                else if (sql.Contains("\"AgentTasks\"")) Order.Add("task");
            }
            if (sql.Contains("antiphon-host-budget")) Order.Add("host");
            if (sql.Contains(DelegationOpenGate.AdvisoryLockKey)) Order.Add("concurrency");
            return ValueTask.FromResult(result);
        }
        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken ct = default)
        {
            if (_locked != 0) HeldMilliseconds += Stopwatch.GetElapsedTime(_locked).TotalMilliseconds;
            _locked = 0;
            return Task.CompletedTask;
        }
    }

    private sealed class AdmissionGit(C544World world) : LandingGit, ILandingGit
    {
        public int Calls { get; private set; }
        public bool CardLocked { get; private set; }
        public double ElapsedMilliseconds { get; private set; }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(world.Schema.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT 1 FROM \"Cards\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", connection);
            command.Parameters.AddWithValue("id", world.Card.Id);
            try { await command.ExecuteScalarAsync(ct); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable) { CardLocked = true; }
            var started = Stopwatch.GetTimestamp();
            var result = await base.RunAsync(repository, arguments, ct);
            ElapsedMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Calls++;
            return result;
        }
    }
}
