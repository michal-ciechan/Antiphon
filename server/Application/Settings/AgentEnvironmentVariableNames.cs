using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

public static class AgentEnvironmentVariableNames
{
    public static StringComparer ForCurrentPlatform() => ForPlatform(
        OperatingSystem.IsWindows()
            ? AgentTuiPlatform.Windows
            : OperatingSystem.IsLinux()
                ? AgentTuiPlatform.Linux
                : OperatingSystem.IsMacOS()
                    ? AgentTuiPlatform.MacOS
                    : AgentTuiPlatform.Other);

    public static StringComparer ForPlatform(AgentTuiPlatform platform) =>
        platform == AgentTuiPlatform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static AgentEnvironmentClassification Classify(
        AgentDefinition definition,
        string definitionPath)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var comparer = ForCurrentPlatform();
        var failures = new List<string>();
        var environment = definition.Env;
        var secretNames = definition.SecretEnvironmentNames;
        var ordinaryNames = definition.NonSecretEnvironmentNames;

        if (environment is null)
            failures.Add($"{definitionPath}:Env must be configured.");
        if (secretNames is null)
            failures.Add($"{definitionPath}:SecretEnvironmentNames must be configured.");
        if (ordinaryNames is null)
            failures.Add($"{definitionPath}:NonSecretEnvironmentNames must be configured.");
        if (environment is null || secretNames is null || ordinaryNames is null)
            return new AgentEnvironmentClassification(
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                failures);

        AddDuplicateFailures(secretNames, "SecretEnvironmentNames", definitionPath, comparer, failures);
        AddDuplicateFailures(ordinaryNames, "NonSecretEnvironmentNames", definitionPath, comparer, failures);

        var environmentNames = environment.Keys.ToArray();
        foreach (var duplicate in environmentNames
                     .GroupBy(name => name, comparer)
                     .Where(group => group.Count() > 1))
        {
            failures.Add(
                $"{definitionPath}:Env contains duplicate host-equivalent environment name '{duplicate.Key}'.");
        }

        foreach (var name in secretNames.Concat(ordinaryNames))
        {
            if (!IsValid(name))
                failures.Add($"{definitionPath} contains invalid environment name '{name}'.");
            if (!environmentNames.Contains(name, comparer))
                failures.Add($"{definitionPath} classifies absent environment name '{name}'.");
        }

        foreach (var overlap in secretNames.Intersect(ordinaryNames, comparer))
        {
            failures.Add(
                $"{definitionPath} classifies environment name '{overlap}' as both secret and ordinary.");
        }

        var secrets = new Dictionary<string, string>(comparer);
        var ordinary = new Dictionary<string, string>(comparer);
        foreach (var (name, value) in environment)
        {
            if (!IsValid(name))
            {
                failures.Add($"{definitionPath}:Env contains invalid environment name '{name}'.");
                continue;
            }

            if (secrets.ContainsKey(name) || ordinary.ContainsKey(name))
                continue;

            var explicitlySecret = secretNames.Contains(name, comparer);
            var explicitlyOrdinary = ordinaryNames.Contains(name, comparer);
            if (explicitlySecret && explicitlyOrdinary)
                continue;

            if (explicitlySecret || (!explicitlyOrdinary && LooksSecret(name)))
            {
                secrets[name] = value;
                continue;
            }

            if (explicitlyOrdinary)
            {
                if (value is null || value.Length > 4000)
                {
                    failures.Add(
                        $"{definitionPath}:Env ordinary entry '{name}' must have a bounded value.");
                    continue;
                }
                ordinary[name] = value;
                continue;
            }

            failures.Add(
                $"{definitionPath}:Env entry '{name}' is not explicitly classified and is not recognizably secret.");
        }

        return new AgentEnvironmentClassification(ordinary, secrets, failures);
    }

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 200
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static bool LooksSecret(string name) =>
        name.Contains("KEY", StringComparison.OrdinalIgnoreCase)
        || name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
        || name.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
        || name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase);

    private static void AddDuplicateFailures(
        IEnumerable<string> names,
        string classificationName,
        string definitionPath,
        StringComparer comparer,
        ICollection<string> failures)
    {
        foreach (var duplicate in names
                     .GroupBy(name => name, comparer)
                     .Where(group => group.Count() > 1))
        {
            failures.Add(
                $"{definitionPath}:{classificationName} contains duplicate host-equivalent environment name "
                + $"'{duplicate.Key}'.");
        }
    }
}

public sealed record AgentEnvironmentClassification(
    IReadOnlyDictionary<string, string> Ordinary,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<string> Failures);
