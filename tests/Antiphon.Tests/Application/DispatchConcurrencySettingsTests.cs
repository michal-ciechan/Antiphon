using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-2. Durable seed, inheritance, audit and races on isolated PostgreSQL.</summary>
[Category("Integration")]
public class DispatchConcurrencySettingsTests
{
    [Test]
    public async Task Import_preserves_bound_values_once()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        var read = await shop.ReadGlobalAsync();
        read.Revision.ShouldBe(1, "bound-seed");
        read.Provenance.ShouldBe("Migration", "bound-seed");
        read.Reason.ShouldBe(DispatchConcurrencySettingsService.MigrationReason, "bound-seed");
        read.UpdatedAt.ShouldBe(DispatchConcurrencyTestHost.SeedInstant.UtcDateTime, "bound-seed");
        read.Seed.Origin.ShouldBe("startupConfiguration", "bound-seed");
        read.Seed.Mode.ShouldBe("LegacyOpen", "bound-seed");
        read.Seed.MaxParallel.ShouldBe(9, "bound-seed");
        read.Seed.MaxQueued.ShouldBeNull("bound-seed");
        Role(read.Seed, "Code").ShouldBe(5, "bound-seed");
        Role(read.Seed, "Review").ShouldBe(4, "bound-seed");
        Role(read.Seed, "Plan").ShouldBe(3, "bound-seed");
        Role(read.Seed, "Custom").ShouldBeNull("bound-seed");
        (await shop.ReadProjectAsync(shop.ProjectP)).Revision.ShouldBe(0, "bound-seed");

        var divergent = new DelegationSettings { MaxOpenTasks = 2 };
        divergent.RolePolicy["Code"].RecommendedInFlight = 1;
        await using var db = shop.Db();
        var still = await shop.Service(db, divergent).GetGlobalAsync(CancellationToken.None);
        still.Seed.MaxParallel.ShouldBe(9, "bound-seed");
        Role(still.Seed, "Code").ShouldBe(5, "bound-seed");

