using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-14. The pinned standing agent stops being mandatory exactly when the runner becomes
// a pool, and not before: an enabled runner with neither a pinned agent nor delegated tasks can
// launch nothing at all, which is a configuration mistake, not a safe default.
[Category("Unit")]
public sealed class PhoneHomeRunnerSettingsValidatorTests
{
    [Test]
    public void Standing_agent_optional_only_with_delegated_tasks()
    {
        var withoutEither = PhoneHomeRunnerSettingsRules.Validate(Settings(s =>
        {
            s.StandingAgentId = Guid.Empty;
            s.AllowDelegatedTasks = false;
        }));
        withoutEither.ShouldContain(m => m.Contains("StandingAgentId", StringComparison.Ordinal));

        var withDelegatedTasks = PhoneHomeRunnerSettingsRules.Validate(Settings(s =>
        {
            s.StandingAgentId = Guid.Empty;
            s.AllowDelegatedTasks = true;
        }));
        withDelegatedTasks.ShouldBeEmpty();

        var withPinnedAgent = PhoneHomeRunnerSettingsRules.Validate(Settings(s =>
        {
            s.StandingAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            s.AllowDelegatedTasks = false;
        }));
        withPinnedAgent.ShouldBeEmpty();
    }

    [Test]
    public void Disabled_runner_validates_nothing()
    {
        PhoneHomeRunnerSettingsRules.Validate(new PhoneHomeRunnerSettings { Enabled = false }).ShouldBeEmpty();
    }

    [Test]
    public void Runner_repository_and_capacity_bound_are_checked()
    {
        PhoneHomeRunnerSettingsRules.Validate(Settings(s => s.RunnerRepository = @"C:\src\Antiphon"))
            .ShouldContain(m => m.Contains("RunnerRepository", StringComparison.Ordinal));
        PhoneHomeRunnerSettingsRules.Validate(Settings(s => s.MaxCapacity = 0))
            .ShouldContain(m => m.Contains("MaxCapacity", StringComparison.Ordinal));
        PhoneHomeRunnerSettingsRules.Validate(Settings(s => s.RawExeAllowList = ["sh"]))
            .ShouldContain(m => m.Contains("RawExeAllowList", StringComparison.Ordinal));
    }

    [Test]
    public void Defaults_are_the_documented_server2_shape()
    {
        var settings = new PhoneHomeRunnerSettings();
        settings.RunnerRepository.ShouldBe("/work/repos/antiphon");
        settings.MaxCapacity.ShouldBe(10);
        settings.RawExeAllowList.ShouldBe(["/bin/sh", "/bin/bash", "/usr/local/bin/pwsh"]);
        settings.AllowDelegatedTasks.ShouldBeFalse("delegated tasks are opt-in, not a default");
        settings.ChildClaudeHome.ShouldBe("/state/claude");
        settings.ClaudeAuthProbeEnabled.ShouldBeTrue();
    }

    [Test]
    public void Child_claude_home_must_be_posix_absolute()
    {
        PhoneHomeRunnerSettingsRules.Validate(Settings(s => s.ChildClaudeHome = "state/claude"))
            .ShouldContain(m => m.Contains("ChildClaudeHome", StringComparison.Ordinal));
    }

    private static PhoneHomeRunnerSettings Settings(Action<PhoneHomeRunnerSettings> mutate)
    {
        var settings = new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "server2",
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon",
            RunnerWorkspace = "/work",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x",
        };
        mutate(settings);
        return settings;
    }
}
