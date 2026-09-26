using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0710 V-12 and V-17. Stage bundles and owner docs name the platform contract.</summary>
[Category("Unit")]
public sealed class TaskPlatformGuidanceTests
{
    [Test]
    public void Stage_bundles_leave_twenty_characters_below_the_size_cap()
    {
        foreach (var name in new[] { "stage-investigate.md", "stage-plan.md", "stage-test-design.md", "stage-code.md", "stage-mutation.md", "stage-review.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", name))
                .Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            text.Length.ShouldBeLessThanOrEqualTo(2_480, name);
        }
    }

    [Test]
    public void Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes()
    {
        foreach (var name in new[] { "stage-code.md", "stage-review.md", "stage-mutation.md", "stage-plan.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", name));
            text.ShouldContain("Omit -Runner unless pinning one host.");
            text.ShouldContain("Omit -Platform unless OS needed");
            text.ShouldContain("-Platform Any unpins.");
            text.ShouldContain("GET /api/runner-defaults");
            text.ShouldContain("/api/session-runners");
            text.ShouldNotContain("pass -Platform Any explicitly");
            if (name is "stage-code.md" or "stage-mutation.md")
                text.ShouldContain("Do not embed a fleet location.");
        }

        var orchestrator = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "orchestrator.md"));
        orchestrator.ShouldContain("inherits its predecessor's platform");
        orchestrator.ShouldContain("inherits the card's platform");
        orchestrator.ShouldContain("unpinned (Any)");
        orchestrator.ShouldContain("runtime default places it");
        orchestrator.ShouldContain("pass -Platform Any explicitly");
        orchestrator.ShouldContain("only when that piece of work requires it");
        orchestrator.ShouldContain("scope a platform-pinned task to just the OS-specific part");
        orchestrator.ShouldContain("habit, a stage name");
        orchestrator.ShouldContain("Normally omit -Runner; the runtime default places the task.");
        orchestrator.ShouldContain("To unpin a stage on a pinned card");
        orchestrator.ShouldContain("with its own filter and budget; leave the rest unpinned");
        orchestrator.ShouldContain("Do not embed a fleet location.");
    }

    [Test]
    public void Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "orchestration-loop.md"));
        text.ShouldContain("a follow-up inherits its predecessor's platform");
        text.ShouldContain("a stage inherits the card's platform");
        text.ShouldContain("else the task is unpinned (`Any`)");
        text.ShouldContain("pass `-Platform Any` explicitly");
        text.ShouldContain("Pass a specific platform only");
        text.ShouldContain("scope a platform-pinned task to just the OS-specific part");
        text.ShouldContain("Normally omit `-Runner`; the runtime default places the task.");
        text.ShouldContain("the runtime default places");
        text.ShouldContain("To unpin a stage on a pinned card");
        text.ShouldContain("Habit, a stage name");
        text.ShouldContain("with its own filter and budget; leave the rest unpinned");
    }

    [Test]
    public void Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "server", "Bundles", "stage-review.md"));
        text.ShouldContain("self-compare, constant, no outcome assertion");
        text.ShouldContain("Full only when the whole required selection ran. The caller lands that Code owner with `-ExpectedSourceSha` from this evidence.");
        text.ShouldContain("GET /api/runner-defaults");
        text.ShouldContain("GET /api/session-runners");
        text.ShouldContain("embed no fleet location");
        text.ShouldContain("the brief's verification profile governs");
        text.ShouldContain("through the real queue");
        text.ShouldContain("Acceptance needs");
        text.ShouldContain("Re-run the claimed checks (Unit plus named affected integration classes)");
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
    public void Platform_pins_are_explicit_and_bundles_defer_to_orchestrator_contract()
    {
        var plan = File.ReadAllText(Path.Combine(Root(), "docs", "superpowers", "plans", "2026-09-25-card-0710-task-platform-placement-plan.md"));
        plan.ShouldContain("CP-13");
        plan.ShouldContain("desktop / Windows");
        plan.ShouldContain("| CP-2 |");
        plan.ShouldContain("server2");
        foreach (var name in new[] { "stage-plan.md", "stage-code.md", "stage-review.md", "stage-mutation.md" })
        {
            var text = File.ReadAllText(Path.Combine(Root(), "server", "Bundles", name));
            text.ShouldContain("Omit -Platform unless OS needed");
            text.ShouldContain("-Platform Any unpins.");
            text.ShouldContain("Omit -Runner unless pinning one host.");
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
