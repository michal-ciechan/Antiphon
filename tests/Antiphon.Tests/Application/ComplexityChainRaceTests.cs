using System.Data.Common;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1275 D1: deterministic races. A save interceptor on the caller's context commits a
/// competing write from a second context between the caller's read and its save. A write
/// without a token makes up to <see cref="ComplexityChainService.UnconditionalAttempts"/> saves
/// (last writer wins); a write with a token is 409 <c>complexity_chain_stale</c> on its first
/// lost race, except a clear that finds the row gone after the lost race, which returns. Also
/// the NotAfter sweep (token rotation, lost races) and the read order of the <c>?role=</c> view.
/// </summary>
[Category("Integration")]
public sealed class ComplexityChainRaceTests
{
    [Test]
    public async Task A_write_without_a_token_that_loses_a_race_is_retried_and_the_callers_values_win()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seed = await WriteAsync(schema, null, TaskComplexity.Hard, AgentKind.Grok, "seed");
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        string? competitorToken = null;
        race.Arm(1, async () => competitorToken = (await WriteAsync(
            schema, null, TaskComplexity.Hard, AgentKind.Codex, "competitor")).ConcurrencyToken.ToString());
        var written = await Service(db).UpsertAsync(
            role: null, TaskComplexity.Hard, Put(AgentKind.ClaudeCode, "caller"), null, CancellationToken.None);

        race.Fired.ShouldBe(1);
        race.Saves.ShouldBe(2);
        written.Reason.ShouldBe("caller");
        written.ConcurrencyToken.ShouldNotBeNull();
        written.ConcurrencyToken.ToString().ShouldNotBe(competitorToken.ShouldNotBeNull());
        written.ConcurrencyToken.ShouldNotBe(seed.ConcurrencyToken);
        var stored = await LiveAsync(schema, null, TaskComplexity.Hard);
        stored.Reason.ShouldBe("caller");
        stored.ParseCandidates()[0].AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ConcurrencyToken.ShouldBe(written.ConcurrencyToken!.Value);

