using System.Collections.Concurrent;
using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1704 S1 (T-D-7): <c>Landing:GitBackend</c>, a scalar in its own options type. It is not a
/// <see cref="LandingSettings"/> property, so an invalid hot reload of it never makes a
/// <c>LandingSettings</c> read throw (the branch-deletion kill switch reads that type).
/// </summary>
public sealed class LandGitBackendSettings
{
    public const string SectionName = "Landing";
    public const string Key = "GitBackend";
    public const string Path = SectionName + ":" + Key;

    /// <summary><c>Cli</c> (default), <c>LibGit2</c> or <c>Compare</c>; ordinal and untrimmed. S1 runs <c>Cli</c> for all three.</summary>
    public string GitBackend { get; set; } = LandGitBackendNames.Cli;
}

/// <summary>The value must be exactly one of the backend names.</summary>
public sealed class LandGitBackendSettingsValidator : IValidateOptions<LandGitBackendSettings>
{
    public ValidateOptionsResult Validate(string? name, LandGitBackendSettings options) =>
        LandGitBackendNames.IsKnown(options.GitBackend)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{LandGitBackendSettings.Path} must be exactly Cli, LibGit2 or Compare, not '{options.GitBackend}' (git_backend_invalid).");
}

/// <summary>
/// The CARD-0824 S1c lesson for one key: the binder silently ignores an array or object written into a scalar and
/// keeps the default, and <c>Exists()</c> reports <c>[]</c> as absent. This validator reads the configuration itself,
/// enumerating the section's children, and refuses a <c>GitBackend</c> child that has children, a null value (an
/// empty array or object) or an empty value (a JSON null or <c>""</c>).
/// </summary>
public sealed class LandGitBackendShapeValidator(IConfiguration configuration) : IValidateOptions<LandGitBackendSettings>
{
    public ValidateOptionsResult Validate(string? name, LandGitBackendSettings options)
    {
        var child = configuration.GetSection(LandGitBackendSettings.SectionName).GetChildren()
            .FirstOrDefault(c => string.Equals(c.Key, LandGitBackendSettings.Key, StringComparison.OrdinalIgnoreCase));
        if (child is null) return ValidateOptionsResult.Success;
        if (child.GetChildren().Any())
            return Fail("must be a scalar, not an array or object");
        if (child.Value is null)
            return Fail("must be a scalar, not an empty array or object");
        if (child.Value.Length == 0)
            return Fail("must be a scalar, not null or empty");
        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult Fail(string why) =>
        ValidateOptionsResult.Fail($"{LandGitBackendSettings.Path} {why} (git_backend_invalid).");
}

/// <summary>
/// CARD-1704 S1 (T-D-8): the effective backend for one land operation scope, read once per
/// <see cref="ILandingGit.BeginOperationScope"/>. It never throws: an invalid value (including an invalid hot reload)
/// is <c>Cli</c> with one Warning per distinct value, and in S1 <c>LibGit2</c> and <c>Compare</c> are not built, so
/// they are <c>Cli</c> with one <c>git_backend_not_built</c> Warning per value.
/// </summary>
public sealed class LandGitBackend(IOptionsMonitor<LandGitBackendSettings> monitor, ILogger<LandGitBackend> logger)
{
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);

    public string Resolve()
    {
        string configured;
        try { configured = monitor.CurrentValue.GitBackend; }
        catch (OptionsValidationException ex)
        {
            var reason = string.Join(" ", ex.Failures);
            if (_warned.TryAdd("invalid\0" + reason, 0))
                logger.LogWarning("git_backend_invalid {Reason}; the land git backend is Cli", reason);
            return LandGitBackendNames.Cli;
        }
        if (configured == LandGitBackendNames.Cli) return LandGitBackendNames.Cli;
        if (!LandGitBackendNames.IsKnown(configured))
        {
            if (_warned.TryAdd("invalid\0" + configured, 0))
                logger.LogWarning("git_backend_invalid {Setting}='{Value}'; the land git backend is Cli",
                    LandGitBackendSettings.Path, configured);
            return LandGitBackendNames.Cli;
        }
        if (_warned.TryAdd("unbuilt\0" + configured, 0))
            logger.LogWarning("git_backend_not_built {Setting}='{Value}' is not built yet; the land git backend is Cli",
                LandGitBackendSettings.Path, configured);
        return LandGitBackendNames.Cli;
    }
}

public static class LandGitBackendSettingsRegistration
{
    /// <summary>T-D-7: binds <c>Landing:GitBackend</c> alone, both validators, ValidateOnStart and reload. Program.cs calls
    /// this; tests bind through it.</summary>
    public static IServiceCollection AddLandGitBackendSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<LandGitBackendSettings>, LandGitBackendSettingsValidator>();
        services.AddSingleton<IValidateOptions<LandGitBackendSettings>>(new LandGitBackendShapeValidator(configuration));
        services.AddOptions<LandGitBackendSettings>()
            .Configure(options =>
            {
                // Only the one key: the rest of Landing belongs to LandingSettings. An empty array or object has no
                // value and keeps the default here; the shape validator refuses it.
                if (configuration[LandGitBackendSettings.Path] is { } value) options.GitBackend = value;
            })
            .ValidateOnStart();
        services.AddSingleton<IOptionsChangeTokenSource<LandGitBackendSettings>>(
            new ConfigurationChangeTokenSource<LandGitBackendSettings>(configuration));
        return services;
    }
}
