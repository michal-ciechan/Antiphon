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
/// when a test serializes a DbSet query whose result type (after any <c>Select</c> projection or
/// member access) is, or carries, a navigation-bearing entity anywhere in the assembly, or when the
/// terminal-seat release/sweep sources serialize anything but a payload literal, an allowlisted
/// navigation-free value, or a DbSet query with a navigation-free result. Scalar, string, record,
/// anonymous and dictionary projections are admitted; an unreadable expression keeps the
/// originating entity type and stays flagged.
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
            foreach (var (set, serialized) in NavigationEntitiesSerialized(db.Model, line))
            {
                if (!DbSetDebt.Contains($"{rel}:{set}"))
                    offenders.Add($"{rel}:{number} serializes {serialized.Name} from {set}");
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
                offenders.AddRange(DisallowedLockSensitiveArguments(db.Model, lines[i]).Select(a => $"{rel}:{i + 1} serializes '{a}'"));
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
        DisallowedLockSensitiveArguments(db.Model, "            var before = JsonSerializer.Serialize(message);").ToArray().ShouldBe(new[] { "message" });
        NavigationDbSetsSerialized(db.Model,
            "            JsonSerializer.Serialize(await db.SessionQueuedMessages.SingleAsync()).ShouldBe(before, \"x\");")
            .ToArray().ShouldBe(new[] { "SessionQueuedMessages" });
        NavigationDbSetsSerialized(db.Model,
            "        var beforeBoard = JsonSerializer.Serialize(await db.Boards.AsNoTracking().SingleAsync(b => b.Id == id));")
            .ToArray().ShouldBe(new[] { "Boards" });
        // Payload literals and navigation-free entities stay legal.
        DisallowedLockSensitiveArguments(db.Model, "        var line = JsonSerializer.Serialize(new { type = \"user\" });").ShouldBeEmpty();
        DisallowedLockSensitiveArguments(db.Model, "            var ledger = JsonSerializer.Serialize(await db.RunnerSeatReleases.ToListAsync());").ShouldBeEmpty();
        NavigationDbSetsSerialized(db.Model, "JsonSerializer.Serialize(await db.RunnerSeatReleases.ToListAsync())").ShouldBeEmpty();
    }

    /// <summary>
    /// F-1: the scanners judge what is serialized, not where it came from. A projection to scalars,
    /// strings, records, anonymous objects or dictionaries of scalars is navigation-free.
    /// </summary>
    [Test]
    [Arguments("await db.SessionQueuedMessages.Select(m => new { m.Id, m.Body }).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.AsNoTracking().Select(m => new QueuedRow(m.Id, m.Body, m.Status)).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.Select(m => new Dictionary<string, object> { [\"id\"] = m.Id, [\"body\"] = m.Body! }).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.Select(m => m.Body).ToListAsync()")]
    [Arguments("await db.Boards.AsNoTracking().Where(b => b.Id == id).Select(b => b.Name).SingleAsync()")]
    [Arguments("await db.SessionQueuedMessages.Select(m => new { m.Id, Session = m.AgentSession.Id, Label = $\"{m.Sequence}:{m.Status}\" }).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.Select(m => m.AgentSession.ToString()).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.Select(m => m.Sequence < 5).ToListAsync()")]
    [Arguments("await db.SessionQueuedMessages.CountAsync()")]
    [Arguments("db.SessionQueuedMessages.Single(m => m.Id == id).Body")]
    public void Scanners_admit_navigation_free_projections(string argument)
    {
        using var db = NewContext();
        var line = $"        var json = JsonSerializer.Serialize({argument});";
        NavigationEntitiesSerialized(db.Model, line).ShouldBeEmpty(argument);
        DisallowedLockSensitiveArguments(db.Model, line).ShouldBeEmpty(argument);
    }

    /// <summary>F-1: a result whose declared type is (or carries) a navigation-bearing entity stays flagged.</summary>
    [Test]
    [Arguments("await db.SessionQueuedMessages.SingleAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.Boards.AsNoTracking().OrderBy(b => b.Name).ToListAsync()", "Boards", typeof(Board))]
    [Arguments("await db.SessionQueuedMessages.Select(m => m).ToListAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.SessionQueuedMessages.Select(m => m.AgentSession).ToListAsync()", "SessionQueuedMessages", typeof(AgentSession))]
    [Arguments("await db.SessionQueuedMessages.Select(m => new { m.Id, m.AgentSession }).ToListAsync()", "SessionQueuedMessages", typeof(AgentSession))]
    [Arguments("await db.SessionQueuedMessages.Select(m => new { Message = m }).ToListAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.SessionQueuedMessages.Select(m => new Dictionary<string, object> { [\"message\"] = m }).ToListAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.SessionQueuedMessages.Select(m => new QueuedRow(m.Id, m.Body, m)).ToListAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.SessionQueuedMessages.Select(m => new SessionQueuedMessage { Id = m.Id }).ToListAsync()", "SessionQueuedMessages", typeof(SessionQueuedMessage))]
    [Arguments("await db.Boards.Select(b => b.Cards).ToListAsync()", "Boards", typeof(Card))]
    [Arguments("db.SessionQueuedMessages.Single(m => m.Id == id).AgentSession", "SessionQueuedMessages", typeof(AgentSession))]
    public void Scanners_flag_navigation_bearing_results(string argument, string set, Type serialized)
    {
        using var db = NewContext();
        var line = $"        var json = JsonSerializer.Serialize({argument});";
        NavigationEntitiesSerialized(db.Model, line).ToArray().ShouldBe(new[] { (set, serialized) }, argument);
        DisallowedLockSensitiveArguments(db.Model, line).ToArray().ShouldBe(new[] { argument });
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

    private static IEnumerable<string> DisallowedLockSensitiveArguments(IModel model, string line)
    {
        foreach (Match match in SerializeCall.Matches(line))
        {
            var arg = Argument(match.Groups["arg"].Value);
            if (arg.Length == 0 || arg.StartsWith("new", StringComparison.Ordinal) || LockSensitiveAllowlist.ContainsKey(arg)
                || (DbSetQuery(arg) is not null && SerializedNavigationEntity(model, arg) is null))
                continue;
            yield return arg;
        }
    }

    private static IEnumerable<string> NavigationDbSetsSerialized(IModel model, string line) =>
        NavigationEntitiesSerialized(model, line).Select(s => s.Set);

    private static IEnumerable<(string Set, Type Serialized)> NavigationEntitiesSerialized(IModel model, string line)
    {
        foreach (Match match in SerializeCall.Matches(line))
        {
            if (SerializedNavigationEntity(model, Argument(match.Groups["arg"].Value)) is { } serialized)
                yield return serialized;
        }
    }

    /// <summary>
    /// For a <c>[await] x.&lt;DbSet&gt;...</c> argument, the navigation-bearing entity type the call
    /// would serialize after the query's projections, or null when the argument is not a DbSet query or
    /// what it serializes is navigation-free (a scalar, string, DTO, anonymous object or dictionary of
    /// scalars, or an entity without navigations). STJ builds metadata for the declared result type, so
    /// the type decides, not whether a navigation value happens to be loaded. Anything the scan cannot
    /// read keeps the originating entity type (fails closed).
    /// </summary>
    private static (string Set, Type Serialized)? SerializedNavigationEntity(IModel model, string arg)
    {
        if (DbSetQuery(arg) is not ({ } property, { } entityType, var end))
            return null;
        Type? current = entityType;
        var sequence = true;
        foreach (var (name, args) in Calls(arg[end..]))
        {
            if (current is null)
                break; // a navigation-free result cannot pick the graph back up
            if (args is null)
            {
                // Member access: the property's own type, when it is one; otherwise unreadable, keep the type.
                var member = sequence ? null : current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (member is not null)
                    (current, sequence) = (NavigationBearing(model, member.PropertyType), ElementOf(member.PropertyType) is not null);
                else if (sequence && name is "Count" or "Length")
                    current = null;
            }
            else if (name == "Select" && sequence)
                current = Projected(model, args, current);
            else if (ScalarResults.Contains(name))
                current = null;
            else if (ElementResults.Contains(name))
                sequence = false;
            // Filters, ordering, tracking, includes, materialization and unknown calls keep the result type.
        }
        return current is not null && NavigationBearing(model, current) is { } entity ? (property.Name, entity) : null;
    }

    /// <summary>The DbSet property, its entity type and the end of <c>[await] x.&lt;DbSet&gt;</c> when <paramref name="arg"/> starts with one.</summary>
    private static (PropertyInfo Property, Type Entity, int End)? DbSetQuery(string arg)
    {
        var match = DbSetArgument.Match(arg);
        var property = match.Success ? typeof(AppDbContext).GetProperty(match.Groups["set"].Value) : null;
        return property?.PropertyType is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(DbSet<>)
            ? (property, t.GetGenericArguments()[0], match.Index + match.Length) : null;
    }

    private static readonly HashSet<string> ScalarResults = new(StringComparer.Ordinal)
    {
        "Count", "CountAsync", "LongCount", "LongCountAsync", "Any", "AnyAsync", "All", "AllAsync", "Contains", "ContainsAsync",
    };

    private static readonly HashSet<string> ElementResults = new(StringComparer.Ordinal)
    {
        "Single", "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync", "First", "FirstAsync", "FirstOrDefault",
        "FirstOrDefaultAsync", "Last", "LastAsync", "LastOrDefault", "LastOrDefaultAsync", "ElementAt", "ElementAtAsync",
        "ElementAtOrDefault", "ElementAtOrDefaultAsync", "Find", "FindAsync",
    };

    private static readonly Regex Lambda = new(@"^\s*\(?\s*(?<p>\w+)\s*(?:,\s*\w+\s*)?\)?\s*=>(?<body>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Creation = new(@"^new\s+(?:[\w]+\.)*(?<type>\w+)", RegexOptions.Compiled);

    /// <summary>The navigation-bearing type a <c>Select</c> lambda's result can carry, or null when it carries none.</summary>
    private static Type? Projected(IModel model, string lambda, Type parameterType)
    {
        var parts = Lambda.Match(lambda);
        if (!parts.Success)
            return NavigationBearing(model, parameterType); // method group or unreadable: assume the element itself
        var parameter = parts.Groups["p"].Value;
        var body = parts.Groups["body"].Value.Trim();
        if (body.StartsWith('"') || body.StartsWith("$\"", StringComparison.Ordinal) || body.StartsWith("@\"", StringComparison.Ordinal)
            || body.StartsWith("$@\"", StringComparison.Ordinal) || body.StartsWith("@$\"", StringComparison.Ordinal))
            return null; // a string: rendering an entity into text does not serialize its graph
        if (Creation.Match(body) is { Success: true } created
            && model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == created.Groups["type"].Value) is { } constructed
            && NavigationBearing(model, constructed.ClrType) is { } constructedEntity)
            return constructedEntity;
        // Every use of the parameter whose member chain ends on a navigation-bearing type flows into the result.
        foreach (Match use in Regex.Matches(body, $@"(?<![\w.@]){Regex.Escape(parameter)}\b(?<members>(?:\s*[?!]?\.\s*\w+)*)(?<call>\s*(?:<[^<>()]*>)?\s*\()?"))
        {
            var members = Regex.Matches(use.Groups["members"].Value, @"\w+").Select(m => m.Value).ToList();
            if (use.Groups["call"].Success && members.Count > 0)
            {
                if (members[^1] == "ToString")
                    continue;
                members.RemoveAt(members.Count - 1); // a method on the receiver: judge the receiver
            }
            var type = parameterType;
            foreach (var name in members)
            {
                var member = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (member is null)
                    break; // unreadable: judge the last type reached
                type = member.PropertyType;
            }
            if (NavigationBearing(model, type) is { } carried)
                return carried;
        }
        return null;
    }

    /// <summary>The entity type behind <paramref name="type"/> (or its sequence element) when it has navigations.</summary>
    private static Type? NavigationBearing(IModel model, Type type)
    {
        var element = ElementOf(type) ?? Nullable.GetUnderlyingType(type) ?? type;
        return model.FindEntityType(element) is { } entity && !entity.IsOwned()
            && EntityScalarSnapshot.NavigationNames(model, element).Count > 0 ? element : null;
    }

    private static Type? ElementOf(Type type) => type == typeof(string) || type == typeof(byte[]) ? null
        : type.IsArray ? type.GetElementType()
        : type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    /// <summary><c>.Name&lt;...&gt;(args)</c> or <c>.Name</c> steps after the DbSet; stops at anything else.</summary>
    private static IEnumerable<(string Name, string? Args)> Calls(string chain)
    {
        var i = 0;
        while (true)
        {
            while (i < chain.Length && (char.IsWhiteSpace(chain[i]) || chain[i] is '!' or '?'))
                i++;
            if (i >= chain.Length || chain[i] != '.')
                yield break;
            var start = ++i;
            while (i < chain.Length && (char.IsLetterOrDigit(chain[i]) || chain[i] == '_'))
                i++;
            if (i == start)
                yield break;
            var name = chain[start..i];
            if (i < chain.Length && chain[i] == '<')
                i = Balanced(chain, i, '<', '>');
            if (i < 0)
                yield break;
            if (i < chain.Length && chain[i] == '(')
            {
                var end = Balanced(chain, i, '(', ')');
                if (end < 0)
                    yield break;
                yield return (name, chain[(i + 1)..(end - 1)]);
                i = end;
            }
            else
                yield return (name, null);
        }
    }

    /// <summary>Index just past the bracket closing the one at <paramref name="open"/>, or -1.</summary>
    private static int Balanced(string text, int open, char opening, char closing)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == opening) depth++;
            else if (text[i] == closing && --depth == 0) return i + 1;
        }
        return -1;
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
