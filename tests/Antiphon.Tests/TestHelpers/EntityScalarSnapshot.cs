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
/// Contract. The value must be an instance of an entity type in <paramref name="db"/>'s model
/// (<c>IModel.FindEntityType</c>), or a sequence of them. Each such object renders every public
/// readable property, in ordinal name order, as one <c>path=value</c> line, except navigations to
/// other (non-owned) entity types: navigation values were constant in the JSON form these tests
/// compared (not loaded: null or the entity's empty default collection). Sequences render each item
/// at <c>path[i]</c> plus <c>path.Count</c>. Leaves use one exact, type-tagged encoding per
/// supported type (see <see cref="Scalar"/>): distinct values of a supported type never render
/// alike, null never collides with a value, and floating-point values carry their bit pattern. A
/// property whose declared type is not supported (see <see cref="IsSupportedPropertyType"/>), or any
/// other runtime value, throws <see cref="NotSupportedException"/> naming the type and the property
/// path, even while the value is null; nothing is skipped or rendered lossily.
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

    /// <summary>
    /// The exact leaf encoding, or null for an unsupported type. Strings are quoted and escaped;
    /// every other value carries a type tag, so no two supported values of different types, and no
    /// value and null (<c>null</c>), share a rendering.
    /// </summary>
    public static string? Scalar(object value) => value switch
    {
        string s => Quote(s),
        byte[] bytes => "b64:" + Convert.ToBase64String(bytes),
        char c => "Char:U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        Enum e => e.GetType().FullName + ":" + e + "(" + Convert.ToString(
            Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + ")",
        DateTime d => "DateTime:" + d.ToString("O", CultureInfo.InvariantCulture) + "/" + d.Kind,
        DateTimeOffset d => "DateTimeOffset:" + d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => "DateOnly:" + d.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t => "TimeOnly:" + t.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan t => "TimeSpan:" + t.ToString("c", CultureInfo.InvariantCulture),
        Guid g => "Guid:" + g.ToString("D"),
        decimal m => "Decimal:" + m.ToString(CultureInfo.InvariantCulture),
        double d => "Double:" + d.ToString("R", CultureInfo.InvariantCulture)
            + "/0x" + BitConverter.DoubleToInt64Bits(d).ToString("X16", CultureInfo.InvariantCulture),
        float f => "Single:" + f.ToString("R", CultureInfo.InvariantCulture)
            + "/0x" + BitConverter.SingleToInt32Bits(f).ToString("X8", CultureInfo.InvariantCulture),
        Half h => "Half:" + h.ToString(CultureInfo.InvariantCulture)
            + "/0x" + BitConverter.HalfToUInt16Bits(h).ToString("X4", CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or Int128 or UInt128 =>
            value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
        JsonDocument document => "Json:" + document.RootElement.GetRawText(),
        JsonElement element => "Json:" + element.GetRawText(),
        _ => null,
    };

    private static readonly HashSet<Type> LeafTypes =
    [
        typeof(string), typeof(byte[]), typeof(char), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(decimal), typeof(double),
        typeof(float), typeof(Half), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int),
        typeof(uint), typeof(long), typeof(ulong), typeof(nint), typeof(nuint), typeof(Int128), typeof(UInt128),
        typeof(JsonDocument), typeof(JsonElement),
    ];

    /// <summary>
    /// Declared property types the renderer accepts: a <see cref="Scalar"/> leaf type or enum (or a
    /// nullable of one), <see cref="object"/> (dispatched on the runtime value, which must itself be
    /// supported), an entity type of <paramref name="model"/>, or a sequence of any of these.
    /// </summary>
    public static bool IsSupportedPropertyType(IModel model, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (LeafTypes.Contains(type) || type.IsEnum || type == typeof(object) || model.FindEntityType(type) is not null)
            return true;
        return ElementType(type) is { } element && element != type && IsSupportedPropertyType(model, element);
    }

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
        if (Scalar(value) is { } text)
        {
            builder.Append(path).Append('=').Append(text).Append('\n');
            return;
        }
        var type = value.GetType();
        if (model.FindEntityType(type) is not null)
        {
            foreach (var property in Properties(type))
            {
                if (IsNavigation(model, property))
                    continue;
                var child = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                if (!IsSupportedPropertyType(model, property.PropertyType))
                    throw new NotSupportedException(
                        $"EntityScalarSnapshot: '{child}' has unsupported type {property.PropertyType.FullName}; add an exact encoding or compare it another way.");
                Render(model, property.GetValue(value), child, builder, depth + 1);
            }
            return;
        }
        if (value is IEnumerable items && ElementType(type) is not null)
        {
            var index = 0;
            foreach (var item in items)
                Render(model, item, $"{path}[{index++}]", builder, depth + 1);
            builder.Append(path).Append(".Count=").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return;
        }
        throw new NotSupportedException(
            $"EntityScalarSnapshot: '{(path.Length == 0 ? "<root>" : path)}' has unsupported type {type.FullName}; add an exact encoding or compare it another way.");
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
