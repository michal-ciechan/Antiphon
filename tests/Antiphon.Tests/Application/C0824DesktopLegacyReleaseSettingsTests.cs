using System.Reflection;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S4a test design, class LS: the scalar-only, shipped-on settings (T-D-1), the fail-closed reload, the
/// job attributes and the end-anchored leaf pattern. No database, no git.
/// </summary>
[Category("Unit")]
[Timeout(15_000)]
public sealed class C0824DesktopLegacyReleaseSettingsTests
{
    /// <summary>V-1 (operator Q-3): the class defaults and the repository's shipped file are on.</summary>
    [Test]
    public async Task Defaults_and_shipped_file_are_on()
    {
        var defaults = new WorktreeLegacyReleaseSettings();
        defaults.Enabled.ShouldBeTrue();
        defaults.Execute.ShouldBeTrue();
        defaults.MinTerminalHours.ShouldBe(48);
        defaults.MaxReleasesPerTick.ShouldBe(25);
        defaults.PauseBetweenSeconds.ShouldBe(5);
        defaults.MaxTickMinutes.ShouldBe(20);
        defaults.RefusedRecheckMinutes.ShouldBe(60);
        defaults.RefusedRecheckMaxHours.ShouldBe(24);
        defaults.MaxRefusedAttempts.ShouldBe(5);
        defaults.RecurringJobId.ShouldBe("antiphon:desktop-legacy-release");
        defaults.Cron.ShouldBe("15 * * * *");

        var shipped = Path.Combine(RepositoryRoot(), "server", "appsettings.json");
        File.Exists(shipped).ShouldBeTrue(shipped);
        var configuration = new ConfigurationBuilder().AddJsonFile(shipped, optional: false, reloadOnChange: false).Build();
        await using var provider = Provider(configuration);
        var bound = provider.GetRequiredService<IOptions<WorktreeLegacyReleaseSettings>>().Value;
        bound.Enabled.ShouldBeTrue();
        bound.Execute.ShouldBeTrue();
        bound.MinTerminalHours.ShouldBe(48);
        bound.MaxReleasesPerTick.ShouldBe(25);
        bound.Cron.ShouldBe("15 * * * *");
    }

    /// <summary>V-2/G-60: each out-of-range value fails Validate naming its key; the paired valid value passes.</summary>
    [Test]
    [Arguments("MinTerminalHours", "47", "48")]
    [Arguments("MinTerminalHours", "0", "48")]
    [Arguments("MinTerminalHours", "8761", "8760")]
    [Arguments("MaxReleasesPerTick", "0", "25")]
    [Arguments("MaxReleasesPerTick", "101", "100")]
    [Arguments("PauseBetweenSeconds", "-1", "5")]
    [Arguments("MaxTickMinutes", "0", "20")]
    [Arguments("MaxTickMinutes", "56", "55")]
    [Arguments("Cron", "15 * * *", "15 * * * *")]
    [Arguments("RecurringJobId", "", "antiphon:desktop-legacy-release")]
    [Arguments("RefusedRecheckMinutes", "0", "60")]
    [Arguments("RefusedRecheckMaxHours", "0", "24")]
    [Arguments("MaxRefusedAttempts", "0", "5")]
    public async Task Out_of_range_values_are_refused(string key, string value, string valid)
    {
        var validator = new WorktreeLegacyReleaseSettingsValidator();
        var refused = validator.Validate(null, With(key, value));
        refused.Failed.ShouldBeTrue($"{key}={value} must be refused");
        refused.FailureMessage.ShouldContain("WorktreeLegacyRelease:" + key);
        validator.Validate(null, With(key, valid)).Succeeded.ShouldBeTrue($"{key}={valid} is valid");
        await Task.CompletedTask;
    }

