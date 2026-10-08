using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
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
/// (<c>IModel.FindEntityType</c> on its runtime type), or an <c>IEnumerable&lt;T&gt;</c> whose
/// <c>T</c> is an entity type and whose items are such instances (rendered as <c>[i]</c> items plus
/// <c>.Count</c>). Each entity renders every public readable property, in ordinal name order, as
/// one <c>Name=value</c> line, except navigations (a property whose type, or
/// element type, is another non-owned entity type of the model; an owned type is unsupported):
/// navigation values were constant in the JSON form these tests compared (not loaded: null or the
/// entity's empty default collection).
/// Every other property must be a supported scalar, checked twice: its declared type (even while
/// the value is null) and the runtime type of its value (so an <see cref="object"/>-typed property
/// holding a collection, empty or not, an entity, a delegate or any other unsupported value
/// throws). Supported scalars are exactly the cases of <see cref="Scalar"/>, enums and nullables of
/// them; each has one exact, type-tagged encoding: distinct values never render alike, null never
/// collides with a value, and floating-point values carry their bit pattern.
/// <see cref="System.Text.Json.JsonDocument"/> and <see cref="System.Text.Json.JsonElement"/> are
/// unsupported: no property of the snapshotted entities has either type. Anything unsupported throws
/// <see cref="NotSupportedException"/> naming the property path and the type; nothing is skipped
/// or rendered lossily.
/// </para>
/// </summary>
internal static class EntityScalarSnapshot
{
    public static string Of(DbContext db, object? value)
    {
        var builder = new StringBuilder();
        if (value is null)
            builder.Append("=null\n");
        else if (db.Model.FindEntityType(value.GetType()) is not null)
            RenderEntity(db.Model, value, "", builder);
        else if (value is IEnumerable items && ElementType(value.GetType()) is { } element && db.Model.FindEntityType(element) is not null)
        {
            var index = 0;
            foreach (var item in items)
            {
                var path = $"[{index++}]";
                if (item is null || db.Model.FindEntityType(item.GetType()) is null)
                    throw Unsupported(path, item?.GetType().FullName ?? "null");
                RenderEntity(db.Model, item, path, builder);
            }
            builder.Append(".Count=").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        else
            throw Unsupported("<root>", value.GetType().FullName);
        return builder.ToString();
    }

    /// <summary>
    /// The exact leaf encoding, or null for an unsupported runtime type. Strings are quoted and
    /// escaped; every other value carries a type tag, so no two supported values of different
    /// types, and no value and null (<c>null</c>), share a rendering.
    /// </summary>
    private static string? Scalar(object value) => value switch
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
        _ => null,
    };

    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(string), typeof(byte[]), typeof(char), typeof(bool), typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(decimal), typeof(double),
        typeof(float), typeof(Half), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int),
        typeof(uint), typeof(long), typeof(ulong), typeof(nint), typeof(nuint), typeof(Int128), typeof(UInt128),
    ];

    /// <summary>
    /// Declared property types the renderer accepts: a <see cref="Scalar"/> type or enum, a nullable
    /// of one, or <see cref="object"/> (whose runtime value must itself be a supported scalar).
    /// </summary>
    private static bool IsSupportedDeclaredType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return ScalarTypes.Contains(type) || type.IsEnum || type == typeof(object);
    }

    private static void RenderEntity(IModel model, object entity, string path, StringBuilder builder)
    {
        foreach (var property in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (IsNavigation(model, property.PropertyType))
                continue;
            var child = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            if (!IsSupportedDeclaredType(property.PropertyType))
                throw Unsupported(child, property.PropertyType.FullName);
            var value = property.GetValue(entity);
            var text = value is null ? "null" : Scalar(value) ?? throw Unsupported(child, value.GetType().FullName);
            builder.Append(child).Append('=').Append(text).Append('\n');
        }
    }

    private static bool IsNavigation(IModel model, Type type) =>
        model.FindEntityType(ElementType(type) ?? type) is { } entity && !entity.IsOwned();

    private static Type? ElementType(Type type) => type == typeof(string) ? null : type.GetInterfaces().Append(type)
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    private static NotSupportedException Unsupported(string path, string? type) =>
        new($"EntityScalarSnapshot: '{path}' has unsupported type {type}; add an exact encoding or compare it another way.");

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
}
