using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;
using static Antiphon.Tests.Application.C0824RefOnlySnapshotMigrationTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("Slow")]
[Category("RepositoryInvariant")]
public sealed class C0824RefOnlySnapshotMigrationInvariantTests
{
    [Test]
    public void Migration_chain_is_linear_and_snapshot_matches_the_final_model()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        // Read declared IDs before EF projects them into a dictionary keyed by ID.
        AssertChain([.. TestMigrationFingerprint.Default.MigrationIds]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("final-model-matches-generated-snapshot");
    }

    /// <summary>
    /// CARD-2074 witness. A migration added after this card's window must not break the pin.
    /// </summary>
    [Test]
    public void C2074_Chain_pin_accepts_a_newer_migration()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
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
        string[] window = [Debt, Park, Telemetry, Membership, Catchup, PromptStamp, ShadowCohort, Summary, LandFailure, ModelAlias, ReviewEngine, CallerReportReceipt];
        var start = Array.IndexOf(chain, Debt);
        start.ShouldBeGreaterThan(0, "the debt migration follows its predecessor");
        chain[start - 1].ShouldBe(Previous, "linear-after-master-with-original-ids");
        chain.Skip(start).Take(window.Length).ShouldBe(window, "linear-after-master-with-original-ids");
        // Strict ordinal increase from the window's last ID rejects duplicates and reversals.
        var later = chain.Skip(start + window.Length).ToArray();
        later.Prepend(CallerReportReceipt).Zip(later)
            .ShouldAllBe(pair => string.CompareOrdinal(pair.First, pair.Second) < 0, "later-migrations-are-linear");
    }
}
