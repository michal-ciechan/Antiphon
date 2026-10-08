using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1137 recurrence guard. Default-options <c>JsonSerializer.Serialize</c> of an EF entity that
/// has navigations builds metadata for the whole domain graph under the process-wide STJ caching
/// lock; the in-process runner's release path waits on the same lock and overruns its 10 s budget.
/// Tests compare entity state through <see cref="EntityScalarSnapshot"/> instead. This census fails
/// when a test serializes a navigation-bearing entity straight out of a DbSet anywhere in the
/// assembly, or when the terminal-seat release/sweep sources serialize anything but a payload
/// literal or an allowlisted navigation-free value.
/// </summary>
[Category("Unit")]
public sealed class EntityGraphSerializationGuardTests
{
    private static readonly string[] LockSensitiveSources =
    [
        "tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs",
        "tests/Antiphon.Tests/Application/RunnerSeatOrphanSweepTests.cs",
        "tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs",
        "tests/Antiphon.Tests/Application/RunnerSeatLiveSeatWarmupTests.cs",
    ];

    /// <summary>Serialized arguments the release/sweep sources may keep, each navigation-free.</summary>
    private static readonly Dictionary<string, Type?> LockSensitiveAllowlist = new(StringComparer.Ordinal)
    {
        ["invalidation.Payload"] = null, // event-bus payload record, not an entity
        ["items"] = null, // attention item DTOs
        ["await db.RunnerSeatReleases.ToListAsync()"] = typeof(RunnerSeatRelease),
    };

    /// <summary>
    /// Known pre-existing offenders outside CARD-1137's scope, listed for a Backlog card rather than
    /// silently tolerated. Remove a row when its site moves to <see cref="EntityScalarSnapshot"/>.
    /// </summary>
    private static readonly HashSet<string> DbSetDebt = new(StringComparer.Ordinal)
    {
        "tests/Antiphon.Tests/Application/RemoteControlModalPersistenceTests.cs:RemoteControlModalEpisodes",
    };

    private static readonly Regex SerializeCall = new(@"JsonSerializer\.Serialize\w*\(\s*(?<arg>[^;]*)", RegexOptions.Compiled);
    private static readonly Regex DbSetArgument = new(@"^(?:await\s+)?\w+\.(?<set>\w+)\b", RegexOptions.Compiled);

    [Test]
    public void Tests_do_not_serialize_navigation_entities_straight_from_a_DbSet()
    {
        using var db = NewContext();
        var offenders = new List<string>();
        foreach (var (rel, line, number) in SourceLines())
            foreach (var set in NavigationDbSetsSerialized(db.Model, line))
            {
                if (!DbSetDebt.Contains($"{rel}:{set}"))
                    offenders.Add($"{rel}:{number} serializes {set}");
            }
        offenders.ShouldBeEmpty("compare EF entities with EntityScalarSnapshot.Of(db, ...), not default-options JsonSerializer (CARD-1137): "
            + string.Join("; ", offenders));
    }

    [Test]
    public void Release_and_sweep_sources_serialize_only_payloads_and_navigation_free_values()
    {
        using var db = NewContext();
        var offenders = new List<string>();
        var root = RepoRoot;
        foreach (var rel in LockSensitiveSources)
        {
            var lines = File.ReadAllLines(Path.Combine(root, rel));
            for (var i = 0; i < lines.Length; i++)
                offenders.AddRange(DisallowedLockSensitiveArguments(lines[i]).Select(a => $"{rel}:{i + 1} serializes '{a}'"));
        }
        offenders.ShouldBeEmpty("terminal-seat release/sweep tests share a host with the runner's release path (CARD-1137): "
            + string.Join("; ", offenders));
        foreach (var type in LockSensitiveAllowlist.Values.OfType<Type>())
            EntityScalarSnapshot.NavigationNames(db.Model, type).ShouldBeEmpty($"{type.Name} is allowlisted only while navigation-free");
    }

    [Test]
    public void Scanners_flag_the_CARD_1137_shapes()
    {
        using var db = NewContext();
        // The two original lines of Pending_delivery_prevents_release.
        DisallowedLockSensitiveArguments("            var before = JsonSerializer.Serialize(message);").ToArray().ShouldBe(new[] { "message" });
        NavigationDbSetsSerialized(db.Model,
            "            JsonSerializer.Serialize(await db.SessionQueuedMessages.SingleAsync()).ShouldBe(before, \"x\");")
            .ToArray().ShouldBe(new[] { "SessionQueuedMessages" });
        NavigationDbSetsSerialized(db.Model,
            "        var beforeBoard = JsonSerializer.Serialize(await db.Boards.AsNoTracking().SingleAsync(b => b.Id == id));")
            .ToArray().ShouldBe(new[] { "Boards" });
        // Payload literals and navigation-free entities stay legal.
        DisallowedLockSensitiveArguments("        var line = JsonSerializer.Serialize(new { type = \"user\" });").ShouldBeEmpty();
        DisallowedLockSensitiveArguments("            var ledger = JsonSerializer.Serialize(await db.RunnerSeatReleases.ToListAsync());").ShouldBeEmpty();
        NavigationDbSetsSerialized(db.Model, "JsonSerializer.Serialize(await db.RunnerSeatReleases.ToListAsync())").ShouldBeEmpty();
    }

