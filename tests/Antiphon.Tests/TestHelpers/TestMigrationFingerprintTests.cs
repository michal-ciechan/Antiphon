using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>The schema key's migration fingerprint: same IDs as EF, stable, and sensitive to schema.</summary>
[Category("Unit")]
public sealed class TestMigrationFingerprintTests
{
    [Test]
    public void Migration_ids_are_the_ones_ef_applies_in_order()
    {
        using var context = new AppDbContext(TestDbFixtureLifecycle.BuildOptions(
            "Host=127.0.0.1;Database=antiphon_test;Username=test;Password=test"));

        TestMigrationFingerprint.Default.MigrationIds.ShouldBe(context.Database.GetMigrations().ToArray());
    }

    [Test]
    public void Fingerprint_is_stable_across_computations()
    {
        var again = TestMigrationFingerprint.Compute(typeof(AppDbContext));

        again.Sha256.ShouldBe(TestMigrationFingerprint.Default.Sha256);
        again.MigrationIds.ShouldBe(TestMigrationFingerprint.Default.MigrationIds);
    }

    [Test]
    public void Serialized_operations_see_column_shape_annotations_and_seed_values()
    {
        static AddColumnOperation Column(bool nullable, string? annotation = null)
        {
            var op = new AddColumnOperation
            {
                Name = "Slot", Table = "AgentTasks", ClrType = typeof(int), ColumnType = "integer",
                IsNullable = nullable, DefaultValue = 0,
            };
            if (annotation is not null)
                op.AddAnnotation("Npgsql:ValueGenerationStrategy", annotation);
            return op;
        }

        static InsertDataOperation Seed(object value) => new()
        {
            Table = "Settings", Columns = ["Key", "Value"], Values = new object[,] { { "k", value } },
        };

        var baseline = TestMigrationFingerprint.Serialize(Column(nullable: true));
        TestMigrationFingerprint.Serialize(Column(nullable: true)).ShouldBe(baseline);
        TestMigrationFingerprint.Serialize(Column(nullable: false)).ShouldNotBe(baseline);
        TestMigrationFingerprint.Serialize(Column(nullable: true, "IdentityByDefaultColumn")).ShouldNotBe(baseline);
        TestMigrationFingerprint.Serialize(Seed("a")).ShouldNotBe(TestMigrationFingerprint.Serialize(Seed("b")));
        TestMigrationFingerprint.Serialize(new SqlOperation { Sql = "SELECT 1" })
            .ShouldNotBe(TestMigrationFingerprint.Serialize(new SqlOperation { Sql = "SELECT 2" }));
    }
}
