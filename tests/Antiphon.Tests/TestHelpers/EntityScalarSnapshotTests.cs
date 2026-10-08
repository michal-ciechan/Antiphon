using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1137 F-2: <see cref="EntityScalarSnapshot"/>'s exact, fail-closed leaf contract at the
/// boundaries the entity census does not reach (Half, sub-millisecond TimeOnly, byte[] versus null,
/// DateTime Kind and offsets, decimal scale, enums, nullables, unsupported types). Offline model, no
/// database.
/// </summary>
[Category("Unit")]
public sealed class EntityScalarSnapshotTests
{
    public static IEnumerable<Func<(string Group, object?[] Values)>> DistinctValueGroups()
    {
        yield return () => ("Half", [
            (Half)1, (Half)2, Half.Zero, Half.NegativeZero, Half.NaN, BitConverter.UInt16BitsToHalf(0x7E01),
            Half.PositiveInfinity, Half.NegativeInfinity, Half.Epsilon, BitConverter.UInt16BitsToHalf(0x03FF),
            BitConverter.UInt16BitsToHalf(0x0400), Half.MaxValue, Half.MinValue,
        ]);
        yield return () => ("TimeOnly", [
            new TimeOnly(12, 34, 1), new TimeOnly(12, 34, 2), new TimeOnly(12, 34, 2, 1),
            new TimeOnly(0), new TimeOnly(1), new TimeOnly(9_999), new TimeOnly(10_000), TimeOnly.MaxValue,
        ]);
        yield return () => ("null-like", [
            null, "null", Convert.FromBase64String("null"), Array.Empty<byte>(), "", new byte[] { 0 },
            new byte[] { 0, 0 }, "b64:", "b64:nulg", new byte[] { 1 },
        ]);
        yield return () => ("DateTime", [
            new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc), new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Unspecified),
            new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc).AddTicks(1), DateTime.MinValue, DateTime.MaxValue,
            new DateTimeOffset(2026, 10, 8, 1, 2, 3, TimeSpan.Zero), new DateTimeOffset(2026, 10, 8, 3, 2, 3, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 10, 8, 1, 2, 3, TimeSpan.Zero).AddTicks(1), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9),
            TimeSpan.FromTicks(1), TimeSpan.FromTicks(-1), TimeSpan.Zero,
        ]);
        yield return () => ("decimal", [
            1m, 1.0m, 1.00m, 0m, 0.0000000000000000000000000001m, decimal.MaxValue, decimal.MinValue, -1m,
        ]);
        yield return () => ("floating", [
            0.0, -0.0, double.NaN, BitConverter.Int64BitsToDouble(0x7FF8000000000001), double.Epsilon,
            0.1, Math.BitIncrement(0.1), double.PositiveInfinity, 0f, -0f, float.NaN, float.Epsilon, 0.1f,
        ]);
        yield return () => ("enum", [
            DayOfWeek.Monday, DayOfWeek.Tuesday, (DayOfWeek)99, StringComparison.Ordinal,
            FileAttributes.ReadOnly | FileAttributes.Hidden, FileAttributes.ReadOnly, (FileAttributes)0,
        ]);
        yield return () => ("cross-type one", [
            1, 1L, (short)1, (byte)1, (sbyte)1, 1u, 1UL, (ushort)1, (Int128)1, (UInt128)1, (nint)1, 1.0, 1.0f, (Half)1,
            1m, '1', "1", true, DayOfWeek.Monday, TimeSpan.FromDays(1),
        ]);
        yield return () => ("strings", [
            "a\nb", "a\\nb", "a\rb", "a\\rb", "a\"b", "a\\\"b", "a\\b", "a\tb", "\u00e9", "e\u0301",
        ]);
    }

    [Test]
    [MethodDataSource(nameof(DistinctValueGroups))]
    public void Distinct_supported_values_never_render_alike(string group, object?[] values)
    {
        using var db = new ProbeContext();
        var renderings = values.Select(v => (Value: v, Text: Snap(db, v))).ToArray();
        for (var i = 0; i < renderings.Length; i++)
        {
            Snap(db, values[i]).ShouldBe(renderings[i].Text, $"{group}: rendering of {Describe(values[i])} is deterministic");
            for (var j = i + 1; j < renderings.Length; j++)
                renderings[j].Text.ShouldNotBe(renderings[i].Text,
                    $"{group}: {Describe(renderings[i].Value)} and {Describe(renderings[j].Value)} must render differently");
        }
    }

    [Test]
    public void Nullable_properties_distinguish_null_default_and_another_value()
    {
        using var db = new ProbeContext();
        foreach (var property in typeof(ScalarProbe).GetProperties().Where(p => p.Name != nameof(ScalarProbe.Id) && p.Name != nameof(ScalarProbe.Value)))
        {
            var underlying = Nullable.GetUnderlyingType(property.PropertyType);
            object?[] values = underlying is null
                ? [null, Array.Empty<byte>(), Convert.FromBase64String("null")]
                : [null, Activator.CreateInstance(underlying), Other(underlying)];
            var texts = values.Select(v =>
            {
                var probe = new ScalarProbe();
                property.SetValue(probe, v);
                return EntityScalarSnapshot.Of(db, probe);
            }).ToArray();
            texts.Distinct(StringComparer.Ordinal).Count().ShouldBe(3,
                $"{property.Name}: null, default and another value must render three ways: {string.Join(" | ", texts)}");
            texts[0].ShouldContain($"{property.Name}=null\n");
        }
    }

    [Test]
    public void Unsupported_types_throw_naming_type_and_property()
    {
        using var db = new ProbeContext();
        Should.Throw<NotSupportedException>(() => EntityScalarSnapshot.Of(db, new UnsupportedProbe()))
            .Message.ShouldSatisfyAllConditions(
                m => m.ShouldContain("'Link'"),
                m => m.ShouldContain("System.Uri"));
        Should.Throw<NotSupportedException>(() => EntityScalarSnapshot.Of(db, new ScalarProbe { Value = new Version(1, 2) }))
            .Message.ShouldSatisfyAllConditions(
                m => m.ShouldContain("'Value'"),
                m => m.ShouldContain("System.Version"));
        Should.Throw<NotSupportedException>(() => EntityScalarSnapshot.Of(db, new ScalarProbe { Value = new KeyValuePair<string, int>("k", 1) }))
            .Message.ShouldContain("'Value'");
        Should.Throw<NotSupportedException>(() => EntityScalarSnapshot.Of(db, new Uri("https://example.invalid/")))
            .Message.ShouldSatisfyAllConditions(
                m => m.ShouldContain("'<root>'"),
                m => m.ShouldContain("System.Uri"));
    }

    [Test]
    [Arguments(typeof(SessionQueuedMessage))]
    [Arguments(typeof(AgentTask))]
    [Arguments(typeof(AgentSession))]
    [Arguments(typeof(AgentTaskLandNotification))]
    [Arguments(typeof(Board))]
    [Arguments(typeof(Card))]
    public void Snapshot_covers_exactly_the_reflected_non_navigation_properties(Type entityType)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=entity_snapshot_coverage;Username=unused;Password=unused").Options);
        // The oracle is EF's own navigation metadata, not the helper's navigation rule.
        var model = db.Model.FindEntityType(entityType)!;
        var navigations = model.GetNavigations().Select(n => n.Name).Concat(model.GetSkipNavigations().Select(n => n.Name))
            .ToHashSet(StringComparer.Ordinal);
        if (entityType == typeof(SessionQueuedMessage))
            navigations.ShouldBe(new[] { nameof(SessionQueuedMessage.AgentSession) });
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0).ToArray();
        var expected = properties.Select(p => p.Name).Where(n => !navigations.Contains(n)).Order(StringComparer.Ordinal).ToArray();
        expected.Length.ShouldBeGreaterThan(5);
        // One instance, mutated and restored per property: some entities default Id to Guid.NewGuid().
        var entity = Activator.CreateInstance(entityType)!;
        var before = EntityScalarSnapshot.Of(db, entity);
        Names(before).ShouldBe(expected,
            $"{entityType.Name}: the snapshot must render each of its {expected.Length} reflected non-navigation properties once, in ordinal order, and no navigation ({string.Join(", ", navigations)})");
        foreach (var property in properties.Where(p => p.SetMethod?.IsPublic == true))
        {
            var original = property.GetValue(entity);
            if (navigations.Contains(property.Name))
            {
                if (property.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable)))
                    continue;
                property.SetValue(entity, Activator.CreateInstance(property.PropertyType));
                EntityScalarSnapshot.Of(db, entity).ShouldBe(before, $"{entityType.Name}.{property.Name} is a navigation");
            }
            else
            {
                property.SetValue(entity, Different(property.PropertyType, original, $"{entityType.Name}.{property.Name}"));
                EntityScalarSnapshot.Of(db, entity).ShouldNotBe(before, $"{entityType.Name}.{property.Name} must be part of the snapshot");
            }
            property.SetValue(entity, original);
        }
    }

    private static string[] Names(string snapshot) =>
        snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[..line.IndexOf('=')]).ToArray();

    private static object? Different(Type type, object? current, string name)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return current is null ? Activator.CreateInstance(underlying) : null;
        if (type == typeof(string)) return (string?)current + "~";
        if (type == typeof(bool)) return !(bool)current!;
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(DateTime)) return ((DateTime)current!).AddTicks(1);
        if (type == typeof(DateTimeOffset)) return ((DateTimeOffset)current!).AddTicks(1);
        if (type == typeof(TimeSpan)) return ((TimeSpan)current!).Add(TimeSpan.FromTicks(1));
        if (type == typeof(byte[])) return new byte[] { 1 };
        if (type.IsEnum) return Enum.ToObject(type, Convert.ToInt64(current) + 1);
        if (type == typeof(int)) return (int)current! + 1;
        if (type == typeof(long)) return (long)current! + 1;
        if (type == typeof(short)) return (short)((short)current! + 1);
        if (type == typeof(decimal)) return (decimal)current! + 1;
        if (type == typeof(double)) return (double)current! + 1;
        if (type == typeof(float)) return (float)current! + 1;
        throw new NotSupportedException($"{name}: add a distinct-value rule for {type.FullName}");
    }

    private static string Snap(ProbeContext db, object? value) => EntityScalarSnapshot.Of(db, new ScalarProbe { Value = value });

    private static string Describe(object? value) => value switch
    {
        null => "null",
        byte[] bytes => $"byte[{bytes.Length}]{{{string.Join(",", bytes)}}}",
        _ => $"{value.GetType().Name}({value})",
    };

    private static object Other(Type type) => type switch
    {
        _ when type == typeof(Half) => (Half)1.5,
        _ when type == typeof(TimeOnly) => new TimeOnly(1),
        _ when type == typeof(DateTime) => new DateTime(1, DateTimeKind.Utc),
        _ when type == typeof(DateTimeOffset) => new DateTimeOffset(1, TimeSpan.Zero),
        _ when type == typeof(decimal) => 0.0m,
        _ when type == typeof(DayOfWeek) => DayOfWeek.Monday,
        _ when type == typeof(int) => 1,
        _ when type == typeof(Guid) => Guid.Parse("00000000-0000-0000-0000-000000000001"),
        _ when type == typeof(double) => -0.0,
        _ when type == typeof(bool) => true,
        _ => throw new NotSupportedException($"add a non-default value for {type.FullName}"),
    };

    public sealed class ScalarProbe
    {
        public int Id { get; set; }
        [NotMapped] public object? Value { get; set; }
        [NotMapped] public Half? NullableHalf { get; set; }
        [NotMapped] public TimeOnly? NullableTime { get; set; }
        [NotMapped] public DateTime? NullableDateTime { get; set; }
        [NotMapped] public DateTimeOffset? NullableOffset { get; set; }
        [NotMapped] public decimal? NullableDecimal { get; set; }
        [NotMapped] public DayOfWeek? NullableEnum { get; set; }
        [NotMapped] public int? NullableInt { get; set; }
        [NotMapped] public Guid? NullableGuid { get; set; }
        [NotMapped] public double? NullableDouble { get; set; }
        [NotMapped] public bool? NullableBool { get; set; }
        [NotMapped] public byte[]? Bytes { get; set; }
    }

    public sealed class UnsupportedProbe
    {
        public int Id { get; set; }
        [NotMapped] public Uri? Link { get; set; }
    }

    private sealed class ProbeContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql("Host=localhost;Database=entity_snapshot_probe;Username=unused;Password=unused");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScalarProbe>();
            modelBuilder.Entity<UnsupportedProbe>();
        }
    }
}
