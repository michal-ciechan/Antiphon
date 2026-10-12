using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1033 S1. Local Execute, cohort and settling gates. Mirror and scheduler vectors stay later slices.</summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardWorktreeCleanupRecoveryTests
{
    private static readonly string[] ExecuteVectors =
    [
        "shippedDefaults", "executeFalse", "executorEntry", "previewRun",
        "admittedLocal", "admittedPublication", "readOnlyComplete", "executeTrue",
    ];

    private static readonly string[] CutoffVectors =
    [
        "malformedConfig", "nonUtcConfig", "utcConfig", "nullCutoff", "beforeCutoff",
        "equalCutoff", "mutableCardTime", "oneMicrosecondAfter", "reopenNewGeneration",
    ];

    private static readonly string[] SettlingVectors =
    [
        "completedAtNull", "belowFloor", "atFloor", "aboveFloor", "clockIndependentOfCutoff",
    ];

    [Test]
    public async Task C1017_ExecuteFalseCannotDelete()
    {
        var reached = new List<string>();
        await AssertShippedDefaultsAsync();
        reached.Add("shippedDefaults");

        await using (var held = await CompletedCardCleanupFixture.CreateAsync())
        {
            held.Residue.Execute = false;
            var refused = await held.CleanupAsync();
            var destructiveCalls = DestructiveCalls(held);
            destructiveCalls.ShouldBe(0);
            refused.Residue.ShouldBe("execute_disabled");
            (await held.SentinelAsync()).ShouldBe(held.OriginalBytes);
            (await held.IntentsAsync()).ShouldBe(0);
            reached.Add("executeFalse");

            var direct = await held.CleanupDirectAsync();
            direct.Residue.ShouldBe("execute_disabled");
            DestructiveCalls(held).ShouldBe(0);
            (await held.SentinelAsync()).ShouldBe(held.OriginalBytes);
            reached.Add("executorEntry");
        }

        await using (var preview = await CompletedCardCleanupFixture.CreateAsync())
        {
            var runId = Guid.NewGuid();
            await using (var db = preview.Host.CreateContext())
            {
                db.WorktreeResidueRuns.Add(new WorktreeResidueRun
                {
                    Id = runId, StartedAt = DateTime.UtcNow, Execute = true, Preview = true, ActionBudget = 25,
                });
                await db.SaveChangesAsync();
            }
            var previewResult = await preview.CleanupDirectAsync(runId);
            previewResult.Residue.ShouldBe("preview");
            DestructiveCalls(preview).ShouldBe(0);
            (await preview.SentinelAsync()).ShouldBe(preview.OriginalBytes);
            (await preview.IntentsAsync()).ShouldBe(0);
            reached.Add("previewRun");
        }

        await using (var admitted = await CompletedCardCleanupFixture.CreateAsync())
        {
            try
            {
                await admitted.CleanupAsync(exec => exec.AfterIntentAsync = _ =>
                    throw new InvalidOperationException("c1033-stop-before-bytes"));
                throw new InvalidOperationException("post-intent seam was not reached");
            }
            catch (InvalidOperationException ex) when (ex.Message == "c1033-stop-before-bytes")
            {
            }

            (await admitted.IntentsAsync()).ShouldBe(1);
            (await admitted.SentinelAsync()).ShouldBe(admitted.OriginalBytes);
            admitted.Residue.Execute = false;
            var replay = await admitted.CleanupDirectAsync();
            replay.Residue.ShouldBe("execute_disabled");
            DestructiveCalls(admitted).ShouldBe(0);
            (await admitted.SentinelAsync()).ShouldBe(admitted.OriginalBytes);
            await using var db = admitted.Host.CreateContext();
            (await db.TaskWorktreeRetirements.CountAsync(r => r.TaskId == admitted.TaskId && r.Active)).ShouldBe(1);
            (await db.WorkspaceUseReservations.CountAsync(r => r.TaskId == admitted.TaskId && r.Active)).ShouldBeGreaterThan(0);
            reached.Add("admittedLocal");
        }

        await using (var publication = await CompletedCardCleanupFixture.CreateAsync())
        {
            await publication.DiscoverAsync();
            await using (var db = publication.Host.CreateContext())
            {
                var landingId = Guid.NewGuid();
                var now = DateTime.UtcNow;
                db.AgentTaskLandings.Add(new AgentTaskLanding
                {
                    Id = landingId,
                    TaskId = publication.TaskId,
                    Active = false,
                    Phase = LandPhase.Refused,
                    CreatedAt = now,
                    UpdatedAt = now,
                    RepositoryPath = publication.Host.Fixture.Repository,
                    WorktreePath = publication.Tree,
                    SourceFullRef = publication.Host.Fixture.SourceRef,
                    OriginalSourceSha = new string('a', 40),
                    TargetFullRef = publication.Host.Fixture.TargetRef,
                    DestinationFullRef = publication.Host.Fixture.TargetRef,
                });
                var endpoint = await db.CardWorktreeCleanupEndpoints.Include(e => e.Target)
                    .SingleAsync(e => e.Id == publication.EndpointId);
                endpoint.OperationId = Guid.NewGuid();
                endpoint.State = CardWorktreeCleanupEndpointState.IntentRecorded;
                endpoint.Target.LandingOperationId = landingId;
                await db.SaveChangesAsync();
            }
            publication.Residue.Execute = false;
            var publicationResult = await publication.CleanupDirectAsync();
            publicationResult.Residue.ShouldBe("execute_disabled");
            DestructiveCalls(publication).ShouldBe(0);
            (await publication.SentinelAsync()).ShouldBe(publication.OriginalBytes);
            reached.Add("admittedPublication");
        }

        await using (var recorded = await CompletedCardCleanupFixture.CreateAsync())
        {
            await recorded.DiscoverAsync();
            await using (var db = recorded.Host.CreateContext())
            {
                var endpoint = await db.CardWorktreeCleanupEndpoints.SingleAsync(e => e.Id == recorded.EndpointId);
                endpoint.State = CardWorktreeCleanupEndpointState.Complete;
                endpoint.DirectoryRemoved = true;
                endpoint.RegistrationRemoved = true;
                endpoint.BranchRemoved = true;
                await db.SaveChangesAsync();
            }
            recorded.Residue.Execute = false;
            var receipt = await recorded.CleanupDirectAsync();
            receipt.DirectoryGone.ShouldBeTrue();
            receipt.Residue.ShouldBeNull();
            DestructiveCalls(recorded).ShouldBe(0);
            (await recorded.SentinelAsync()).ShouldBe(recorded.OriginalBytes);
            reached.Add("readOnlyComplete");
        }

        await using (var allowed = await CompletedCardCleanupFixture.CreateAsync())
        {
            (await allowed.CleanupAsync()).IsClean.ShouldBeTrue();
            await allowed.AssertRemovedAsync();
            reached.Add("executeTrue");
        }

        reached.ShouldBe(ExecuteVectors);
    }

    [Test]
    public async Task C1017_HistoricalCutoffCannotDelete()
    {
        var reached = new List<string>();
        AssertStartupRejects("not-a-date", "malformed.");
        reached.Add("malformedConfig");
        AssertStartupRejects("2026-10-12T00:00:00", "must be UTC.");
        AssertStartupRejects("2026-10-12T00:00:00+01:00", "must be UTC.");
        reached.Add("nonUtcConfig");
        BindAndValidate("2026-10-12T00:00:00Z").Succeeded.ShouldBeTrue();
        BindAndValidate("2026-10-12T00:00:00+00:00").Succeeded.ShouldBeTrue();
        reached.Add("utcConfig");

        await using (var absent = await CompletedCardCleanupFixture.CreateAsync())
        {
            absent.Residue.Execute = true;
            absent.SetCutoff(null);
            var result = await absent.CleanupAsync();
            var nullCutoffTreeExists = Directory.Exists(absent.Tree);
            nullCutoffTreeExists.ShouldBeTrue();
            result.Residue.ShouldBe("cutoff_absent");
            (await absent.IntentsAsync()).ShouldBe(0);
            (await absent.SentinelAsync()).ShouldBe(absent.OriginalBytes);
            reached.Add("nullCutoff");
        }

        await using (var before = await CompletedCardCleanupFixture.CreateAsync())
        {
            var doneAt = await before.PersistedDoneAtAsync();
            before.SetCutoff(doneAt.AddTicks(WorktreeResidueSettings.MicrosecondTicks));
            await PinCardUpdatedAtAsync(before, doneAt);
            var result = await before.CleanupAsync();
            var historicalTreeExists = Directory.Exists(before.Tree);
            historicalTreeExists.ShouldBeTrue();
            result.Residue.ShouldBe("historical_cohort");
            (await before.SentinelAsync()).ShouldBe(before.OriginalBytes);
            (await before.IntentsAsync()).ShouldBe(0);
            reached.Add("beforeCutoff");
        }

        await using (var equal = await CompletedCardCleanupFixture.CreateAsync())
        {
            var doneAt = await equal.PersistedDoneAtAsync();
            equal.SetCutoff(doneAt);
            await PinCardUpdatedAtAsync(equal, doneAt);
            await equal.CleanupAsync();
            var equalCutoffTreeExists = Directory.Exists(equal.Tree);
            equalCutoffTreeExists.ShouldBeTrue();
            (await equal.SentinelAsync()).ShouldBe(equal.OriginalBytes);
            (await equal.IntentsAsync()).ShouldBe(0);
            reached.Add("equalCutoff");
        }

        await using (var mutable = await CompletedCardCleanupFixture.CreateAsync())
        {
            var doneAt = await mutable.PersistedDoneAtAsync();
            mutable.SetCutoff(doneAt);
            await using (var db = mutable.Host.CreateContext())
            {
                var card = await db.Cards.SingleAsync(c => c.Id == mutable.CardId);
                card.UpdatedAt = DateTime.SpecifyKind(doneAt, DateTimeKind.Utc).AddDays(30);
                await db.SaveChangesAsync();
            }
            var result = await mutable.CleanupAsync();
            var historicalRevisionTreeExists = Directory.Exists(mutable.Tree);
            historicalRevisionTreeExists.ShouldBeTrue();
            result.Residue.ShouldBe("historical_cohort");
            (await mutable.IntentsAsync()).ShouldBe(0);
            reached.Add("mutableCardTime");
        }

        await using (var after = await CompletedCardCleanupFixture.CreateAsync())
        {
            var doneAt = await after.PersistedDoneAtAsync();
            after.SetCutoff(doneAt.AddTicks(-WorktreeResidueSettings.MicrosecondTicks));
            (await after.CleanupAsync()).IsClean.ShouldBeTrue();
            await after.AssertRemovedAsync();
            reached.Add("oneMicrosecondAfter");
        }

        await using (var generation = await CompletedCardCleanupFixture.CreateAsync())
        {
            var first = await generation.PersistedDoneAtAsync();
            generation.SetCutoff(first);
            (await generation.CleanupAsync()).Residue.ShouldBe("historical_cohort");
            Directory.Exists(generation.Tree).ShouldBeTrue();
            await generation.MoveAsync(generation.ReviewColumnId);
            await generation.MoveAsync(generation.DoneColumnId);
            await using (var db = generation.Host.CreateContext())
            {
                var revision = await db.CardRevisions
                    .Where(r => r.CardId == generation.CardId && r.ToStatus == CardStatus.Done && r.FromStatus != CardStatus.Done)
                    .OrderByDescending(r => r.RevisionNumber).FirstAsync();
                revision.CreatedAt = DateTime.SpecifyKind(first, DateTimeKind.Utc).AddSeconds(1);
                await db.SaveChangesAsync();
            }
            await generation.DiscoverAsync();
            var second = await generation.PersistedDoneAtAsync();
            second.ShouldBeGreaterThan(DateTime.SpecifyKind(first, DateTimeKind.Utc));
            (await generation.CleanupAsync()).IsClean.ShouldBeTrue();
            await generation.AssertRemovedAsync();
            reached.Add("reopenNewGeneration");
        }

        reached.ShouldBe(CutoffVectors);
    }

    [Test]
    public async Task C1017_SettlingFloorCannotDelete()
    {
        var reached = new List<string>();
        var below = TimeSpan.FromMinutes(119) + TimeSpan.FromMilliseconds(59_999);
        var atFloor = TimeSpan.FromMinutes(120);
        var above = atFloor + TimeSpan.FromMilliseconds(1);

        await using (var missing = await CreateFrozenAsync())
        {
            await missing.SetCompletedAgeAsync(null);
            var result = await missing.CleanupAsync();
            var nullCompletedIntents = await missing.IntentsAsync();
            nullCompletedIntents.ShouldBe(0);
            result.Residue.ShouldBe("settling");
            (await missing.SentinelAsync()).ShouldBe(missing.OriginalBytes);
            reached.Add("completedAtNull");
        }

        await using (var young = await CreateFrozenAsync())
        {
            await young.SetCompletedAgeAsync(below);
            var result = await young.CleanupAsync();
            var belowFloorTreeExists = Directory.Exists(young.Tree);
            belowFloorTreeExists.ShouldBeTrue();
            result.Residue.ShouldBe("settling");
            (await young.IntentsAsync()).ShouldBe(0);
            (await young.SentinelAsync()).ShouldBe(young.OriginalBytes);
            WorktreeResidueSettings.TryReadCardDoneAfterUtc(young.Residue.CardDoneAfterUtc, out var cutoff, out var failure).ShouldBeTrue();
            failure.ShouldBeNull();
            var doneAt = await young.PersistedDoneAtAsync();
            Instant(doneAt).ShouldBeGreaterThan(cutoff!.Value);
            young.Host.Clock.GetUtcNow().UtcDateTime.ShouldBeLessThan(Instant(doneAt));
            reached.Add("belowFloor");
        }

        await using (var exact = await CreateFrozenAsync())
        {
            await exact.SetCompletedAgeAsync(atFloor);
            (await exact.CleanupAsync()).IsClean.ShouldBeTrue();
            await exact.AssertRemovedAsync();
            reached.Add("atFloor");
        }

        await using (var older = await CreateFrozenAsync())
        {
            await older.SetCompletedAgeAsync(above);
            (await older.CleanupAsync()).IsClean.ShouldBeTrue();
            await older.AssertRemovedAsync();
            reached.Add("aboveFloor");
        }

        reached.Add("clockIndependentOfCutoff");
        reached.ShouldBe(SettlingVectors);
    }

    private static async Task<CompletedCardCleanupFixture> CreateFrozenAsync() =>
        await CompletedCardCleanupFixture.CreateAsync(clock: new FakeTimeProvider(
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

    private static async Task AssertShippedDefaultsAsync()
    {
        new WorktreeResidueSettings().Execute.ShouldBeFalse();
        new WorktreeResidueSettings().CardDoneAfterUtc.ShouldBeNull();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Antiphon.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        var json = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "server", "appsettings.json"));
        json.Contains("\"Execute\": false", StringComparison.Ordinal).ShouldBeTrue();
        json.Contains("\"CardDoneAfterUtc\": null", StringComparison.Ordinal).ShouldBeTrue();
    }

    private static void AssertStartupRejects(string raw, string fragment)
    {
        var result = BindAndValidate(raw);
        result.Succeeded.ShouldBeFalse();
        var failures = string.Join(" ", result.Failures ?? []);
        failures.Contains(fragment, StringComparison.Ordinal).ShouldBeTrue();
        using var provider = BuildOptions(raw);
        var thrown = false;
        try
        {
            foreach (var startup in provider.GetServices<IStartupValidator>())
                startup.Validate();
        }
        catch (OptionsValidationException)
        {
            thrown = true;
        }
        thrown.ShouldBeTrue();
    }

    private static ValidateOptionsResult BindAndValidate(string? raw)
    {
        var bound = new WorktreeResidueSettings();
        BuildConfiguration(raw).GetSection("WorktreeResidue").Bind(bound);
        return new WorktreeResidueSettingsValidator().Validate(null, bound);
    }

    private static ServiceProvider BuildOptions(string? raw)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<WorktreeResidueSettings>, WorktreeResidueSettingsValidator>();
        services.AddOptions<WorktreeResidueSettings>().Bind(BuildConfiguration(raw).GetSection("WorktreeResidue")).ValidateOnStart();
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(string? raw)
    {
        var values = new Dictionary<string, string?>
        {
            ["WorktreeResidue:RecurringJobId"] = "antiphon:worktree-residue",
            ["WorktreeResidue:Cron"] = "0 10 * * *",
            ["WorktreeResidue:TimeZoneId"] = "UTC",
            ["WorktreeResidue:Execute"] = "false",
            ["WorktreeResidue:MinSettledMinutes"] = "120",
            ["WorktreeResidue:MaxActionsPerRun"] = "25",
            ["WorktreeResidue:RunResultPageSize"] = "50",
            ["WorktreeResidue:LaunchGraceMinutes"] = "15",
        };
        if (raw is not null) values["WorktreeResidue:CardDoneAfterUtc"] = raw;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static async Task PinCardUpdatedAtAsync(CompletedCardCleanupFixture fixture, DateTime doneAt)
    {
        await using var db = fixture.Host.CreateContext();
        var card = await db.Cards.SingleAsync(c => c.Id == fixture.CardId);
        card.UpdatedAt = DateTime.SpecifyKind(doneAt, DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }

    private static int DestructiveCalls(CompletedCardCleanupFixture fixture)
    {
        var git = fixture.Host.Fixture.Git;
        var commands = git.Commands.Count(c =>
            c.Arguments.Length >= 2 && c.Arguments[0] == "worktree" && c.Arguments[1] == "remove"
            || c.Arguments.Length > 0 && c.Arguments[0] == "update-ref" && Array.IndexOf(c.Arguments, "-d") >= 0);
        return commands + git.RegistrationDrops.Count;
    }

    private static DateTime Instant(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
