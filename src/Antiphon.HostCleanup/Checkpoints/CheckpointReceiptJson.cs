using Antiphon.HostCleanup.Checkpoints;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Antiphon.Checkpoints;

/// <summary>A new receipt property that older serialized receipts may omit.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CheckpointOptionalAttribute : Attribute { }

/// <summary>Validate serialized facts and deserialized shapes before any verb uses them.</summary>
public static class CheckpointReceiptJson
{
    public static T Read<T>(string path, JsonSerializerOptions options) where T : class =>
        Read<T>(CheckpointCleanupDefaults.Context, path, options);

    public static T Read<T>(CheckpointCleanupContext context, string path, JsonSerializerOptions options,
        CancellationToken cancellationToken = default) where T : class =>
        Parse<T>(ReceiptFile.ReadText(context, path, cancellationToken), options);

    public static T Parse<T>(string text, JsonSerializerOptions options) where T : class
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            foreach (var property in info.Properties)
                if (property.Set is not null && !property.PropertyType.IsValueType
                    && property.AttributeProvider?.IsDefined(typeof(CheckpointOptionalAttribute), inherit: true) != true)
                    property.IsRequired = true;
        });
        var strict = new JsonSerializerOptions(options)
        { RespectNullableAnnotations = true, TypeInfoResolver = resolver };
        T value;
        try { value = JsonSerializer.Deserialize<T>(text, strict) ?? throw new JsonException("receipt-shape: empty object"); }
        catch (JsonException ex) when (ex.Path?.EndsWith(".id", StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains("'id'", StringComparison.Ordinal))
        { throw new JsonException("receipt-identity: invalid id at " + ex.Path, ex); }
        Validate(value);
        return value;
    }

    public static void Validate(object value)
    {
        var nullable = new NullabilityInfoContext();
        Walk(value, nullable, "$", 0);
    }

    private static void Walk(object value, NullabilityInfoContext nullable, string path, int depth)
    {
        if (depth > 64) throw new JsonException("receipt-shape: depth limit");
        if (value is string || value.GetType().IsValueType) return;
        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is null || item is string text && string.IsNullOrWhiteSpace(text))
                    throw new JsonException("receipt-shape: invalid collection element at " + path);
                Walk(item, nullable, path + "[]", depth + 1);
            }
            return;
        }
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod is null || property.GetIndexParameters().Length != 0) continue;
            var item = property.GetValue(value);
            var name = path + "." + property.Name;
            if (property.Name == "Id" && (item is null || item is string identity && string.IsNullOrWhiteSpace(identity)))
                throw new JsonException("receipt-identity: empty " + name);
            if (item is null)
            {
                if (nullable.Create(property).ReadState == NullabilityState.NotNull)
                    throw new JsonException("receipt-shape: null " + name);
                continue;
            }
            if (item is string text && property.Name is "RunId" or "ToolOwner" or "Phase" or "Verdict" or "State" or "BuildSource" or "CaptureStatus"
                && string.IsNullOrWhiteSpace(text)) throw new JsonException("receipt-shape: empty " + name);
            Walk(item, nullable, name, depth + 1);
        }
    }
}
