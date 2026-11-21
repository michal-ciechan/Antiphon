using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// ProbeModelComparison decides whether the channel-outbound probes may use a generated compiled model instead of the
// model AppDbContext builds (ProbeModelGenerator publishes one only when it finds no difference). These pin that it
// finds each kind of model difference a probe's database work could notice, and none between two builds of one model.
[Category("Unit")]
public sealed class ProbeModelComparisonTests
{
    private static readonly Dictionary<string, Action<ModelBuilder>> Changes = new(StringComparer.Ordinal)
    {
        ["column"] = b => b.Entity<Child>().Property(c => c.Name).HasColumnName("label"),
        ["table"] = b => b.Entity<Child>().ToTable("kids"),
        ["store-type"] = b => b.Entity<Child>().Property(c => c.Name).HasColumnType("varchar(10)"),
        ["max-length"] = b => b.Entity<Child>().Property(c => c.Name).HasMaxLength(10),
        ["nullability"] = b => b.Entity<Child>().Property(c => c.Name).IsRequired(false),
        ["array-nullability"] = b => b.Entity<Child>().Property(c => c.Tags).IsRequired(false),
        ["delete-behaviour"] = b => b.Entity<Child>().HasOne(c => c.Parent).WithMany(p => p.Children)
            .HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict),
        ["converter"] = b => b.Entity<Child>().Property(c => c.Kind).HasConversion<string>(),
        ["comparer"] = b => b.Entity<Child>().Property(c => c.Name).Metadata.SetValueComparer(new IgnoreCase()),
        ["access-mode"] = b => b.Entity<Child>().Property(c => c.Name).UsePropertyAccessMode(PropertyAccessMode.Property),
        ["shadow"] = b => b.Entity<Child>().Property<int>("Revision"),
        ["unique-index"] = b => b.Entity<Child>().HasIndex(c => c.Name).IsUnique(),
        ["value-generation"] = b => b.Entity<Child>().Property(c => c.Id).ValueGeneratedNever(),
        ["concurrency"] = b => b.Entity<Child>().Property(c => c.Name).IsConcurrencyToken(),
        // A provider annotation the runtime model keeps. Design-time-only ones (a column comment) never reach a
        // runtime model, so no probe can see them.
        ["provider-annotation"] = b => b.Entity<Child>().Property(c => c.Id).UseIdentityAlwaysColumn(),
        // Element-level differences of a primitive collection, whose element type the model text cannot show
        // (ElementType). An element converter, even one that keeps the store type (uuid[]), changes the array's own
        // mapping; element nullability changes only the elements, so only Element() finds it (checked by removing it).
        ["element-converter"] = b => b.Entity<Child>().PrimitiveCollection(c => c.Tags)
            .ElementType(e => e.HasConversion(new ValueConverter<Guid, Guid>(g => g, g => g))),
        ["element-nullability"] = b => b.Entity<Child>().PrimitiveCollection(c => c.Labels)
            .ElementType(e => e.IsRequired(false)),
    };

    [Test]
    public void Two_builds_of_one_model_do_not_differ() =>
        ProbeModelComparison.Differences(Model(_ => { }), Model(_ => { })).ShouldBeEmpty();

    [Test]
    [Arguments("column")]
    [Arguments("table")]
    [Arguments("store-type")]
    [Arguments("max-length")]
    [Arguments("nullability")]
    [Arguments("array-nullability")]
    [Arguments("delete-behaviour")]
    [Arguments("converter")]
    [Arguments("comparer")]
    [Arguments("access-mode")]
    [Arguments("shadow")]
    [Arguments("unique-index")]
    [Arguments("value-generation")]
    [Arguments("concurrency")]
    [Arguments("provider-annotation")]
    [Arguments("element-converter")]
    [Arguments("element-nullability")]
    public void Each_kind_of_model_difference_is_found(string change) =>
        ProbeModelComparison.Differences(Model(_ => { }), Model(Changes[change])).ShouldNotBeEmpty(change);

    private static Microsoft.EntityFrameworkCore.Metadata.IModel Model(Action<ModelBuilder> change)
    {
        using var context = new Tiny(change);
        return context.Model;
    }

    // No service provider caching, so each context builds its own model rather than sharing the cached one.
    private sealed class Tiny(Action<ModelBuilder> change) : DbContext(new DbContextOptionsBuilder()
        .UseNpgsql("Host=unused").EnableServiceProviderCaching(false).Options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Parent>();
            b.Entity<Child>().HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
            b.Entity<Child>().HasIndex(c => c.Name);
            change(b);
        }
    }

    private sealed class IgnoreCase() : ValueComparer<string>(
        (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
        s => StringComparer.OrdinalIgnoreCase.GetHashCode(s));

    private sealed class Parent
    {
        public int Id { get; set; }
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public Parent Parent { get; set; } = null!;
        public string Name { get; set; } = "";
        public ChildKind Kind { get; set; }
        public Guid[] Tags { get; set; } = [];
        public string[] Labels { get; set; } = [];
    }

    private enum ChildKind { First, Second }
}
