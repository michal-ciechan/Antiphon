using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Dtos;

// Explicit delegatable alias. An init-only property, not a new positional parameter,
// so existing CreateAgentTaskRequest constructors stay source-compatible.
public sealed partial record CreateAgentTaskRequest
{
    /// <summary>
    /// Optional explicit delegatable alias, for example <c>gpt-6.2-astra</c>.
    /// Null keeps the tier ladder. <c>*</c>, an unknown alias, or a slug the resolved
    /// kind does not delegate is 422. Omitted on the wire when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }
}
