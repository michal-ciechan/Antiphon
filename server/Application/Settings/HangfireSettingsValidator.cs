using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class HangfireSettingsValidator : IValidateOptions<HangfireSettings>
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-z_][a-z0-9_]{0,62}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, HangfireSettings options)
    {
        if (options.HistoryRetentionDays <= 0)
            return ValidateOptionsResult.Fail("Hangfire:HistoryRetentionDays must be positive.");

        if (options.Storage is not (HangfireSettings.InMemoryStorageName or HangfireSettings.PostgreSqlStorageName))
            return ValidateOptionsResult.Fail("Hangfire:Storage must be InMemory or PostgreSql.");

        if (string.IsNullOrWhiteSpace(options.SchemaName) || !SchemaNamePattern.IsMatch(options.SchemaName))
            return ValidateOptionsResult.Fail("Hangfire:SchemaName must be a lowercase PostgreSQL identifier.");

        if (string.Equals(options.SchemaName, "public", StringComparison.Ordinal))
            return ValidateOptionsResult.Fail("Hangfire:SchemaName must be a dedicated schema, not public.");

        return ValidateOptionsResult.Success;
    }
}