    /// <summary>
    /// V-3/G-61/G-62: through the production registration, an array, object or null in a scalar (or the section as
    /// an array) is refused naming the key, because binding would otherwise keep the default true (S1c, measured).
    /// Review 1dcb8d90 A-1: a scalar the binder cannot convert ("no", "0") reaches only the BindError refusal. A-4: a
    /// misspelled key is refused, so a mistyped kill switch never leaves the shipped-on default running.
    /// </summary>
    [Test]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": []}}", "WorktreeLegacyRelease:Enabled", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": [\"false\"]}}", "WorktreeLegacyRelease:Enabled", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Execute\": {}}}", "WorktreeLegacyRelease:Execute", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"MaxReleasesPerTick\": [25]}}", "WorktreeLegacyRelease:MaxReleasesPerTick", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": null}}", "WorktreeLegacyRelease:Enabled", null)]
    [Arguments("{\"WorktreeLegacyRelease\": []}", "WorktreeLegacyRelease", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": \"no\"}}", "WorktreeLegacyRelease:Enabled", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Execute\": \"0\"}}", "WorktreeLegacyRelease:Execute", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabeld\": false}}", "Enabeld", null)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": false}}", null, false)]
    [Arguments("{\"WorktreeLegacyRelease\": {\"Enabled\": \"false\"}}", null, false)]
    public async Task Non_scalar_values_are_refused(string json, string? refusedKey, bool? enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "c0824-s4a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "appsettings.json");
            await File.WriteAllTextAsync(file, json);
            var configuration = new ConfigurationBuilder().AddJsonFile(file, optional: false, reloadOnChange: false).Build();
            await using var provider = Provider(configuration);
            if (refusedKey is null)
            {
                provider.GetRequiredService<IOptions<WorktreeLegacyReleaseSettings>>().Value.Enabled.ShouldBe(enabled!.Value);
                return;
            }
            var refused = Should.Throw<OptionsValidationException>(() =>
                _ = provider.GetRequiredService<IOptions<WorktreeLegacyReleaseSettings>>().Value);
            refused.Message.ShouldContain(refusedKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>V-4/G-63: an invalid hot reload reads as disabled (never the last good value), then recovers.</summary>
    [Test]
    public async Task Invalid_reload_fails_closed_then_recovers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "c0824-s4a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "appsettings.json");
            await File.WriteAllTextAsync(file, "{\"WorktreeLegacyRelease\": {\"Enabled\": true}}");
            var configuration = new ConfigurationBuilder().AddJsonFile(file, optional: false, reloadOnChange: true).Build();
            await using var provider = Provider(configuration);
            var monitor = provider.GetRequiredService<IOptionsMonitor<WorktreeLegacyReleaseSettings>>();
            var logs = new WarningLog();

            DesktopLegacyReleasePolicyService.ReadSettings(monitor, logs).Enabled.ShouldBeTrue();

            await File.WriteAllTextAsync(file, "{\"WorktreeLegacyRelease\": {\"Enabled\": []}}");
            var invalid = await PollAsync(() => DesktopLegacyReleasePolicyService.ReadSettings(monitor, logs),
                r => r.Reason == "settings_invalid");
            invalid.Enabled.ShouldBeFalse();
            logs.Warnings.Count(w => w.Contains("WorktreeLegacyRelease", StringComparison.Ordinal)).ShouldBe(1);

            await File.WriteAllTextAsync(file, "{\"WorktreeLegacyRelease\": {\"Enabled\": false}}");
            var disabled = await PollAsync(() => DesktopLegacyReleasePolicyService.ReadSettings(monitor), r => r.Reason == "disabled");
            disabled.Enabled.ShouldBeFalse();

            await File.WriteAllTextAsync(file, "{\"WorktreeLegacyRelease\": {\"Enabled\": true}}");
            var enabled = await PollAsync(() => DesktopLegacyReleasePolicyService.ReadSettings(monitor), r => r.Enabled);
            enabled.Reason.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>V-5/G-64: the job never overlaps itself and never retries automatically.</summary>
    [Test]
    public async Task Job_never_runs_concurrently()
    {
        var method = typeof(DesktopLegacyReleasePolicyJob).GetMethod(nameof(DesktopLegacyReleasePolicyJob.RunAsync))!;
        method.GetCustomAttribute<DisableConcurrentExecutionAttribute>().ShouldNotBeNull();
        method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts.ShouldBe(0);
        await Task.CompletedTask;
    }

    /// <summary>V-27/G-36: the pure <c>\z</c> witness (no control-character gate in front of it).</summary>
    [Test]
    [Arguments("", true)]
    [Arguments("\n", false)]
    public async Task Leaf_pattern_is_end_anchored(string suffix, bool expected)
    {
        var id8 = Guid.NewGuid().ToString("N")[..8];
        DesktopLegacyReleasePolicyService.IsLegacyLeaf("card-task-" + id8 + suffix, id8).ShouldBe(expected);
        await Task.CompletedTask;
    }

    private static ServiceProvider Provider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddWorktreeLegacyReleaseSettings(configuration);
        return services.BuildServiceProvider();
    }

    private static WorktreeLegacyReleaseSettings With(string key, string value)
    {
        var settings = new WorktreeLegacyReleaseSettings();
        var property = typeof(WorktreeLegacyReleaseSettings).GetProperty(key)!;
        property.SetValue(settings, property.PropertyType == typeof(int) ? int.Parse(value) : value);
        return settings;
    }

    private static async Task<DesktopLegacyReleasePolicyService.SettingsRead> PollAsync(
        Func<DesktopLegacyReleasePolicyService.SettingsRead> read, Func<DesktopLegacyReleasePolicyService.SettingsRead, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var current = read();
            if (done(current) || DateTime.UtcNow > deadline) return current;
            await Task.Delay(100);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }

    private sealed class WarningLog : ILogger
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