        // A create that loses to a create (unique active-row index) is re-run as an update.
        race.Arm(1, () => WriteAsync(schema, AgentTaskRole.Plan, TaskComplexity.Medium, AgentKind.Codex, "competitor"));
        var created = await Service(db).UpsertAsync(
            AgentTaskRole.Plan, TaskComplexity.Medium, Put(AgentKind.ClaudeCode, "caller"), null, CancellationToken.None);
        race.Fired.ShouldBe(1);
        race.Saves.ShouldBe(2);
        created.Reason.ShouldBe("caller");
        (await LiveAsync(schema, AgentTaskRole.Plan, TaskComplexity.Medium)).Reason.ShouldBe("caller");
    }

    [Test]
    public async Task A_delete_without_a_token_that_loses_a_race_is_retried_and_clears()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await WriteAsync(schema, null, TaskComplexity.Medium, AgentKind.Grok, "seed");
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        race.Arm(1, () => WriteAsync(schema, null, TaskComplexity.Medium, AgentKind.Codex, "competitor"));
        await Service(db).ClearAsync(null, TaskComplexity.Medium, expectedToken: null, CancellationToken.None);
        race.Fired.ShouldBe(1);
        race.Saves.ShouldBe(2);
        (await LiveOrNullAsync(schema, null, TaskComplexity.Medium)).ShouldBeNull();

        // Lost to a clear: the row is gone, which is what the caller asked for (204, no retry).
        await WriteAsync(schema, null, TaskComplexity.Easy, AgentKind.Grok, "seed");
        race.Arm(1, () => ClearWithAsync(schema, null, TaskComplexity.Easy));
        await Service(db).ClearAsync(null, TaskComplexity.Easy, expectedToken: null, CancellationToken.None);
        race.Fired.ShouldBe(1);
        race.Saves.ShouldBe(1);
        (await LiveOrNullAsync(schema, null, TaskComplexity.Easy)).ShouldBeNull();
    }

    [Test]
    public async Task A_write_with_a_token_that_loses_a_race_is_stale_at_once_with_the_current_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seed = await WriteAsync(schema, null, TaskComplexity.Hard, AgentKind.Grok, "seed");
        var opened = seed.ConcurrencyToken!.Value;
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        Guid? competitor = null;
        race.Arm(1, async () => competitor = (await WriteAsync(
            schema, null, TaskComplexity.Hard, AgentKind.Codex, "competitor")).ConcurrencyToken);
        var stale = await Should.ThrowAsync<HttpException>(() => Service(db).UpsertAsync(
            null, TaskComplexity.Hard, Put(AgentKind.ClaudeCode, "caller") with { ConcurrencyToken = opened },
            null, CancellationToken.None));
        race.Fired.ShouldBe(1);
        race.Saves.ShouldBe(1);
        stale.StatusCode.ShouldBe(409);
        stale.Code.ShouldBe(ComplexityChainService.StaleCode);
        CurrentToken(stale).ShouldBe(competitor.ShouldNotBeNull().ToString());
        (await LiveAsync(schema, null, TaskComplexity.Hard)).Reason.ShouldBe("competitor");

        // The same old version sent again is refused by the read check, with the current row.
        var again = await Should.ThrowAsync<HttpException>(() => Service(db).UpsertAsync(
            null, TaskComplexity.Hard, Put(AgentKind.ClaudeCode, "caller") with { ConcurrencyToken = opened },
            null, CancellationToken.None));
        again.Code.ShouldBe(ComplexityChainService.StaleCode);
        CurrentToken(again).ShouldBe(competitor.ToString());

        // A token DELETE that loses to a write is stale too, and keeps the competitor's row.
        race.Arm(1, async () => competitor = (await WriteAsync(
            schema, null, TaskComplexity.Hard, AgentKind.Grok, "second competitor")).ConcurrencyToken);
        var current = (await LiveAsync(schema, null, TaskComplexity.Hard)).ConcurrencyToken;
        var staleClear = await Should.ThrowAsync<HttpException>(() => Service(db).ClearAsync(
            null, TaskComplexity.Hard, current, CancellationToken.None));
        race.Saves.ShouldBe(1);
        staleClear.Code.ShouldBe(ComplexityChainService.StaleCode);
        CurrentToken(staleClear).ShouldBe(competitor.ToString());
        (await LiveAsync(schema, null, TaskComplexity.Hard)).Reason.ShouldBe("second competitor");

        // The empty token ("I saw no row") that loses to a create: the unique index refuses it.
        race.Arm(1, () => WriteAsync(schema, AgentTaskRole.Code, TaskComplexity.Easy, AgentKind.Codex, "first"));
        var lateCreate = await Should.ThrowAsync<HttpException>(() => Service(db).UpsertAsync(
            AgentTaskRole.Code, TaskComplexity.Easy, Put(AgentKind.Grok, "late") with { ConcurrencyToken = Guid.Empty },
            null, CancellationToken.None));
        race.Saves.ShouldBe(1);
        lateCreate.Code.ShouldBe(ComplexityChainService.StaleCode);
        (await LiveAsync(schema, AgentTaskRole.Code, TaskComplexity.Easy)).Reason.ShouldBe("first");
    }

    [Test]
    public async Task A_race_lost_on_every_attempt_exhausts_the_bound_and_is_stale()
    {
        // docs/antiphon-api.md names the bound.
        ComplexityChainService.UnconditionalAttempts.ShouldBe(3);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await WriteAsync(schema, null, TaskComplexity.Hard, AgentKind.Grok, "seed");
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        var round = 0;
        race.Arm(int.MaxValue, () => WriteAsync(
            schema, null, TaskComplexity.Hard, AgentKind.Codex, $"competitor {++round}"));
        var put = await Should.ThrowAsync<HttpException>(() => Service(db).UpsertAsync(
            null, TaskComplexity.Hard, Put(AgentKind.ClaudeCode, "caller"), null, CancellationToken.None));
        put.StatusCode.ShouldBe(409);
        put.Code.ShouldBe(ComplexityChainService.StaleCode);
        race.Saves.ShouldBe(ComplexityChainService.UnconditionalAttempts);
        race.Fired.ShouldBe(ComplexityChainService.UnconditionalAttempts);
        (await LiveAsync(schema, null, TaskComplexity.Hard)).Reason
            .ShouldBe($"competitor {ComplexityChainService.UnconditionalAttempts}");

        race.Arm(int.MaxValue, () => WriteAsync(
            schema, null, TaskComplexity.Hard, AgentKind.Codex, $"competitor {++round}"));
        var clear = await Should.ThrowAsync<HttpException>(() => Service(db).ClearAsync(
            null, TaskComplexity.Hard, expectedToken: null, CancellationToken.None));
        clear.Code.ShouldBe(ComplexityChainService.StaleCode);
        race.Saves.ShouldBe(ComplexityChainService.UnconditionalAttempts);
        (await LiveOrNullAsync(schema, null, TaskComplexity.Hard)).ShouldNotBeNull();
    }

    [Test]
    public async Task The_expiry_sweep_rotates_the_token_so_a_write_that_read_the_row_live_loses_its_save()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var seed = await WriteAsync(schema, null, TaskComplexity.Hard, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        // The caller reads the row live; the sweep clears it before the caller's save.
        race.Arm(1, () => SweepAsync(schema, clock, TaskComplexity.Hard));
        var stale = await Should.ThrowAsync<HttpException>(() => Service(db, clock).UpsertAsync(
            null, TaskComplexity.Hard,
            Put(AgentKind.ClaudeCode, "caller") with { ConcurrencyToken = seed.ConcurrencyToken },
            null, CancellationToken.None));
        stale.Code.ShouldBe(ComplexityChainService.StaleCode);
        CurrentToken(stale).ShouldBeNull();
        (await LiveOrNullAsync(schema, null, TaskComplexity.Hard)).ShouldBeNull();
        await using (var read = Open(schema))
        {
            var swept = await read.ComplexityChains.AsNoTracking().SingleAsync(c => c.Complexity == TaskComplexity.Hard);
            swept.ClearedAt.ShouldNotBeNull();
            swept.ConcurrencyToken.ShouldNotBe(seed.ConcurrencyToken!.Value);
        }

        // Without a token the caller re-reads, finds no row and creates one.
        await WriteAsync(schema, null, TaskComplexity.Medium, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        race.Arm(1, () => SweepAsync(schema, clock, TaskComplexity.Medium));
        var written = await Service(db, clock).UpsertAsync(
            null, TaskComplexity.Medium, Put(AgentKind.ClaudeCode, "caller"), null, CancellationToken.None);
        race.Saves.ShouldBe(2);
        written.Reason.ShouldBe("caller");
        (await LiveAsync(schema, null, TaskComplexity.Medium)).Reason.ShouldBe("caller");
    }

    [Test]
    public async Task A_sweep_that_loses_to_a_write_reads_again_and_returns_the_writers_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        await WriteAsync(schema, null, TaskComplexity.Easy, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(2));
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        // The competitor sweeps the same row (rotating its token) and writes a new one.
        race.Arm(1, () => WriteAsync(schema, null, TaskComplexity.Easy, AgentKind.Codex, "competitor", clock));
        var live = await Routing(db, clock).FindActiveAsync(null, TaskComplexity.Easy, CancellationToken.None);

        race.Fired.ShouldBe(1);
        live.ShouldNotBeNull().Reason.ShouldBe("competitor");
        (await LiveAsync(schema, null, TaskComplexity.Easy)).Id.ShouldBe(live.Id);
    }

    [Test]
    public async Task A_sweep_that_loses_every_attempt_lets_the_concurrency_error_propagate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        await WriteAsync(schema, null, TaskComplexity.Easy, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(2));
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        // Before each sweep save another writer rotates the expired row's token, so every save loses.
        race.Arm(int.MaxValue, async () =>
        {
            await using var other = Open(schema);
            await other.ComplexityChains
                .Where(c => c.ClearedAt == null && c.Role == null && c.Complexity == TaskComplexity.Easy)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));
        });

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() =>
            Routing(db, clock).FindActiveAsync(null, TaskComplexity.Easy, CancellationToken.None));
        race.Saves.ShouldBe(ComplexityRoutingService.SweepAttempts);
        race.Fired.ShouldBe(ComplexityRoutingService.SweepAttempts);
        (await LiveOrNullAsync(schema, null, TaskComplexity.Easy)).ShouldNotBeNull();

        // CARD-1278: the context keeps no stale clear, so its next save (a task create) carries none.
        db.ChangeTracker.Entries<ComplexityChain>().ShouldNotContain(e => e.State == EntityState.Modified);
        race.Arm(0, () => Task.CompletedTask);
        (await db.SaveChangesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_sweep_of_several_rows_that_loses_every_attempt_leaves_none_of_them_modified()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        await WriteAsync(schema, AgentTaskRole.Plan, TaskComplexity.Easy, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        await WriteAsync(schema, AgentTaskRole.Code, TaskComplexity.Easy, AgentKind.Grok, "expiring",
            clock, clock.GetUtcNow().AddMinutes(1));
        var lost = await LiveAsync(schema, AgentTaskRole.Plan, TaskComplexity.Easy);
        clock.Advance(TimeSpan.FromMinutes(2));
        var race = new CompetingCommit();
        await using var db = Open(schema, race);

        // Only the Plan row loses; the Code row's clear was valid but rolled back with the batch.
        race.Arm(int.MaxValue, async () =>
        {
            await using var other = Open(schema);
            await other.ComplexityChains.Where(c => c.Id == lost.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ConcurrencyToken, Guid.NewGuid()));
        });

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() =>
            Routing(db, clock).ListLiveCellsAsync(CancellationToken.None));
        race.Saves.ShouldBe(ComplexityRoutingService.SweepAttempts);
        db.ChangeTracker.Entries<ComplexityChain>().ShouldNotContain(e => e.State == EntityState.Modified);
        race.Arm(0, () => Task.CompletedTask);
        (await db.SaveChangesAsync()).ShouldBe(0);
        (await LiveOrNullAsync(schema, AgentTaskRole.Plan, TaskComplexity.Easy)).ShouldNotBeNull();
        (await LiveOrNullAsync(schema, AgentTaskRole.Code, TaskComplexity.Easy)).ShouldNotBeNull();
    }

    [Test]
    public async Task The_role_view_reads_the_token_before_the_candidates_so_a_change_between_them_makes_a_save_stale()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();

        // Cleared and re-created between the reads: newer candidates, older token; the save is 409.
        var old = await WriteAsync(schema, AgentTaskRole.Review, TaskComplexity.Hard, AgentKind.Grok, "old");
        Guid? recreated = null;
        var cell = await RoleViewAsync(schema, TaskComplexity.Hard, async () =>
        {
            await ClearWithAsync(schema, AgentTaskRole.Review, TaskComplexity.Hard);
            recreated = (await WriteAsync(
                schema, AgentTaskRole.Review, TaskComplexity.Hard, AgentKind.Codex, "new")).ConcurrencyToken;
        });
        cell.ResolvedFrom.ShouldBe("role");
        cell.Candidates.ShouldHaveSingleItem().AgentKind.ShouldBe(AgentKind.Codex);
        cell.ConcurrencyToken.ShouldBe(old.ConcurrencyToken);
        var stale = await SaveAsEditorAsync(schema, TaskComplexity.Hard, cell);
        stale.Code.ShouldBe(ComplexityChainService.StaleCode);
        CurrentToken(stale).ShouldBe(recreated.ShouldNotBeNull().ToString());
        (await LiveAsync(schema, AgentTaskRole.Review, TaskComplexity.Hard)).Reason.ShouldBe("new");

        // Created between the reads: the view has the row's candidates and no token; the save is 409.
        var created = await RoleViewAsync(schema, TaskComplexity.Easy,
            () => WriteAsync(schema, AgentTaskRole.Review, TaskComplexity.Easy, AgentKind.Codex, "new"));
        created.ResolvedFrom.ShouldBe("role");
        created.ConcurrencyToken.ShouldBeNull();
        (await SaveAsEditorAsync(schema, TaskComplexity.Easy, created)).Code.ShouldBe(ComplexityChainService.StaleCode);
        (await LiveAsync(schema, AgentTaskRole.Review, TaskComplexity.Easy)).Reason.ShouldBe("new");

        // Cleared between the reads: the view shows no row and no token, the state a save then meets.
        await WriteAsync(schema, AgentTaskRole.Review, TaskComplexity.Medium, AgentKind.Grok, "old");
        var cleared = await RoleViewAsync(schema, TaskComplexity.Medium,
            () => ClearWithAsync(schema, AgentTaskRole.Review, TaskComplexity.Medium));
        cleared.ResolvedFrom.ShouldBe("none");
        cleared.Candidates.ShouldBeEmpty();
        cleared.Reason.ShouldBeNull();
        cleared.ConcurrencyToken.ShouldBeNull();
    }

    /// <summary>The <c>?role=Review</c> cell, with <paramref name="between"/> run after its first chain read.</summary>
    private static async Task<ComplexityChainDto> RoleViewAsync(
        IsolatedTestSchema schema, TaskComplexity complexity, Func<Task> between)
    {
        var hook = new AfterFirstChainRead(between);
        await using var db = Open(schema, hook);
        var cell = await Service(db).GetEffectiveAsync(AgentTaskRole.Review, complexity, CancellationToken.None);
        hook.Fired.ShouldBeTrue();
        return cell;
    }

    /// <summary>A Save from the editor opened on <paramref name="cell"/>: its token, or the empty GUID.</summary>
    private static async Task<HttpException> SaveAsEditorAsync(
        IsolatedTestSchema schema, TaskComplexity complexity, ComplexityChainDto cell)
    {
        await using var db = Open(schema);
        var stale = await Should.ThrowAsync<HttpException>(() => Service(db).UpsertAsync(
            AgentTaskRole.Review, complexity,
            Put(AgentKind.ClaudeCode, "editor") with { ConcurrencyToken = cell.ConcurrencyToken ?? Guid.Empty },
            null, CancellationToken.None));
        stale.StatusCode.ShouldBe(409);
        return stale;
    }

    private static PutComplexityChainRequest Put(AgentKind kind, string reason) =>
        new([new ComplexityCandidateRequest(kind, AgentModelLevel.Frontier)], RoutingPinProvenance.Human, reason);

    private static async Task<ComplexityChainDto> WriteAsync(
        IsolatedTestSchema schema, AgentTaskRole? role, TaskComplexity complexity, AgentKind kind, string reason,
        TimeProvider? clock = null, DateTimeOffset? notAfter = null)
    {
        await using var db = Open(schema);
        return await Service(db, clock).UpsertAsync(
            role, complexity, Put(kind, reason) with { NotAfter = notAfter }, null, CancellationToken.None);
    }

    private static async Task ClearWithAsync(IsolatedTestSchema schema, AgentTaskRole? role, TaskComplexity complexity)
    {
        await using var db = Open(schema);
        await Service(db).ClearAsync(role, complexity, expectedToken: null, CancellationToken.None);
    }

    private static async Task SweepAsync(IsolatedTestSchema schema, FakeTimeProvider clock, TaskComplexity complexity)
    {
        clock.Advance(TimeSpan.FromMinutes(2));
        await using var db = Open(schema);
        (await Routing(db, clock).FindActiveAsync(null, complexity, CancellationToken.None)).ShouldBeNull();
    }

    private static async Task<ComplexityChain?> LiveOrNullAsync(
        IsolatedTestSchema schema, AgentTaskRole? role, TaskComplexity complexity)
    {
        await using var db = Open(schema);
        return await db.ComplexityChains.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ClearedAt == null && c.Role == role && c.Complexity == complexity);
    }

    private static async Task<ComplexityChain> LiveAsync(
        IsolatedTestSchema schema, AgentTaskRole? role, TaskComplexity complexity) =>
        (await LiveOrNullAsync(schema, role, complexity)).ShouldNotBeNull();

    private static string? CurrentToken(HttpException stale)
    {
        var current = (JsonElement)stale.Extensions.ShouldNotBeNull()["current"]!;
        var token = current.GetProperty("concurrencyToken");
        return token.ValueKind == JsonValueKind.Null ? null : token.GetString();
    }

    private static AppDbContext Open(IsolatedTestSchema schema, params IInterceptor[] interceptors) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString, interceptors));

    private static ComplexityRoutingService Routing(AppDbContext db, TimeProvider? clock = null)
    {
        var options = Options.Create(new DelegationSettings());
        clock ??= TimeProvider.System;
        return new ComplexityRoutingService(
            db, options, clock, new ModelAvailability(db, clock, NullLogger<ModelAvailability>.Instance));
    }

    private static ComplexityChainService Service(AppDbContext db, TimeProvider? clock = null) =>
        new(db, clock ?? TimeProvider.System, Routing(db, clock), Options.Create(new DelegationSettings()),
            NullLogger<ComplexityChainService>.Instance);

    /// <summary>Runs the competing write before the caller's save, the armed number of times.</summary>
    private sealed class CompetingCommit : SaveChangesInterceptor
    {
        private Func<Task> _compete = () => Task.CompletedTask;
        private int _remaining;

        public int Saves { get; private set; }
        public int Fired { get; private set; }

        public void Arm(int times, Func<Task> compete)
        {
            _compete = compete;
            _remaining = times;
            Saves = 0;
            Fired = 0;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (_remaining > 0)
            {
                _remaining--;
                Fired++;
                await _compete();
            }
            return result;
        }
    }

    /// <summary>Runs <paramref name="between"/> once, after the first ComplexityChains query.</summary>
    private sealed class AfterFirstChainRead(Func<Task> between) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("\"ComplexityChains\"", StringComparison.Ordinal))
            {
                Fired = true;
                await between();
            }
            return result;
        }
    }
}
