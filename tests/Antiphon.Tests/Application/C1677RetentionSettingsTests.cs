using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1677 S1 CP-5. Retention ships on for server2 with Execute (operator decision 2026-11-03, replacing I-10's
/// off and dry-run), the validator bounds (§5), and the job never overlaps (D-4).
/// </summary>
[Category("Integration")]
public sealed class C1677RetentionSettingsTests
{
    [Test]
    public async Task Shipped_defaults_are_on_for_server2_with_execute()
    {
        // V-36 (G-78, G-79, G-80), flipped by the operator on 2026-11-03 ("Enable this on by default"). Both the
        // type's defaults and the shipped server/appsettings.json. Runners stays empty in the type: binding
        // appends to a list initializer, so a C# default could never be emptied by configuration.
        var defaults = new RunnerWorktreeRetentionSettings();
        defaults.Enabled.ShouldBeTrue();
        defaults.Execute.ShouldBeTrue();
        defaults.Runners.ShouldBeEmpty();
        defaults.MinTerminalHours.ShouldBe(12);
        defaults.MaxTickMinutes.ShouldBe(20);
        defaults.KeptRecheckMinutes.ShouldBe(60);
        defaults.KeptRecheckMaxHours.ShouldBe(24);
        // CARD-1684 S26, S27 and the low J07-J10 values: the per-tick bound, the pacing and the shipped roots, cron and timeout.
        defaults.MaxRemovalsPerTick.ShouldBe(25);
        // CARD-1796 (review e4276bff A-3): the gone-mirror bound of CARD-1794.
        defaults.MaxAbsentPerTick.ShouldBe(200);
        defaults.PauseBetweenSeconds.ShouldBe(5);
        defaults.RequestTimeoutSeconds.ShouldBe(300);
        defaults.Cron.ShouldBe("*/30 * * * *");
        defaults.WorktreeRoot.ShouldBe("/work/worktrees");
        defaults.RetiringRoot.ShouldBe("/work/worktrees-retiring");
        defaults.TempSweep.Enabled.ShouldBeFalse();
        defaults.TempSweep.Execute.ShouldBeFalse();
        defaults.TempSweep.Families["claude-scratch"].ShouldBeFalse();

        var path = Path.Combine(C1514Support.RepoRoot(), "server", "appsettings.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var section = json.RootElement.GetProperty(RunnerWorktreeRetentionSettings.SectionName);
        section.GetProperty("Enabled").GetBoolean().ShouldBeTrue();
        section.GetProperty("Execute").GetBoolean().ShouldBeTrue();
        section.GetProperty("Runners").EnumerateArray().Select(runner => runner.GetString()).ShouldBe(["server2"]);
        section.GetProperty("MinTerminalHours").GetInt32().ShouldBe(12);
        section.GetProperty("MaxTickMinutes").GetInt32().ShouldBe(20);
        section.GetProperty("KeptRecheckMinutes").GetInt32().ShouldBe(60);
        section.GetProperty("KeptRecheckMaxHours").GetInt32().ShouldBe(24);
        // CARD-1684 J05-J10.
        section.GetProperty("MaxRemovalsPerTick").GetInt32().ShouldBe(25);
        section.GetProperty("MaxAbsentPerTick").GetInt32().ShouldBe(200);
        section.GetProperty("PauseBetweenSeconds").GetInt32().ShouldBe(5);
        section.GetProperty("RequestTimeoutSeconds").GetInt32().ShouldBe(300);
        section.GetProperty("Cron").GetString().ShouldBe("*/30 * * * *");
        section.GetProperty("WorktreeRoot").GetString().ShouldBe("/work/worktrees");
        section.GetProperty("RetiringRoot").GetString().ShouldBe("/work/worktrees-retiring");
        var temp = section.GetProperty("TempSweep");
        temp.GetProperty("Enabled").GetBoolean().ShouldBeFalse();
        temp.GetProperty("Execute").GetBoolean().ShouldBeFalse();
        temp.GetProperty("Families").GetProperty("claude-scratch").GetBoolean().ShouldBeFalse();

        // Bound as the server binds it: exactly one runner, so an override can still empty or replace the list.
        var bound = new ConfigurationBuilder().AddJsonFile(path).Build()
            .GetSection(RunnerWorktreeRetentionSettings.SectionName).Get<RunnerWorktreeRetentionSettings>()!;
        bound.Runners.ShouldBe(["server2"]);
        bound.Enabled.ShouldBeTrue();
        bound.Execute.ShouldBeTrue();
        bound.MaxRemovalsPerTick.ShouldBe(25);
        bound.MaxAbsentPerTick.ShouldBe(200);
        bound.PauseBetweenSeconds.ShouldBe(5);
    }

    [Test]
    [Arguments("MinTerminalHours", "0", false)]
    [Arguments("MinTerminalHours", "1", true)]
    [Arguments("MaxRemovalsPerTick", "0", false)]
    [Arguments("MaxRemovalsPerTick", "1", true)]
    [Arguments("MaxRemovalsPerTick", "100", true)]
    [Arguments("MaxRemovalsPerTick", "101", false)]
    [Arguments("MaxAbsentPerTick", "-1", false)]
    [Arguments("MaxAbsentPerTick", "0", true)]
    [Arguments("MaxAbsentPerTick", "1000", true)]
    [Arguments("MaxAbsentPerTick", "1001", false)]
    [Arguments("RetiringRoot", "work/r", false)]
    [Arguments("RetiringRoot", "/work/worktrees-retiring", true)]
    [Arguments("MaxTickMinutes", "0", false)]
    [Arguments("MaxTickMinutes", "20", true)]
    [Arguments("KeptRecheckMinutes", "0", false)]
    [Arguments("KeptRecheckMinutes", "1", true)]
    public async Task Validator_bounds(string field, string value, bool valid)
    {
        // V-36b (G-81, G-82, G-83).
        var settings = new RunnerWorktreeRetentionSettings();
        switch (field)
        {
            case "MinTerminalHours": settings.MinTerminalHours = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "MaxRemovalsPerTick": settings.MaxRemovalsPerTick = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "MaxAbsentPerTick": settings.MaxAbsentPerTick = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "MaxTickMinutes": settings.MaxTickMinutes = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "KeptRecheckMinutes": settings.KeptRecheckMinutes = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
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
