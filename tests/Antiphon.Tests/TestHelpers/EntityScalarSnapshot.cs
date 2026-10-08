using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1137: a before/after equality snapshot of EF entities that never touches System.Text.Json.
/// <para>
/// <c>JsonSerializer.Serialize(entity)</c> with default options builds reflection metadata for the
/// entity's whole navigation graph (122 types for <c>SessionQueuedMessage</c>) while holding the
/// process-wide <c>JsonSerializerOptions.Default</c> caching-context lock. Any other first-use
/// serialization in the same test host blocks on that lock, including the in-process session
/// runner's <c>RunnerSessionExitedEvent</c> publish inside a terminal-seat release, which then
/// overruns its 10 s HTTP budget. See docs/investigations/2026-10-08-card-1137-release-stall.md.
/// </para>
/// <para>
/// The snapshot renders every public readable property of the entity, in ordinal name
/// order, except navigations to other (non-owned) entity types in <paramref name="db"/>'s model.
/// Navigation values were constant in the JSON form these tests compared (not loaded: null or the
/// entity's empty default collection), so dropping them keeps the comparison's meaning. Owned or
/// plain complex values are rendered recursively; a type the renderer does not understand throws
/// instead of being skipped silently.
/// </para>
/// </summary>
internal static class EntityScalarSnapshot
{
    private const int MaxDepth = 8;

    public static string Of(DbContext db, object? value)
    {
        var builder = new StringBuilder();
        Render(db.Model, value, "", builder, 0);
        return builder.ToString();
    }

    /// <summary>True when <paramref name="property"/> points at another (non-owned) entity or a collection of them.</summary>
    public static bool IsNavigation(IModel model, PropertyInfo property)
    {
        var type = ElementType(property.PropertyType) ?? property.PropertyType;
        return model.FindEntityType(type) is { } entity && !entity.IsOwned();
    }

    /// <summary>Navigation properties of <paramref name="type"/> as defined by <see cref="IsNavigation"/>.</summary>
    public static IReadOnlyList<string> NavigationNames(IModel model, Type type) =>
        Properties(type).Where(p => IsNavigation(model, p)).Select(p => p.Name).ToArray();

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    private static void Render(IModel model, object? value, string path, StringBuilder builder, int depth)
    {
        if (depth > MaxDepth)
            throw new NotSupportedException($"EntityScalarSnapshot: '{path}' nests deeper than {MaxDepth}; a cycle outside the EF navigation set?");
        if (value is null)
        {
            builder.Append(path).Append("=null\n");
            return;
        }
        if (TryScalar(value, out var text))
        {
            builder.Append(path).Append('=').Append(text).Append('\n');
            return;
        }
        if (value is IEnumerable items)
        {
            var index = 0;
            foreach (var item in items)
                Render(model, item, $"{path}[{index++}]", builder, depth + 1);
            builder.Append(path).Append(".Count=").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return;
        }
        var type = value.GetType();
        if (type.IsPointer || typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type))
            throw new NotSupportedException($"EntityScalarSnapshot: '{path}' has unsupported type {type.FullName}.");
        foreach (var property in Properties(type))
        {
            if (IsNavigation(model, property))
                continue;
            var child = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            Render(model, property.GetValue(value), child, builder, depth + 1);
        }
    }

    private static bool TryScalar(object value, out string text)
    {
        text = value switch
        {
            string s => Quote(s),
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture) + "/" + d.Kind,
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            JsonDocument document => document.RootElement.GetRawText(),
            JsonElement element => element.GetRawText(),
            Enum e => e.GetType().Name + "." + e.ToString(),
            bool b => b ? "true" : "false",
            Guid or TimeSpan or DateOnly or TimeOnly or decimal or char => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            _ when value.GetType().IsPrimitive => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => null!,
        };
        return text is not null;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        return type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
