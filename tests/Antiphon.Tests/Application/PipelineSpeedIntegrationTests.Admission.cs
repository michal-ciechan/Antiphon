using System.Data.Common;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PipelineSpeedIntegrationTests
{
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

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1289_GitLockDuration(bool handoff)
    {
        var locks = new AdmissionLocks();
        await using var w = await C544World.CreateAsync(locks);
        var git = new AdmissionGit(w);
        w.ConfigureServices = services => services.AddSingleton<ILandingGit>(git);
        await w.RestartAsync(locks);
        var action = handoff ? await SeedActionAsync(w) : null;
        var started = Stopwatch.GetTimestamp();
        var child = await CreateAsync(w, ChildRequest(w), action?.Id);
        Console.WriteLine($"C1289 measurement handoff={handoff} createMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} cardLockMs={locks.HeldMilliseconds:F1} commonGitMs={git.ElapsedMilliseconds:F1} commonGitCalls={git.Calls} lockedDuringGit={git.CardLocked}");
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

    private sealed class AdmissionLocks : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public List<string> Order { get; } = [];
        private long _locked;
        public double HeldMilliseconds { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("FOR UPDATE"))
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
        public new async Task<string> CommonDirectoryAsync(string repository, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(world.Schema.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT 1 FROM \"Cards\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", connection);
            command.Parameters.AddWithValue("id", world.Card.Id);
            try { await command.ExecuteScalarAsync(ct); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable) { CardLocked = true; }
            var started = Stopwatch.GetTimestamp();
            var result = await base.CommonDirectoryAsync(repository, ct);
            ElapsedMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Calls++;
            return result;
        }
    }
}
