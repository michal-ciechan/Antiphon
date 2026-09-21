using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxTestRosterTests
{
    [Test]
    public async Task Unknown_class_is_refused()
    {
        var output = await Validate(Rows(Include("Known")), ["Known", "Mystery"]);
        output.ShouldContain("unknown class Mystery");
    }

    [Test]
    public async Task Duplicate_class_is_refused()
    {
        var output = await Validate([Include("Known"), Include("Known")], ["Known"]);
        output.ShouldContain("duplicate class Known");
    }

    [Test]
    public async Task Stale_class_is_refused()
    {
        var output = await Validate(Rows(Include("Known"), Include("Missing")), ["Known"]);
        output.ShouldContain("stale class Missing");
    }

    [Test]
    public async Task Native_class_is_refused()
    {
        var output = await Validate(Rows(new Dictionary<string, object?>
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
    public async Task Indirect_spawner_is_refused()
    {
        var output = await Validate(Rows(Include("WorkspaceHookRunnerTests")), ["WorkspaceHookRunnerTests"]);
        output.ShouldContain("IncludedSpawner");
    }

    [Test]
    public async Task Unowned_exclusion_is_refused()
    {
        var output = await Validate(Rows(new Dictionary<string, object?>
        {
            ["class"] = "Excluded",
            ["disposition"] = "exclude",
            ["boundary"] = "",
            ["owner"] = "",
            ["reason"] = "",
        }), ["Excluded"]);
        output.ShouldContain("MissingExclusionOwner");
    }

    private static async Task<string> Validate(object[] rows, string[] discovery)
    {
        var root = Directory.CreateTempSubdirectory("c590-roster-").FullName;
        var roster = Path.Combine(root, "roster.json");
        var found = Path.Combine(root, "discovery.json");
        await File.WriteAllTextAsync(roster, JsonSerializer.Serialize(new { classes = rows }));
        await File.WriteAllTextAsync(found, JsonSerializer.Serialize(discovery));
        var run = await Antiphon.Tests.Scripts.C590Harness.RunAsync(
            "test-docker-container.ps1",
            Antiphon.Tests.Scripts.C590Harness.Happy(),
            args: ["-ValidateRoster", "-Roster", roster, "-Discovery", found]);
        return run.Output;
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
