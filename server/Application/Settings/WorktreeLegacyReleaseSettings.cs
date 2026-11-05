using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0824 S4a. Automatic release of terminal desktop <c>card-task-*</c> worktrees. On by default (operator
/// decision Q-3, 2026-11-03): <see cref="Enabled"/> and <see cref="Execute"/> are true here and in
/// server/appsettings.json. Kill switch <c>Enabled=false</c>; dry run <c>Execute=false</c> (or the preview route,
/// always dry). Scalars only (T-D-1): the runner skip set is the existing <c>RunnerWorktreeRetention:Runners</c>.
/// </summary>
public sealed class WorktreeLegacyReleaseSettings
{
    public const string SectionName = "WorktreeLegacyRelease";

    /// <summary>The kill switch. Re-read before every row, so switching it off also stops a running tick.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>False files nothing and removes nothing (the tick writes would_release rows). Re-read before every row.</summary>
    public bool Execute { get; set; } = true;

    /// <summary>A terminal task is a candidate only once its CompletedAt is at least this old (operator floor 48).</summary>
    public int MinTerminalHours { get; set; } = 48;

    public int MaxReleasesPerTick { get; set; } = 25;

    public int PauseBetweenSeconds { get; set; } = 5;

    public int MaxTickMinutes { get; set; } = 20;

    /// <summary>T-D-4: a refused task is retried after this many minutes, doubling per refusal up to <see cref="RefusedRecheckMaxHours"/>.</summary>
    public int RefusedRecheckMinutes { get; set; } = 60;

    public int RefusedRecheckMaxHours { get; set; } = 24;

    /// <summary>T-D-4: after this many refusals a task is Held <c>retry_exhausted</c> and never attempted again.</summary>
    public int MaxRefusedAttempts { get; set; } = 5;

    /// <summary>
    /// CARD-1708: an executing tick deletes a <c>refs/antiphon/salvage/*</c> ref (a reflog-only commit kept when its
    /// tree was released) only once that ref's own reflog says it is at least this many days old.
    /// </summary>
    public int SalvageRefRetentionDays { get; set; } = 30;

    public string RecurringJobId { get; set; } = "antiphon:desktop-legacy-release";

    /// <summary>Five-field cron (UTC).</summary>
    public string Cron { get; set; } = "15 * * * *";

    /// <summary>
    /// T-D-1: why binding failed (a value no scalar accepts, such as a JSON null for a bool). Never bound; the range
    /// validator refuses it, so a value that cannot be read never falls back to the shipped-on default.
    /// </summary>
    internal string? BindError { get; set; }
}

public sealed class WorktreeLegacyReleaseSettingsValidator : IValidateOptions<WorktreeLegacyReleaseSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeLegacyReleaseSettings options)
    {
        const string p = WorktreeLegacyReleaseSettings.SectionName + ":";
        var failures = new List<string>();
        if (options.BindError is not null)
            failures.Add(p + " could not be bound: " + options.BindError);
        if (options.MinTerminalHours is < 48 or > 8760)
            failures.Add(p + "MinTerminalHours must be between 48 and 8760.");
        if (options.MaxReleasesPerTick is < 1 or > 100)
            failures.Add(p + "MaxReleasesPerTick must be between 1 and 100.");
        if (options.PauseBetweenSeconds is < 0 or > 300)
            failures.Add(p + "PauseBetweenSeconds must be between 0 and 300.");
        if (options.MaxTickMinutes is < 1 or > 55)
            failures.Add(p + "MaxTickMinutes must be between 1 and 55.");
        if (options.RefusedRecheckMinutes < 1)
            failures.Add(p + "RefusedRecheckMinutes must be at least 1.");
        if (options.RefusedRecheckMaxHours < 1)
            failures.Add(p + "RefusedRecheckMaxHours must be at least 1.");
        if (options.MaxRefusedAttempts is < 1 or > 50)
            failures.Add(p + "MaxRefusedAttempts must be between 1 and 50.");
        if (options.SalvageRefRetentionDays is < 1 or > 3650)
            failures.Add(p + "SalvageRefRetentionDays must be between 1 and 3650.");
        if (string.IsNullOrWhiteSpace(options.RecurringJobId))
            failures.Add(p + "RecurringJobId must not be empty.");
        if (string.IsNullOrWhiteSpace(options.Cron)
            || options.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
            failures.Add(p + "Cron must be a five-field expression.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// T-D-1 (the S1c T-D-2 pattern). Binding silently ignores an array or object written into a scalar and keeps the
/// default, which for <c>Enabled</c> and <c>Execute</c> is true, so <c>"Enabled": ["false"]</c> or <c>[]</c> would keep
/// S4a on. This validator reads the configuration itself: it enumerates the section's children (never
/// <c>Exists()</c>, which reports <c>[]</c> as absent) and refuses any child with children, a JSON null (bound as
/// an empty string) or an empty array or object (a null value), and a section that is itself an array.
/// </summary>
public sealed class WorktreeLegacyReleaseShapeValidator(IConfiguration configuration) : IValidateOptions<WorktreeLegacyReleaseSettings>
{
    public ValidateOptionsResult Validate(string? name, WorktreeLegacyReleaseSettings options)
    {
        const string p = WorktreeLegacyReleaseSettings.SectionName;
        var failures = new List<string>();
        var declared = configuration.GetChildren().Any(c => string.Equals(c.Key, p, StringComparison.OrdinalIgnoreCase));
        var section = configuration.GetSection(p);
        var children = section.GetChildren().ToList();
        if (declared && children.Count == 0 && section.Value is null or "")
            failures.Add(p + " must be an object of scalar settings, not an empty array, object or null.");
        else if (section.Value is { Length: > 0 })
            failures.Add(p + " must be an object of scalar settings, not a scalar.");
        foreach (var child in children)
        {
            if (child.Key.All(char.IsAsciiDigit))
                failures.Add(p + " must be an object of scalar settings, not an array.");
            else if (child.GetChildren().Any())
                failures.Add($"{p}:{child.Key} must be a scalar, not an array or object.");
            else if (child.Value is null)
                failures.Add($"{p}:{child.Key} must be a scalar, not an empty array or object.");
            else if (child.Value.Length == 0)
                failures.Add($"{p}:{child.Key} must be a scalar, not null or empty.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public static class WorktreeLegacyReleaseSettingsRegistration
{
    /// <summary>T-D-1: both validators with ValidateOnStart. Program.cs calls this; tests bind through it.</summary>
    public static IServiceCollection AddWorktreeLegacyReleaseSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<WorktreeLegacyReleaseSettings>, WorktreeLegacyReleaseSettingsValidator>();
        services.AddSingleton<IValidateOptions<WorktreeLegacyReleaseSettings>>(new WorktreeLegacyReleaseShapeValidator(configuration));
        services.AddOptions<WorktreeLegacyReleaseSettings>()
            .Configure(options =>
            {
                // A value the binder cannot convert, or a key it does not know (review 1dcb8d90 A-4: a misspelled kill
                // switch must not leave the shipped-on default running), throws before validation; it is recorded and
                // refused instead.
                try { configuration.GetSection(WorktreeLegacyReleaseSettings.SectionName).Bind(options, o => o.ErrorOnUnknownConfiguration = true); }
                catch (InvalidOperationException ex) { options.BindError = ex.Message; }
            })
            .ValidateOnStart();
        services.AddSingleton<IOptionsChangeTokenSource<WorktreeLegacyReleaseSettings>>(
            new ConfigurationChangeTokenSource<WorktreeLegacyReleaseSettings>(configuration));
        return services;
    }
}
