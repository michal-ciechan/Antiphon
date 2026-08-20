using System.Text.RegularExpressions;
using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What an API key may be called, and how big its value may be (CARD-0106 S1).
///
/// <para>The name charset is deliberately the same one <c>{{key:NAME}}</c> accepts: a key that
/// cannot be spelled inside a placeholder is a key nothing can ever reference, and a name allowed
/// here but rejected by the placeholder regex would store fine and then fail every launch.</para>
/// </summary>
public static class ApiKeyNaming
{
    /// <summary>The one character class shared by stored names and placeholder tokens.</summary>
    public const string CharacterClass = "[A-Za-z0-9_.-]";

    public const int MaxNameLength = 128;

    /// <summary>
    /// Same ceiling <c>AgentTuiLaunchResolver</c> enforces on a managed environment value, enforced
    /// at write time AND after decrypt — a value that cannot be exported is a launch that fails
    /// later for a reason the operator could have been told about at the moment they typed it.
    /// </summary>
    public const int MaxValueLength = 4000;

    private static readonly Regex NameRegex = new(
        $"^{CharacterClass}+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && NameRegex.IsMatch(name);

    /// <summary>Validates and returns the name, or throws 422 naming the rule it broke.</summary>
    public static string Validate(string? name, string field = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(field, "An API key name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationException(
                field,
                $"An API key name may be at most {MaxNameLength} characters.");
        }
        if (!NameRegex.IsMatch(trimmed))
        {
            throw new ValidationException(
                field,
                "An API key name may contain only letters, digits, '_', '.' and '-' — "
                + "the same characters a {{key:NAME}} placeholder can spell.");
        }
        return trimmed;
    }
}
