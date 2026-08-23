using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Messaging;

/// <summary>
/// String-enum converter that writes names exactly as <see cref="JsonStringEnumConverter"/> does
/// (the live wire format) and on read maps an unknown name to the enum's
/// <see cref="UnknownValueAttribute"/> sentinel instead of throwing.
/// </summary>
public sealed class TolerantStringEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(Converter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly T Sentinel = ResolveSentinel();

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var name = reader.GetString();
                    if (name is not null && Enum.TryParse(name, ignoreCase: false, out T parsed))
                        return parsed;
                    return Sentinel;
                }
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var number))
                        return (T)Enum.ToObject(typeof(T), number);
                    throw new JsonException($"Unexpected numeric value for enum {typeof(T).Name}.");
                default:
                    throw new JsonException($"Unexpected token {reader.TokenType} for enum {typeof(T).Name}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var name = Enum.GetName(value);
            if (name is not null)
                writer.WriteStringValue(name);
            else
                writer.WriteNumberValue(Convert.ToInt32(value));
        }

        private static T ResolveSentinel()
        {
            var attr = typeof(T).GetCustomAttribute<UnknownValueAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(T).FullName} must declare [UnknownValue] so unknown wire names have a sentinel.");
            if (!Enum.TryParse(attr.MemberName, ignoreCase: false, out T sentinel))
            {
                throw new InvalidOperationException(
                    $"{typeof(T).FullName} [UnknownValue(\"{attr.MemberName}\")] does not name a member of the enum.");
            }

            return sentinel;
        }
    }
}
