using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// What a settled task actually touched, mapped back onto the repo's areas (CARD-0063 S4).
///
/// <para>Observability only. Nothing here fails, holds, kills or re-types anything — the card's
/// explicit non-goal. A path-blocking hook could only ever be armed in a task's own worktree, where
/// an out-of-area write is already isolated, and it would turn every wrong prediction into a stuck
/// delegate at exactly the moment it found the file nobody predicted.</para>
/// </summary>
[Category("Unit")]
public class ScopeDriftTests
{
    private static readonly AreaMap Map = BuildMap();

    [Test]
    public void paths_inside_the_declared_areas_record_the_area_and_no_drift()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "delivery",
            ["server/Application/Services/SessionMessageQueueService.cs",
             "src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs"],
            Map);

        result.ObservedScope.ShouldBe("delivery");
        result.Drifted.ShouldBeEmpty();
    }

    [Test]
    public void a_migration_written_under_a_declared_delivery_scope_drifts_into_schema()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "delivery",
            ["server/Application/Services/SessionMessageQueueService.cs",
             "server/Migrations/20260827130000_RenameThing.cs"],
            Map);

        result.ObservedScope.ShouldBe("delivery,schema");
        result.Drifted.ShouldHaveSingleItem()
            .ShouldBe("schema (server/Migrations/20260827130000_RenameThing.cs)");
        ScopeDriftPolicy.DescribeDrift("delivery", result.Drifted)
            .ShouldBe("Touched schema (server/Migrations/20260827130000_RenameThing.cs) "
                + "outside declared [delivery].");
        ScopeDriftPolicy.DescribeHeader(result.Drifted).ShouldBe("schema");
    }

    [Test]
    public void a_path_that_matches_no_area_is_named_verbatim()
    {
        var result = ScopeDriftPolicy.Evaluate("delivery", ["tools/oddball/thing.py"], Map);

        result.ObservedScope.ShouldBe("tools/oddball/thing.py");
        result.Drifted.ShouldHaveSingleItem().ShouldBe("tools/oddball/thing.py");
    }

    [Test]
    public void a_task_with_no_declared_scope_records_what_it_touched_and_drifts_from_nothing()
    {
        var result = ScopeDriftPolicy.Evaluate(
            null, ["server/Migrations/20260827_Thing.cs", "docs/setup.md"], Map);

        result.ObservedScope.ShouldBe("schema,docs");
        result.Drifted.ShouldBeEmpty("a task that promised nothing cannot have broken a promise");
    }

    [Test]
    public void a_declared_path_glob_covers_the_files_it_matches()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "server/Migrations/**", ["server/Migrations/20260827_Thing.cs"], Map);

        result.Drifted.ShouldBeEmpty();
        result.ObservedScope.ShouldBe("schema", "the observation is still by AREA");
    }

    [Test]
    public void a_declared_single_file_does_not_cover_its_neighbours()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "server/Application/Services/SessionMessageQueueService.cs",
            ["server/Application/Services/SessionMessageQueueService.cs",
             "server/Application/Services/AgentTaskService.cs"],
            Map);

        result.Drifted.ShouldHaveSingleItem()
            .ShouldBe("delegation (server/Application/Services/AgentTaskService.cs)");
    }

    [Test]
    public void an_unknown_area_name_covers_nothing()
    {
        // It owns no paths — which is exactly why it earns a Warning at create time. Declaring one
        // is NOT the same as declaring nothing: the caller made a promise that resolves to no part
        // of the tree, so everything it wrote is outside it. That is the signal that pushes a name
        // into the map instead of leaving it an opaque label forever.
        var result = ScopeDriftPolicy.Evaluate("made-up", ["docs/setup.md"], Map);

        result.Drifted.ShouldHaveSingleItem().ShouldBe("docs (docs/setup.md)");
    }

    [Test]
    public void windows_separators_and_leading_dot_slashes_are_normalised()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "delivery", [@".\server\Application\Services\SessionMessageQueueService.cs"], Map);

        result.Drifted.ShouldBeEmpty();
        result.ObservedScope.ShouldBe("delivery");
    }

    [Test]
    public void one_drift_line_per_area_however_many_files_it_holds()
    {
        var result = ScopeDriftPolicy.Evaluate(
            "delivery",
            ["server/Migrations/a.cs", "server/Migrations/b.cs", "server/Domain/Entities/Thing.cs"],
            Map);

        result.Drifted.ShouldHaveSingleItem().ShouldStartWith("schema (");
    }

    [Test]
    public void nothing_touched_records_nothing()
    {
        var result = ScopeDriftPolicy.Evaluate("delivery", [], Map);

        result.ObservedScope.ShouldBeNull();
        result.Drifted.ShouldBeEmpty();
        ScopeDriftPolicy.DescribeDrift("delivery", result.Drifted).ShouldBeNull();
        ScopeDriftPolicy.DescribeHeader(result.Drifted).ShouldBeNull();
    }

    [Test]
    public void the_completion_header_carries_the_drift()
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "the work",
            Goal = "do the work",
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.Combine("C:", "src", "antiphon"),
            Status = AgentTaskStatus.Succeeded,
        };

        var note = DelegationReportFormatter.BuildCompletionNote(
            task, new DelegationSettings(), "done", drift: "schema");

        note.Header.ShouldContain("drift=schema");
    }

    private static AreaMap BuildMap() => new(
        new Dictionary<string, AreaDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["delivery"] = new("delivery", [
                "server/Application/Services/SessionMessageQueueService*.cs",
                "src/Antiphon.Agents.Pty/ComposerDeliveryEvidence*.cs",
            ], AreaWeight.Serialise),
            ["delegation"] = new("delegation", [
                "server/Application/Services/AgentTask*.cs",
            ], AreaWeight.Serialise),
            ["schema"] = new("schema", [
                "server/Migrations/**", "server/Domain/Entities/**",
            ], AreaWeight.Serialise),
            ["docs"] = new("docs", ["docs/**", "AGENTS.md"], AreaWeight.Allow),
        },
        sourcePath: "in-memory");
}
