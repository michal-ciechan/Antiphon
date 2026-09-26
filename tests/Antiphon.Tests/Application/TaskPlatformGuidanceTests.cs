using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0710 V-12 and V-17. Stage bundles and owner docs name the platform contract.</summary>
[Category("Unit")]
public sealed class TaskPlatformGuidanceTests
{
    [Test]
    public void Stage_guidance_inherits_platform_before_using_any_and_names_the_platform_contract()
    {
        foreach (var name in new[] { "stage-code.md", "stage-review.md", "stage-mutation.md", "stage-plan.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", name));
            text.ShouldContain("inherits its predecessor's platform");
            text.ShouldContain("inherits the card's platform");
            text.ShouldContain("unpinned (Any)");
            text.ShouldContain("runtime default places it");
            text.ShouldContain("pass -Platform Any explicitly");
            text.ShouldContain("only when that piece of work requires it");
            text.ShouldContain("scope a platform-pinned task to just the OS-specific part");
            text.ShouldContain("OS-specific");
            text.ShouldContain("habit, stage name");
            text.ShouldNotContain("-Platform Windows for");
        }

        var orchestrator = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("inherits its predecessor's platform");
        orchestrator.ShouldContain("inherits the card's platform");
        orchestrator.ShouldContain("unpinned (Any)");
        orchestrator.ShouldContain("pass -Platform Any explicitly");
        orchestrator.ShouldContain("only when that piece of work requires it");
        orchestrator.ShouldContain("scope a platform-pinned task to just the OS-specific part");
        orchestrator.ShouldContain("habit, a stage name");
        orchestrator.ShouldNotContain("-Platform Windows for");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}

[Category("Unit")]
public sealed class RunnerDefaultGuidanceTests
{
    [Test]
    public void Bundles_read_runtime_defaults_without_pinning_location()
    {
        var orchestrator = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("GET /api/runner-defaults");
        orchestrator.ShouldContain("GET /api/session-runners");
        orchestrator.ShouldNotContain("always pass -Runner server2");
    }

    [Test]
    public void Platform_pins_are_explicit_and_bundles_inherit_before_any()
    {
        var plan = File.ReadAllText(Path.Combine(Root(), "docs", "superpowers", "plans", "2026-09-25-card-0710-task-platform-placement-plan.md"));
        plan.ShouldContain("CP-13");
        plan.ShouldContain("desktop / Windows");
        plan.ShouldContain("| CP-2 |");
        plan.ShouldContain("server2");
        foreach (var name in new[] { "stage-plan.md", "stage-code.md", "stage-review.md", "stage-mutation.md" })
        {
            var text = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", name));
            text.ShouldContain("inherits its predecessor's platform");
            text.ShouldContain("inherits the card's platform");
            text.ShouldContain("pass -Platform Any explicitly");
            text.ShouldContain("OS-specific");
            text.Contains("CP-13", StringComparison.Ordinal).ShouldBeFalse(name + " names CP-13");
            text.Contains("server2", StringComparison.Ordinal).ShouldBeFalse(name + " names server2");
        }
        var orchestrator = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("inherits its predecessor's platform");
        orchestrator.ShouldContain("pass -Platform Any explicitly");
        orchestrator.ShouldContain("OS-only probe");
    }

    [Test]
    public void Reroute_diagnostic_names_codex()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "server", "Application", "Services", "AgentTaskService.cs"));
        text.ShouldNotContain("Reroute to Grok or ClaudeCode.");
        text.ShouldContain("Reroute to Grok, ClaudeCode or Codex.");
    }

    [Test]
    public void Legacy_default_is_import_only_in_documented_configuration()
    {
        var settings = File.ReadAllText(Path.Combine(Root(), "server", "Application", "Settings", "DelegationSettings.cs"));
        settings.ShouldContain("import input only");
        var testing = File.ReadAllText(Path.Combine(Root(), "docs", "testing-and-build.md"));
        testing.ShouldContain("Delegation:DefaultRunnerId");
        testing.ShouldContain("import");
        testing.ShouldNotContain("automatic default placement still keeps Codex on the desktop");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
