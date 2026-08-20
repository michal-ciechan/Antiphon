using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reads and writes <see cref="Agent.LaunchEnvJson"/> (CARD-0106 S2).
///
/// <para>Parsing NEVER throws: a row with unreadable JSON reads as an empty environment and the
/// agent launches without it. The alternative — failing every launch of an agent whose column got
/// corrupted — would take a working agent permanently offline over a field it may not even use, and
/// there is nothing an operator could do about it from the terminal that is now dead. Writes are
/// validated instead, which is where a malformed value can actually be refused to somebody's face.</para>
/// </summary>
public static class AgentLaunchEnv
{
    public const int MaxEntries = 64;
    public const int MaxNameLength = 200;

    public static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is null || parsed.Count == 0)
                return Empty;
            return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public static IReadOnlyDictionary<string, string> ParseForAgent(Agent? agent) =>
        agent is null ? Empty : Parse(agent.LaunchEnvJson);

    public static string Serialize(IReadOnlyDictionary<string, string>? env) =>
        env is null || env.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(env.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));

    /// <summary>
    /// Validates an operator-supplied launch environment and returns it trimmed. Throws 422 naming
    /// the offending variable.
    ///
    /// <para>A placeholder in a NAME is refused: only VALUES are a resolution surface, and a
    /// variable literally called <c>{{key:X}}</c> would be exported as-is and then refused by the
    /// launch tripwire, which is a worse place to find out.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(
        IReadOnlyDictionary<string, string>? env,
        string field = "launchEnv")
    {
        if (env is null || env.Count == 0)
            return Empty;

        if (env.Count > MaxEntries)
            throw new ValidationException(field, $"At most {MaxEntries} environment entries are supported.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawName, rawValue) in env)
        {
            var name = rawName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ValidationException(field, "An environment variable name cannot be empty.");
            if (name.Length > MaxNameLength)
            {
                throw new ValidationException(
                    field,
                    $"Environment variable names may be at most {MaxNameLength} characters.");
            }
            if (name.Contains('=', StringComparison.Ordinal)
                || name.Contains('\0', StringComparison.Ordinal))
            {
                throw new ValidationException(
                    field,
                    $"Environment variable name '{name}' may not contain '=' or a null character.");
            }
            if (ApiKeyPlaceholder.ContainsMarker(name))
            {
                throw new ValidationException(
                    field,
                    "An API key placeholder is only legal in a VALUE, never in a variable name.");
            }
            if (result.ContainsKey(name))
                throw new ValidationException(field, $"Environment variable '{name}' is listed twice.");

            var value = rawValue ?? string.Empty;
            if (value.Length > ApiKeyNaming.MaxValueLength)
            {
                throw new ValidationException(
                    field,
                    $"Environment variable '{name}' is {value.Length} characters; the maximum is "
                    + $"{ApiKeyNaming.MaxValueLength}.");
            }
            if (ApiKeyPlaceholder.HasMalformedToken(value))
            {
                throw new ValidationException(
                    field,
                    $"Environment variable '{name}' contains a malformed API key placeholder. The "
                    + "form is {{key:NAME}}, where NAME may contain only letters, digits, '_', '.' "
                    + "and '-'.");
            }

            result[name] = value;
        }

        return result;
    }
}
