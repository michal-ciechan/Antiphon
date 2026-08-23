using System.Text.Json;

namespace Antiphon.Messaging.Client;

/// <summary>
/// Forwards to <see cref="Antiphon.Messaging.MessagingJson"/>. Kept for one minor version so
/// existing <c>using Antiphon.Messaging.Client</c> consumers keep compiling.
/// </summary>
[Obsolete("Use Antiphon.Messaging.MessagingJson. This forwarding type will be removed in the next major version.")]
public static class MessagingJson
{
    public static JsonSerializerOptions Options => global::Antiphon.Messaging.MessagingJson.Options;
}
