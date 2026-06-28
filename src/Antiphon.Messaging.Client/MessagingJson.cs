using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Messaging.Client;

/// <summary>
/// Canonical wire JSON for the Kafka topics — camelCase + string enums, identical to what
/// <c>Antiphon.Messaging.Service</c> uses. Consumers and the bridge must agree on this, so it lives in one place.
/// </summary>
public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
