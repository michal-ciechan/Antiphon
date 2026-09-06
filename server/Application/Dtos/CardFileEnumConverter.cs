using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Preserve semantic invalid values for aggregated 422 validation; reject wrong shapes.</summary>
public abstract class CardFileEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.Number) return (T)Enum.ToObject(typeof(T), -1);
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Invalid enum shape.");
        var text = reader.GetString();
        return Enum.GetNames<T>().Any(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase))
            ? Enum.Parse<T>(text!, true) : (T)Enum.ToObject(typeof(T), -1);
    }
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.ToString());
    }
}
public sealed class CardFileVisibilityConverter : CardFileEnumConverter<CardFileVisibility>;
public sealed class RepositoryVisibilityConverter : CardFileEnumConverter<RepositoryVisibility>;