    [Test]
    [Arguments(typeof(SessionQueuedMessage))]
    [Arguments(typeof(AgentTask))]
    [Arguments(typeof(AgentSession))]
    [Arguments(typeof(AgentTaskLandNotification))]
    [Arguments(typeof(Board))]
    [Arguments(typeof(Card))]
    public void Snapshot_changes_when_any_settable_non_navigation_property_changes(Type entityType)
    {
        using var db = NewContext();
        var navigations = EntityScalarSnapshot.NavigationNames(db.Model, entityType);
        if (entityType == typeof(SessionQueuedMessage))
            navigations.ToArray().ShouldBe(new[] { "AgentSession" });
        // One instance, mutated and restored per property: some entities default Id to Guid.NewGuid().
        var entity = Activator.CreateInstance(entityType)!;
        var before = EntityScalarSnapshot.Of(db, entity);
        var compared = 0;
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length > 0 || navigations.Contains(property.Name))
                continue;
            var original = property.GetValue(entity);
            property.SetValue(entity, Different(property.PropertyType, original, $"{entityType.Name}.{property.Name}"));
            EntityScalarSnapshot.Of(db, entity).ShouldNotBe(before, $"{entityType.Name}.{property.Name} must be part of the snapshot");
            property.SetValue(entity, original);
            compared++;
        }
        compared.ShouldBeGreaterThan(5);
        // Navigations are excluded, so loading one cannot pull the domain graph into the snapshot.
        foreach (var name in navigations)
        {
            var property = entityType.GetProperty(name)!;
            if (property.SetMethod?.IsPublic != true || property.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable)))
                continue;
            var original = property.GetValue(entity);
            property.SetValue(entity, Activator.CreateInstance(property.PropertyType));
            EntityScalarSnapshot.Of(db, entity).ShouldBe(before, $"{entityType.Name}.{name} is a navigation");
            property.SetValue(entity, original);
        }
    }

    private static IEnumerable<string> DisallowedLockSensitiveArguments(string line)
    {
        foreach (Match match in SerializeCall.Matches(line))
        {
            var arg = Argument(match.Groups["arg"].Value);
            if (arg.Length == 0 || arg.StartsWith("new", StringComparison.Ordinal) || LockSensitiveAllowlist.ContainsKey(arg))
                continue;
            yield return arg;
        }
    }

    private static IEnumerable<string> NavigationDbSetsSerialized(IModel model, string line)
    {
        foreach (Match match in SerializeCall.Matches(line))
        {
            var set = DbSetArgument.Match(match.Groups["arg"].Value);
            if (!set.Success)
                continue;
            var property = typeof(AppDbContext).GetProperty(set.Groups["set"].Value);
            var entity = property?.PropertyType is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(DbSet<>)
                ? t.GetGenericArguments()[0] : null;
            if (entity is not null && EntityScalarSnapshot.NavigationNames(model, entity).Count > 0)
                yield return property!.Name;
        }
    }

    /// <summary>The call's first argument: text up to the matching close paren or top-level comma.</summary>
    private static string Argument(string tail)
    {
        var depth = 0;
        for (var i = 0; i < tail.Length; i++)
        {
            var c = tail[i];
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']')
            {
                if (depth == 0) return tail[..i].Trim();
                depth--;
            }
            else if (c == ',' && depth == 0) return tail[..i].Trim();
        }
        return tail.Trim();
    }

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

    private static IEnumerable<(string Rel, string Line, int Number)> SourceLines()
    {
        var root = RepoRoot;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "tests", "Antiphon.Tests"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsBuildOutput(rel) || rel.EndsWith("/EntityGraphSerializationGuardTests.cs", StringComparison.Ordinal))
                continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                yield return (rel, lines[i], i + 1);
        }
    }

    private static bool IsBuildOutput(string rel) => rel.Split('/').Any(p =>
        p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || p.StartsWith("bin-", StringComparison.OrdinalIgnoreCase));

    private static AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=entity_snapshot_guard;Username=unused;Password=unused")
            .Options);

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
