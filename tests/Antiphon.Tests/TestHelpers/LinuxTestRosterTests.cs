using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0590 S5 roster validator (Assert-LinuxTestRoster of the deleted scripts/test-docker-container.ps1): the test-docker-container
/// command's --validate-roster in process.
/// </summary>
[Category("Unit")]
public sealed class LinuxTestRosterTests
{
    [Test]
    public void Unknown_class_is_refused()
    {
        var output = Validate(Rows(Include("Known")), ["Known", "Mystery"]);
        output.ShouldContain("unknown class Mystery");
    }

    [Test]
    public void Duplicate_class_is_refused()
    {
        var output = Validate([Include("Known"), Include("Known")], ["Known"]);
        output.ShouldContain("duplicate class Known");
    }

    [Test]
    public void Stale_class_is_refused()
    {
        var output = Validate(Rows(Include("Known"), Include("Missing")), ["Known"]);
        output.ShouldContain("stale class Missing");
    }

    [Test]
    public void Native_class_is_refused()
    {
        var output = Validate(Rows(new Dictionary<string, object?>
        {
            ["class"] = "Antiphon.Tests.Application.FakeGrokContractTests",
            ["disposition"] = "include",
            ["boundary"] = "native launch",
            ["owner"] = "CARD-0588",
            ["reason"] = "native",
        }), ["Antiphon.Tests.Application.FakeGrokContractTests"]);
        output.ShouldContain("IncludedNative");
    }

    [Test]
    public void Indirect_spawner_is_refused()
    {
        var output = Validate(Rows(Include("WorkspaceHookRunnerTests")), ["WorkspaceHookRunnerTests"]);
        output.ShouldContain("IncludedSpawner");
    }

    [Test]
    public void Unowned_exclusion_is_refused()
    {
        var output = Validate(Rows(new Dictionary<string, object?>
        {
            ["class"] = "Excluded",
            ["disposition"] = "exclude",
            ["boundary"] = "",
            ["owner"] = "",
            ["reason"] = "",
        }), ["Excluded"]);
        output.ShouldContain("MissingExclusionOwner");
    }

    [Test]
    public void Valid_roster_prints_roster_ok()
    {
        var run = Run(Rows(Include("Known"), new Dictionary<string, object?>
        {
            ["class"] = "Excluded",
            ["disposition"] = "exclude",
            ["boundary"] = "native launch",
            ["owner"] = "CARD-0590",
            ["reason"] = "native",
        }), ["known", "Excluded"]);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldBe("roster-ok" + Environment.NewLine);
    }

    [Test]
    public void Every_error_is_one_line_and_exit_2()
    {
        var run = Run([Include("Known"), Include("known"), Include("WorkspaceHookRunnerTests")], ["Known", "Mystery"]);
        run.ExitCode.ShouldBe(2);
        run.Output.ReplaceLineEndings("\n").ShouldBe(string.Join("\n",
            "duplicate class known", "IncludedSpawner WorkspaceHookRunnerTests", "unknown class Mystery", "stale class WorkspaceHookRunnerTests") + "\n");
    }

    [Test]
    public void Missing_roster_path_is_a_usage_error()
    {
        var run = Antiphon.Tests.Scripts.C590Harness.Run("test-docker-container", Antiphon.Tests.Scripts.C590Harness.Happy(), args: ["--validate-roster"]);
        run.ExitCode.ShouldBe(2);
        run.Output.ShouldContain("--roster is required with --validate-roster");
    }

    // The validator of test-docker-container (DockerTestContainer.Validate since unit 47 of the 2026-11-22 plan) in process: the
    // roster and the discovery list live in C590Harness's mock file system.
    private static string Validate(object[] rows, string[] discovery) => Run(rows, discovery).Output;

    private static Antiphon.Tests.Scripts.C590Run Run(object[] rows, string[] discovery)
    {
        var roster = Path.Combine(Antiphon.Tests.Scripts.C590Harness.FixtureRoot, "roster.json");
        var found = Path.Combine(Antiphon.Tests.Scripts.C590Harness.FixtureRoot, "discovery.json");
        return Antiphon.Tests.Scripts.C590Harness.Run(
            "test-docker-container",
            Antiphon.Tests.Scripts.C590Harness.Happy(),
            files: new Dictionary<string, string>
            {
                [roster] = JsonSerializer.Serialize(new { classes = rows }),
                [found] = JsonSerializer.Serialize(discovery),
            },
            args: ["--validate-roster", "--roster", roster, "--discovery", found]);
    }

    private static object[] Rows(params Dictionary<string, object?>[] rows) => rows;

    private static Dictionary<string, object?> Include(string name) => new()
    {
        ["class"] = name,
        ["disposition"] = "include",
        ["boundary"] = "managed",
        ["owner"] = "CARD-0590",
        ["reason"] = "included",
        ["testHelperClosure"] = Array.Empty<string>(),
    };
}