        var wide = new DelegationSettings { MaxOpenTasks = 600 };
        wide.RolePolicy["Code"].RecommendedInFlight = 700;
        await using var wideShop = await DispatchConcurrencyShop.Open(wide);
        var imported = await wideShop.ReadGlobalAsync();
        imported.Seed.MaxParallel.ShouldBe(600, "bound-seed");
        Role(imported.Seed, "Code").ShouldBe(700, "bound-seed");
        await Should.ThrowAsync<ValidationException>(() => wideShop.PutGlobalAsync(1, """{"maxParallel":600}""", "reject parallel"));
        await Should.ThrowAsync<ValidationException>(() => wideShop.PutGlobalAsync(1, """{"roles":{"Code":{"maxParallel":700}}}""", "reject role"));
        var cleared = await wideShop.PutGlobalAsync(1, "{}", "clear preserves the seed");
        cleared.Seed.MaxParallel.ShouldBe(600, "bound-seed");
        Role(cleared.Seed, "Code").ShouldBe(700, "bound-seed");
        cleared.Effective.MaxParallel.ShouldBe(600, "bound-seed");
        cleared.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(700, "bound-seed");
    }

    [Test]
    public async Task Concurrent_initialization_writes_one_seed()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        var pause = new PauseSettingsSaveInterceptor();
        await using var left = shop.Db(pause);
        var leftRun = shop.Service(left).EnsureInitializedAsync(CancellationToken.None);
        await pause.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var right = shop.Db();
        var rightRun = shop.Service(right).EnsureInitializedAsync(CancellationToken.None);
        var blocked = await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(
            shop.ConnectionString, TimeSpan.FromSeconds(10));
        blocked.ShouldBeTrue("initializer-B-blocked=true");
        pause.Release.TrySetResult();
        await Task.WhenAll(leftRun, rightRun);

        await using var read = shop.Db();
        (await read.DispatchConcurrencySettings.CountAsync()).ShouldBe(1, "one-seed");
        (await read.DispatchConcurrencyRevisions.CountAsync()).ShouldBe(1, "one-seed");
        shop.Bus.PublishedEvents.Count(item => item.EventName == "DispatchConcurrencyChanged").ShouldBe(1, "one-seed");
        var snapshot = await shop.ReadGlobalAsync();
        snapshot.Revision.ShouldBe(1, "one-seed");
        snapshot.Seed.MaxParallel.ShouldBe(9, "one-seed");
        snapshot.Seed.ImportedAt.ShouldBe(DispatchConcurrencyTestHost.SeedInstant.UtcDateTime, "one-seed");
        snapshot.Provenance.ShouldBe("Migration", "one-seed");
    }

    [Test]
    public async Task Global_put_changes_existing_service_reads()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await using var readerDb = shop.Db();
        var reader = shop.Service(readerDb);
        (await reader.GetGlobalAsync(CancellationToken.None)).Revision.ShouldBe(1);
        var taskId = await SeedTaskAsync(shop, "untouched");
        var before = await SideAsync(shop, taskId);

        var changed = await shop.PutGlobalAsync(
            1, """{"maxParallel":7,"maxQueued":6,"roles":{"Code":{"maxParallel":4}}}""", "raise the global cap");
        changed.Revision.ShouldBe(2);
        var next = await reader.GetGlobalAsync(CancellationToken.None);
        next.Revision.ShouldBe(2);
        next.Effective.MaxParallel.ShouldBe(7);
        next.Effective.MaxParallelSource.ShouldBe("global");
        next.Effective.MaxQueued.ShouldBe(6);
        next.Effective.MaxQueuedSource.ShouldBe("global");
        next.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(4);
        next.Effective.Roles.Single(role => role.Role == "Code").MaxParallelSource.ShouldBe("global");

        foreach (var project in new Guid?[] { shop.ProjectP, shop.ProjectQ, null })
        {
            var effective = project is Guid id
                ? (await shop.ReadProjectAsync(id)).Effective
                : (await shop.ReadGlobalAsync()).NullProject;
            effective.MaxParallel.ShouldBe(7);
            effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(4);
        }

        (await SideAsync(shop, taskId)).ShouldBe(before);
    }

    [Test]
    public async Task Project_override_isolated_and_clear_restores_inheritance()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.PutGlobalAsync(1, """{"maxParallel":8,"roles":{"Code":{"maxParallel":4}}}""", "global code");
        var project = await shop.PutProjectAsync(
            shop.ProjectP, 0, 2,
            """{"maxQueued":0,"roles":{"Code":{"maxParallel":2,"maxQueued":null}}}""",
            "pause P");
        project.Revision.ShouldBe(1, "clear-keeps-revision");
        project.Effective.MaxQueued.ShouldBe(0, "clear-inherits");
        project.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(2, "clear-inherits");
        project.Effective.Roles.Single(role => role.Role == "Code").MaxQueued.ShouldBeNull("clear-inherits");

        var other = await shop.ReadProjectAsync(shop.ProjectQ);
        other.Revision.ShouldBe(0, "clear-inherits");
        other.Effective.MaxParallel.ShouldBe(8, "clear-inherits");
        other.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(4, "clear-inherits");
        (await shop.ReadGlobalAsync()).NullProject.MaxQueued.ShouldBeNull("clear-inherits");

        var cleared = await shop.PutProjectAsync(shop.ProjectP, 1, 2, "{}", "clear P");
        cleared.Revision.ShouldBe(2, "clear-keeps-revision");
        cleared.Overrides.GetRawText().Replace(" ", "").ShouldBe("""{"schemaVersion":1}""", "clear-inherits");

        await shop.PutGlobalAsync(2, """{"maxParallel":8,"roles":{"Code":{"maxParallel":3}}}""", "lower code");
        var inherited = await shop.ReadProjectAsync(shop.ProjectP);
        inherited.Revision.ShouldBe(2, "clear-keeps-revision");
        inherited.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(3, "clear-inherits");
        inherited.Effective.Roles.Single(role => role.Role == "Code").MaxParallelSource.ShouldBe("global", "clear-inherits");
        inherited.Effective.MaxQueued.ShouldBeNull("clear-inherits");

        var stale = await Should.ThrowAsync<ConflictException>(() =>
            shop.PutProjectAsync(shop.ProjectP, 1, 3, """{"maxQueued":1}""", "stale pre-clear"));
        stale.Code.ShouldBe(DispatchConcurrencySettingsService.CodeRevisionConflict, "clear-keeps-revision");
        (await shop.ReadProjectAsync(shop.ProjectP)).Revision.ShouldBe(2, "clear-keeps-revision");
    }

    [Test]
    public async Task Stale_scope_or_global_revision_writes_nothing()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.PutGlobalAsync(1, """{"maxParallel":7}""", "current");
        await AssertUnchanged(shop, () => shop.PutGlobalAsync(1, """{"maxParallel":8}""", "stale global"), 2, 0, "stale-global");

        await shop.PutProjectAsync(shop.ProjectP, 0, 2, """{"maxQueued":2}""", "project");
        await AssertUnchanged(shop, () => shop.PutProjectAsync(shop.ProjectP, 0, 2, """{"maxQueued":3}""", "stale project"), 2, 1, "stale-scope");
        await AssertUnchanged(shop, () => shop.PutProjectAsync(shop.ProjectP, 1, 1, """{"maxQueued":4}""", "stale global pair"), 2, 1, "stale-global");

        await shop.PutProjectAsync(shop.ProjectP, 1, 2, "{}", "clear");
        await AssertUnchanged(shop, () => shop.PutProjectAsync(shop.ProjectP, 1, 2, """{"maxQueued":5}""", "pre-clear token"), 2, 2, "no-aba");
        (await shop.ReadProjectAsync(shop.ProjectP)).Overrides.GetRawText().Replace(" ", "").ShouldBe("""{"schemaVersion":1}""", "no-aba");
    }

    [Test]
    public async Task Concurrent_puts_have_one_winner()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.ReadGlobalAsync();
        var global = await RaceGlobalAsync(shop, 1, """{"maxParallel":7}""", """{"maxParallel":8}""");
        global.Revision.ShouldBe(2, "one-put-winner");
        global.Effective.MaxParallel.ShouldBe(7, "one-put-winner");
        (await HistoryCountAsync(shop)).ShouldBe(2, "one-put-winner");

        var project = await RaceProjectAsync(shop, shop.ProjectP, 0, 2, """{"maxQueued":1}""", """{"maxQueued":2}""");
        project.Revision.ShouldBe(1, "one-put-winner");
        project.Effective.MaxQueued.ShouldBe(1, "one-put-winner");

        var pauseGlobal = new PauseSettingsSaveInterceptor();
        await using var globalDb = shop.Db(pauseGlobal);
        var globalFirst = shop.Service(globalDb).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(2, """{"maxParallel":6}""", "global first"), null, CancellationToken.None);
        await pauseGlobal.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var staleProject = shop.PutProjectAsync(shop.ProjectP, 1, 2, """{"maxQueued":4}""", "stale against global");
        (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(shop.ConnectionString, TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("one-put-winner");
        pauseGlobal.Release.TrySetResult();
        var globalSaved = await globalFirst;
        var stale = await Should.ThrowAsync<ConflictException>(() => staleProject);
        globalSaved.Revision.ShouldBe(3, "one-put-winner");
        stale.Extensions!["globalRevision"].ShouldBe(3L, "one-put-winner");
        stale.Extensions["projectRevision"].ShouldBe(1L, "one-put-winner");

        var pauseProject = new PauseSettingsSaveInterceptor();
        await using var projectDb = shop.Db(pauseProject);
        var projectFirst = shop.Service(projectDb).PutProjectAsync(
            shop.ProjectP,
            DispatchConcurrencyTestHost.Put(1, """{"maxQueued":5}""", "project first", expectedGlobalRevision: 3),
            null, CancellationToken.None);
        await pauseProject.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var globalSecond = shop.PutGlobalAsync(3, """{"maxParallel":5}""", "global second");
        (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(shop.ConnectionString, TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("one-put-winner");
        pauseProject.Release.TrySetResult();
        var projectSaved = await projectFirst;
        var globalAfter = await globalSecond;
        projectSaved.Revision.ShouldBe(2, "one-put-winner");
        globalAfter.Revision.ShouldBe(4, "one-put-winner");
        (await shop.ReadProjectAsync(shop.ProjectP)).GlobalRevision.ShouldBe(4, "one-put-winner");
    }

    [Test]
    public async Task Auto_cannot_replace_or_shadow_human()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.PutGlobalAsync(1, """{"maxParallel":7}""", "human global", "Human");
        await AssertHuman(shop, () => shop.PutGlobalAsync(2, """{"maxParallel":8}""", "auto replace", "Auto"), "human-scope");
        await AssertHuman(shop, () => shop.PutGlobalAsync(2, "{}", "auto clear", "Auto"), "human-scope");

        await shop.PutProjectAsync(shop.ProjectP, 0, 2, """{"maxQueued":1}""", "human project", "Human");
        await AssertHuman(shop, () => shop.PutProjectAsync(shop.ProjectP, 1, 2, """{"maxQueued":2}""", "auto replace", "Auto"), "human-scope");
        await AssertHuman(shop, () => shop.PutProjectAsync(shop.ProjectP, 1, 2, "{}", "auto clear", "Auto"), "human-scope");

        await AssertHuman(shop, () => shop.PutProjectAsync(shop.ProjectQ, 0, 2, """{"maxQueued":1}""", "shadow", "Auto"), "human-inheritance");
        (await shop.ReadProjectAsync(shop.ProjectQ)).Revision.ShouldBe(0, "human-inheritance");

        await using var migration = await DispatchConcurrencyShop.Open();
        var auto = await migration.PutGlobalAsync(1, """{"maxParallel":8}""", "auto over migration", "Auto");
        auto.Revision.ShouldBe(2);
        auto.Provenance.ShouldBe("Auto");
        var again = await migration.PutGlobalAsync(2, """{"maxParallel":7}""", "auto over auto", "Auto");
        again.Provenance.ShouldBe("Auto");
        again.Effective.MaxParallel.ShouldBe(7);

        await using var differed = await DispatchConcurrencyShop.Open();
        await differed.PutGlobalAsync(1, """{"maxParallel":7}""", "human", "Human");
        var project = await differed.PutProjectAsync(differed.ProjectP, 0, 2, """{"maxParallel":4}""", "human differs", "Human");
        project.Effective.MaxParallel.ShouldBe(4);
        project.Effective.MaxParallelSource.ShouldBe("project");
        (await differed.ReadGlobalAsync()).Effective.MaxParallel.ShouldBe(7);
    }

    [Test]
    public async Task Noop_preserves_revision_human_claim_audits()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        var first = await shop.PutGlobalAsync(1, """{"maxQueued":4,"maxParallel":8}""", "set", "Auto");
        var history = await HistoryCountAsync(shop);
        var events = shop.Bus.PublishedEvents.Count;
        var repeat = await shop.PutGlobalAsync(first.Revision, """{"maxParallel":8,"maxQueued":4}""", "  set  ", "Auto");
        repeat.Revision.ShouldBe(first.Revision, "human-claim-audits");
        repeat.UpdatedAt.ShouldBe(first.UpdatedAt, "human-claim-audits");
        (await HistoryCountAsync(shop)).ShouldBe(history, "human-claim-audits");
        shop.Bus.PublishedEvents.Count.ShouldBe(events, "human-claim-audits");

        var claimed = await shop.PutGlobalAsync(first.Revision, """{"maxParallel":8,"maxQueued":4}""", "claim", "Human");
        claimed.Revision.ShouldBe(first.Revision + 1, "human-claim-audits");
        claimed.Provenance.ShouldBe("Human", "human-claim-audits");
        var humanRepeat = await shop.PutGlobalAsync(claimed.Revision, """{"maxQueued":4,"maxParallel":8}""", "again", "Human");
        humanRepeat.Revision.ShouldBe(claimed.Revision, "human-claim-audits");

        var explicitNull = await shop.PutGlobalAsync(
            claimed.Revision, """{"roles":{"Custom":{"maxParallel":null}}}""", "explicit null", "Human");
        explicitNull.Revision.ShouldBe(claimed.Revision + 1, "human-claim-audits");
        explicitNull.Effective.Roles.Single(role => role.Role == "Custom").MaxParallel.ShouldBeNull("human-claim-audits");
        explicitNull.Overrides.GetProperty("roles").GetProperty("Custom").GetProperty("maxParallel").ValueKind
            .ShouldBe(JsonValueKind.Null, "human-claim-audits");
    }

    [Test]
    public async Task History_paginates_and_survives_clear()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        var caller = Guid.NewGuid();
        await using (var db = shop.Db())
        {
            await shop.Service(db).PutGlobalAsync(
                DispatchConcurrencyTestHost.Put(1, """{"maxParallel":8}""", "two"), caller, CancellationToken.None);
        }

        await shop.PutGlobalAsync(2, """{"maxParallel":8,"maxQueued":3}""", "three");
        await shop.PutGlobalAsync(3, """{"maxParallel":7}""", "four");
        await shop.PutGlobalAsync(4, "{}", "five");

        await using var dbRead = shop.Db();
        var service = shop.Service(dbRead);
        var first = await service.RevisionsAsync(null, null, 2, CancellationToken.None);
        first.Revisions.Select(row => row.Revision).ShouldBe([5L, 4L]);
        first.NextBeforeRevision.ShouldBe(4);
        var second = await service.RevisionsAsync(null, first.NextBeforeRevision, 2, CancellationToken.None);
        second.Revisions.Select(row => row.Revision).ShouldBe([3L, 2L]);
        var third = await service.RevisionsAsync(null, second.NextBeforeRevision, 2, CancellationToken.None);
        third.Revisions.Select(row => row.Revision).ShouldBe([1L]);
        third.NextBeforeRevision.ShouldBeNull();
        var seen = first.Revisions.Concat(second.Revisions).Concat(third.Revisions).Select(row => row.Revision).ToArray();
        seen.ShouldBe([5L, 4L, 3L, 2L, 1L]);
        first.Revisions[0].PreviousRevision.ShouldBe(4);
        third.Revisions[0].PreviousRevision.ShouldBeNull();

        await Should.ThrowAsync<ValidationException>(() => service.RevisionsAsync(null, null, 0, CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() => service.RevisionsAsync(null, null, 101, CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() => service.RevisionsAsync(null, -1, 1, CancellationToken.None));

        var early = third.Revisions[0];
        early.Revision.ShouldBe(1);
        var kept = second.Revisions.Single(row => row.Revision == 2);
        kept.CallerTaskId.ShouldBe(caller);
        kept.Reason.ShouldBe("two");
        kept.Snapshot.GetProperty("overrides").GetProperty("maxParallel").GetInt32().ShouldBe(8);

        var history = await HistoryCountAsync(shop);
        await service.RevisionsAsync(null, null, 50, CancellationToken.None);
        await service.RevisionsAsync(shop.ProjectP, null, 50, CancellationToken.None);
        (await HistoryCountAsync(shop)).ShouldBe(history);
    }

    [Test]
    public async Task Restart_and_event_failure_preserve_committed_policy()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.ReadGlobalAsync();
        var bus = new CommitVisibleBus(shop.ConnectionString);
        await using var db = shop.Db();
        var saved = await shop.Service(db, bus: bus).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(1, """{"mode":"SeparateQueues","maxParallel":6}""", "keep"),
            null, CancellationToken.None);
        bus.SawCommittedRevision.ShouldBe(2, "event-after-commit");
        saved.Revision.ShouldBe(2, "event-after-commit");
        var read = await shop.ReadGlobalAsync();
        read.Effective.Mode.ShouldBe("SeparateQueues", "restart-durable");
        read.Effective.MaxParallel.ShouldBe(6, "restart-durable");

        var divergent = new DelegationSettings { MaxOpenTasks = 2 };
        divergent.RolePolicy["Code"].RecommendedInFlight = 1;
        await using var restarted = shop.Db();
        var after = await shop.Service(restarted, divergent, new MockEventBus()).GetGlobalAsync(CancellationToken.None);
        after.Seed.MaxParallel.ShouldBe(9, "restart-durable");
        after.Effective.MaxParallel.ShouldBe(6, "restart-durable");
        after.Effective.Mode.ShouldBe("SeparateQueues", "restart-durable");
        (await restarted.AgentSessions.CountAsync()).ShouldBe(0, "restart-durable");
    }

    private static async Task AssertUnchanged(
        DispatchConcurrencyShop shop, Func<Task> write, long globalRevision, long projectRevision, string message)
    {
        var history = await HistoryCountAsync(shop);
        var events = shop.Bus.PublishedEvents.Count;
        var exception = await Should.ThrowAsync<ConflictException>(write);
        exception.Code.ShouldBe(DispatchConcurrencySettingsService.CodeRevisionConflict, message);
        exception.Extensions!["globalRevision"].ShouldBe(globalRevision, message);
        exception.Extensions["projectRevision"].ShouldBe(projectRevision, message);
        (await HistoryCountAsync(shop)).ShouldBe(history, message);
        shop.Bus.PublishedEvents.Count.ShouldBe(events, message);
        (await shop.ReadGlobalAsync()).Revision.ShouldBe(globalRevision, message);
    }

    private static async Task AssertHuman(DispatchConcurrencyShop shop, Func<Task> write, string message)
    {
        var history = await HistoryCountAsync(shop);
        var exception = await Should.ThrowAsync<ConflictException>(write);
        exception.Code.ShouldBe(DispatchConcurrencySettingsService.CodeHuman, message);
        (await HistoryCountAsync(shop)).ShouldBe(history, message);
    }

    private static async Task<DispatchConcurrencyGlobalDto> RaceGlobalAsync(
        DispatchConcurrencyShop shop, long revision, string winnerJson, string loserJson)
    {
        var pause = new PauseSettingsSaveInterceptor();
        await using var db = shop.Db(pause);
        var winner = shop.Service(db).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(revision, winnerJson, "winner"), null, CancellationToken.None);
        await pause.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var loser = shop.PutGlobalAsync(revision, loserJson, "loser");
        (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(shop.ConnectionString, TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("one-put-winner");
        pause.Release.TrySetResult();
        var saved = await winner;
        await Should.ThrowAsync<ConflictException>(() => loser);
        return saved;
    }

    private static async Task<DispatchConcurrencyProjectDto> RaceProjectAsync(
        DispatchConcurrencyShop shop, Guid project, long revision, long globalRevision, string winnerJson, string loserJson)
    {
        var pause = new PauseSettingsSaveInterceptor();
        await using var db = shop.Db(pause);
        var winner = shop.Service(db).PutProjectAsync(
            project, DispatchConcurrencyTestHost.Put(revision, winnerJson, "winner", expectedGlobalRevision: globalRevision),
            null, CancellationToken.None);
        await pause.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var loser = shop.PutProjectAsync(project, revision, globalRevision, loserJson, "loser");
        (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(shop.ConnectionString, TimeSpan.FromSeconds(10)))
            .ShouldBeTrue("one-put-winner");
        pause.Release.TrySetResult();
        var saved = await winner;
        await Should.ThrowAsync<ConflictException>(() => loser);
        saved.Overrides.GetRawText().ShouldContain("1", Case.Sensitive, "one-put-winner");
        return saved;
    }

    private static int? Role(DispatchConcurrencySeedDto seed, string role) =>
        seed.Roles.Single(item => item.Role == role).MaxParallel;

    private static async Task<int> HistoryCountAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        return await db.DispatchConcurrencyRevisions.CountAsync();
    }

    private static async Task<Guid> SeedTaskAsync(DispatchConcurrencyShop shop, string title)
    {
        var id = Guid.NewGuid();
        await using var db = shop.Db();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Code,
            Status = AgentTaskStatus.Queued,
            ProjectId = shop.ProjectP,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = "/tmp/card-0505",
            CreatedAt = shop.Clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<string> SideAsync(DispatchConcurrencyShop shop, Guid taskId)
    {
        await using var db = shop.Db();
        var budget = await db.HostBudgets.AsNoTracking().SingleAsync();
        var routing = await db.RunnerRoutingSettings.AsNoTracking().SingleAsync();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(item => item.Id == taskId);
        var pins = await db.RoutingPins.CountAsync();
        return $"{budget.HostId}:{budget.MaxInFlight}:{budget.Reason}:{budget.Revision}|{routing.Revision}:{routing.GlobalRunnerId}|{task.Title}:{task.Status}|{pins}";
    }
}

file sealed class CommitVisibleBus(string connectionString) : IEventBus
{
    public long SawCommittedRevision { get; private set; }

    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        SawCommittedRevision = await db.DispatchConcurrencySettings.AsNoTracking()
            .Where(row => row.ScopeKey == DispatchConcurrencySettings.GlobalScopeKey)
            .Select(row => row.Revision)
            .SingleAsync(ct);
        throw new InvalidOperationException("event delivery failed");
    }
}
