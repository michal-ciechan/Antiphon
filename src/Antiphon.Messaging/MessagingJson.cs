using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Messaging;

/// <summary>
/// Canonical wire JSON for the Kafka topics — camelCase + string enums. One options instance
/// for every producer and consumer so the bus cannot drift.
/// </summary>
public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new TolerantStringEnumConverterFactory() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
