using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Antiphon.Checkpoints.Coverage;

namespace Antiphon.Checkpoints;

/// <summary>Read receipts only through a bounded, regular-file, verified handle.</summary>
public static class ReceiptFile
{
    public static byte[] ReadBytes(string path)
    {
        try
        {
            // Windows metadata lookup refuses pipe/device namespaces before CreateFile.
            // Linux ConfinedFileReader uses statx(AT_SYMLINK_NOFOLLOW), O_NOFOLLOW,
            // O_NONBLOCK and then statx on the retained descriptor before reading.
            if (OperatingSystem.IsWindows() &&
                (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("receipt-nonregular: " + path);
            return ConfinedFileReader.ReadBytes(Path.GetDirectoryName(Path.GetFullPath(path))!, path, 64 * 1024 * 1024);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        { throw new IOException("receipt-read-refused: " + path + ": " + ex.Message, ex); }
    }

    public static string ReadText(string path)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(path)));
        return reader.ReadToEnd();
    }
}

/// <summary>Validate serialized facts and deserialized shapes before any verb uses them.</summary>
public static class ReceiptJson
{
    public static T Read<T>(string path, JsonSerializerOptions options) where T : class =>
        Parse<T>(ReceiptFile.ReadText(path), options);

    public static T Parse<T>(string text, JsonSerializerOptions options) where T : class
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            foreach (var property in info.Properties)
                if (property.Set is not null && !property.PropertyType.IsValueType)
                    property.IsRequired = true;
        });
        var strict = new JsonSerializerOptions(options)
        { RespectNullableAnnotations = true, TypeInfoResolver = resolver };
        var value = JsonSerializer.Deserialize<T>(text, strict) ?? throw new JsonException("receipt-shape: empty object");
        Validate(value);
        return value;
    }

    public static void Validate(object value)
    {
        var nullable = new NullabilityInfoContext();
        Walk(value, nullable, "$", 0);
        if (value is ReportModel report)
        {
            if (string.IsNullOrWhiteSpace(report.MarkdownSha256)) throw new JsonException("receipt-shape: markdownSha256");
            if (report.OutputNames.Count > 0 && report.OutputPaths.Count == 0)
                throw new JsonException("receipt-shape: outputPaths required for outputNames");
        }
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
