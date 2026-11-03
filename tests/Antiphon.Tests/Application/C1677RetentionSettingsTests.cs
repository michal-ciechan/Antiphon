using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1677 S1 CP-5. Retention ships off and dry-run (I-10), the validator bounds (§5), and the job never overlaps (D-4).</summary>
[Category("Integration")]
public sealed class C1677RetentionSettingsTests
{
    [Test]
    public async Task Shipped_defaults_are_off_and_dry_run()
    {
        // V-36 (G-78, G-79, G-80). Both the type's defaults and the shipped server/appsettings.json.
        var defaults = new RunnerWorktreeRetentionSettings();
        defaults.Enabled.ShouldBeFalse();
        defaults.Execute.ShouldBeFalse();
        defaults.Runners.ShouldBeEmpty();
        defaults.MinTerminalHours.ShouldBe(12);
        defaults.TempSweep.Enabled.ShouldBeFalse();
        defaults.TempSweep.Execute.ShouldBeFalse();
        defaults.TempSweep.Families["claude-scratch"].ShouldBeFalse();

        var path = Path.Combine(C1514Support.RepoRoot(), "server", "appsettings.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var section = json.RootElement.GetProperty(RunnerWorktreeRetentionSettings.SectionName);
        section.GetProperty("Enabled").GetBoolean().ShouldBeFalse();
        section.GetProperty("Execute").GetBoolean().ShouldBeFalse();
        section.GetProperty("Runners").GetArrayLength().ShouldBe(0);
        section.GetProperty("MinTerminalHours").GetInt32().ShouldBe(12);
        var temp = section.GetProperty("TempSweep");
        temp.GetProperty("Enabled").GetBoolean().ShouldBeFalse();
        temp.GetProperty("Execute").GetBoolean().ShouldBeFalse();
        temp.GetProperty("Families").GetProperty("claude-scratch").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Arguments("MinTerminalHours", "0", false)]
    [Arguments("MinTerminalHours", "1", true)]
    [Arguments("MaxRemovalsPerTick", "0", false)]
    [Arguments("MaxRemovalsPerTick", "1", true)]
    [Arguments("MaxRemovalsPerTick", "100", true)]
    [Arguments("MaxRemovalsPerTick", "101", false)]
    [Arguments("RetiringRoot", "work/r", false)]
    [Arguments("RetiringRoot", "/work/worktrees-retiring", true)]
    public async Task Validator_bounds(string field, string value, bool valid)
    {
        // V-36b (G-81, G-82, G-83).
        var settings = new RunnerWorktreeRetentionSettings();
        switch (field)
        {
            case "MinTerminalHours": settings.MinTerminalHours = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "MaxRemovalsPerTick": settings.MaxRemovalsPerTick = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            default: settings.RetiringRoot = value; break;
        }

        var result = new RunnerWorktreeRetentionSettingsValidator().Validate(null, settings);
        result.Succeeded.ShouldBe(valid, result.FailureMessage);
        if (!valid)
            result.FailureMessage.ShouldContain(field);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Job_is_not_concurrent()
    {
        // V-38 (G-84).
        var method = typeof(RunnerWorktreeRetentionJob).GetMethod(nameof(RunnerWorktreeRetentionJob.RunAsync))!;
        method.GetCustomAttribute<DisableConcurrentExecutionAttribute>().ShouldNotBeNull();
        method.GetCustomAttribute<AutomaticRetryAttribute>().ShouldNotBeNull().Attempts.ShouldBe(0);
        await Task.CompletedTask;
    }
}
