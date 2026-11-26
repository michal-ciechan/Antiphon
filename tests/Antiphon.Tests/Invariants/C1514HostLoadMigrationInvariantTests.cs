using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;
using static Antiphon.Tests.Application.C1514HostLoadMigrationTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("Slow")]
[Category("RepositoryInvariant")]
[Timeout(60_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1514HostLoadMigrationInvariantTests
{
    [Test]
    public void ChainIsLinearAndSnapshotMatchesModel()
    {
        using var db = Metadata();
        // Read declared IDs before EF projects them into a dictionary keyed by ID.
        AssertChain([.. TestMigrationFingerprint.Default.MigrationIds]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("generated-snapshot-matches-runtime-model");
    }

    /// <summary>
    /// CARD-2074 witness. The pin is this card's linear window; a migration added after it
    /// (the next card's) must not break the pin, which the Merge task had to repair by hand.
    /// </summary>
    [Test]
    public void C2074_Chain_pin_accepts_a_newer_migration()
    {
        using var db = Metadata();
        AssertChain([.. db.Database.GetMigrations(), FutureMigration]);
    }

    [Test, Arguments(FutureMigration), Arguments("29991231235958_C2078EarlierFutureMigration")]
    public void C2078_Chain_pin_rejects_invalid_later_migrations(string second)
    {
        string[] temporaryChain = [.. TestMigrationFingerprint.Default.MigrationIds, FutureMigration, second];
        var error = Should.Throw<ShouldAssertException>(() => AssertChain(temporaryChain));
        error.Message.ShouldContain("later-migrations-are-linear");
    }

    private static void AssertChain(string[] chain)
    {
        var previous = Array.IndexOf(chain, Previous);
        previous.ShouldBeGreaterThanOrEqualTo(0);
        chain.Skip(previous).Take(3).ShouldBe([Previous, Telemetry, Membership], "linear-after-current-master");
        chain[previous + 3].ShouldBe(Catchup, "retention-catchup-follows-membership");
        chain[previous + 4].ShouldBe(PromptStamp, "prompt-stamp-index-follows-retention-catchup");
        chain[previous + 5].ShouldBe(ShadowCohort, "shadow-cohort-follows-prompt-stamp");
        chain[previous + 6].ShouldBe(Summary, "transcript-summary-follows-shadow-cohort");
        chain[previous + 7].ShouldBe(LandFailure, "land-verification-failure-follows-transcript-summary");
        chain[previous + 8].ShouldBe(ModelAlias, "requested-model-alias-follows-land-verification-failure");
        chain[previous + 9].ShouldBe(ReviewEngine, "review-engine-follows-requested-model-alias");
        chain[previous + 10].ShouldBe(CallerReportReceipt, "caller-report-receipt-follows-review-engine");
        // Strict ordinal increase from the window's last ID rejects duplicates and reversals.
        var later = chain.Skip(previous + 11).ToArray();
        later.Prepend(CallerReportReceipt).Zip(later)
            .ShouldAllBe(pair => string.CompareOrdinal(pair.First, pair.Second) < 0, "later-migrations-are-linear");
    }
}
