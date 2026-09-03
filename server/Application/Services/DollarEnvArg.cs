using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0345: a whole argument of the form <c>$env:NAME</c> or <c>${env:NAME}</c> resolves to
/// the env value when the name is present (exact case first, then case-insensitive — Windows
/// env names are case-insensitive). Anything else, including a token whose name the env does
/// not carry, is left verbatim.
/// Keep in lockstep with <c>HerdrLaunchScript.TryResolveEnvToken</c> (CARD-0341); do not share a type.
/// </summary>
internal static partial class DollarEnvArg
{
    public static string Expand(string argument, IReadOnlyDictionary<string, string> env)
        => TryResolve(argument, env, out var value) ? value : argument;

    internal static bool TryResolve(
        string argument,
        IReadOnlyDictionary<string, string> env,
        out string value)
    {
        value = "";
        if (!TryReadName(argument, out var name))
            return false;

        if (env.TryGetValue(name, out var exact))
        {
            value = exact ?? "";
            return true;
        }

        foreach (var (key, candidate) in env)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate ?? "";
                return true;
            }
        }

        return false;
    }

    /// <summary>The NAME of a whole-argument <c>$env:NAME</c> / <c>${env:NAME}</c> token; false otherwise.</summary>
    internal static bool TryReadName(string argument, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(argument))
            return false;

        var match = EnvToken().Match(argument);
        if (!match.Success)
            return false;

        name = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["bare"].Value;
        return name.Length > 0;
    }

    [GeneratedRegex(@"^\$(?:\{[Ee][Nn][Vv]:(?<braced>[^}]+)\}|[Ee][Nn][Vv]:(?<bare>[A-Za-z_][A-Za-z0-9_]*))$")]
    private static partial Regex EnvToken();
}
